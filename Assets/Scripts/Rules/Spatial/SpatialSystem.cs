// Cell obstacle model (blocks movement / blocks sight / cover per cell), zone projection separate from
// occupying space, IsInsideZone, temporary (overwatch) zones, the windowed distance field, gradient-traced
// paths, body radii, cover reach scaled by body radius, and zone entries only where the owner sees them by Claude
// (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>
    /// Owns the map grid and every unit's position, facing and zone of control, and answers all
    /// geometric questions: cells, cover, line of sight, distance fields, paths and zone entries.
    /// </summary>
    /// <remarks>
    /// World positions are <see cref="Vector2"/> on the ground plane: X is world X, Y is world Z.
    /// Cell (x, y) covers [Origin + (x, y) * CellSize, Origin + (x + 1, y + 1) * CellSize) and has
    /// index y * Width + x. A cell is passable when it has floor and no obstacle that blocks movement. A unit with
    /// a body radius stands in and walks through only passable cells whose centre is at least that far from every
    /// impassable cell and from other units' bodies.
    /// </remarks>
    public sealed class SpatialSystem
    {
        private const float pathSampleStep = 0.25f;

        /// <summary>Number of cells along world X.</summary>
        public readonly int Width;
        /// <summary>Number of cells along world Z.</summary>
        public readonly int Height;
        /// <summary>Edge length of one square cell, in metres.</summary>
        public readonly float CellSize;
        /// <summary>World position of the corner of cell (0, 0).</summary>
        public readonly Vector2 Origin;

        private readonly UnitRegistry units;
        private readonly IDistanceFieldSolver solver;
        private readonly bool[] floor;
        private readonly CellObstacle[] obstacles;

        private readonly Vector2[] positions;
        private readonly Vector2[] facings;
        private readonly ZoneSetup[] zones;
        private readonly bool[] occupiesSpace;
        private readonly bool[] projectsZone;
        private readonly ZoneSetup[] temporaryZones;
        private readonly Vector2[] temporaryFacings;
        private readonly bool[] hasTemporaryZone;
        private readonly float[] bodyRadii;
        private readonly Dictionary<float, bool[]> clearOfTerrain = new Dictionary<float, bool[]>();

        private readonly bool[] passableForFieldOwner;
        private readonly bool[] seededForFieldOwner;
        private readonly float[] distanceField;
        private DistanceSeed[] seeds = new DistanceSeed[64];
        private int seedCount;
        private readonly List<Vector2> rawPath = new List<Vector2>(64);
        private readonly List<Vector2> gradientPath = new List<Vector2>(16);
        private readonly bool[] connectedToFieldOwner;
        private readonly Stack<int> fillStack = new Stack<int>(64);
        private int connectivityVersion = -1;
        private int fieldStartCell = -1;

        /// <summary>
        /// Creates the map. <paramref name="floor"/> marks cells that have floor (false for pits and
        /// gaps); <paramref name="obstacles"/> records what stands on each cell. Both have
        /// <paramref name="width"/> * <paramref name="height"/> elements and are copied.
        /// </summary>
        public SpatialSystem(UnitRegistry units, int width, int height, float cellSize, Vector2 origin,
                             bool[] floor, CellObstacle[] obstacles, IDistanceFieldSolver solver)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (floor.Length != width * height || obstacles.Length != width * height)
                throw new ArgumentException("floor and obstacles must have width * height elements.");

            this.units = units;
            this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = origin;
            this.floor = (bool[])floor.Clone();
            this.obstacles = (CellObstacle[])obstacles.Clone();

            positions = new Vector2[units.Capacity];
            facings = new Vector2[units.Capacity];
            zones = new ZoneSetup[units.Capacity];
            occupiesSpace = new bool[units.Capacity];
            projectsZone = new bool[units.Capacity];
            temporaryZones = new ZoneSetup[units.Capacity];
            temporaryFacings = new Vector2[units.Capacity];
            hasTemporaryZone = new bool[units.Capacity];
            bodyRadii = new float[units.Capacity];

            passableForFieldOwner = new bool[width * height];
            seededForFieldOwner = new bool[width * height];
            distanceField = new float[width * height];
            for (int i = 0; i < distanceField.Length; i++) distanceField[i] = float.PositiveInfinity;
            connectedToFieldOwner = new bool[width * height];
        }

        /// <summary>Total number of cells.</summary>
        public int CellCount => Width * Height;

        // ---------------------------------------------------------------- cells

        /// <summary>Finds the cell containing a world position; false if the position is off the map.</summary>
        public bool TryGetCell(Vector2 position, out int cell)
        {
            int x = (int)MathF.Floor((position.X - Origin.X) / CellSize);
            int y = (int)MathF.Floor((position.Y - Origin.Y) / CellSize);
            if (x < 0 || y < 0 || x >= Width || y >= Height) { cell = -1; return false; }
            cell = y * Width + x;
            return true;
        }

        /// <summary>World position of a cell's centre.</summary>
        public Vector2 CellCenter(int cell) =>
            Origin + new Vector2((cell % Width + 0.5f) * CellSize, (cell / Width + 0.5f) * CellSize);

        /// <summary>True if the cell has floor and no obstacle that blocks movement (ignores units).</summary>
        public bool IsOpenFloor(int cell) => floor[cell] && !obstacles[cell].BlocksMovement;

        /// <summary>True if the position is on the map, on floor, and free of obstacles that block movement (ignores units).</summary>
        public bool IsOpenFloor(Vector2 position) => TryGetCell(position, out int cell) && IsOpenFloor(cell);

        /// <summary>What stands on a cell (<see cref="CellObstacle.None"/> if nothing).</summary>
        public CellObstacle GetObstacle(int cell) => obstacles[cell];

        // ---------------------------------------------------------------- units

        /// <summary>
        /// Places a newly registered unit with a body of <paramref name="bodyRadius"/> metres (0 for a point). It
        /// occupies space and projects its zone until <see cref="SetOccupiesSpace"/> or <see cref="SetProjectsZone"/>
        /// clears them. Throws <see cref="ArgumentOutOfRangeException"/> if the radius is negative or NaN.
        /// </summary>
        public void AddUnit(UnitId id, Vector2 position, Vector2 facing, in ZoneSetup zone, float bodyRadius = 0f)
        {
            units.Require(id);
            if (!(bodyRadius >= 0f)) throw new ArgumentOutOfRangeException(nameof(bodyRadius));
            bodyRadii[id.Index] = bodyRadius;
            positions[id.Index] = position;
            facings[id.Index] = facing.LengthSquared() > 1e-12f ? Vector2.Normalize(facing) : Vector2.UnitX;
            zones[id.Index] = zone;
            occupiesSpace[id.Index] = true;
            projectsZone[id.Index] = true;
            hasTemporaryZone[id.Index] = false;
        }

        /// <summary>A unit's body radius in metres.</summary>
        public float GetBodyRadius(UnitId id) { units.Require(id); return bodyRadii[id.Index]; }

        /// <summary>A unit's current position.</summary>
        public Vector2 GetPosition(UnitId id) { units.Require(id); return positions[id.Index]; }

        /// <summary>Moves a unit. Does not validate the move; <see cref="MovementSystem"/> does that.</summary>
        public void SetPosition(UnitId id, Vector2 position) { units.Require(id); positions[id.Index] = position; }

        /// <summary>A unit's current facing (unit length).</summary>
        public Vector2 GetFacing(UnitId id) { units.Require(id); return facings[id.Index]; }

        /// <summary>Turns a unit to face along <paramref name="direction"/>; a zero direction leaves facing unchanged.</summary>
        public void FaceTowards(UnitId id, Vector2 direction)
        {
            units.Require(id);
            if (direction.LengthSquared() > 1e-12f) facings[id.Index] = Vector2.Normalize(direction);
        }

        /// <summary>
        /// Whether a unit is present on the map: it blocks movement and can project its zone of control.
        /// Cleared when a unit is defeated.
        /// </summary>
        public void SetOccupiesSpace(UnitId id, bool occupies) { units.Require(id); occupiesSpace[id.Index] = occupies; }

        /// <summary>True if the unit blocks movement.</summary>
        public bool OccupiesSpace(UnitId id) { units.Require(id); return occupiesSpace[id.Index]; }

        /// <summary>Whether a unit's zone of control is in force. Cleared while a unit is downed.</summary>
        public void SetProjectsZone(UnitId id, bool projects) { units.Require(id); projectsZone[id.Index] = projects; }

        // ---------------------------------------------------------------- sight and cover

        /// <summary>
        /// True if no cell that blocks sight lies strictly between the two positions' cells.
        /// </summary>
        public bool HasLineOfSight(Vector2 from, Vector2 to) => trace(from, to, forWalking: false);

        /// <summary>
        /// The best cover protecting a unit with body radius <paramref name="bodyRadius"/> at
        /// <paramref name="position"/> from an attacker at <paramref name="attackerPosition"/>. An obstacle counts if
        /// its cell lies within the body radius plus one cell of the centre of the unit's cell, and the direction
        /// from that centre to the obstacle's lies within 60 degrees of the direction to the attacker. The extra
        /// cell always reaches the nearest obstacle a unit of that radius can stand beside: with no radius, the 8
        /// cells around the unit's cell.
        /// </summary>
        public CoverLevel CoverAgainst(Vector2 position, Vector2 attackerPosition, float bodyRadius = 0f)
        {
            if (!TryGetCell(position, out int cell)) return CoverLevel.None;
            Vector2 toAttacker = attackerPosition - position;
            if (toAttacker.LengthSquared() < 1e-12f) return CoverLevel.None;
            toAttacker = Vector2.Normalize(toAttacker);

            int x = cell % Width, y = cell / Width;
            float reach = MathF.Max(0f, bodyRadius) + CellSize, half = 0.5f * CellSize;
            int cells = (int)MathF.Ceiling(reach / CellSize);
            CoverLevel best = CoverLevel.None;
            for (int ny = Math.Max(0, y - cells); ny <= Math.Min(Height - 1, y + cells); ny++)
            {
                for (int nx = Math.Max(0, x - cells); nx <= Math.Min(Width - 1, x + cells); nx++)
                {
                    CoverLevel cover = obstacles[ny * Width + nx].Cover;
                    if (cover <= best || (nx == x && ny == y)) continue;
                    // Distance from the unit's cell centre to the obstacle cell's square.
                    float gapX = MathF.Max(0f, Math.Abs(nx - x) * CellSize - half);
                    float gapY = MathF.Max(0f, Math.Abs(ny - y) * CellSize - half);
                    if (gapX * gapX + gapY * gapY > reach * reach + 1e-6f) continue;
                    var direction = Vector2.Normalize(new Vector2(nx - x, ny - y));
                    if (Vector2.Dot(direction, toAttacker) >= 0.5f) best = cover;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- distance field and paths

        /// <summary>The unit the current distance field was built for, or <see cref="UnitId.None"/>.</summary>
        public UnitId FieldOwner { get; private set; }

        /// <summary>Incremented every time the distance field is rebuilt, so presentation knows when to re-upload it.</summary>
        public int FieldVersion { get; private set; }

        /// <summary>
        /// The distance limit the current field was built with (at least the owner's distance to its own cell's
        /// centre); <see cref="float.PositiveInfinity"/> for the whole map.
        /// </summary>
        public float FieldRange { get; private set; } = float.PositiveInfinity;

        /// <summary>
        /// True if the current field was cut off at <see cref="FieldRange"/>: some cells lie beyond it, so a cell
        /// left at <see cref="float.PositiveInfinity"/> may be reachable after all. False means every infinite
        /// cell is truly unreachable.
        /// </summary>
        public bool FieldIsTruncated { get; private set; }

        /// <summary>
        /// Distance in metres from <see cref="FieldOwner"/>'s position (when the field was built) to each cell
        /// centre, avoiding obstacles and other units; <see cref="float.PositiveInfinity"/> where unreachable or
        /// farther than <see cref="FieldRange"/>. Exact at every cell centre the owner can walk to in a straight
        /// line. Indexed like cells.
        /// </summary>
        public ReadOnlySpan<float> Field => distanceField;

        /// <summary>
        /// Rebuilds <see cref="Field"/> for a unit at its current position, out to <paramref name="maxDistance"/>
        /// metres (the whole map by default). Other units that occupy space block movement; the unit itself does not.
        /// Cells closer to anything impassable than the unit's body radius are closed to it, as are cells closer to
        /// another unit than their two radii together; the unit's own cell stays open, so it can always walk out.
        /// Throws <see cref="ArgumentOutOfRangeException"/> if <paramref name="maxDistance"/> is negative or NaN.
        /// </summary>
        /// <remarks>
        /// Every cell centre within range that the unit can walk to in a straight line is passed to the solver as
        /// a seed at its exact distance, so the solver only approximates distances around obstacles. Finding the
        /// seeds traces a line to every passable cell within <paramref name="maxDistance"/> in a straight line (no
        /// walk is shorter, so no cell within range is missed), which costs about (cells in range) x (range in
        /// cells) cell visits; the whole map costs about (cell count) x (map width).
        /// </remarks>
        public void RebuildField(UnitId unit, float maxDistance = float.PositiveInfinity)
        {
            units.Require(unit);
            DistanceLimit.Validate(maxDistance);
            float radius = bodyRadii[unit.Index];
            Array.Copy(clearOfTerrainFor(radius), passableForFieldOwner, passableForFieldOwner.Length);

            for (int slot = 0; slot < units.Capacity; slot++)
            {
                UnitId other = units.GetIdAtSlot(slot);
                if (other.IsNone || other == unit || !occupiesSpace[slot]) continue;
                if (TryGetCell(positions[slot], out int occupied)) passableForFieldOwner[occupied] = false;
                closeCellsNear(positions[slot], radius + bodyRadii[slot]);
            }
            // A unit placed inside its clearance (next to a wall or another unit) can still walk out.
            if (TryGetCell(positions[unit.Index], out int own) && IsOpenFloor(own)) passableForFieldOwner[own] = true;

            FieldOwner = unit;
            FieldVersion++;

            // Seed every passable cell centre within range and in straight-line walking reach with its exact
            // distance. The unit's own cell is always included, stretching the range if need be. An off-map
            // unit, or one whose cell is blocked, gets no seeds and so an all-infinite field.
            Vector2 position = positions[unit.Index];
            float range = maxDistance;
            seedCount = 0;
            fieldStartCell = -1;
            if (TryGetCell(position, out int start) && passableForFieldOwner[start])
            {
                fieldStartCell = start;
                range = MathF.Max(range, Vector2.Distance(position, CellCenter(start)));
                // Clamp in float before converting: a huge range must not overflow the conversion to int.
                int minX = (int)MathF.Max(0f, MathF.Floor((position.X - range - Origin.X) / CellSize));
                int maxX = (int)MathF.Min(Width - 1, MathF.Floor((position.X + range - Origin.X) / CellSize));
                int minY = (int)MathF.Max(0f, MathF.Floor((position.Y - range - Origin.Y) / CellSize));
                int maxY = (int)MathF.Min(Height - 1, MathF.Floor((position.Y + range - Origin.Y) / CellSize));
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        int cell = y * Width + x;
                        if (!passableForFieldOwner[cell]) continue;
                        Vector2 centre = CellCenter(cell);
                        float distance = Vector2.Distance(position, centre);
                        if (distance > range) continue;
                        if (cell != start && !trace(position, centre, forWalking: true)) continue;
                        if (seedCount == seeds.Length) Array.Resize(ref seeds, seeds.Length * 2);
                        seeds[seedCount++] = new DistanceSeed(cell, distance);
                    }
                }
            }

            FieldRange = range;
            FieldIsTruncated = solver.Solve(Width, Height, CellSize, passableForFieldOwner,
                                            new ReadOnlySpan<DistanceSeed>(seeds, 0, seedCount), range, distanceField);
            for (int i = 0; i < seededForFieldOwner.Length; i++) seededForFieldOwner[i] = false;
            for (int k = 0; k < seedCount; k++) seededForFieldOwner[seeds[k].Cell] = true;
        }

        // Per radius, once: true for open-floor cells whose centre is at least the radius from every cell that is not
        // open floor (measured to that cell's square). Space off the map does not count.
        private bool[] clearOfTerrainFor(float radius)
        {
            if (clearOfTerrain.TryGetValue(radius, out bool[] clear)) return clear;
            clear = new bool[CellCount];
            int reach = (int)MathF.Ceiling(radius / CellSize);
            float half = 0.5f * CellSize;
            for (int cell = 0; cell < clear.Length; cell++)
            {
                if (!IsOpenFloor(cell)) continue;
                int x = cell % Width, y = cell / Width;
                bool isClear = true;
                for (int ny = Math.Max(0, y - reach); ny <= Math.Min(Height - 1, y + reach) && isClear; ny++)
                {
                    for (int nx = Math.Max(0, x - reach); nx <= Math.Min(Width - 1, x + reach); nx++)
                    {
                        if (IsOpenFloor(ny * Width + nx)) continue;
                        // Distance from this cell's centre to the blocked cell's square.
                        float gapX = MathF.Max(0f, Math.Abs(nx - x) * CellSize - half);
                        float gapY = MathF.Max(0f, Math.Abs(ny - y) * CellSize - half);
                        if (gapX * gapX + gapY * gapY < radius * radius)
                        {
                            isClear = false;
                            break;
                        }
                    }
                }
                clear[cell] = isClear;
            }
            clearOfTerrain[radius] = clear;
            return clear;
        }

        // Closes every cell whose centre lies closer than distance to a point.
        private void closeCellsNear(Vector2 point, float distance)
        {
            if (distance <= 0f) return;
            int minX = (int)MathF.Max(0f, MathF.Floor((point.X - distance - Origin.X) / CellSize));
            int maxX = (int)MathF.Min(Width - 1, MathF.Floor((point.X + distance - Origin.X) / CellSize));
            int minY = (int)MathF.Max(0f, MathF.Floor((point.Y - distance - Origin.Y) / CellSize));
            int maxY = (int)MathF.Min(Height - 1, MathF.Floor((point.Y + distance - Origin.Y) / CellSize));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (Vector2.DistanceSquared(CellCenter(y * Width + x), point) < distance * distance)
                        passableForFieldOwner[y * Width + x] = false;
        }

        /// <summary>
        /// True if <see cref="FieldOwner"/> could stand in the cell: open floor, clear of walls and other units by its
        /// body radius, and not another unit's cell.
        /// </summary>
        public bool IsPassableForFieldOwner(int cell) => passableForFieldOwner[cell];

        /// <summary>
        /// True if <see cref="FieldOwner"/> could walk to the cell at any distance, given walls, gaps and the other
        /// units as they stood when the field was built. Unlike <see cref="Field"/> this ignores the field's range;
        /// it is worked out (once per field) only when first asked.
        /// </summary>
        public bool IsConnectedToFieldOwner(int cell)
        {
            if (connectivityVersion != FieldVersion)
            {
                Array.Clear(connectedToFieldOwner, 0, connectedToFieldOwner.Length);
                if (fieldStartCell >= 0)
                {
                    connectedToFieldOwner[fieldStartCell] = true;
                    fillStack.Push(fieldStartCell);
                }
                while (fillStack.Count > 0)
                {
                    int current = fillStack.Pop(), x = current % Width, y = current / Width;
                    if (x > 0) fillFrom(current - 1);
                    if (x < Width - 1) fillFrom(current + 1);
                    if (y > 0) fillFrom(current - Width);
                    if (y < Height - 1) fillFrom(current + Width);
                }
                connectivityVersion = FieldVersion;
            }
            return connectedToFieldOwner[cell];
        }

        // Diagonal steps need both shared orthogonal cells passable, so 4-connectivity is exactly what walking reaches.
        private void fillFrom(int cell)
        {
            if (connectedToFieldOwner[cell] || !passableForFieldOwner[cell]) return;
            connectedToFieldOwner[cell] = true;
            fillStack.Push(cell);
        }

        /// <summary>
        /// Builds a path for <see cref="FieldOwner"/> from its position to <paramref name="destination"/>. The path
        /// is traced from the destination down the field's gradient (-grad T) until the owner can be reached in a
        /// straight line, then waypoints that a straight walk can skip are removed. Its length therefore tracks
        /// the field value that prices the move. Descending from cell centre to cell centre is kept as a fallback,
        /// and the shorter of the two paths is returned. The result starts at the owner's position and ends exactly
        /// at the destination. Returns false (and leaves <paramref name="path"/> empty) if the destination is unreachable.
        /// </summary>
        public bool TryBuildPath(Vector2 destination, List<Vector2> path)
        {
            path.Clear();
            if (FieldOwner.IsNone || !units.IsValid(FieldOwner)) return false;
            Vector2 start = positions[FieldOwner.Index];
            if (!TryGetCell(destination, out int cell)) return false;
            if (float.IsPositiveInfinity(distanceField[cell])) return false;

            if (!traceCellChain(destination, cell, start)) return false;
            pullString(rawPath, path);
            if (traceGradient(destination, start))
            {
                pullString(rawPath, gradientPath);
                if (PathLength(gradientPath) < PathLength(path))
                {
                    path.Clear();
                    path.AddRange(gradientPath);
                }
            }
            tightenAroundCorners(path);
            return true;
        }

        // Shortest walks bend only at wall corners. Moves each interior waypoint to the nearby corner of a blocked cell
        // (nudged just off it, since a walk may not pass exactly through a corner) that most shortens the path while
        // keeping both neighbouring segments walkable, then drops waypoints a straight walk can skip. Repeats a few
        // times; the path only ever gets shorter.
        private void tightenAroundCorners(List<Vector2> path)
        {
            const int searchRadius = 2;
            float nudge = 2e-3f * CellSize;
            for (int pass = 0; pass < 4; pass++)
            {
                bool improved = false;
                for (int i = 1; i < path.Count - 1; i++)
                {
                    Vector2 previous = path[i - 1], next = path[i + 1];
                    float best = Vector2.Distance(previous, path[i]) + Vector2.Distance(path[i], next);
                    Vector2 bestPoint = path[i];
                    if (!TryGetCell(path[i], out int waypointCell)) continue;
                    int wx = waypointCell % Width, wy = waypointCell / Width;
                    for (int by = wy - searchRadius; by <= wy + searchRadius; by++)
                    {
                        for (int bx = wx - searchRadius; bx <= wx + searchRadius; bx++)
                        {
                            if (bx < 0 || by < 0 || bx >= Width || by >= Height || passableForFieldOwner[by * Width + bx]) continue;
                            Vector2 blockedCentre = CellCenter(by * Width + bx);
                            for (int corner = 0; corner < 4; corner++)
                            {
                                var offset = new Vector2(corner % 2 == 0 ? -0.5f : 0.5f, corner < 2 ? -0.5f : 0.5f);
                                Vector2 candidate = blockedCentre + offset * CellSize + Vector2.Normalize(offset) * nudge;
                                float length = Vector2.Distance(previous, candidate) + Vector2.Distance(candidate, next);
                                if (!(length < best - 1e-5f)) continue;
                                if (!TryGetCell(candidate, out int candidateCell) || !passableForFieldOwner[candidateCell]) continue;
                                if (!trace(previous, candidate, forWalking: true) || !trace(candidate, next, forWalking: true)) continue;
                                best = length;
                                bestPoint = candidate;
                            }
                        }
                    }
                    if (bestPoint == path[i]) continue;
                    path[i] = bestPoint;
                    improved = true;
                }
                if (!improved) break;
                rawPath.Clear();
                rawPath.AddRange(path);
                pullString(rawPath, path);
            }
        }

        // Fills rawPath, from the owner to the destination, by descending from cell centre to cell centre until a
        // seed cell (whose centre the owner reaches in a straight line).
        private bool traceCellChain(Vector2 destination, int cell, Vector2 start)
        {
            rawPath.Clear();
            rawPath.Add(destination);
            int guard = CellCount;
            while (!seededForFieldOwner[cell])
            {
                if (--guard < 0) return false;
                int next = nextCellTowardsSeed(cell);
                if (next < 0) return false;
                cell = next;
                rawPath.Add(CellCenter(cell));
            }
            if (rawPath.Count == 1) rawPath.Add(CellCenter(cell)); // the destination's own cell is a seed
            rawPath.Add(start);
            rawPath.Reverse();
            return true;
        }

        // Fills rawPath, from the owner to the destination, by stepping a quarter cell at a time down the field's
        // gradient from the destination until the owner can be reached in a straight line. Where a gradient step
        // would not be walkable, it takes one cell-descent step instead, which always is. Returns false if the
        // trace runs too long or gets stuck; the caller then keeps the cell-chain path.
        private bool traceGradient(Vector2 destination, Vector2 start)
        {
            rawPath.Clear();
            rawPath.Add(destination);
            float stepLength = 0.25f * CellSize;
            TryGetCell(destination, out int destinationCell);
            int guard = (int)(4f * distanceField[destinationCell] / stepLength) + 4 * (Width + Height);
            Vector2 point = destination;
            while (!trace(point, start, forWalking: true))
            {
                if (--guard < 0 || !TryGetCell(point, out int cell) || float.IsPositiveInfinity(distanceField[cell])) return false;
                Vector2 direction = descentDirection(cell);
                Vector2 next = point + direction * stepLength;
                if (direction == Vector2.Zero || !trace(point, next, forWalking: true))
                {
                    int lower = nextCellTowardsSeed(cell);
                    if (lower < 0) return false;
                    next = CellCenter(lower);
                }
                point = next;
                rawPath.Add(point);
            }
            rawPath.Add(start);
            rawPath.Reverse();
            return true;
        }

        // The unit vector down the field's upwind gradient at a cell, taken on whichever stencil gives the cell the
        // lower value (as the multi-stencil solver does): the axis stencil, or the diagonal stencil whose axes are
        // the two diagonals. On each axis of the chosen stencil, it points towards the smaller of the two neighbours
        // if that is below the cell's value, weighted by the difference. Zero at a local minimum.
        private Vector2 descentDirection(int cell)
        {
            int x = cell % Width, y = cell / Width;
            float value = distanceField[cell];
            float left = fieldValueAt(x - 1, y), right = fieldValueAt(x + 1, y);
            float down = fieldValueAt(x, y - 1), up = fieldValueAt(x, y + 1);
            float northEast = diagonalValueAt(x, y, 1, 1), southWest = diagonalValueAt(x, y, -1, -1);
            float northWest = diagonalValueAt(x, y, -1, 1), southEast = diagonalValueAt(x, y, 1, -1);

            float axisValue = EikonalDistanceFieldSolver.Update(MathF.Min(left, right), MathF.Min(down, up), CellSize);
            float diagonalValue = EikonalDistanceFieldSolver.Update(MathF.Min(northEast, southWest), MathF.Min(northWest, southEast),
                                                                    1.41421356f * CellSize);
            Vector2 direction;
            if (diagonalValue < axisValue)
            {
                // Along u = (1, 1) / sqrt(2) and v = (-1, 1) / sqrt(2), then back to world axes.
                float gu = downhill(southWest, northEast, value), gv = downhill(southEast, northWest, value);
                direction = gu * new Vector2(1f, 1f) + gv * new Vector2(-1f, 1f);
            }
            else
            {
                direction = new Vector2(downhill(left, right, value), downhill(down, up, value));
            }
            return direction.LengthSquared() > 1e-12f ? Vector2.Normalize(direction) : Vector2.Zero;
        }

        // Signed step along one stencil axis towards the smaller of its two neighbours (negative side first), if that
        // neighbour is below the cell's value; zero otherwise.
        private static float downhill(float negativeSide, float positiveSide, float value)
        {
            if (!(MathF.Min(negativeSide, positiveSide) < value)) return 0f;
            return negativeSide <= positiveSide ? negativeSide - value : value - positiveSide;
        }

        // The value of the diagonal neighbour at (x + dx, y + dy), or infinity if the step there would cut a wall corner.
        private float diagonalValueAt(int x, int y, int dx, int dy)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) return float.PositiveInfinity;
            if (!GridSteps.CanStepDiagonally(passableForFieldOwner, Width, x, y, nx, ny)) return float.PositiveInfinity;
            return fieldValueAt(nx, ny);
        }

        private float fieldValueAt(int x, int y) =>
            x < 0 || y < 0 || x >= Width || y >= Height || !passableForFieldOwner[y * Width + x]
                ? float.PositiveInfinity
                : distanceField[y * Width + x];

        // String pulling: from each kept point of the raw path, jump to the farthest point reachable in a straight line.
        private void pullString(List<Vector2> raw, List<Vector2> path)
        {
            path.Clear();
            path.Add(raw[0]);
            int from = 0;
            while (from < raw.Count - 1)
            {
                int to = raw.Count - 1;
                while (to > from + 1 && !trace(raw[from], raw[to], forWalking: true)) to--;
                path.Add(raw[to]);
                from = to;
            }
        }

        /// <summary>
        /// The neighbour to step to when tracing back towards the seeds: among neighbours with a strictly
        /// smaller field value, the one minimising value + step length. For a Dijkstra field this is a
        /// shortest-path predecessor, so the traced chain, plus the straight walk from its seed cell's centre
        /// to the owner, is exactly as long as the field value.
        /// </summary>
        private int nextCellTowardsSeed(int cell)
        {
            int x = cell % Width, y = cell / Width;
            int best = -1;
            float bestScore = float.PositiveInfinity;
            float current = distanceField[cell];
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                    int neighbour = ny * Width + nx;
                    if (!passableForFieldOwner[neighbour] || !(distanceField[neighbour] < current)) continue;
                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal && !GridSteps.CanStepDiagonally(passableForFieldOwner, Width, x, y, nx, ny)) continue;
                    float score = distanceField[neighbour] + (diagonal ? 1.41421356f : 1f) * CellSize;
                    if (score < bestScore) { bestScore = score; best = neighbour; }
                }
            }
            return best;
        }

        /// <summary>Total length of a path in metres.</summary>
        public static float PathLength(List<Vector2> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
            return length;
        }

        // ---------------------------------------------------------------- zones of control

        /// <summary>
        /// True if the unit occupies space, projects its zones, and has a base zone or a temporary zone (overwatch).
        /// </summary>
        public bool HasActiveZone(UnitId owner)
        {
            units.Require(owner);
            return projects(owner) && (zones[owner.Index].Radius > 0f || hasTemporaryZone[owner.Index]);
        }

        /// <summary>
        /// True if <paramref name="position"/> lies inside one of the owner's active zones and, for a zone that
        /// requires line of sight, the owner can see it.
        /// </summary>
        public bool IsInsideZone(UnitId owner, Vector2 position)
        {
            units.Require(owner);
            for (int k = 0; k < 2; k++)
            {
                if (!tryGetActiveZone(owner, k, out ZoneShape zone)) continue;
                if (zone.Contains(position) && (!zone.RequiresLineOfSight || HasLineOfSight(zone.Center, position))) return true;
            }
            return false;
        }

        /// <summary>The current shape of a unit's base zone of control, which follows its facing.</summary>
        public ZoneShape GetZone(UnitId owner)
        {
            units.Require(owner);
            return new ZoneShape(positions[owner.Index], facings[owner.Index], zones[owner.Index]);
        }

        /// <summary>
        /// Gives a unit a temporary zone (overwatch) that faces <paramref name="facing"/> regardless of where the
        /// unit later turns, replacing any it had. It lasts until <see cref="ClearTemporaryZone"/>.
        /// </summary>
        public void PlaceTemporaryZone(UnitId owner, in ZoneSetup zone, Vector2 facing)
        {
            units.Require(owner);
            temporaryZones[owner.Index] = zone;
            temporaryFacings[owner.Index] = facing.LengthSquared() > 1e-12f ? Vector2.Normalize(facing) : facings[owner.Index];
            hasTemporaryZone[owner.Index] = true;
        }

        /// <summary>Removes a unit's temporary zone; returns false if it had none.</summary>
        public bool ClearTemporaryZone(UnitId owner)
        {
            units.Require(owner);
            bool had = hasTemporaryZone[owner.Index];
            hasTemporaryZone[owner.Index] = false;
            return had;
        }

        /// <summary>True if the unit has a temporary zone (it may still be inactive while the unit is downed).</summary>
        public bool HasTemporaryZone(UnitId owner) { units.Require(owner); return hasTemporaryZone[owner.Index]; }

        /// <summary>The current shape of a unit's temporary zone (meaningful only if <see cref="HasTemporaryZone"/>).</summary>
        public ZoneShape GetTemporaryZone(UnitId owner)
        {
            units.Require(owner);
            return new ZoneShape(positions[owner.Index], temporaryFacings[owner.Index], temporaryZones[owner.Index]);
        }

        /// <summary>True if <paramref name="position"/> lies inside an active zone of a unit hostile to <paramref name="unit"/>.</summary>
        public bool IsInsideHostileZone(UnitId unit, Vector2 position)
        {
            for (int slot = 0; slot < units.Capacity; slot++)
            {
                UnitId owner = units.GetIdAtSlot(slot);
                if (owner.IsNone || !units.AreHostile(owner, unit)) continue;
                for (int k = 0; k < 2; k++)
                    if (tryGetActiveZone(owner, k, out ZoneShape zone) && zone.Contains(position)) return true;
            }
            return false;
        }

        /// <summary>
        /// Finds, for each hostile unit with an active zone, the first point where <paramref name="path"/>
        /// enters one of its zones (the earlier of its base and temporary zones), and appends them to
        /// <paramref name="entries"/> sorted by distance along the path. Zones that already contain the
        /// path's start are skipped. For zones that require line of sight, the entry is the first point
        /// inside the zone (sampled every 0.25 m) that the owner can see. For other zones it is where the path
        /// crosses the zone's edge, and only if the owner can see that point: a reaction needs the owner to see the
        /// mover, so an unseen entry is no threat and is not reported.
        /// </summary>
        public void FindZoneEntries(UnitId mover, List<Vector2> path, List<ZoneEntry> entries)
        {
            entries.Clear();
            if (path.Count < 2) return;

            for (int slot = 0; slot < units.Capacity; slot++)
            {
                UnitId owner = units.GetIdAtSlot(slot);
                if (owner.IsNone || !units.AreHostile(owner, mover)) continue;
                bool found = false;
                ZoneEntry earliest = default;
                for (int k = 0; k < 2; k++)
                {
                    if (!tryGetActiveZone(owner, k, out ZoneShape zone) || zone.Contains(path[0])) continue;
                    if (!tryFindPathEntry(zone, path, out ZoneEntry entry)) continue;
                    if (found && entry.PathDistance >= earliest.PathDistance) continue;
                    earliest = entry;
                    found = true;
                }
                if (!found) continue;
                earliest.Owner = owner;
                entries.Add(earliest);
            }
            entries.Sort((a, b) => a.PathDistance != b.PathDistance
                ? a.PathDistance.CompareTo(b.PathDistance)
                : a.Owner.Index.CompareTo(b.Owner.Index));
        }

        private bool projects(UnitId owner) => occupiesSpace[owner.Index] && projectsZone[owner.Index];

        /// <summary>Zone 0 is the base zone, zone 1 the temporary zone; false if that zone is not in force.</summary>
        private bool tryGetActiveZone(UnitId owner, int which, out ZoneShape zone)
        {
            zone = default;
            if (!projects(owner)) return false;
            if (which == 0)
            {
                if (zones[owner.Index].Radius <= 0f) return false;
                zone = GetZone(owner);
                return true;
            }
            if (!hasTemporaryZone[owner.Index] || temporaryZones[owner.Index].Radius <= 0f) return false;
            zone = GetTemporaryZone(owner);
            return true;
        }

        private bool tryFindPathEntry(in ZoneShape zone, List<Vector2> path, out ZoneEntry entry)
        {
            entry = default;
            float distanceBefore = 0f;
            for (int k = 0; k + 1 < path.Count; k++)
            {
                Vector2 a = path[k], b = path[k + 1];
                float segmentLength = Vector2.Distance(a, b);
                if (zone.TryFindEntry(a, b, out float t))
                {
                    float entryDistance = distanceBefore + t * segmentLength;
                    if (HasLineOfSight(zone.Center, a + t * (b - a)))
                    {
                        entry = new ZoneEntry { Position = a + t * (b - a), PathDistance = entryDistance, SegmentIndex = k };
                        return true;
                    }
                    // Unseen: a sight cone may still catch the mover further in; any other zone never will.
                    return zone.RequiresLineOfSight && tryFindVisibleSample(zone, path, entryDistance, out entry);
                }
                distanceBefore += segmentLength;
            }
            return false;
        }

        private bool tryFindVisibleSample(in ZoneShape zone, List<Vector2> path, float startDistance, out ZoneEntry entry)
        {
            entry = default;
            float total = PathLength(path);
            for (float d = startDistance + pathSampleStep; d <= total + 1e-4f; d += pathSampleStep)
            {
                Vector2 p = PointAlongPath(path, MathF.Min(d, total), out int segment);
                if (zone.Contains(p) && HasLineOfSight(zone.Center, p))
                {
                    entry = new ZoneEntry { Position = p, PathDistance = MathF.Min(d, total), SegmentIndex = segment };
                    return true;
                }
            }
            return false;
        }

        /// <summary>The point at a given distance along a path, and the index of the segment it lies on.</summary>
        public static Vector2 PointAlongPath(List<Vector2> path, float distance, out int segment)
        {
            for (int k = 0; k + 1 < path.Count; k++)
            {
                float length = Vector2.Distance(path[k], path[k + 1]);
                if (distance <= length || k + 2 == path.Count)
                {
                    segment = k;
                    float t = length > 1e-9f ? MathF.Min(distance / length, 1f) : 0f;
                    return Vector2.Lerp(path[k], path[k + 1], t);
                }
                distance -= length;
            }
            segment = 0;
            return path[0];
        }

        // ---------------------------------------------------------------- grid traversal

        /// <summary>
        /// Walks the cells crossed by the segment a-b (Amanatides and Woo, 1987). For sight, fails on a cell
        /// that blocks sight other than the two end cells, or at a corner squeezed between two such cells. For walking, fails on any cell the field owner
        /// cannot stand in, including both cells at an exact corner crossing.
        /// </summary>
        private bool trace(Vector2 a, Vector2 b, bool forWalking)
        {
            float ax = (a.X - Origin.X) / CellSize, ay = (a.Y - Origin.Y) / CellSize;
            float bx = (b.X - Origin.X) / CellSize, by = (b.Y - Origin.Y) / CellSize;
            int x = (int)MathF.Floor(ax), y = (int)MathF.Floor(ay);
            int endX = (int)MathF.Floor(bx), endY = (int)MathF.Floor(by);
            int startX = x, startY = y;

            float dx = bx - ax, dy = by - ay;
            int stepX = Math.Sign(dx), stepY = Math.Sign(dy);
            float tDeltaX = stepX != 0 ? 1f / MathF.Abs(dx) : float.PositiveInfinity;
            float tDeltaY = stepY != 0 ? 1f / MathF.Abs(dy) : float.PositiveInfinity;
            float tMaxX = stepX > 0 ? (x + 1 - ax) * tDeltaX : stepX < 0 ? (ax - x) * tDeltaX : float.PositiveInfinity;
            float tMaxY = stepY > 0 ? (y + 1 - ay) * tDeltaY : stepY < 0 ? (ay - y) * tDeltaY : float.PositiveInfinity;

            int guard = Math.Abs(endX - x) + Math.Abs(endY - y) + 2;
            while (true)
            {
                bool isEndpoint = (x == startX && y == startY) || (x == endX && y == endY);
                if (forWalking ? !walkableCell(x, y) : (!isEndpoint && blocksSight(x, y))) return false;
                if ((x == endX && y == endY) || --guard < 0) return true;

                if (MathF.Abs(tMaxX - tMaxY) < 1e-6f && stepX != 0 && stepY != 0)
                {
                    if (forWalking && (!walkableCell(x + stepX, y) || !walkableCell(x, y + stepY))) return false;
                    if (!forWalking && blocksSight(x + stepX, y) && blocksSight(x, y + stepY)) return false;
                    x += stepX; y += stepY;
                    tMaxX += tDeltaX; tMaxY += tDeltaY;
                    guard--;
                }
                else if (tMaxX < tMaxY) { x += stepX; tMaxX += tDeltaX; }
                else { y += stepY; tMaxY += tDeltaY; }
            }
        }

        private bool walkableCell(int x, int y) =>
            x >= 0 && y >= 0 && x < Width && y < Height && passableForFieldOwner[y * Width + x];

        private bool blocksSight(int x, int y) =>
            x < 0 || y < 0 || x >= Width || y >= Height || obstacles[y * Width + x].BlocksSight;
    }
}
