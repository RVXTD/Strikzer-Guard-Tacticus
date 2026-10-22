// Distance limit (maxDistance and the cut-off result) by Claude (Anthropic), October 2026; to be reviewed
// by Alexander Stangl per the course syllabus.

using System;

namespace Game.Rules
{
    /// <summary>
    /// Computes a distance field on the map grid: for every cell, the length of the shortest route to its
    /// centre that starts at a seed cell (whose distance is given) and passes only through passable cells.
    /// This is the seam where the eikonal solver plugs in; <see cref="DijkstraDistanceFieldSolver"/> is the
    /// reference implementation.
    /// </summary>
    /// <remarks>
    /// <para>Cells are numbered row by row: cell = y * width + x, with x along world X and y along world Z.</para>
    /// <para>Seeds are fixed values. Each passable seed cell within the distance limit ends with exactly its
    /// seed's distance (the smallest, if the cell is seeded more than once), whatever the solver would have
    /// computed there. Seeds on impassable cells are ignored. With exact straight-line distances as seeds, as
    /// <see cref="SpatialSystem"/> supplies, the seeds never conflict with each other.</para>
    /// <para>Implementations must write every element of <c>distances</c>: the seed distance at every seed
    /// cell within the limit, a finite distance in world units (metres) at every other cell reachable from a
    /// seed through passable cells within the limit, and <see cref="float.PositiveInfinity"/> at every other
    /// cell, including impassable ones and those farther than the limit.</para>
    /// <para>Every finite cell that is not a seed must have a neighbour it can step to directly with a
    /// strictly smaller value: one of its 4 orthogonal neighbours, or a diagonal neighbour whose two shared
    /// orthogonal neighbours are both passable. Descending the field from any finite cell therefore ends
    /// at a seed cell, which is how paths are traced.</para>
    /// <para>A limit does not change any value within it: the field equals the unlimited field wherever that
    /// is at most the limit. Solvers stop once every remaining cell lies beyond the limit, so the marching work
    /// grows with the area within the limit; a few cheap passes over the whole grid (initialising and clearing
    /// arrays) remain.</para>
    /// </remarks>
    public interface IDistanceFieldSolver
    {
        /// <summary>
        /// Fills <paramref name="distances"/> (length width * height) as described on the interface, leaving
        /// cells farther than <paramref name="maxDistance"/> (which may be <see cref="float.PositiveInfinity"/>)
        /// at <see cref="float.PositiveInfinity"/>. Returns true if the field was cut off: some cell was reached
        /// but lies beyond the limit, so a cell left infinite may be reachable after all.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="passable"/> or <paramref name="distances"/> has the wrong length.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A seed's cell is off the grid, its distance is negative or not finite, or <paramref name="maxDistance"/> is negative or NaN.
        /// </exception>
        bool Solve(int width, int height, float cellSize, bool[] passable, ReadOnlySpan<DistanceSeed> seeds,
                   float maxDistance, float[] distances);
    }

    /// <summary>The movement rule on the cell grid, shared by the solvers and path tracing so they cannot disagree.</summary>
    internal static class GridSteps
    {
        /// <summary>
        /// True if a diagonal step from (x, y) to (nx, ny) keeps clear of wall corners: both orthogonal neighbours
        /// the two cells share must be passable. (The cells themselves are checked by the caller.)
        /// </summary>
        public static bool CanStepDiagonally(bool[] passable, int width, int x, int y, int nx, int ny) =>
            passable[y * width + nx] && passable[ny * width + x];
    }

    /// <summary>The distance-limit checks shared by the solvers.</summary>
    internal static class DistanceLimit
    {
        /// <summary>Throws unless <paramref name="maxDistance"/> is non-negative (infinity allowed).</summary>
        public static void Validate(float maxDistance)
        {
            if (!(maxDistance >= 0f))
                throw new ArgumentOutOfRangeException(nameof(maxDistance), $"The distance limit {maxDistance} must not be negative or NaN.");
        }

        /// <summary>
        /// Sets every value above <paramref name="maxDistance"/> to infinity once a solve has stopped, and
        /// returns true if any of them was finite (the field was cut off).
        /// </summary>
        public static bool CutOff(float[] distances, float maxDistance)
        {
            bool cutOff = false;
            for (int i = 0; i < distances.Length; i++)
            {
                if (distances[i] <= maxDistance || float.IsPositiveInfinity(distances[i])) continue;
                distances[i] = float.PositiveInfinity;
                cutOff = true;
            }
            return cutOff;
        }
    }
}
