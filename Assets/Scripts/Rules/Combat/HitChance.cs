// [Serializable] tuning by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the
// course syllabus.

using System;

namespace Game.Rules
{
    /// <summary>Designer-tunable numbers for hit chance. All values are percentage points unless noted.</summary>
    [System.Serializable]
    public struct HitChanceTuning
    {
        /// <summary>Penalty when the target has half cover against the attacker.</summary>
        public int HalfCoverPenalty;
        /// <summary>Penalty when the target has full cover against the attacker.</summary>
        public int FullCoverPenalty;
        /// <summary>Penalty per metre beyond the weapon's optimal range (fractional points are rounded).</summary>
        public float PenaltyPerMetreBeyondOptimal;
        /// <summary>Penalty when the attacker stands inside a hostile zone of control.</summary>
        public int AttackerInHostileZonePenalty;
        /// <summary>Lowest final chance, so no shot is ever certain to miss.</summary>
        public int Minimum;
        /// <summary>Highest final chance, so no shot is ever certain to hit.</summary>
        public int Maximum;

        /// <summary>Starting values; expected to change with playtesting.</summary>
        public static HitChanceTuning Default => new HitChanceTuning
        {
            HalfCoverPenalty = 20,
            FullCoverPenalty = 40,
            PenaltyPerMetreBeyondOptimal = 3f,
            AttackerInHostileZonePenalty = 15,
            Minimum = 5,
            Maximum = 95,
        };
    }

    /// <summary>Everything hit chance depends on, gathered by <see cref="CombatSystem.PreviewHitChance(UnitId, System.Numerics.Vector2, UnitId, System.Numerics.Vector2)"/>.</summary>
    public struct HitChanceInputs
    {
        /// <summary>Attacker's base accuracy in percent.</summary>
        public int AttackerAccuracy;
        /// <summary>Cover protecting the target from the attacker.</summary>
        public CoverLevel TargetCover;
        /// <summary>Distance between the two units in metres.</summary>
        public float Distance;
        /// <summary>Weapon's optimal range in metres.</summary>
        public float OptimalRange;
        /// <summary>True if the attacker stands inside a hostile zone of control.</summary>
        public bool AttackerInHostileZone;
        /// <summary>Sum of the attacker's accuracy status modifiers, in percentage points.</summary>
        public int StatusModifier;
    }

    /// <summary>
    /// A hit chance and the contribution of each factor, for the HUD to display line by line.
    /// <see cref="Total"/> is the sum of the parts, clamped to the tuning's minimum and maximum.
    /// </summary>
    public struct HitChanceBreakdown
    {
        /// <summary>Attacker's base accuracy.</summary>
        public int Base;
        /// <summary>Cover modifier (zero or negative).</summary>
        public int Cover;
        /// <summary>Range modifier (zero or negative).</summary>
        public int Range;
        /// <summary>Hostile-zone modifier (zero or negative).</summary>
        public int Zone;
        /// <summary>Status-effect modifier (any sign).</summary>
        public int Status;
        /// <summary>Final chance to hit per round, in percent.</summary>
        public int Total;
    }

    /// <summary>Pure hit-chance calculation, independent of any game state.</summary>
    public static class HitChance
    {
        /// <summary>Computes the chance for one round to hit, with its breakdown.</summary>
        public static HitChanceBreakdown Compute(in HitChanceInputs inputs, in HitChanceTuning tuning)
        {
            var result = new HitChanceBreakdown
            {
                Base = inputs.AttackerAccuracy,
                Cover = inputs.TargetCover == CoverLevel.Full ? -tuning.FullCoverPenalty
                      : inputs.TargetCover == CoverLevel.Half ? -tuning.HalfCoverPenalty
                      : 0,
                Range = -roundToInt(MathF.Max(0f, inputs.Distance - inputs.OptimalRange) * tuning.PenaltyPerMetreBeyondOptimal),
                Zone = inputs.AttackerInHostileZone ? -tuning.AttackerInHostileZonePenalty : 0,
                Status = inputs.StatusModifier,
            };
            int sum = result.Base + result.Cover + result.Range + result.Zone + result.Status;
            result.Total = Math.Min(tuning.Maximum, Math.Max(tuning.Minimum, sum));
            return result;
        }

        private static int roundToInt(float value) => (int)MathF.Floor(value + 0.5f);
    }
}
