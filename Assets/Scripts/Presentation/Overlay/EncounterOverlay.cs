// Written by Claude (Anthropic), October 2026; soft zone coverage and the capped field added after the first
// playtest, and soft terrain and body masks, the extended field and sight-clipped zones after the second; to be
// reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using Game.Rules;
using Unity.Profiling;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws the movement locus and the zones of control on the floor while the player can act. Once per change (not
    /// per frame) it copies what the rules produce into two textures with one texel per map cell.
    /// <list type="bullet">
    /// <item>Field texture: the active unit's distance field (R), how clear of walls each cell is for the unit's body
    /// (G), and how far it lies inside another unit's body (B). Cells the unit cannot stand in take the distance of
    /// their passable neighbours plus the step to them, so the field keeps rising into wall clearance and carries on
    /// under other units, which are drawn on top as blocked discs.</item>
    /// <item>Zone texture: how far each cell lies inside a hostile zone whose owner can still react (R) or already has
    /// (G), from each zone's signed distance (a disc, or a sector for cones) and clipped by the owner's line of sight,
    /// since a reaction needs the owner to see the mover.</item>
    /// </list>
    /// Every soft value is 0.5 plus a signed distance over two cells, so its filtered half level traces the true edge
    /// and is crossed exactly where the rules' own cell test changes. A quad over the map draws both with the
    /// <see cref="ShaderName"/> shader: fills for one and two points, rings at both thresholds, a pulse running outward
    /// along the field, other units' discs, and tinted zones.
    /// </summary>
    [RequireComponent(typeof(EncounterInput))]
    public sealed class EncounterOverlay : MonoBehaviour
    {
        /// <summary>Name of the overlay shader, used when <see cref="Shader"/> is left empty.</summary>
        public const string ShaderName = "Auto Buster/Encounter Overlay";

        /// <summary>The overlay shader; Set Up Scene assigns it, so builds include it.</summary>
        [SerializeField] private Shader shader;
        /// <summary>Ground a move costing one point reaches.</summary>
        [SerializeField] private Color onePointColour = new Color(0.3f, 0.8f, 1f);
        /// <summary>Ground a move costing two points reaches.</summary>
        [SerializeField] private Color twoPointColour = new Color(1f, 0.8f, 0.2f);
        /// <summary>Hostile zones of control.</summary>
        [SerializeField] private Color zoneColour = new Color(1f, 0.1f, 0.6f);
        /// <summary>Ground within reach that another unit's body blocks; its alpha is the fill's opacity.</summary>
        [SerializeField] private Color occupiedColour = new Color(1f, 0.25f, 0.2f, 0.35f);
        /// <summary>How strongly zones whose owners have already reacted this round are drawn, relative to live ones.</summary>
        [SerializeField, Range(0f, 1f)] private float spentZoneStrength = 0.4f;
        /// <summary>Opacity of the locus fill.</summary>
        [SerializeField, Range(0f, 1f)] private float fillOpacity = 0.12f;
        /// <summary>Width of the rings and zone edges, in pixels.</summary>
        [SerializeField, Min(0.1f)] private float lineWidth = 1.5f;
        /// <summary>Speed of the outward pulse, in metres per second.</summary>
        [SerializeField, Min(0f)] private float pulseSpeed = 3f;
        /// <summary>Distance between pulse bands, in metres.</summary>
        [SerializeField, Min(0.1f)] private float pulseSpacing = 2f;
        /// <summary>Height of the overlay above the floor, in metres.</summary>
        [SerializeField, Min(0f)] private float heightAboveFloor = 0.02f;

        private const float unreachable = 1000f; // the most a cell stores; infinity would not filter well
        private const int extensionLayers = 6;   // how far, in cells, the field is carried into cells the unit cannot use
        private static readonly ProfilerMarker refreshMarker = new ProfilerMarker("Encounter.Overlay");
        private static readonly int fieldTexId = Shader.PropertyToID("_FieldTex");
        private static readonly int zoneTexId = Shader.PropertyToID("_ZoneTex");
        private static readonly int occupiedColourId = Shader.PropertyToID("_OccupiedColour");
        private static readonly int mapOriginId = Shader.PropertyToID("_MapOrigin");
        private static readonly int mapSizeId = Shader.PropertyToID("_MapSize");
        private static readonly int budgetId = Shader.PropertyToID("_Budget");
        private static readonly int reachId = Shader.PropertyToID("_Reach");
        private static readonly int oneColourId = Shader.PropertyToID("_OneColour");
        private static readonly int twoColourId = Shader.PropertyToID("_TwoColour");
        private static readonly int zoneColourId = Shader.PropertyToID("_ZoneColour");
        private static readonly int spentStrengthId = Shader.PropertyToID("_SpentStrength");
        private static readonly int fillOpacityId = Shader.PropertyToID("_FillOpacity");
        private static readonly int lineWidthId = Shader.PropertyToID("_LineWidth");
        private static readonly int pulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int pulseSpacingId = Shader.PropertyToID("_PulseSpacing");

        private EncounterInput input;
        private MapGridAsset builtFor;
        private float[] values = new float[0];
        private float[] zoneValues = new float[0];
        private ushort[] texels = new ushort[0];
        private ushort[] zoneTexels = new ushort[0];
        private bool[] known = new bool[0], queued = new bool[0];
        private readonly List<int> frontier = new List<int>(), nextFrontier = new List<int>();
        private readonly Dictionary<float, float[]> terrainByRadius = new Dictionary<float, float[]>();
        private Texture2D data, zoneData;
        private Material material;
        private Mesh quadMesh;
        private MeshRenderer quad;
        private bool warnedAboutShader;
        private int seenFieldVersion = -1, seenEvents = -1;
        private Encounter seenEncounter;

        /// <summary>The overlay shader.</summary>
        public Shader Shader { get => shader; set => shader = value; }

        /// <summary>True while the overlay is drawn.</summary>
        public bool IsShown => quad != null && quad.enabled;

        /// <summary>
        /// The field data for map cell (x, y), as last refreshed: the distance (r; capped just past the field's range
        /// where it does not reach), clearance from walls for the unit's body (g, at least 0.5 where it is clear), and
        /// inside another unit's body (b, above 0.5 where that body closes the cell).
        /// </summary>
        public Color FieldAt(int x, int y)
        {
            int i = 4 * (y * builtFor.Width + x);
            return new Color(values[i], values[i + 1], values[i + 2], values[i + 3]);
        }

        /// <summary>
        /// The zone data for map cell (x, y), as last refreshed: inside a hostile zone whose owner can still react (x)
        /// or one whose owner already has (y), each at least 0.5 where the zone covers the cell centre and its owner
        /// sees it.
        /// </summary>
        public Vector2 ZonesAt(int x, int y)
        {
            int i = 2 * (y * builtFor.Width + x);
            return new Vector2(zoneValues[i], zoneValues[i + 1]);
        }

        /// <summary>
        /// Shows or hides the overlay for the current state and, if the field or anything else has changed since the
        /// last time, refills and uploads its data. Called every frame; tests call it directly.
        /// </summary>
        public void Refresh()
        {
            if (input == null) input = GetComponent<EncounterInput>();
            TurnController controller = input.Controller;
            EncounterRoot root = input.Root;
            if (controller == null || root == null || root.Map == null || !controller.CanAct)
            {
                setShown(false);
                return;
            }

            Encounter encounter = root.Encounter;
            prepare(root.Map);
            if (encounter != seenEncounter || encounter.Spatial.FieldVersion != seenFieldVersion || encounter.Events.Count != seenEvents)
            {
                using (refreshMarker.Auto()) fill(encounter);
                seenEncounter = encounter;
                seenFieldVersion = encounter.Spatial.FieldVersion;
                seenEvents = encounter.Events.Count;
            }
            if (material == null)
            {
                setShown(false);
                return;
            }
            UnitId active = encounter.Flow.ActiveUnit;
            float budget = encounter.Movement.GetBudget(active);
            bool canMove = encounter.Spatial.FieldOwner == active && !encounter.Status.IsSuppressed(active);
            float reach = canMove ? budget * Mathf.Min(encounter.Flow.ActiveUnitActionPoints, 2) : 0f;
            material.SetFloat(budgetId, budget);
            material.SetFloat(reachId, reach);
            applySettings();
            setShown(true);
        }

        private void LateUpdate() => Refresh();

        private void OnDestroy()
        {
            if (data != null) PileView.DestroyImmediateOrLater(data);
            if (zoneData != null) PileView.DestroyImmediateOrLater(zoneData);
            if (material != null) PileView.DestroyImmediateOrLater(material);
            if (quadMesh != null) PileView.DestroyImmediateOrLater(quadMesh);
        }

        private void setShown(bool shown)
        {
            if (quad != null) quad.enabled = shown;
        }

        // Sizes the data for the map and, once the shader is found, builds the quad and its material.
        private void prepare(MapGridAsset map)
        {
            if (builtFor != map)
            {
                builtFor = map;
                values = new float[4 * map.CellCount];
                zoneValues = new float[2 * map.CellCount];
                texels = new ushort[values.Length];
                zoneTexels = new ushort[zoneValues.Length];
                known = new bool[map.CellCount];
                queued = new bool[map.CellCount];
                terrainByRadius.Clear();
                if (data != null) PileView.DestroyImmediateOrLater(data);
                if (zoneData != null) PileView.DestroyImmediateOrLater(zoneData);
                data = dataTexture(map, TextureFormat.RGBAHalf, "Encounter overlay field");
                zoneData = dataTexture(map, TextureFormat.RGHalf, "Encounter overlay zones");
                if (quad != null) buildQuad(map);
                seenEncounter = null;
            }
            if (material != null) return;
            Shader overlayShader = shader != null ? shader : Shader.Find(ShaderName);
            if (overlayShader == null)
            {
                if (!warnedAboutShader) Debug.LogWarning($"[Encounter overlay] Shader '{ShaderName}' not found; run Tools ▸ Encounter ▸ Set Up Scene.", this);
                warnedAboutShader = true;
                return;
            }
            material = new Material(overlayShader) { name = "Encounter overlay" };
            var holder = new GameObject("Encounter overlay");
            holder.transform.SetParent(transform, false);
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.AddComponent<MeshFilter>();
            quad = holder.AddComponent<MeshRenderer>();
            quad.sharedMaterial = material;
            quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.receiveShadows = false;
            buildQuad(map);
        }

        private static Texture2D dataTexture(MapGridAsset map, TextureFormat format, string name) =>
            new Texture2D(map.Width, map.Height, format, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

        private void buildQuad(MapGridAsset map)
        {
            if (quadMesh == null) quadMesh = new Mesh { name = "Encounter overlay" };
            float x0 = map.Origin.x, z0 = map.Origin.y, x1 = x0 + map.Width * map.CellSize, z1 = z0 + map.Height * map.CellSize;
            float y = map.FloorHeight + heightAboveFloor;
            quadMesh.Clear();
            quadMesh.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };
            quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quadMesh.RecalculateBounds();
            quad.GetComponent<MeshFilter>().sharedMesh = quadMesh;
            material.SetTexture(fieldTexId, data);
            material.SetTexture(zoneTexId, zoneData);
            material.SetVector(mapOriginId, new Vector4(x0, z0, 0f, 0f));
            material.SetVector(mapSizeId, new Vector4(x1 - x0, z1 - z0, 0f, 0f));
        }

        private void applySettings()
        {
            material.SetColor(oneColourId, onePointColour);
            material.SetColor(twoColourId, twoPointColour);
            material.SetColor(zoneColourId, zoneColour);
            material.SetColor(occupiedColourId, occupiedColour);
            material.SetFloat(spentStrengthId, spentZoneStrength);
            material.SetFloat(fillOpacityId, fillOpacity);
            material.SetFloat(lineWidthId, lineWidth);
            material.SetFloat(pulseSpeedId, pulseSpeed);
            material.SetFloat(pulseSpacingId, pulseSpacing);
        }

        private void fill(Encounter encounter)
        {
            SpatialSystem spatial = encounter.Spatial;
            UnitId active = encounter.Flow.ActiveUnit;
            int cells = spatial.CellCount;
            bool ownsField = spatial.FieldOwner == active;
            ReadOnlySpan<float> field = spatial.Field;
            float softness = 2f * spatial.CellSize;
            // Cells the field does not reach store a value just past its range rather than a huge one, so the data has
            // no big step at the window's edge.
            float cap = Mathf.Min(unreachable, spatial.FieldRange + 2f * spatial.CellSize);
            float radius = spatial.GetBodyRadius(active);

            float[] terrain = terrainFor(spatial, radius);
            for (int cell = 0; cell < cells; cell++)
            {
                bool passable = ownsField && spatial.IsPassableForFieldOwner(cell);
                values[4 * cell] = passable && field[cell] < cap ? field[cell] : cap;
                values[4 * cell + 1] = terrain[cell];
                values[4 * cell + 2] = 0f;
                values[4 * cell + 3] = 0f;
                known[cell] = passable;
                zoneValues[2 * cell] = 0f;
                zoneValues[2 * cell + 1] = 0f;
            }
            // The unit can always walk out of its own cell, even inside its clearance.
            if (spatial.TryGetCell(spatial.GetPosition(active), out int own) && spatial.IsOpenFloor(own)) values[4 * own + 1] = 1f;

            // Other units' bodies, as the rules close cells: centres closer than the two radii together, and their own cell.
            UnitRegistry units = encounter.Units;
            for (int slot = 0; slot < units.Capacity; slot++)
            {
                UnitId other = units.GetIdAtSlot(slot);
                if (other.IsNone || other == active || !spatial.OccupiesSpace(other)) continue;
                paintBody(spatial, spatial.GetPosition(other), radius + spatial.GetBodyRadius(other), softness);
            }

            if (ownsField) extendField(spatial, cap);

            for (int slot = 0; slot < units.Capacity; slot++)
            {
                UnitId owner = units.GetIdAtSlot(slot);
                if (owner.IsNone || !units.AreHostile(owner, active) || !spatial.HasActiveZone(owner)) continue;
                int channel = encounter.Flow.HasReaction(owner) ? 0 : 1;
                ZoneShape baseZone = spatial.GetZone(owner);
                if (baseZone.Radius > 0f) paintZone(spatial, baseZone, channel, softness);
                if (spatial.HasTemporaryZone(owner)) paintZone(spatial, spatial.GetTemporaryZone(owner), channel, softness);
            }

            for (int i = 0; i < values.Length; i++) texels[i] = Mathf.FloatToHalf(values[i]);
            for (int i = 0; i < zoneValues.Length; i++) zoneTexels[i] = Mathf.FloatToHalf(zoneValues[i]);
            data.SetPixelData(texels, 0);
            data.Apply(false, false);
            zoneData.SetPixelData(zoneTexels, 0);
            zoneData.Apply(false, false);
        }

        // Per body radius, once per map: 0.5 + (gap - radius) / (2 cells) for open floor, where gap is the distance
        // from the cell centre to the nearest square that is not open floor (space off the map does not count), and 0
        // elsewhere. At least 0.5 exactly where the rules count the cell as clear of terrain (gap >= radius).
        private float[] terrainFor(SpatialSystem spatial, float radius)
        {
            if (terrainByRadius.TryGetValue(radius, out float[] cached)) return cached;
            float cellSize = spatial.CellSize, half = 0.5f * cellSize, softness = 2f * cellSize;
            int reach = Mathf.CeilToInt((radius + softness) / cellSize);
            var terrain = new float[spatial.CellCount];
            for (int cell = 0; cell < terrain.Length; cell++)
            {
                if (!spatial.IsOpenFloor(cell)) continue;
                int x = cell % spatial.Width, y = cell / spatial.Width;
                float gapSquared = float.PositiveInfinity;
                for (int ny = Math.Max(0, y - reach); ny <= Math.Min(spatial.Height - 1, y + reach); ny++)
                    for (int nx = Math.Max(0, x - reach); nx <= Math.Min(spatial.Width - 1, x + reach); nx++)
                    {
                        if (spatial.IsOpenFloor(ny * spatial.Width + nx)) continue;
                        float gapX = Mathf.Max(0f, Math.Abs(nx - x) * cellSize - half);
                        float gapY = Mathf.Max(0f, Math.Abs(ny - y) * cellSize - half);
                        gapSquared = Mathf.Min(gapSquared, gapX * gapX + gapY * gapY);
                    }
                terrain[cell] = float.IsPositiveInfinity(gapSquared) ? 1f : Mathf.Clamp01(0.5f + (Mathf.Sqrt(gapSquared) - radius) / softness);
            }
            terrainByRadius[radius] = terrain;
            return terrain;
        }

        // Raises the body channel to 0.5 - (distance - reach) / softness around a unit, and to 1 in its own cell, so it
        // is above 0.5 exactly where the rules close a cell for that body.
        private void paintBody(SpatialSystem spatial, System.Numerics.Vector2 centre, float reach, float softness)
        {
            float extent = reach + softness;
            forCellsAround(spatial, centre, extent, cell =>
            {
                float coverage = Mathf.Clamp01(0.5f - (System.Numerics.Vector2.Distance(spatial.CellCenter(cell), centre) - reach) / softness);
                values[4 * cell + 2] = Mathf.Max(values[4 * cell + 2], coverage);
            });
            if (spatial.TryGetCell(centre, out int occupied)) values[4 * occupied + 2] = 1f;
        }

        // Carries the field into the cells the unit cannot stand in, up to a few cells out: each takes the smallest
        // value of a neighbour already known plus the step to it, so contours run on into wall clearance (and stop at
        // the wall) instead of sliding along it, and continue under other units' bodies.
        private void extendField(SpatialSystem spatial, float cap)
        {
            int width = spatial.Width, height = spatial.Height;
            float straight = spatial.CellSize, diagonal = 1.41421356f * spatial.CellSize;
            frontier.Clear();
            for (int cell = 0; cell < known.Length; cell++)
                if (!known[cell] && hasKnownNeighbour(cell, width, height)) frontier.Add(cell);
            for (int layer = 0; layer < extensionLayers && frontier.Count > 0; layer++)
            {
                foreach (int cell in frontier)
                {
                    int x = cell % width, y = cell / width;
                    float best = cap;
                    for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                        for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                        {
                            int neighbour = ny * width + nx;
                            if (neighbour == cell || !known[neighbour]) continue;
                            best = Mathf.Min(best, values[4 * neighbour] + (nx != x && ny != y ? diagonal : straight));
                        }
                    values[4 * cell] = best;
                }
                nextFrontier.Clear();
                foreach (int cell in frontier) known[cell] = true;
                foreach (int cell in frontier)
                {
                    int x = cell % width, y = cell / width;
                    for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                        for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                        {
                            int neighbour = ny * width + nx;
                            if (known[neighbour] || queued[neighbour]) continue;
                            queued[neighbour] = true;
                            nextFrontier.Add(neighbour);
                        }
                }
                foreach (int cell in nextFrontier) queued[cell] = false;
                frontier.Clear();
                frontier.AddRange(nextFrontier);
            }
        }

        private bool hasKnownNeighbour(int cell, int width, int height)
        {
            int x = cell % width, y = cell / width;
            for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                    if (known[ny * width + nx]) return true;
            return false;
        }

        private static void forCellsAround(SpatialSystem spatial, System.Numerics.Vector2 centre, float extent, Action<int> visit)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt((centre.X - extent - spatial.Origin.X) / spatial.CellSize));
            int maxX = Mathf.Min(spatial.Width - 1, Mathf.FloorToInt((centre.X + extent - spatial.Origin.X) / spatial.CellSize));
            int minY = Mathf.Max(0, Mathf.FloorToInt((centre.Y - extent - spatial.Origin.Y) / spatial.CellSize));
            int maxY = Mathf.Min(spatial.Height - 1, Mathf.FloorToInt((centre.Y + extent - spatial.Origin.Y) / spatial.CellSize));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    visit(y * spatial.Width + x);
        }

        // Raises a zone channel to the zone's soft coverage, 0.5 + (signed distance inside) / softness, at every cell
        // centre near the zone that its owner can see: a reaction needs the owner to see the mover, so ground hidden
        // behind a wall is never threatened.
        private void paintZone(SpatialSystem spatial, ZoneShape zone, int channel, float softness)
        {
            forCellsAround(spatial, zone.Center, zone.Radius + softness, cell =>
            {
                System.Numerics.Vector2 centre = spatial.CellCenter(cell);
                float coverage = Mathf.Clamp01(0.5f + signedDistanceInside(zone, centre) / softness);
                if (coverage <= zoneValues[2 * cell + channel]) return;
                if (!spatial.HasLineOfSight(zone.Center, centre)) return;
                zoneValues[2 * cell + channel] = coverage;
            });
        }

        // Distance from the point to the zone's edge, positive inside: a disc, or a sector about the zone's facing
        // (the negated two-dimensional "pie" signed distance of Inigo Quilez).
        private static float signedDistanceInside(in ZoneShape zone, System.Numerics.Vector2 point)
        {
            System.Numerics.Vector2 offset = point - zone.Center;
            if (zone.IsFullCircle) return zone.Radius - offset.Length();
            // In a frame with the facing along +y, folded onto the positive side since the sector is symmetric.
            float across = Mathf.Abs(offset.X * zone.Facing.Y - offset.Y * zone.Facing.X);
            float along = offset.X * zone.Facing.X + offset.Y * zone.Facing.Y;
            float sin = Mathf.Sin(zone.HalfAngleRadians), cos = Mathf.Cos(zone.HalfAngleRadians);
            float outsideDisc = Mathf.Sqrt(across * across + along * along) - zone.Radius;
            float onEdge = Mathf.Clamp(across * sin + along * cos, 0f, zone.Radius);
            float edgeX = across - sin * onEdge, edgeY = along - cos * onEdge;
            float toEdge = Mathf.Sqrt(edgeX * edgeX + edgeY * edgeY) * Mathf.Sign(cos * across - sin * along);
            return -Mathf.Max(outsideDisc, toEdge);
        }
    }
}
