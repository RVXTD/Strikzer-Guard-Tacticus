// Suppressed status by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the
// course syllabus.

using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>
    /// The stat a status effect modifies. Named effects (slowed, focused, ...) are defined as data in
    /// presentation and map onto one or more of these.
    /// </summary>
    public enum StatusKind
    {
        /// <summary>Adds Magnitude metres to the movement budget at the start of each turn.</summary>
        Movement,
        /// <summary>Adds Magnitude percentage points to hit chance.</summary>
        Accuracy,
        /// <summary>Adds Magnitude to initiative when turn order is rolled.</summary>
        Initiative,
        /// <summary>Pins the unit: it cannot move while any such effect is active. Magnitude is ignored.</summary>
        Suppressed,
    }

    /// <summary>One active status effect.</summary>
    public struct StatusEffect
    {
        /// <summary>The affected unit.</summary>
        public UnitId Unit;
        /// <summary>The stat modified.</summary>
        public StatusKind Kind;
        /// <summary>Signed amount added to the stat.</summary>
        public float Magnitude;
        /// <summary>Number of the unit's own turn ends before the effect expires.</summary>
        public int TurnsRemaining;
    }

    /// <summary>
    /// Owns all active status effects and answers "effective stat" queries, so no other system needs
    /// to know which effects exist.
    /// </summary>
    public sealed class StatusSystem
    {
        private readonly UnitRegistry units;
        private readonly List<StatusEffect> effects = new List<StatusEffect>(32);

        /// <summary>Creates an empty status system.</summary>
        public StatusSystem(UnitRegistry units) { this.units = units; }

        /// <summary>Number of active effects across all units.</summary>
        public int Count => effects.Count;

        /// <summary>An active effect, for display (order is unspecified).</summary>
        public StatusEffect this[int index] => effects[index];

        /// <summary>
        /// Applies an effect lasting <paramref name="turns"/> of the unit's own turn ends (at least 1),
        /// and appends StatusApplied to <paramref name="log"/>. Effects stack.
        /// </summary>
        public void Apply(UnitId unit, StatusKind kind, float magnitude, int turns, EventLog log)
        {
            units.Require(unit);
            effects.Add(new StatusEffect { Unit = unit, Kind = kind, Magnitude = magnitude, TurnsRemaining = Math.Max(1, turns) });
            log.Add(new GameEvent { Kind = EventKind.StatusApplied, Unit = unit, Value = (int)kind });
        }

        /// <summary>Sum of a unit's effects of one kind.</summary>
        public float GetTotal(UnitId unit, StatusKind kind)
        {
            float total = 0f;
            for (int i = 0; i < effects.Count; i++)
                if (effects[i].Unit == unit && effects[i].Kind == kind) total += effects[i].Magnitude;
            return total;
        }

        /// <summary>Movement modifier in metres.</summary>
        public float GetMovementModifier(UnitId unit) => GetTotal(unit, StatusKind.Movement);

        /// <summary>Accuracy modifier in whole percentage points (rounded half away from zero).</summary>
        public int GetAccuracyModifier(UnitId unit) => roundToInt(GetTotal(unit, StatusKind.Accuracy));

        /// <summary>True while the unit has a <see cref="StatusKind.Suppressed"/> effect and so cannot move.</summary>
        public bool IsSuppressed(UnitId unit)
        {
            for (int i = 0; i < effects.Count; i++)
                if (effects[i].Unit == unit && effects[i].Kind == StatusKind.Suppressed) return true;
            return false;
        }

        /// <summary>Initiative modifier (rounded half away from zero).</summary>
        public int GetInitiativeModifier(UnitId unit) => roundToInt(GetTotal(unit, StatusKind.Initiative));

        /// <summary>
        /// Counts down the unit's effects at the end of its turn, removing expired ones and appending
        /// StatusExpired for each.
        /// </summary>
        public void EndTurn(UnitId unit, EventLog log)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (effects[i].Unit != unit) continue;
                StatusEffect effect = effects[i];
                effect.TurnsRemaining--;
                if (effect.TurnsRemaining > 0) { effects[i] = effect; continue; }
                effects.RemoveAt(i);
                log.Add(new GameEvent { Kind = EventKind.StatusExpired, Unit = unit, Value = (int)effect.Kind });
            }
        }

        /// <summary>Removes every effect on a unit without events (used when a unit leaves the encounter).</summary>
        public void RemoveAll(UnitId unit) => effects.RemoveAll(e => e.Unit == unit);

        private static int roundToInt(float value) =>
            value >= 0f ? (int)MathF.Floor(value + 0.5f) : -(int)MathF.Floor(-value + 0.5f);
    }
}
