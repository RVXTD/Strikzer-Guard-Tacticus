// EncounterSetup.Obstacles (cell obstacle model), the action-point turn economy, downed units, the
// kill-release pickup refund, trading, and abilities (overwatch, suppression, revive, blast) with
// reaction abilities, the windowed field with the eikonal solver as default, and command validation for
// previews, and the AI, by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>Everything needed to build an <see cref="Encounter"/>. Defaults are filled in by the constructor.</summary>
    public sealed class EncounterSetup
    {
        /// <summary>Number of cells along world X.</summary>
        public int Width;
        /// <summary>Number of cells along world Z.</summary>
        public int Height;
        /// <summary>Cell edge length in metres.</summary>
        public float CellSize = 1f;
        /// <summary>World position of the corner of cell (0, 0).</summary>
        public Vector2 Origin;
        /// <summary>Per cell: true if it has floor. Length Width * Height.</summary>
        public bool[] Floor;
        /// <summary>Per cell: what stands on it (<see cref="CellObstacle.None"/> if nothing). Length Width * Height.</summary>
        public CellObstacle[] Obstacles;
        /// <summary>Maximum number of units.</summary>
        public int UnitCapacity = 64;
        /// <summary>Number of distinct ammo types.</summary>
        public int AmmoTypeCount = 1;
        /// <summary>Seed for every random roll in the encounter.</summary>
        public ulong Seed = 1;
        /// <summary>Hit-chance numbers.</summary>
        public HitChanceTuning HitTuning = HitChanceTuning.Default;
        /// <summary>Round-fate numbers.</summary>
        public AmmoFateTuning FateTuning = AmmoFateTuning.Default;
        /// <summary>Pick-up radius in metres.</summary>
        public float PickupRadius = 1.5f;
        /// <summary>Radius within which landing rounds join an existing pile, in metres.</summary>
        public float PileMergeRadius = 1f;
        /// <summary>Distance field solver; null uses <see cref="EikonalDistanceFieldSolver"/>, the most accurate.</summary>
        public IDistanceFieldSolver Solver;
        /// <summary>The ability table; <see cref="UnitSetup.Abilities"/> holds indices into it. Null for none.</summary>
        public AbilityDefinition[] Abilities;
    }

    /// <summary>
    /// Composition root of the rules layer: constructs every system, wires them together explicitly,
    /// and is the only entry point for changing game state (<see cref="Execute"/>). Unity's
    /// EncounterRoot component owns one instance per combat.
    /// </summary>
    /// <remarks>
    /// Systems are exposed for read-only queries (HUD, AI, shaders). Code outside the rules layer
    /// changes state only through <see cref="Execute"/>; every change is recorded in <see cref="Events"/>.
    /// </remarks>
    public sealed class Encounter
    {
        /// <summary>Unit handles and teams.</summary>
        public readonly UnitRegistry Units;
        /// <summary>The single random source.</summary>
        public readonly GameRandom Random;
        /// <summary>Everything that has happened, in order.</summary>
        public readonly EventLog Events;
        /// <summary>Map, positions, sight, distance field, zones.</summary>
        public readonly SpatialSystem Spatial;
        /// <summary>Status effects.</summary>
        public readonly StatusSystem Status;
        /// <summary>Health, weapons, hit chance.</summary>
        public readonly CombatSystem Combat;
        /// <summary>Movement budgets and move planning.</summary>
        public readonly MovementSystem Movement;
        /// <summary>Ammunition.</summary>
        public readonly ResourceSystem Resources;
        /// <summary>Turn loop.</summary>
        public readonly EncounterFlow Flow;
        /// <summary>Ability definitions and which abilities each unit knows.</summary>
        public readonly AbilitySystem Abilities;
        /// <summary>Chooses commands for AI-controlled units.</summary>
        public readonly AiSystem Ai;

        private readonly MovePlan plan = new MovePlan();
        private readonly List<UnitId> abilityTargets = new List<UnitId>(8);
        private readonly List<RoundOutcome> outcomes = new List<RoundOutcome>(32);

        /// <summary>Builds an encounter on the given map. Add units with <see cref="AddUnit"/>, then call <see cref="Start"/>.</summary>
        public Encounter(EncounterSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            Units = new UnitRegistry(setup.UnitCapacity);
            Random = new GameRandom(setup.Seed);
            Events = new EventLog();
            Spatial = new SpatialSystem(Units, setup.Width, setup.Height, setup.CellSize, setup.Origin,
                                        setup.Floor, setup.Obstacles, setup.Solver ?? new EikonalDistanceFieldSolver());
            Status = new StatusSystem(Units);
            Combat = new CombatSystem(Units, Spatial, Status, Random, setup.HitTuning, setup.FateTuning);
            Movement = new MovementSystem(Units, Spatial);
            Resources = new ResourceSystem(Units, setup.AmmoTypeCount, setup.PickupRadius, setup.PileMergeRadius);
            Flow = new EncounterFlow(Units, Combat, Status, Random);
            Abilities = new AbilitySystem(Units, setup.Abilities, Spatial, Combat, Resources);
            Ai = new AiSystem(this);
        }

        /// <summary>
        /// Registers a unit with every system and returns its handle. Only allowed before <see cref="Start"/>.
        /// Throws <see cref="ArgumentOutOfRangeException"/> if the unit has fewer than 1 action point, a negative or
        /// NaN body radius, or an ability index outside the ability table, and <see cref="ArgumentException"/> if a player unit is marked as an objective.
        /// </summary>
        public UnitId AddUnit(in UnitSetup setup)
        {
            if (Flow.Phase != EncounterPhase.NotStarted) throw new InvalidOperationException("Units must be added before the encounter starts.");
            if (setup.ActionPoints < 1) throw new ArgumentOutOfRangeException(nameof(setup), "A unit needs at least 1 action point per turn.");
            if (!(setup.BodyRadius >= 0f)) throw new ArgumentOutOfRangeException(nameof(setup), "A unit's body radius must be zero or more.");
            if (setup.IsObjective && setup.Team == Team.Player) throw new ArgumentException("Only enemy units can be objectives.", nameof(setup));
            if (!Abilities.AreValidIndices(setup.Abilities)) throw new ArgumentOutOfRangeException(nameof(setup), "Every ability index must refer to EncounterSetup.Abilities.");
            UnitId id = Units.Create(setup.Team);
            Spatial.AddUnit(id, setup.Position, setup.Facing, setup.Zone, setup.BodyRadius);
            Combat.AddUnit(id, setup.MaxHealth, setup.Accuracy, setup.Weapon);
            Movement.AddUnit(id, setup.MovementBudget);
            Resources.AddUnit(id, setup.Weapon.AmmoType, setup.StartingAmmo);
            Flow.AddUnit(id, setup.Initiative, setup.ActionPoints, setup.IsObjective);
            Abilities.AddUnit(id, setup.Abilities);
            Ai.AddUnit(id, setup.Ai);
            return id;
        }

        /// <summary>Rolls turn order and begins the first turn.</summary>
        public void Start()
        {
            Flow.Start(Events);
            beginActiveTurn();
        }

        /// <summary>
        /// Plans a move for the UI or AI to preview (path, action-point cost, reaction warnings). Changes no
        /// game state. The active unit is priced against its remaining points, any other unit against a full turn's.
        /// </summary>
        public RejectReason PlanMove(UnitId mover, Vector2 destination, MovePlan into) =>
            planMove(mover, destination, into);

        /// <summary>
        /// Checks an ability use for the UI or AI to preview and fills <paramref name="affected"/> with the units it
        /// would affect. Changes no game state.
        /// </summary>
        public RejectReason PreviewAbility(UnitId actor, int ability, UnitId target, Vector2 point, List<UnitId> affected)
        {
            int availableActionPoints = actor == Flow.ActiveUnit ? Flow.ActiveUnitActionPoints : Flow.GetActionPointsPerTurn(actor);
            return Abilities.Validate(actor, ability, target, point, availableActionPoints, affected);
        }

        /// <summary>Hit chance for the UI to preview. Changes no game state.</summary>
        public HitChanceBreakdown PreviewAttack(UnitId attacker, UnitId target) =>
            Combat.PreviewHitChance(attacker, target);

        /// <summary>
        /// Checks a command without executing it: returns the reason <see cref="Execute"/> would reject it with, or
        /// <see cref="RejectReason.None"/> if it would be accepted. Changes no game state. Previews use it, so what the
        /// UI offers and what the rules accept cannot disagree.
        /// </summary>
        public RejectReason Validate(in Command command)
        {
            if (Flow.Phase == EncounterPhase.NotStarted) throw new InvalidOperationException("Call Start before validating commands.");
            if (Flow.Phase == EncounterPhase.Ended) return RejectReason.EncounterOver;
            if (command.Actor != Flow.ActiveUnit) return RejectReason.NotActiveUnit;

            switch (command.Kind)
            {
                case CommandKind.Move: return planMove(command.Actor, command.Destination, plan);
                case CommandKind.Attack: return validateAttack(command.Actor, command.Target);
                case CommandKind.PickUp: return validatePickUp(command.Actor, command.PileId);
                case CommandKind.EndTurn: return RejectReason.None;
                case CommandKind.Trade: return validateTrade(command.Actor, command.Target, command.Count);
                case CommandKind.UseAbility:
                    return Abilities.Validate(command.Actor, command.Ability, command.Target, command.Destination,
                                              Flow.ActiveUnitActionPoints, abilityTargets);
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
        }

        /// <summary>
        /// True if <paramref name="unit"/> could still do something that costs no action points: hand rounds to an
        /// ally inside its zone, or use a known ability that costs nothing. When the active unit has no points left
        /// and this is false, presentation and the AI send <see cref="CommandKind.EndTurn"/> themselves.
        /// </summary>
        public bool HasFreeAction(UnitId unit)
        {
            IReadOnlyList<int> known = Abilities.GetAbilities(unit);
            for (int i = 0; i < known.Count; i++)
                if (Abilities.GetDefinition(known[i]).ActionPointCost == 0) return true;
            for (int slot = 0; slot < Units.Capacity; slot++)
            {
                UnitId other = Units.GetIdAtSlot(slot);
                if (!other.IsNone && validateTrade(unit, other, 1) == RejectReason.None) return true;
            }
            return false;
        }

        /// <summary>
        /// Validates and executes a command from the player or the AI. A rejected command changes nothing;
        /// an accepted one appends everything it caused to <see cref="Events"/>.
        /// </summary>
        public CommandResult Execute(in Command command)
        {
            if (Flow.Phase == EncounterPhase.NotStarted) throw new InvalidOperationException("Call Start before executing commands.");
            if (Flow.Phase == EncounterPhase.Ended) return CommandResult.Reject(RejectReason.EncounterOver);
            if (command.Actor != Flow.ActiveUnit) return CommandResult.Reject(RejectReason.NotActiveUnit);

            RejectReason reason;
            switch (command.Kind)
            {
                case CommandKind.Move: reason = executeMove(command.Actor, command.Destination); break;
                case CommandKind.Attack: reason = executeAttack(command.Actor, command.Target); break;
                case CommandKind.PickUp: reason = executePickUp(command.Actor, command.PileId); break;
                case CommandKind.EndTurn: reason = executeEndTurn(command.Actor); break;
                case CommandKind.Trade: reason = executeTrade(command.Actor, command.Target, command.Count); break;
                case CommandKind.UseAbility: reason = executeAbility(command.Actor, command.Ability, command.Target, command.Destination); break;
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
            return reason == RejectReason.None ? CommandResult.Ok : CommandResult.Reject(reason);
        }

        private RejectReason planMove(UnitId mover, Vector2 destination, MovePlan into)
        {
            if (Status.IsSuppressed(mover))
            {
                into.Clear();
                return RejectReason.Pinned;
            }
            int availableActionPoints = mover == Flow.ActiveUnit ? Flow.ActiveUnitActionPoints : Flow.GetActionPointsPerTurn(mover);
            RejectReason reason = Movement.PlanMove(mover, destination, availableActionPoints, into);
            // A zone whose owner has already reacted this round neither warns nor triggers.
            into.Interrupts.RemoveAll(entry => !Flow.HasReaction(entry.Owner));
            return reason;
        }

        private RejectReason executeMove(UnitId mover, Vector2 destination)
        {
            RejectReason reason = planMove(mover, destination, plan);
            if (reason != RejectReason.None) return reason;
            Flow.SpendActionPoints(plan.ActionPointCost, Events);

            List<Vector2> path = plan.Path;
            Vector2 current = path[0];
            int nextWaypoint = 1;
            bool stopped = false;

            for (int i = 0; i < plan.Interrupts.Count && !stopped; i++)
            {
                ZoneEntry entry = plan.Interrupts[i];
                while (nextWaypoint <= entry.SegmentIndex)
                    step(mover, ref current, path[nextWaypoint++]);
                step(mover, ref current, entry.Position);

                UnitId owner = entry.Owner;
                if (Combat.IsOutOfAction(owner) || !Spatial.HasLineOfSight(Spatial.GetPosition(owner), current)) continue;
                if (!tryReact(owner, mover, current, out int hits)) continue;

                Flow.SpendReaction(owner);
                clearTemporaryZone(owner); // an overwatch cone is spent once its owner reacts
                if (hits > 0 || Status.IsSuppressed(mover) || Combat.IsOutOfAction(mover))
                {
                    stopped = true;
                    Events.Add(new GameEvent { Kind = EventKind.MoveInterrupted, Unit = mover, Other = owner, Position = current });
                }
            }
            while (!stopped && nextWaypoint < path.Count)
                step(mover, ref current, path[nextWaypoint++]);

            if (Flow.CheckForEnd(Events)) return RejectReason.None;
            if (Combat.IsOutOfAction(mover)) return executeEndTurn(mover); // a downed unit's turn ends at once
            Movement.RebuildField(mover);
            return RejectReason.None;
        }

        /// <summary>
        /// Makes <paramref name="owner"/> react to <paramref name="mover"/> at <paramref name="moverPosition"/>, with its
        /// reaction ability if it has one and otherwise a plain burst. Returns false if it could not react (no ammo,
        /// or the ability's conditions are not met); <paramref name="hits"/> counts rounds or blasts that hit.
        /// </summary>
        private bool tryReact(UnitId owner, UnitId mover, Vector2 moverPosition, out int hits)
        {
            hits = 0;
            if (!Abilities.TryGetReactionAbility(owner, out int ability))
            {
                AttackResult result = resolveAttack(owner, mover, isReaction: true);
                hits = result.Hits;
                return result.RoundsFired > 0;
            }
            if (Abilities.Validate(owner, ability, mover, moverPosition, int.MaxValue, abilityTargets) != RejectReason.None) return false;
            Events.Add(new GameEvent { Kind = EventKind.AbilityUsed, Unit = owner, Other = mover, Position = moverPosition, Value = ability, IsReaction = true });
            hits = applyAbility(owner, Abilities.GetDefinition(ability), moverPosition, isReaction: true);
            return true;
        }

        private void step(UnitId mover, ref Vector2 current, Vector2 next)
        {
            if (Vector2.Distance(current, next) < 1e-6f) return;
            Spatial.FaceTowards(mover, next - current);
            Spatial.SetPosition(mover, next);
            current = next;
            Events.Add(new GameEvent { Kind = EventKind.UnitMoved, Unit = mover, Position = next });
        }

        private RejectReason validateAttack(UnitId attacker, UnitId target)
        {
            if (Flow.ActiveUnitActionPoints < 1) return RejectReason.NotEnoughActionPoints;
            if (!Units.IsValid(target) || Combat.IsOutOfAction(target)) return RejectReason.InvalidTarget;
            if (!Units.AreHostile(attacker, target)) return RejectReason.TargetNotHostile;
            if (!Spatial.HasLineOfSight(Spatial.GetPosition(attacker), Spatial.GetPosition(target))) return RejectReason.NoLineOfSight;
            if (Resources.GetCarried(attacker, Combat.GetWeapon(attacker).AmmoType) == 0) return RejectReason.NoAmmo;
            return RejectReason.None;
        }

        private RejectReason executeAttack(UnitId attacker, UnitId target)
        {
            RejectReason reason = validateAttack(attacker, target);
            if (reason != RejectReason.None) return reason;

            Flow.SpendActionPoints(1, Events);
            resolveAttack(attacker, target, isReaction: false);
            if (Flow.CheckForEnd(Events)) return RejectReason.None;
            return executeEndTurn(attacker); // attacking ends the turn
        }

        private RejectReason validatePickUp(UnitId unit, int pileId)
        {
            if (Flow.ActiveUnitActionPoints < 1) return RejectReason.NotEnoughActionPoints;
            return Resources.CanPickUp(Spatial.GetPosition(unit), pileId);
        }

        private RejectReason executePickUp(UnitId unit, int pileId)
        {
            RejectReason reason = validatePickUp(unit, pileId);
            if (reason != RejectReason.None) return reason;
            Vector2 position = Spatial.GetPosition(unit);
            Resources.TryGetPile(pileId, out AmmoPile pile);
            Flow.SpendActionPoints(1, Events);
            Resources.PickUp(unit, position, pileId, Events);
            if (pile.Origin == PileOrigin.Released) Flow.RefundActionPoints(1, Events); // recovering a kill's ammo is free
            return RejectReason.None;
        }

        private RejectReason validateTrade(UnitId giver, UnitId receiver, int count)
        {
            if (!Units.IsValid(receiver) || receiver == giver || Combat.IsOutOfAction(receiver)) return RejectReason.InvalidTarget;
            if (Units.AreHostile(giver, receiver)) return RejectReason.TargetNotFriendly;
            if (!Spatial.IsInsideZone(giver, Spatial.GetPosition(receiver))) return RejectReason.ReceiverOutsideZone;
            if (count < 1 || count > Resources.GetCarried(giver, Combat.GetWeapon(giver).AmmoType)) return RejectReason.InvalidAmount;
            return RejectReason.None;
        }

        private RejectReason executeTrade(UnitId giver, UnitId receiver, int count)
        {
            RejectReason reason = validateTrade(giver, receiver, count);
            if (reason != RejectReason.None) return reason;

            Resources.Trade(giver, receiver, Combat.GetWeapon(giver).AmmoType, count, Events); // trading costs no action points
            return RejectReason.None;
        }

        private RejectReason executeAbility(UnitId actor, int ability, UnitId target, Vector2 point)
        {
            RejectReason reason = Abilities.Validate(actor, ability, target, point, Flow.ActiveUnitActionPoints, abilityTargets);
            if (reason != RejectReason.None) return reason;
            AbilityDefinition definition = Abilities.GetDefinition(ability);

            Flow.SpendActionPoints(definition.ActionPointCost, Events);
            UnitId aimedAt = definition.Targeting == AbilityTargeting.Unit ? target : UnitId.None;
            Events.Add(new GameEvent { Kind = EventKind.AbilityUsed, Unit = actor, Other = aimedAt, Position = point, Value = ability });
            applyAbility(actor, definition, point, isReaction: false);

            if (Flow.CheckForEnd(Events)) return RejectReason.None;
            if (definition.EndsTurn) return executeEndTurn(actor);
            Movement.RebuildField(actor); // a unit taken out or revived changes what blocks the actor's movement
            return RejectReason.None;
        }

        /// <summary>
        /// Applies an ability's effects in order to the units in <see cref="abilityTargets"/> (PlaceZone applies to the
        /// user, facing <paramref name="point"/>). Returns how many rounds or blasts hit.
        /// </summary>
        private int applyAbility(UnitId user, in AbilityDefinition definition, Vector2 point, bool isReaction)
        {
            int hits = 0;
            if (definition.Effects == null) return hits;
            for (int e = 0; e < definition.Effects.Length; e++)
            {
                AbilityEffect effect = definition.Effects[e];
                if (effect.Kind == AbilityEffectKind.PlaceZone)
                {
                    Vector2 facing = point - Spatial.GetPosition(user);
                    Spatial.FaceTowards(user, facing);
                    Spatial.PlaceTemporaryZone(user, effect.Zone, facing);
                    Events.Add(new GameEvent { Kind = EventKind.ZonePlaced, Unit = user, Position = point });
                    continue;
                }
                for (int t = 0; t < abilityTargets.Count; t++)
                {
                    UnitId target = abilityTargets[t];
                    // A unit taken out by an earlier effect is not affected by later ones (revive aside).
                    if (effect.Kind != AbilityEffectKind.Revive && Combat.IsOutOfAction(target)) continue;
                    switch (effect.Kind)
                    {
                        case AbilityEffectKind.Attack:
                            hits += resolveAttack(user, target, isReaction).Hits;
                            break;
                        case AbilityEffectKind.Damage:
                            AttackResult result = Combat.ApplyDamage(user, target, effect.Amount, Events);
                            handleTakenOut(target, result);
                            hits += result.Hits;
                            break;
                        case AbilityEffectKind.ApplyStatus:
                            Status.Apply(target, effect.Status, effect.Magnitude, effect.Turns, Events);
                            break;
                        case AbilityEffectKind.Revive:
                            if (!Combat.IsDowned(target)) break;
                            Combat.Revive(target, user, effect.Amount, Events);
                            Spatial.SetProjectsZone(target, true);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(definition), $"Unknown ability effect {effect.Kind}.");
                    }
                }
            }
            return hits;
        }

        private AttackResult resolveAttack(UnitId attacker, UnitId target, bool isReaction)
        {
            WeaponStats weapon = Combat.GetWeapon(attacker);
            int rounds = Math.Min(weapon.RoundsPerAttack, Resources.GetCarried(attacker, weapon.AmmoType));
            if (rounds <= 0) return default;

            Spatial.FaceTowards(attacker, Spatial.GetPosition(target) - Spatial.GetPosition(attacker));
            outcomes.Clear();
            AttackResult result = Combat.ResolveAttack(attacker, target, rounds, isReaction, Events, outcomes);
            Resources.Consume(attacker, weapon.AmmoType, result.RoundsFired);
            Resources.ApplyOutcomes(outcomes, Events);
            handleTakenOut(target, result);
            return result;
        }

        /// <summary>Applies the map and ammo consequences of a unit being defeated or downed.</summary>
        private void handleTakenOut(UnitId target, in AttackResult result)
        {
            if (result.TargetDefeated)
            {
                Spatial.SetOccupiesSpace(target, false);
                clearTemporaryZone(target);
                Resources.ReleaseEmbedded(target, Spatial.GetPosition(target), Events);
            }
            else if (result.TargetDowned)
            {
                // A downed unit's body still blocks movement and keeps its embedded rounds until revived.
                Spatial.SetProjectsZone(target, false);
                clearTemporaryZone(target);
            }
        }

        private void clearTemporaryZone(UnitId unit)
        {
            if (Spatial.ClearTemporaryZone(unit))
                Events.Add(new GameEvent { Kind = EventKind.ZoneRemoved, Unit = unit });
        }

        private RejectReason executeEndTurn(UnitId unit)
        {
            Status.EndTurn(unit, Events);
            Flow.EndTurn(Events);
            beginActiveTurn();
            return RejectReason.None;
        }

        private void beginActiveTurn()
        {
            if (Flow.Phase != EncounterPhase.AwaitingCommand) return;
            UnitId active = Flow.ActiveUnit;
            clearTemporaryZone(active); // overwatch lasts until the owner's next turn
            Movement.BeginTurn(active, Movement.GetBaseBudget(active) + Status.GetMovementModifier(active));
            Movement.RebuildField(active);
        }
    }
}
