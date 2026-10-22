// Action points, once-per-round reactions, objectives and the all-downed loss by Claude (Anthropic),
// October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>Where the encounter is in its lifecycle.</summary>
    public enum EncounterPhase
    {
        /// <summary>Units are being added; no turns yet.</summary>
        NotStarted,
        /// <summary>Waiting for the active unit's commands.</summary>
        AwaitingCommand,
        /// <summary>One side has no undefeated units left.</summary>
        Ended,
    }

    /// <summary>
    /// The turn loop: rolls turn order each round, tracks whose turn it is, the active unit's action
    /// points and every unit's reaction, and detects the end of the encounter.
    /// </summary>
    /// <remarks>
    /// Selecting, previewing and animating are presentation states; the rules layer resolves each
    /// command completely, so it only ever waits for a command or is over.
    /// Turn order is rolled at the start of every round as base initiative + initiative modifiers + 1d10,
    /// highest first, ties broken by slot index. Defeated and downed units are skipped. A unit's action points are
    /// refilled and its reaction restored when its turn starts; running out of points does not end the
    /// turn, so free actions remain possible until an EndTurn command.
    /// </remarks>
    public sealed class EncounterFlow
    {
        private readonly UnitRegistry units;
        private readonly CombatSystem combat;
        private readonly StatusSystem status;
        private readonly GameRandom random;
        private readonly int[] baseInitiative;
        private readonly int[] rolledInitiative;
        private readonly bool[] participating;
        private readonly int[] actionPointsPerTurn;
        private readonly bool[] reactionAvailable;
        private readonly bool[] objective;
        private readonly List<UnitId> order = new List<UnitId>(16);
        private int turnIndex;

        /// <summary>Creates the turn loop.</summary>
        public EncounterFlow(UnitRegistry units, CombatSystem combat, StatusSystem status, GameRandom random)
        {
            this.units = units;
            this.combat = combat;
            this.status = status;
            this.random = random;
            baseInitiative = new int[units.Capacity];
            rolledInitiative = new int[units.Capacity];
            participating = new bool[units.Capacity];
            actionPointsPerTurn = new int[units.Capacity];
            reactionAvailable = new bool[units.Capacity];
            objective = new bool[units.Capacity];
        }

        /// <summary>Current phase.</summary>
        public EncounterPhase Phase { get; private set; } = EncounterPhase.NotStarted;

        /// <summary>Current round, starting at 1 (0 before the encounter starts).</summary>
        public int Round { get; private set; }

        /// <summary>The unit whose turn it is, or <see cref="UnitId.None"/> outside <see cref="EncounterPhase.AwaitingCommand"/>.</summary>
        public UnitId ActiveUnit { get; private set; }

        /// <summary>Action points the active unit has left this turn (0 outside a turn).</summary>
        public int ActiveUnitActionPoints { get; private set; }

        /// <summary>The winning side; meaningful only once <see cref="Phase"/> is <see cref="EncounterPhase.Ended"/>.</summary>
        public Team Winner { get; private set; }

        /// <summary>This round's turn order, including units defeated since it was rolled.</summary>
        public IReadOnlyList<UnitId> TurnOrder => order;

        /// <summary>Index of the active unit in <see cref="TurnOrder"/>.</summary>
        public int TurnIndex => turnIndex;

        /// <summary>
        /// Adds a unit to the turn loop with its reaction available. Only allowed before <see cref="Start"/>.
        /// Throws <see cref="ArgumentOutOfRangeException"/> if <paramref name="actionPoints"/> is less than 1,
        /// and <see cref="ArgumentException"/> if <paramref name="isObjective"/> is set on a player unit.
        /// </summary>
        public void AddUnit(UnitId id, int initiative, int actionPoints, bool isObjective)
        {
            units.Require(id);
            if (Phase != EncounterPhase.NotStarted) throw new InvalidOperationException("Units must be added before the encounter starts.");
            if (actionPoints < 1) throw new ArgumentOutOfRangeException(nameof(actionPoints), "A unit needs at least 1 action point per turn.");
            baseInitiative[id.Index] = initiative;
            participating[id.Index] = true;
            actionPointsPerTurn[id.Index] = actionPoints;
            if (isObjective && units.GetTeam(id) == Team.Player) throw new ArgumentException("Only enemy units can be objectives.", nameof(isObjective));
            reactionAvailable[id.Index] = true;
            objective[id.Index] = isObjective;
        }

        /// <summary>Action points the unit receives at the start of each of its turns.</summary>
        public int GetActionPointsPerTurn(UnitId id)
        {
            units.Require(id);
            return actionPointsPerTurn[id.Index];
        }

        /// <summary>True if the unit can still make a reaction before its next turn starts.</summary>
        public bool HasReaction(UnitId id)
        {
            units.Require(id);
            return reactionAvailable[id.Index];
        }

        /// <summary>Uses up the unit's reaction until its next turn starts.</summary>
        public void SpendReaction(UnitId id)
        {
            units.Require(id);
            reactionAvailable[id.Index] = false;
        }

        /// <summary>Starts round 1 and the first unit's turn, appending RoundStarted and TurnStarted.</summary>
        public void Start(EventLog log)
        {
            if (Phase != EncounterPhase.NotStarted) throw new InvalidOperationException("The encounter has already started.");
            Phase = EncounterPhase.AwaitingCommand;
            if (CheckForEnd(log)) return;
            beginRound(log);
        }

        /// <summary>
        /// Deducts action points from the active unit and appends ActionPointsChanged.
        /// Throws <see cref="InvalidOperationException"/> if it has fewer than <paramref name="points"/> left.
        /// </summary>
        public void SpendActionPoints(int points, EventLog log)
        {
            if (Phase != EncounterPhase.AwaitingCommand) throw new InvalidOperationException("No turn is in progress.");
            if (points < 0 || points > ActiveUnitActionPoints)
                throw new InvalidOperationException($"{ActiveUnit} cannot spend {points} action points with {ActiveUnitActionPoints} left.");
            ActiveUnitActionPoints -= points;
            log.Add(new GameEvent { Kind = EventKind.ActionPointsChanged, Unit = ActiveUnit, Value = ActiveUnitActionPoints });
        }

        /// <summary>Gives action points back to the active unit (for example after a kill-release pickup) and appends ActionPointsChanged.</summary>
        public void RefundActionPoints(int points, EventLog log)
        {
            if (Phase != EncounterPhase.AwaitingCommand) throw new InvalidOperationException("No turn is in progress.");
            if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
            ActiveUnitActionPoints += points;
            log.Add(new GameEvent { Kind = EventKind.ActionPointsChanged, Unit = ActiveUnit, Value = ActiveUnitActionPoints });
        }

        /// <summary>
        /// Ends the active unit's turn and starts the next undefeated unit's turn, rolling a new round when
        /// the order is exhausted. Appends TurnEnded, possibly RoundStarted, and TurnStarted.
        /// </summary>
        public void EndTurn(EventLog log)
        {
            if (Phase != EncounterPhase.AwaitingCommand) throw new InvalidOperationException("No turn is in progress.");
            log.Add(new GameEvent { Kind = EventKind.TurnEnded, Unit = ActiveUnit });
            advanceToNextUnit(log);
        }

        /// <summary>
        /// Ends the encounter, appending EncounterEnded, if the player has won (every objective unit defeated,
        /// or every enemy if there are no objective units) or lost (every player unit downed).
        /// Returns true if the encounter is over.
        /// </summary>
        public bool CheckForEnd(EventLog log)
        {
            if (Phase == EncounterPhase.Ended) return true;
            bool playerInAction = false, enemyInAction = false, hasObjective = false, objectiveRemaining = false;
            for (int slot = 0; slot < units.Capacity; slot++)
            {
                UnitId id = units.GetIdAtSlot(slot);
                if (id.IsNone || !participating[slot]) continue;
                bool inAction = !combat.IsOutOfAction(id);
                if (units.GetTeam(id) == Team.Player)
                {
                    playerInAction |= inAction;
                    continue;
                }
                enemyInAction |= inAction;
                if (!objective[slot]) continue;
                hasObjective = true;
                objectiveRemaining |= inAction;
            }
            bool playerWon = hasObjective ? !objectiveRemaining : !enemyInAction;
            if (!playerWon && playerInAction) return false;

            Phase = EncounterPhase.Ended;
            Winner = playerWon ? Team.Player : Team.Enemy;
            ActiveUnit = UnitId.None;
            ActiveUnitActionPoints = 0;
            log.Add(new GameEvent { Kind = EventKind.EncounterEnded, Value = (int)Winner });
            return true;
        }

        private void beginRound(EventLog log)
        {
            Round++;
            order.Clear();
            for (int slot = 0; slot < units.Capacity; slot++)
            {
                UnitId id = units.GetIdAtSlot(slot);
                if (id.IsNone || !participating[slot] || combat.IsOutOfAction(id)) continue;
                rolledInitiative[slot] = baseInitiative[slot] + status.GetInitiativeModifier(id) + random.NextInt(1, 11);
                order.Add(id);
            }
            order.Sort((a, b) => rolledInitiative[a.Index] != rolledInitiative[b.Index]
                ? rolledInitiative[b.Index].CompareTo(rolledInitiative[a.Index])
                : a.Index.CompareTo(b.Index));
            if (order.Count == 0)
            {
                CheckForEnd(log);
                return;
            }
            log.Add(new GameEvent { Kind = EventKind.RoundStarted, Value = Round });
            turnIndex = -1;
            advanceToNextUnit(log);
        }

        private void advanceToNextUnit(EventLog log)
        {
            do turnIndex++;
            while (turnIndex < order.Count && (!units.IsValid(order[turnIndex]) || combat.IsOutOfAction(order[turnIndex])));

            if (turnIndex >= order.Count)
            {
                beginRound(log);
                return;
            }
            ActiveUnit = order[turnIndex];
            ActiveUnitActionPoints = actionPointsPerTurn[ActiveUnit.Index];
            reactionAvailable[ActiveUnit.Index] = true;
            log.Add(new GameEvent { Kind = EventKind.TurnStarted, Unit = ActiveUnit, Value = ActiveUnitActionPoints });
        }
    }
}
