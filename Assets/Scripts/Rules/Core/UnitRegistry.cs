using System;

namespace Game.Rules
{
    /// <summary>
    /// Issues and retires <see cref="UnitId"/> handles and records each unit's team.
    /// Every other system sizes its per-unit arrays to <see cref="Capacity"/>.
    /// </summary>
    public sealed class UnitRegistry
    {
        private readonly int[] generations;
        private readonly bool[] registered;
        private readonly Team[] teams;
        private readonly int[] freeSlots;
        private int freeCount;
        private int nextUnusedSlot;

        /// <summary>Creates a registry that can hold up to <paramref name="capacity"/> units at once.</summary>
        public UnitRegistry(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            generations = new int[capacity];
            registered = new bool[capacity];
            teams = new Team[capacity];
            freeSlots = new int[capacity];
        }

        /// <summary>Maximum number of units registered at the same time.</summary>
        public int Capacity => generations.Length;

        /// <summary>Number of units currently registered.</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Registers a new unit and returns its handle. Retired slots are reused with a higher generation.
        /// Throws <see cref="InvalidOperationException"/> when the registry is full.
        /// </summary>
        public UnitId Create(Team team)
        {
            int slot;
            if (freeCount > 0) slot = freeSlots[--freeCount];
            else if (nextUnusedSlot < Capacity) slot = nextUnusedSlot++;
            else throw new InvalidOperationException($"UnitRegistry is full ({Capacity} units).");

            generations[slot]++;
            registered[slot] = true;
            teams[slot] = team;
            Count++;
            return new UnitId(slot, generations[slot]);
        }

        /// <summary>
        /// Unregisters a unit. Its handle, and every copy of it, stops being valid immediately.
        /// Throws <see cref="InvalidOperationException"/> if the handle is already invalid.
        /// </summary>
        public void Retire(UnitId id)
        {
            Require(id);
            registered[id.Index] = false;
            freeSlots[freeCount++] = id.Index;
            Count--;
        }

        /// <summary>True if the handle refers to a currently registered unit.</summary>
        public bool IsValid(UnitId id) =>
            id.Index >= 0 && id.Index < Capacity && registered[id.Index] && generations[id.Index] == id.Generation;

        /// <summary>Throws <see cref="InvalidOperationException"/> unless <see cref="IsValid"/> is true.</summary>
        public void Require(UnitId id)
        {
            if (!IsValid(id)) throw new InvalidOperationException($"{id} is not a registered unit (stale or never issued).");
        }

        /// <summary>The team of a registered unit.</summary>
        public Team GetTeam(UnitId id)
        {
            Require(id);
            return teams[id.Index];
        }

        /// <summary>The handle of the unit currently in a slot, or <see cref="UnitId.None"/> if the slot is empty.</summary>
        public UnitId GetIdAtSlot(int slot) =>
            slot >= 0 && slot < Capacity && registered[slot] ? new UnitId(slot, generations[slot]) : UnitId.None;

        /// <summary>True if the two units are on different teams.</summary>
        public bool AreHostile(UnitId a, UnitId b) => GetTeam(a) != GetTeam(b);
    }
}
