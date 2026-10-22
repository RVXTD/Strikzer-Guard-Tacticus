// Written by Claude (Anthropic), October 2026: the ability table, each unit's abilities and ability
// validation (also from a hypothetical position, for the AI); to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>
    /// Owns the encounter's ability definitions and which abilities each unit knows, and validates ability
    /// use. Effects are applied by <see cref="Encounter"/>, which owns the cross-system consequences of
    /// attacks and damage.
    /// </summary>
    public sealed class AbilitySystem
    {
        private const float rangeTolerance = 1e-4f;

        private readonly UnitRegistry units;
        private readonly SpatialSystem spatial;
        private readonly CombatSystem combat;
        private readonly ResourceSystem resources;
        private readonly AbilityDefinition[] definitions;
        private readonly int[][] known;

        /// <summary>Creates the system with the encounter's ability table (null for none); the table is copied.</summary>
        public AbilitySystem(UnitRegistry units, AbilityDefinition[] definitions, SpatialSystem spatial,
                             CombatSystem combat, ResourceSystem resources)
        {
            this.units = units;
            this.spatial = spatial;
            this.combat = combat;
            this.resources = resources;
            this.definitions = definitions == null ? Array.Empty<AbilityDefinition>() : (AbilityDefinition[])definitions.Clone();
            known = new int[units.Capacity][];
        }

        /// <summary>Number of abilities in the table.</summary>
        public int DefinitionCount => definitions.Length;

        /// <summary>The ability at an index in the table.</summary>
        public AbilityDefinition GetDefinition(int ability) => definitions[ability];

        /// <summary>True if every index (null counts as none) refers to an ability in the table.</summary>
        public bool AreValidIndices(int[] abilities)
        {
            if (abilities == null) return true;
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i] < 0 || abilities[i] >= definitions.Length) return false;
            return true;
        }

        /// <summary>Records the abilities a new unit knows (null for none). Throws if an index is not in the table.</summary>
        public void AddUnit(UnitId id, int[] abilities)
        {
            units.Require(id);
            if (!AreValidIndices(abilities)) throw new ArgumentOutOfRangeException(nameof(abilities), "Every ability index must refer to the ability table.");
            known[id.Index] = abilities == null ? Array.Empty<int>() : (int[])abilities.Clone();
        }

        /// <summary>The indices of the abilities a unit knows.</summary>
        public IReadOnlyList<int> GetAbilities(UnitId id)
        {
            units.Require(id);
            return known[id.Index];
        }

        /// <summary>True if the unit knows the ability.</summary>
        public bool Knows(UnitId id, int ability)
        {
            units.Require(id);
            return Array.IndexOf(known[id.Index], ability) >= 0;
        }

        /// <summary>Finds the first reaction ability the unit knows; false if it reacts with a plain burst.</summary>
        public bool TryGetReactionAbility(UnitId id, out int ability)
        {
            units.Require(id);
            int[] abilities = known[id.Index];
            for (int i = 0; i < abilities.Length; i++)
            {
                if (!definitions[abilities[i]].IsReaction) continue;
                ability = abilities[i];
                return true;
            }
            ability = -1;
            return false;
        }

        /// <summary>
        /// Checks whether <paramref name="actor"/> may use <paramref name="ability"/> on <paramref name="target"/>
        /// (Unit targeting) or <paramref name="point"/> (Point targeting) with <paramref name="availableActionPoints"/>
        /// points, without changing any state. On success fills <paramref name="affected"/> with the units the
        /// effects apply to, in slot order.
        /// </summary>
        public RejectReason Validate(UnitId actor, int ability, UnitId target, Vector2 point, int availableActionPoints,
                                     List<UnitId> affected) =>
            Validate(actor, spatial.GetPosition(actor), ability, target, point, availableActionPoints, affected);

        /// <summary>
        /// As <see cref="Validate(UnitId, int, UnitId, Vector2, int, List{UnitId})"/>, but as if
        /// <paramref name="actor"/> stood at <paramref name="from"/>, for the AI to weigh positions.
        /// </summary>
        public RejectReason Validate(UnitId actor, Vector2 from, int ability, UnitId target, Vector2 point,
                                     int availableActionPoints, List<UnitId> affected)
        {
            affected.Clear();
            if (!Knows(actor, ability)) return RejectReason.AbilityNotAvailable;
            AbilityDefinition definition = definitions[ability];
            if (definition.ActionPointCost > availableActionPoints) return RejectReason.NotEnoughActionPoints;
            bool revives = hasEffect(definition, AbilityEffectKind.Revive);

            switch (definition.Targeting)
            {
                case AbilityTargeting.Self:
                    affected.Add(actor);
                    break;

                case AbilityTargeting.Unit:
                {
                    if (!units.IsValid(target)) return RejectReason.InvalidTarget;
                    bool hostile = units.AreHostile(actor, target);
                    if (definition.TargetTeam == AbilityTeam.Hostile && !hostile) return RejectReason.TargetNotHostile;
                    if (definition.TargetTeam == AbilityTeam.Friendly && hostile) return RejectReason.TargetNotFriendly;
                    if (!isEligible(target, revives)) return RejectReason.InvalidTarget;
                    Vector2 to = spatial.GetPosition(target);
                    if (!inRange(definition, from, to)) return RejectReason.OutOfRange;
                    if (definition.RequiresLineOfSight && !spatial.HasLineOfSight(from, to)) return RejectReason.NoLineOfSight;
                    affected.Add(target);
                    break;
                }

                case AbilityTargeting.Point:
                {
                    if (!spatial.TryGetCell(point, out _)) return RejectReason.InvalidTarget;
                    if (!inRange(definition, from, point)) return RejectReason.OutOfRange;
                    if (definition.RequiresLineOfSight && !spatial.HasLineOfSight(from, point)) return RejectReason.NoLineOfSight;
                    if (definition.Radius <= 0f) break;
                    for (int slot = 0; slot < units.Capacity; slot++)
                    {
                        UnitId other = units.GetIdAtSlot(slot);
                        if (other.IsNone || !isEligible(other, revives)) continue;
                        if (units.AreHostile(actor, other) != (definition.TargetTeam == AbilityTeam.Hostile)) continue;
                        Vector2 position = spatial.GetPosition(other);
                        if (Vector2.Distance(point, position) > definition.Radius + rangeTolerance) continue;
                        if (definition.RequiresLineOfSight && !spatial.HasLineOfSight(point, position)) continue;
                        affected.Add(other);
                    }
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(ability));
            }

            if (hasEffect(definition, AbilityEffectKind.Attack) && resources.GetCarried(actor, combat.GetWeapon(actor).AmmoType) == 0)
                return RejectReason.NoAmmo;
            return RejectReason.None;
        }

        private bool isEligible(UnitId unit, bool revives) => revives ? combat.IsDowned(unit) : !combat.IsOutOfAction(unit);

        private static bool inRange(in AbilityDefinition definition, Vector2 from, Vector2 to) =>
            definition.Range <= 0f || Vector2.Distance(from, to) <= definition.Range + rangeTolerance;

        private static bool hasEffect(in AbilityDefinition definition, AbilityEffectKind kind)
        {
            if (definition.Effects == null) return false;
            for (int i = 0; i < definition.Effects.Length; i++)
                if (definition.Effects[i].Kind == kind) return true;
            return false;
        }
    }
}
