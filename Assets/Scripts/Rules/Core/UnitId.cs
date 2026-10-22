using System;

namespace Game.Rules
{
    /// <summary>
    /// Handle to one unit. A unit is nothing more than this handle: all of its data lives in the
    /// systems, in arrays indexed by <see cref="Index"/>.
    /// </summary>
    /// <remarks>
    /// The default value is <see cref="None"/> and never refers to a unit. Handles are issued only by
    /// <see cref="UnitRegistry"/>; a handle to a retired unit never equals the handle of a unit that
    /// later reuses the same slot, so stale handles are detected instead of silently aliasing.
    /// </remarks>
    public readonly struct UnitId : IEquatable<UnitId>
    {
        /// <summary>A handle that refers to no unit.</summary>
        public static readonly UnitId None = default;

        /// <summary>Slot index into every system's per-unit arrays.</summary>
        public readonly int Index;

        /// <summary>Generation of the slot when this handle was issued; at least 1 for any real unit.</summary>
        public readonly int Generation;

        internal UnitId(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        /// <summary>True for <see cref="None"/>.</summary>
        public bool IsNone => Generation == 0;

        /// <summary>Two handles are equal when both slot and generation match.</summary>
        public bool Equals(UnitId other) => Index == other.Index && Generation == other.Generation;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is UnitId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (Index * 397) ^ Generation;

        /// <summary>Equality by slot and generation.</summary>
        public static bool operator ==(UnitId a, UnitId b) => a.Equals(b);

        /// <summary>Inequality by slot and generation.</summary>
        public static bool operator !=(UnitId a, UnitId b) => !a.Equals(b);

        /// <inheritdoc/>
        public override string ToString() => IsNone ? "Unit(None)" : $"Unit({Index}:{Generation})";
    }
}
