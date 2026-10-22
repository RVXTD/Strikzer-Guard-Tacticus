// Fate-based damage (glancing rounds), the downed state, ApplyDamage, Revive, serializable tuning and hit-chance
// previews from any position by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>Where one fired round ended up.</summary>
    public enum RoundFate
    {
        /// <summary>Lodged in the target; recoverable once the target is defeated.</summary>
        Embedded,
        /// <summary>Landed on the floor at a position; recoverable by picking it up.</summary>
        Scattered,
        /// <summary>Gone for good.</summary>
        Lost,
    }

    /// <summary>The fate of one fired round, produced by <see cref="CombatSystem"/> and applied by <see cref="ResourceSystem"/>.</summary>
    public struct RoundOutcome
    {
        /// <summary>Where the round ended up.</summary>
        public RoundFate Fate;
        /// <summary>Ammo type of the round.</summary>
        public int AmmoType;
        /// <summary>Scattered only: where it landed.</summary>
        public Vector2 Position;
        /// <summary>Embedded only: the unit it is lodged in.</summary>
        public UnitId EmbeddedIn;
    }

    /// <summary>Designer-tunable odds and distances for what happens to fired rounds.</summary>
    [System.Serializable]
    public struct AmmoFateTuning
    {
        /// <summary>Chance in percent that a round that hits embeds in the target.</summary>
        public int HitEmbedPercent;
        /// <summary>Chance in percent that a round that hits glances off and scatters (the rest are destroyed on impact and lost).</summary>
        public int HitScatterPercent;
        /// <summary>Damage a glancing round deals, in percent of the weapon's damage per round (rounded to nearest).</summary>
        public int GlanceDamagePercent;
        /// <summary>Chance in percent that a round that misses lands somewhere recoverable (the rest are lost).</summary>
        public int MissScatterPercent;
        /// <summary>Glancing rounds land within this many metres of the target.</summary>
        public float GlanceScatterRadius;
        /// <summary>Missed rounds land between this many metres beyond the target...</summary>
        public float MissScatterMinDistance;
        /// <summary>...and this many metres beyond the target, along the line of fire.</summary>
        public float MissScatterMaxDistance;
        /// <summary>Missed rounds drift up to this many metres sideways from the line of fire.</summary>
        public float MissScatterSpread;

        /// <summary>Starting values; expected to change with playtesting.</summary>
        public static AmmoFateTuning Default => new AmmoFateTuning
        {
            HitEmbedPercent = 60,
            HitScatterPercent = 25,
            GlanceDamagePercent = 50,
            MissScatterPercent = 70,
            GlanceScatterRadius = 1f,
            MissScatterMinDistance = 1f,
            MissScatterMaxDistance = 4f,
            MissScatterSpread = 1f,
        };
    }

    /// <summary>Summary of one resolved attack.</summary>
    public struct AttackResult
    {
        /// <summary>Rounds actually fired; the burst stops early once the target is defeated.</summary>
        public int RoundsFired;
        /// <summary>Rounds that hit (1 for damage dealt without rounds, such as a blast).</summary>
        public int Hits;
        /// <summary>True if this attack defeated the target (an enemy unit).</summary>
        public bool TargetDefeated;
        /// <summary>True if this attack downed the target (a player unit).</summary>
        public bool TargetDowned;
    }

    /// <summary>
    /// Owns health, downed and defeated state, accuracy and weapons, computes hit chances and resolves
    /// attacks round by round. A player unit reduced to zero health is downed (it can be revived);
    /// any other unit is defeated.
    /// It decides where each fired round ends up but does not touch inventories; the caller passes
    /// the outcomes to <see cref="ResourceSystem"/>.
    /// </summary>
    public sealed class CombatSystem
    {
        private readonly UnitRegistry units;
        private readonly SpatialSystem spatial;
        private readonly StatusSystem status;
        private readonly GameRandom random;
        private readonly int[] health;
        private readonly int[] maxHealth;
        private readonly int[] accuracy;
        private readonly WeaponStats[] weapons;
        private readonly bool[] defeated;
        private readonly bool[] downed;
        private readonly List<GameEvent> pendingEvents = new List<GameEvent>(32);

        /// <summary>Hit-chance numbers; may be changed between attacks.</summary>
        public HitChanceTuning HitTuning;

        /// <summary>Round-fate numbers; may be changed between attacks.</summary>
        public AmmoFateTuning FateTuning;

        /// <summary>Creates the system. All rolls draw from <paramref name="random"/>.</summary>
        public CombatSystem(UnitRegistry units, SpatialSystem spatial, StatusSystem status, GameRandom random,
                            in HitChanceTuning hitTuning, in AmmoFateTuning fateTuning)
        {
            this.units = units;
            this.spatial = spatial;
            this.status = status;
            this.random = random;
            HitTuning = hitTuning;
            FateTuning = fateTuning;
            health = new int[units.Capacity];
            maxHealth = new int[units.Capacity];
            accuracy = new int[units.Capacity];
            weapons = new WeaponStats[units.Capacity];
            defeated = new bool[units.Capacity];
            downed = new bool[units.Capacity];
        }

        /// <summary>Records a new unit's combat stats; it starts at full health.</summary>
        public void AddUnit(UnitId id, int maxHealth, int accuracy, in WeaponStats weapon)
        {
            units.Require(id);
            this.maxHealth[id.Index] = Math.Max(1, maxHealth);
            health[id.Index] = this.maxHealth[id.Index];
            this.accuracy[id.Index] = accuracy;
            weapons[id.Index] = weapon;
            defeated[id.Index] = false;
            downed[id.Index] = false;
        }

        /// <summary>Current health (zero once defeated).</summary>
        public int GetHealth(UnitId id) { units.Require(id); return health[id.Index]; }

        /// <summary>Maximum health.</summary>
        public int GetMaxHealth(UnitId id) { units.Require(id); return maxHealth[id.Index]; }

        /// <summary>Base accuracy in percent.</summary>
        public int GetAccuracy(UnitId id) { units.Require(id); return accuracy[id.Index]; }

        /// <summary>The unit's weapon.</summary>
        public WeaponStats GetWeapon(UnitId id) { units.Require(id); return weapons[id.Index]; }

        /// <summary>True once an enemy unit's health has reached zero.</summary>
        public bool IsDefeated(UnitId id) { units.Require(id); return defeated[id.Index]; }

        /// <summary>True while a player unit's health is zero and it has not been revived.</summary>
        public bool IsDowned(UnitId id) { units.Require(id); return downed[id.Index]; }

        /// <summary>True if the unit is defeated or downed: it cannot act, react or be targeted.</summary>
        public bool IsOutOfAction(UnitId id) { units.Require(id); return defeated[id.Index] || downed[id.Index]; }

        /// <summary>Hit chance per round if <paramref name="attacker"/> fired at <paramref name="target"/> now. Changes no state.</summary>
        public HitChanceBreakdown PreviewHitChance(UnitId attacker, UnitId target) =>
            PreviewHitChance(attacker, spatial.GetPosition(attacker), target, spatial.GetPosition(target));

        /// <summary>
        /// Hit chance per round if <paramref name="attacker"/> stood at <paramref name="from"/> and fired at
        /// <paramref name="target"/> standing at <paramref name="to"/>, for the AI to weigh positions. Changes no state.
        /// </summary>
        public HitChanceBreakdown PreviewHitChance(UnitId attacker, Vector2 from, UnitId target, Vector2 to)
        {
            units.Require(attacker);
            units.Require(target);
            var inputs = new HitChanceInputs
            {
                AttackerAccuracy = accuracy[attacker.Index],
                TargetCover = spatial.CoverAgainst(to, from, spatial.GetBodyRadius(target)),
                Distance = Vector2.Distance(from, to),
                OptimalRange = weapons[attacker.Index].OptimalRange,
                AttackerInHostileZone = spatial.IsInsideHostileZone(attacker, from),
                StatusModifier = status.GetAccuracyModifier(attacker),
            };
            return HitChance.Compute(inputs, HitTuning);
        }

        /// <summary>
        /// Fires up to <paramref name="rounds"/> rounds from <paramref name="attacker"/> at <paramref name="target"/>.
        /// Each round rolls to hit, then rolls its fate, then deals damage by fate: full for a round that
        /// embeds or is destroyed on impact, <see cref="AmmoFateTuning.GlanceDamagePercent"/> for a glance.
        /// Appends AttackStarted, then one RoundHit or RoundMissed per round, then UnitDefeated or UnitDowned
        /// if applicable, to <paramref name="log"/>; appends one outcome per fired round to
        /// <paramref name="outcomes"/>. The caller has already checked line of sight and ammunition.
        /// </summary>
        public AttackResult ResolveAttack(UnitId attacker, UnitId target, int rounds, bool isReaction,
                                          EventLog log, List<RoundOutcome> outcomes)
        {
            units.Require(attacker);
            units.Require(target);
            var result = new AttackResult();
            if (rounds <= 0 || IsOutOfAction(target)) return result;

            int chance = PreviewHitChance(attacker, target).Total;
            WeaponStats weapon = weapons[attacker.Index];
            Vector2 from = spatial.GetPosition(attacker);
            Vector2 to = spatial.GetPosition(target);
            pendingEvents.Clear();

            for (int i = 0; i < rounds && !IsOutOfAction(target); i++)
            {
                result.RoundsFired++;
                bool hit = random.ChancePercent(chance);
                outcomes.Add(decideFate(hit, weapon.AmmoType, from, to, target, out bool glanced));
                if (hit)
                {
                    result.Hits++;
                    int damage = glanced ? (weapon.DamagePerRound * FateTuning.GlanceDamagePercent + 50) / 100 : weapon.DamagePerRound;
                    pendingEvents.Add(new GameEvent { Kind = EventKind.RoundHit, Unit = attacker, Other = target, Value = damage, IsReaction = isReaction });
                    takeDamage(target, damage, ref result);
                }
                else
                {
                    pendingEvents.Add(new GameEvent { Kind = EventKind.RoundMissed, Unit = attacker, Other = target, IsReaction = isReaction });
                }
            }

            log.Add(new GameEvent { Kind = EventKind.AttackStarted, Unit = attacker, Other = target, Value = result.RoundsFired, IsReaction = isReaction });
            for (int i = 0; i < pendingEvents.Count; i++) log.Add(pendingEvents[i]);
            logTakenOut(target, result, log);
            return result;
        }

        /// <summary>
        /// Deals <paramref name="amount"/> damage to <paramref name="target"/> without firing a round (e.g. a blast).
        /// Appends UnitDamaged, then UnitDefeated or UnitDowned if applicable. A target already out of action is unaffected.
        /// </summary>
        public AttackResult ApplyDamage(UnitId source, UnitId target, int amount, EventLog log)
        {
            units.Require(source);
            units.Require(target);
            var result = new AttackResult();
            if (amount <= 0 || IsOutOfAction(target)) return result;
            result.Hits = 1;
            log.Add(new GameEvent { Kind = EventKind.UnitDamaged, Unit = target, Other = source, Value = amount });
            takeDamage(target, amount, ref result);
            logTakenOut(target, result, log);
            return result;
        }

        /// <summary>
        /// Revives a downed unit with <paramref name="restoredHealth"/> health (clamped to 1..max) and appends UnitRevived.
        /// Throws <see cref="InvalidOperationException"/> if either handle is invalid or the unit is not downed.
        /// </summary>
        public void Revive(UnitId target, UnitId reviver, int restoredHealth, EventLog log)
        {
            units.Require(target);
            units.Require(reviver);
            if (!downed[target.Index]) throw new InvalidOperationException($"{target} is not downed.");
            downed[target.Index] = false;
            health[target.Index] = Math.Min(maxHealth[target.Index], Math.Max(1, restoredHealth));
            log.Add(new GameEvent { Kind = EventKind.UnitRevived, Unit = target, Other = reviver, Value = health[target.Index] });
        }

        private void takeDamage(UnitId target, int damage, ref AttackResult result)
        {
            health[target.Index] = Math.Max(0, health[target.Index] - damage);
            if (health[target.Index] > 0) return;
            if (units.GetTeam(target) == Team.Player)
            {
                downed[target.Index] = true;
                result.TargetDowned = true;
            }
            else
            {
                defeated[target.Index] = true;
                result.TargetDefeated = true;
            }
        }

        private void logTakenOut(UnitId target, in AttackResult result, EventLog log)
        {
            Vector2 position = spatial.GetPosition(target);
            if (result.TargetDefeated) log.Add(new GameEvent { Kind = EventKind.UnitDefeated, Unit = target, Position = position });
            if (result.TargetDowned) log.Add(new GameEvent { Kind = EventKind.UnitDowned, Unit = target, Position = position });
        }

        private RoundOutcome decideFate(bool hit, int ammoType, Vector2 from, Vector2 to, UnitId target, out bool glanced)
        {
            var outcome = new RoundOutcome { AmmoType = ammoType, Fate = RoundFate.Lost };
            glanced = false;
            int roll = random.NextInt(0, 100);
            if (hit)
            {
                if (roll < FateTuning.HitEmbedPercent)
                {
                    outcome.Fate = RoundFate.Embedded;
                    outcome.EmbeddedIn = target;
                }
                else if (roll < FateTuning.HitEmbedPercent + FateTuning.HitScatterPercent)
                {
                    float angle = random.NextFloat(0f, 2f * MathF.PI);
                    float radius = FateTuning.GlanceScatterRadius * MathF.Sqrt(random.NextFloat());
                    outcome.Position = to + radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    outcome.Fate = RoundFate.Scattered;
                    glanced = true;
                }
            }
            else if (roll < FateTuning.MissScatterPercent)
            {
                Vector2 along = to - from;
                along = along.LengthSquared() > 1e-12f ? Vector2.Normalize(along) : Vector2.UnitX;
                Vector2 side = new Vector2(-along.Y, along.X);
                float beyond = random.NextFloat(FateTuning.MissScatterMinDistance, FateTuning.MissScatterMaxDistance);
                float drift = random.NextFloat(-FateTuning.MissScatterSpread, FateTuning.MissScatterSpread);
                outcome.Position = to + beyond * along + drift * side;
                outcome.Fate = RoundFate.Scattered;
            }

            // A round that lands somewhere no unit can stand cannot be recovered.
            if (outcome.Fate == RoundFate.Scattered && !spatial.IsOpenFloor(outcome.Position))
                outcome.Fate = RoundFate.Lost;
            return outcome;
        }
    }
}
