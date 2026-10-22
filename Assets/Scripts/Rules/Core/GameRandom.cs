using System;

namespace Game.Rules
{
    /// <summary>
    /// The single source of randomness for the rules layer: a seeded PCG32 generator
    /// (O'Neill, "PCG: A Family of Simple Fast Space-Efficient Statistically Good Algorithms for
    /// Random Number Generation", 2014). The same seed and the same sequence of calls always
    /// produce the same results on every platform.
    /// </summary>
    public sealed class GameRandom
    {
        private const ulong multiplier = 6364136223846793005UL;
        private ulong state;
        private ulong increment;

        /// <summary>Creates a generator. <paramref name="stream"/> selects one of 2^63 independent sequences.</summary>
        public GameRandom(ulong seed, ulong stream = 54UL)
        {
            state = 0UL;
            increment = (stream << 1) | 1UL;
            NextUInt();
            state += seed;
            NextUInt();
        }

        /// <summary>The next uniformly distributed 32-bit value.</summary>
        public uint NextUInt()
        {
            ulong old = state;
            state = unchecked(old * multiplier + increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        /// <summary>A uniformly distributed float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>A uniformly distributed float in [min, max).</summary>
        public float NextFloat(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>A uniformly distributed integer in [minInclusive, maxExclusive), without modulo bias.</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            uint range = (uint)(maxExclusive - minInclusive);
            uint threshold = unchecked(0u - range) % range;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return minInclusive + (int)(r % range);
            }
        }

        /// <summary>True with the given probability (values outside [0, 1] are clamped).</summary>
        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>True with the given chance in percent (0 = never, 100 = always).</summary>
        public bool ChancePercent(int percent) => NextInt(0, 100) < percent;

        /// <summary>Captures the generator's position so it can be saved and restored exactly.</summary>
        public (ulong State, ulong Increment) SaveState() => (state, increment);

        /// <summary>Restores a position captured by <see cref="SaveState"/>.</summary>
        public void RestoreState((ulong State, ulong Increment) saved)
        {
            state = saved.State;
            increment = saved.Increment;
        }
    }
}
