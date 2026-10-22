using System;

namespace Game.Rules
{
    /// <summary>
    /// A cell whose distance is known before a distance field is solved, such as a cell centre the
    /// unit can walk to in a straight line. See <see cref="IDistanceFieldSolver"/>.
    /// </summary>
    public readonly struct DistanceSeed
    {
        /// <summary>Index of the seeded cell (y * width + x).</summary>
        public readonly int Cell;

        /// <summary>Known distance to the cell's centre, in metres; finite and not negative.</summary>
        public readonly float Distance;

        /// <summary>Creates a seed that fixes <paramref name="cell"/> at <paramref name="distance"/> metres.</summary>
        public DistanceSeed(int cell, float distance)
        {
            Cell = cell;
            Distance = distance;
        }

        /// <summary>Throws unless every seed names a cell in [0, cellCount) and has a finite, non-negative distance.</summary>
        internal static void Validate(ReadOnlySpan<DistanceSeed> seeds, int cellCount)
        {
            foreach (DistanceSeed seed in seeds)
            {
                if (seed.Cell < 0 || seed.Cell >= cellCount)
                    throw new ArgumentOutOfRangeException(nameof(seeds), $"Seed cell {seed.Cell} is outside the grid.");
                if (!(seed.Distance >= 0f) || float.IsPositiveInfinity(seed.Distance))
                    throw new ArgumentOutOfRangeException(nameof(seeds), $"Seed distance {seed.Distance} must be finite and not negative.");
            }
        }
    }
}
