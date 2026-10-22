// Written by Alexander Stangl. Fixed multiple seeds, buffer reuse between solves and documentation
// comments by Claude (Anthropic), October 2026; multi-stencil update and distance limit by Claude
// (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;

using Game.Utility;

namespace Game.Rules
{
    /// <summary>
    /// Solves the eikonal equation |grad T| = 1 by the fast marching method (Sethian, 1996) with a
    /// multi-stencil first-order upwind update (Qi, 2022, section 3): each cell takes the smaller of the
    /// update on the axis-aligned stencil (orthogonal neighbours, spacing h) and the update on the diagonal
    /// stencil (diagonal neighbours, spacing h * sqrt(2)).
    /// </summary>
    /// <remarks>
    /// The diagonal stencil removes most of the axis stencil's error along diagonals, which is largest near
    /// point sources such as the wall corners a field bends around. A diagonal neighbour counts only when
    /// both orthogonal neighbours it shares with the cell are passable, so the field never cuts a wall corner
    /// or slips between two diagonally touching obstacles. The scheme is first order, which is why callers
    /// should seed every cell whose distance they know exactly (see <see cref="SpatialSystem.RebuildField"/>).
    /// The march stops once the next cell lies beyond the distance limit. Working buffers are kept between
    /// calls, so one instance must not be used by two threads at once.
    /// </remarks>
    public sealed class EikonalDistanceFieldSolver : IDistanceFieldSolver
    {
        // Orthogonal neighbours first, then diagonals.
        private static readonly int[] neighbourDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] neighbourDy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float sqrt2 = MathF.Sqrt(2f);

        // Reused between solves; reallocated only when the cell count changes.
        private bool[] alive = Array.Empty<bool>();
        private bool[] seeded = Array.Empty<bool>();
        private IndexedMinHeap narrowBand = new IndexedMinHeap(0);

        /// <inheritdoc/>
        public bool Solve(int width, int height, float cellSize, bool[] passable, ReadOnlySpan<DistanceSeed> seeds,
                          float maxDistance, float[] distances)
        {
            int cellCount = width * height;
            if (passable.Length != cellCount || distances.Length != cellCount)
                throw new ArgumentException("passable and distances must have width * height elements.");
            DistanceSeed.Validate(seeds, cellCount);
            DistanceLimit.Validate(maxDistance);

            prepareBuffers(cellCount);
            for (int i = 0; i < cellCount; i++) distances[i] = float.PositiveInfinity;

            foreach (DistanceSeed seed in seeds)
            {
                if (!passable[seed.Cell] || !(seed.Distance < distances[seed.Cell]))
                    continue;
                distances[seed.Cell] = seed.Distance;
                seeded[seed.Cell] = true;
                if (!narrowBand.Contains(seed.Cell))
                    narrowBand.AddWithPriority(seed.Distance, seed.Cell);
                else
                    narrowBand.ChangePriority(seed.Distance, seed.Cell);
            }

            float diagonalSpacing = sqrt2 * cellSize;
            while (narrowBand.Count != 0)
            {
                narrowBand.ExtractMin(out float value, out int cell);
                if (value > maxDistance)
                    break; // values leave the band in order: everything left is beyond the limit
                alive[cell] = true;

                int x = cell % width, y = cell / width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + neighbourDx[k], ny = y + neighbourDy[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;
                    int neighbour = ny * width + nx;
                    // Seeds are fixed: never overwrite one, even before it has been extracted.
                    if (!passable[neighbour] || alive[neighbour] || seeded[neighbour])
                        continue;
                    if (k >= 4 && !GridSteps.CanStepDiagonally(passable, width, x, y, nx, ny))
                        continue;
                    // Values of cells still in the narrow band may be read here. Values only ever decrease, and
                    // one small enough to change the result would be extracted before this cell and trigger a
                    // recomputation, so the final values match an update from alive neighbours only.
                    float axis = Update(MathF.Min(read(width, height, passable, distances, nx, ny, -1, 0), read(width, height, passable, distances, nx, ny, 1, 0)),
                                        MathF.Min(read(width, height, passable, distances, nx, ny, 0, -1), read(width, height, passable, distances, nx, ny, 0, 1)),
                                        cellSize);
                    float diagonal = Update(MathF.Min(read(width, height, passable, distances, nx, ny, 1, 1), read(width, height, passable, distances, nx, ny, -1, -1)),
                                            MathF.Min(read(width, height, passable, distances, nx, ny, -1, 1), read(width, height, passable, distances, nx, ny, 1, -1)),
                                            diagonalSpacing);
                    float candidate = MathF.Min(axis, diagonal);
                    if (!(candidate < distances[neighbour]))
                        continue;
                    distances[neighbour] = candidate;
                    if (!narrowBand.Contains(neighbour))
                        narrowBand.AddWithPriority(candidate, neighbour);
                    else
                        narrowBand.ChangePriority(candidate, neighbour);
                }
            }
            return DistanceLimit.CutOff(distances, maxDistance);
        }

        // Clears the working buffers, or reallocates them if the grid size changed.
        private void prepareBuffers(int cellCount)
        {
            if (alive.Length != cellCount)
            {
                alive = new bool[cellCount];
                seeded = new bool[cellCount];
                narrowBand = new IndexedMinHeap(cellCount);
                return;
            }
            Array.Clear(alive, 0, cellCount);
            Array.Clear(seeded, 0, cellCount);
            narrowBand.Clear();
        }

        // The value of the neighbour of (x, y) at offset (dx, dy), or infinity if it is off the grid or a
        // diagonal neighbour that would cut a wall corner. Impassable cells are always infinite.
        private static float read(int width, int height, bool[] passable, float[] distances, int x, int y, int dx, int dy)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                return float.PositiveInfinity;
            if (dx != 0 && dy != 0 && !GridSteps.CanStepDiagonally(passable, width, x, y, nx, ny))
                return float.PositiveInfinity;
            return distances[ny * width + nx];
        }

        /// <summary>
        /// First-order upwind update on one stencil, from the smaller neighbour along each of its two axes
        /// (<paramref name="a"/> and <paramref name="b"/>) at spacing <paramref name="hf"/>: the root T >= max(a, b) of
        /// (T - a)^2 + (T - b)^2 = hf^2, or min(a, b) + hf when that root does not exist or would not lie upwind of
        /// both neighbours. The result is strictly greater than every neighbour value it uses. Path tracing uses it
        /// to tell which stencil set a cell's value.
        /// </summary>
        internal static float Update(float a, float b, float hf)
        {
            float lo = MathF.Min(a, b);
            float hi = MathF.Max(a, b);
            float t = lo + hf;
            if (t <= hi)
                return t;
            float d = a - b;
            return 0.5f * (a + b + MathF.Sqrt(2f * hf * hf - d * d));
        }
    }
}
