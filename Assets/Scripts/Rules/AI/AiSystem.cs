// Written by Claude (Anthropic), October 2026: the utility-scoring AI for AI-controlled units; to be reviewed by
// Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>
    /// Chooses the next command for an AI-controlled unit, one command at a time, by scoring spots. A spot is worth
    /// its safety (minus the expected damage hostiles that can see it could deal there), minus a penalty for
    /// standing beyond the unit's optimal range of the nearest hostile, plus the best thing the unit could do from
    /// it with the points it would have left: attack, use an ability, or pick up rounds. The unit acts where it
    /// stands unless some spot it can reach is clearly better, in which case it moves there and is asked again.
    /// Each unit's <see cref="AiProfile"/> weighs the terms, so enemy types differ by data alone.
    /// </summary>
    /// <remarks>
    /// "As if from a spot" questions go through the same rules code the commands use
    /// (<see cref="CombatSystem.PreviewHitChance(UnitId, Vector2, UnitId, Vector2)"/> and
    /// <see cref="AbilitySystem.Validate(UnitId, Vector2, int, UnitId, Vector2, int, List{UnitId})"/>), and the
    /// best few moves are checked with <see cref="Encounter.PlanMove"/>, so the AI only proposes commands the rules
    /// accept. It rolls nothing, so the same seed and the same player commands replay the same encounter. Spots are
    /// sampled about a metre apart within reach, which keeps a decision to a few hundred spots on a fine grid.
    /// </remarks>
    public sealed class AiSystem
    {
        private const float candidateSpacing = 1f; // metres between sampled spots
        private const int exactChecks = 8;         // best spots checked with a full move plan and its exact zone entries
        private const float moveMargin = 0.25f;    // a move must beat staying by this much, so units don't jitter
        private const float distanceTieBreak = 0.01f; // per metre walked: between equal spots, the nearer wins
        private const float killBonus = 1f;        // extra value per health point of a unit expected to go down
        private const float suppressedValue = 2f;  // value of pinning a hostile
        private const float statusValue = 0.1f;    // value per point of status magnitude per turn
        private const float overwatchShare = 0.5f; // share of a burst an overwatch cone is worth per hostile it may catch
        private const float ammoValueShare = 0.5f; // share of a round's damage a recovered round is worth
        private const float tolerance = 1e-4f;

        private readonly Encounter encounter;
        private AiProfile[] profiles;
        private readonly MovePlan plan = new MovePlan();
        private readonly List<UnitId> affected = new List<UnitId>(8);
        private readonly List<UnitId> hostiles = new List<UnitId>(16);
        private readonly List<Spot> spots = new List<Spot>(512);

        private struct Spot
        {
            public Vector2 Position;
            public float Value;
            public float ZoneEstimate;
            public int Order;
        }

        /// <summary>Creates the AI for an encounter; the encounter registers each unit's profile as it is added.</summary>
        public AiSystem(Encounter encounter)
        {
            this.encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
            profiles = new AiProfile[encounter.Units.Capacity];
        }

        /// <summary>Records how the AI weighs a unit's options.</summary>
        public void AddUnit(UnitId id, in AiProfile profile)
        {
            encounter.Units.Require(id);
            profiles[id.Index] = profile;
        }

        /// <summary>The unit's AI profile.</summary>
        public AiProfile GetProfile(UnitId id)
        {
            encounter.Units.Require(id);
            return profiles[id.Index];
        }

        /// <summary>
        /// The next command for <paramref name="unit"/>, which should be the active unit: a move, an attack, an
        /// ability, a pickup, or <see cref="CommandKind.EndTurn"/> when nothing is worth doing. Changes no game state
        /// (it may rebuild the unit's distance field). Call again after each command until the turn passes.
        /// </summary>
        public Command ChooseCommand(UnitId unit)
        {
            EncounterFlow flow = encounter.Flow;
            if (flow.Phase != EncounterPhase.AwaitingCommand || unit != flow.ActiveUnit) return Command.EndTurn(unit);
            gatherHostiles(unit);
            AiProfile profile = profiles[unit.Index];
            int points = flow.ActiveUnitActionPoints;
            Vector2 here = encounter.Spatial.GetPosition(unit);

            float actHere = bestAction(unit, profile, here, points, out Command action);
            float stayValue = standingValue(unit, profile, here) + MathF.Max(0f, actHere);
            if (tryBestMove(unit, profile, points, out Vector2 destination, out float moveValue) && moveValue > stayValue + moveMargin)
                return Command.Move(unit, destination);
            return actHere > 0f ? action : Command.EndTurn(unit);
        }

        private void gatherHostiles(UnitId unit)
        {
            hostiles.Clear();
            UnitRegistry units = encounter.Units;
            for (int slot = 0; slot < units.Capacity; slot++)
            {
                UnitId other = units.GetIdAtSlot(slot);
                if (!other.IsNone && units.AreHostile(unit, other) && !encounter.Combat.IsOutOfAction(other)) hostiles.Add(other);
            }
        }

        // Safety and range keeping at a spot, before anything the unit does there.
        private float standingValue(UnitId unit, in AiProfile profile, Vector2 spot)
        {
            SpatialSystem spatial = encounter.Spatial;
            CombatSystem combat = encounter.Combat;
            float threat = 0f, nearest = float.PositiveInfinity;
            for (int i = 0; i < hostiles.Count; i++)
            {
                Vector2 from = spatial.GetPosition(hostiles[i]);
                nearest = MathF.Min(nearest, Vector2.Distance(spot, from));
                if (spatial.HasLineOfSight(from, spot)) threat += expectedBurst(hostiles[i], from, unit, spot);
            }
            float healthLost = 1f - (float)combat.GetHealth(unit) / Math.Max(1, combat.GetMaxHealth(unit));
            float caution = profile.Caution * (1f + profile.RetreatWhenHurt * healthLost);
            float beyondRange = float.IsPositiveInfinity(nearest) ? 0f : MathF.Max(0f, nearest - combat.GetWeapon(unit).OptimalRange);
            return -caution * threat - profile.Approach * beyondRange;
        }

        // The best thing the unit could do from a spot with the given points, and its weighted value (0 if nothing helps).
        private float bestAction(UnitId unit, in AiProfile profile, Vector2 from, int points, out Command command)
        {
            command = Command.EndTurn(unit);
            float best = 0f;
            SpatialSystem spatial = encounter.Spatial;
            ResourceSystem resources = encounter.Resources;
            WeaponStats weapon = encounter.Combat.GetWeapon(unit);
            int carried = resources.GetCarried(unit, weapon.AmmoType);

            if (points >= 1 && carried > 0)
            {
                for (int i = 0; i < hostiles.Count; i++)
                {
                    if (!spatial.HasLineOfSight(from, spatial.GetPosition(hostiles[i]))) continue;
                    float value = profile.Aggression * attackValue(unit, from, hostiles[i]);
                    if (value > best) { best = value; command = Command.Attack(unit, hostiles[i]); }
                }
            }

            IReadOnlyList<int> known = encounter.Abilities.GetAbilities(unit);
            for (int k = 0; k < known.Count; k++)
            {
                int ability = known[k];
                AbilityDefinition definition = encounter.Abilities.GetDefinition(ability);
                if (definition.ActionPointCost > points) continue;
                if (definition.Targeting == AbilityTargeting.Self)
                {
                    considerAbility(unit, profile, from, points, ability, definition, UnitId.None, from, ref best, ref command);
                    continue;
                }
                UnitRegistry units = encounter.Units;
                for (int slot = 0; slot < units.Capacity; slot++)
                {
                    UnitId other = units.GetIdAtSlot(slot);
                    if (other.IsNone || encounter.Combat.IsDefeated(other)) continue;
                    UnitId target = definition.Targeting == AbilityTargeting.Unit ? other : UnitId.None;
                    considerAbility(unit, profile, from, points, ability, definition, target, spatial.GetPosition(other), ref best, ref command);
                }
            }

            if (points >= 1 && carried < weapon.RoundsPerAttack)
            {
                int wanted = 2 * weapon.RoundsPerAttack - carried;
                for (int i = 0; i < resources.PileCount; i++)
                {
                    AmmoPile pile = resources.GetPileAt(i);
                    if (pile.AmmoType != weapon.AmmoType || resources.CanPickUp(from, pile.Id) != RejectReason.None) continue;
                    float value = profile.AmmoGreed * Math.Min(pile.Count, wanted) * weapon.DamagePerRound * ammoValueShare;
                    if (value > best) { best = value; command = Command.PickUp(unit, pile.Id); }
                }
            }
            return best;
        }

        private void considerAbility(UnitId unit, in AiProfile profile, Vector2 from, int points, int ability,
                                     in AbilityDefinition definition, UnitId target, Vector2 point, ref float best, ref Command command)
        {
            if (encounter.Abilities.Validate(unit, from, ability, target, point, points, affected) != RejectReason.None) return;
            float value = profile.Aggression * effectsValue(unit, from, definition, point);
            if (value <= best) return;
            best = value;
            command = Command.UseAbility(unit, ability, target, point);
        }

        // What an ability's effects on the units in `affected` are worth: good for hostiles hurt and allies helped.
        private float effectsValue(UnitId unit, Vector2 from, in AbilityDefinition definition, Vector2 point)
        {
            if (definition.Effects == null) return 0f;
            UnitRegistry units = encounter.Units;
            CombatSystem combat = encounter.Combat;
            float total = 0f;
            for (int e = 0; e < definition.Effects.Length; e++)
            {
                AbilityEffect effect = definition.Effects[e];
                if (effect.Kind == AbilityEffectKind.PlaceZone)
                {
                    total += overwatchValue(unit, from, effect.Zone, point);
                    continue;
                }
                for (int t = 0; t < affected.Count; t++)
                {
                    UnitId target = affected[t];
                    float sign = units.AreHostile(unit, target) ? 1f : -1f;
                    switch (effect.Kind)
                    {
                        case AbilityEffectKind.Attack:
                            if (sign > 0f) total += attackValue(unit, from, target);
                            break;
                        case AbilityEffectKind.Damage:
                            total += sign * damageValue(effect.Amount, combat.GetHealth(target));
                            break;
                        case AbilityEffectKind.ApplyStatus:
                            total += effect.Status == StatusKind.Suppressed
                                ? sign * suppressedValue
                                : -sign * effect.Magnitude * Math.Max(1, effect.Turns) * statusValue;
                            break;
                        case AbilityEffectKind.Revive:
                            if (sign < 0f && combat.IsDowned(target)) total += effect.Amount;
                            break;
                    }
                }
            }
            return total;
        }

        // An overwatch cone facing the point is worth a share of a burst at each hostile that could walk into it.
        private float overwatchValue(UnitId unit, Vector2 from, in ZoneSetup zone, Vector2 point)
        {
            float total = 0f;
            for (int i = 0; i < hostiles.Count; i++)
            {
                var reach = new ZoneSetup
                {
                    Radius = zone.Radius + encounter.Movement.GetBaseBudget(hostiles[i]),
                    HalfAngleDegrees = zone.HalfAngleDegrees,
                };
                Vector2 position = encounter.Spatial.GetPosition(hostiles[i]);
                if (new ZoneShape(from, point - from, reach).Contains(position))
                    total += overwatchShare * expectedBurst(unit, from, hostiles[i], position);
            }
            return total;
        }

        private float attackValue(UnitId attacker, Vector2 from, UnitId target) =>
            damageValue(expectedBurst(attacker, from, target, encounter.Spatial.GetPosition(target)), encounter.Combat.GetHealth(target));

        private static float damageValue(float damage, int health) =>
            MathF.Min(damage, health) + (damage >= health ? killBonus * health : 0f);

        // Expected damage of the attacker's next burst from `from` at the target standing at `to`.
        private float expectedBurst(UnitId attacker, Vector2 from, UnitId target, Vector2 to)
        {
            CombatSystem combat = encounter.Combat;
            WeaponStats weapon = combat.GetWeapon(attacker);
            int rounds = Math.Min(weapon.RoundsPerAttack, encounter.Resources.GetCarried(attacker, weapon.AmmoType));
            if (rounds <= 0) return 0f;
            float chance = combat.PreviewHitChance(attacker, from, target, to).Total / 100f;
            AmmoFateTuning fate = combat.FateTuning;
            float damagePerHit = weapon.DamagePerRound * (1f - fate.HitScatterPercent / 100f * (1f - fate.GlanceDamagePercent / 100f));
            return rounds * chance * damagePerHit;
        }

        // A cheap stand-in for a move's reaction exposure, used to rank spots before the exact check: the bursts of
        // hostiles with a reaction left whose zone holds the spot but not the unit's current position.
        private float zoneEstimate(UnitId unit, Vector2 spot)
        {
            SpatialSystem spatial = encounter.Spatial;
            Vector2 here = spatial.GetPosition(unit);
            float total = 0f;
            for (int i = 0; i < hostiles.Count; i++)
            {
                UnitId owner = hostiles[i];
                if (!encounter.Flow.HasReaction(owner) || !spatial.HasActiveZone(owner)) continue;
                if (spatial.IsInsideZone(owner, spot) && !spatial.IsInsideZone(owner, here))
                    total += expectedBurst(owner, spatial.GetPosition(owner), unit, spot);
            }
            return total;
        }

        // The best spot to move to: sampled spots are scored from the field, then the best few are planned exactly.
        private bool tryBestMove(UnitId unit, in AiProfile profile, int points, out Vector2 destination, out float value)
        {
            destination = default;
            value = float.NegativeInfinity;
            MovementSystem movement = encounter.Movement;
            SpatialSystem spatial = encounter.Spatial;
            float budget = movement.GetBudget(unit);
            if (points < 1 || budget <= 0f || encounter.Status.IsSuppressed(unit)) return false;
            if (spatial.FieldOwner != unit || spatial.FieldRange < movement.GetFieldRange(unit)) movement.RebuildField(unit);
            if (!spatial.TryGetCell(spatial.GetPosition(unit), out int start)) return false;

            float reach = budget * Math.Min(points, 2);
            int stride = Math.Max(1, (int)MathF.Round(candidateSpacing / spatial.CellSize));
            int cells = (int)MathF.Ceiling(reach / spatial.CellSize);
            int startX = start % spatial.Width, startY = start / spatial.Width;
            ReadOnlySpan<float> field = spatial.Field;
            spots.Clear();
            for (int dy = -cells; dy <= cells; dy += stride)
            {
                int y = startY + dy;
                if (y < 0 || y >= spatial.Height) continue;
                for (int dx = -cells; dx <= cells; dx += stride)
                {
                    int x = startX + dx;
                    if (x < 0 || x >= spatial.Width || (dx == 0 && dy == 0)) continue;
                    int cell = y * spatial.Width + x;
                    float distance = field[cell];
                    if (!spatial.IsPassableForFieldOwner(cell) || !(distance <= reach + tolerance)) continue;
                    int cost = distance <= budget + tolerance ? 1 : 2;
                    Vector2 spot = spatial.CellCenter(cell);
                    float estimate = zoneEstimate(unit, spot);
                    float spotValue = standingValue(unit, profile, spot) + MathF.Max(0f, bestAction(unit, profile, spot, points - cost, out _))
                                      - distanceTieBreak * distance - profile.ZoneAvoidance * estimate;
                    spots.Add(new Spot { Position = spot, Value = spotValue, ZoneEstimate = estimate, Order = spots.Count });
                }
            }
            spots.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : a.Order.CompareTo(b.Order));

            bool found = false;
            for (int i = 0; i < spots.Count && i < exactChecks; i++)
            {
                if (encounter.PlanMove(unit, spots[i].Position, plan) != RejectReason.None) continue;
                float exposure = 0f;
                for (int e = 0; e < plan.Interrupts.Count; e++)
                {
                    UnitId owner = plan.Interrupts[e].Owner;
                    exposure += expectedBurst(owner, spatial.GetPosition(owner), unit, plan.Interrupts[e].Position);
                }
                float planned = spots[i].Value + profile.ZoneAvoidance * (spots[i].ZoneEstimate - exposure);
                if (planned <= value) continue;
                value = planned;
                destination = spots[i].Position;
                found = true;
            }
            return found;
        }
    }
}
