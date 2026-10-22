// Distance limit by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course
// syllabus.

using System;

namespace Game.Rules
{
    /// <summary>
    /// Reference <see cref="IDistanceFieldSolver"/>: Dijkstra's algorithm on the 8-connected cell graph.
    /// Diagonal steps are allowed only when both orthogonal neighbours are passable (no corner cutting).
    /// </summary>
    /// <remarks>
    /// Away from the seeds, distances follow the octile metric, which overestimates straight-line distance
    /// by up to about 8% for directions between the axes and diagonals. An eikonal (fast marching) solver
    /// removes most of that error and can replace this class without other changes. The search stops once
    /// the next cell lies beyond the distance limit. Working buffers are kept between calls, so one instance
    /// must not be used by two threads at once.
    /// </remarks>
    public sealed class DijkstraDistanceFieldSolver : IDistanceFieldSolver
    {
        private static readonly int[] neighbourDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] neighbourDy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float sqrt2 = MathF.Sqrt(2f);

        private float[] heapKeys = new float[64];
        private int[] heapCells = new int[64];
        private int heapCount;
        private bool[] seeded = Array.Empty<bool>();

        /// <inheritdoc/>
        public bool Solve(int width, int height, float cellSize, bool[] passable, ReadOnlySpan<DistanceSeed> seeds,
                          float maxDistance, float[] distances)
        {
            int cellCount = width * height;
            if (passable.Length != cellCount || distances.Length != cellCount)
                throw new ArgumentException("passable and distances must have width * height elements.");
            DistanceSeed.Validate(seeds, cellCount);
            DistanceLimit.Validate(maxDistance);

            if (seeded.Length != cellCount) seeded = new bool[cellCount];
            else Array.Clear(seeded, 0, cellCount);
            for (int i = 0; i < cellCount; i++) distances[i] = float.PositiveInfinity;

            heapCount = 0;
            foreach (DistanceSeed seed in seeds)
            {
                if (!passable[seed.Cell] || !(seed.Distance < distances[seed.Cell])) continue;
                distances[seed.Cell] = seed.Distance;
                seeded[seed.Cell] = true;
                push(seed.Distance, seed.Cell);
            }

            while (heapCount > 0)
            {
                pop(out float distance, out int cell);
                if (distance > maxDistance) break; // keys pop in order: everything left is beyond the limit
                if (distance > distances[cell]) continue; // stale heap entry, or a cell seeded more than once

                int x = cell % width, y = cell / width;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + neighbourDx[k], ny = y + neighbourDy[k];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int neighbour = ny * width + nx;
                    if (!passable[neighbour] || seeded[neighbour]) continue; // seeds are fixed

                    bool diagonal = k >= 4;
                    if (diagonal && !GridSteps.CanStepDiagonally(passable, width, x, y, nx, ny)) continue;

                    float candidate = distance + (diagonal ? sqrt2 : 1f) * cellSize;
                    if (candidate < distances[neighbour])
                    {
                        distances[neighbour] = candidate;
                        push(candidate, neighbour);
                    }
                }
            }
            return DistanceLimit.CutOff(distances, maxDistance);
        }

        private void push(float key, int cell)
        {
            if (heapCount == heapKeys.Length)
            {
                Array.Resize(ref heapKeys, heapCount * 2);
                Array.Resize(ref heapCells, heapCount * 2);
            }
            int i = heapCount++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heapKeys[parent] <= key) break;
                heapKeys[i] = heapKeys[parent];
                heapCells[i] = heapCells[parent];
                i = parent;
            }
            heapKeys[i] = key;
            heapCells[i] = cell;
        }

        private void pop(out float key, out int cell)
        {
            key = heapKeys[0];
            cell = heapCells[0];
            float lastKey = heapKeys[--heapCount];
            int lastCell = heapCells[heapCount];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= heapCount) break;
                if (child + 1 < heapCount && heapKeys[child + 1] < heapKeys[child]) child++;
                if (heapKeys[child] >= lastKey) break;
                heapKeys[i] = heapKeys[child];
                heapCells[i] = heapCells[child];
                i = child;
            }
            heapKeys[i] = lastKey;
            heapCells[i] = lastCell;
        }
    }
}
