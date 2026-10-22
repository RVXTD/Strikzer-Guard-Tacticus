// Action-point reject reasons, the Trade and UseAbility commands, and windowed-field reject semantics by
// Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System.Numerics;

namespace Game.Rules
{
    /// <summary>What a <see cref="Command"/> asks the rules layer to do.</summary>
    public enum CommandKind
    {
        /// <summary>Move <see cref="Command.Actor"/> to <see cref="Command.Destination"/>.</summary>
        Move,
        /// <summary><see cref="Command.Actor"/> attacks <see cref="Command.Target"/> with its weapon.</summary>
        Attack,
        /// <summary><see cref="Command.Actor"/> picks up the ammo pile <see cref="Command.PileId"/>.</summary>
        PickUp,
        /// <summary><see cref="Command.Actor"/> ends its turn.</summary>
        EndTurn,
        /// <summary><see cref="Command.Actor"/> hands <see cref="Command.Count"/> rounds to the ally <see cref="Command.Target"/>.</summary>
        Trade,
        /// <summary>
        /// <see cref="Command.Actor"/> uses ability <see cref="Command.Ability"/>, aimed at <see cref="Command.Target"/>
        /// (Unit targeting) or <see cref="Command.Destination"/> (Point targeting).
        /// </summary>
        UseAbility,
    }

    /// <summary>
    /// A request from the player or the AI. Commands are plain data: both produce them the same way,
    /// and the rules layer validates and executes them through <see cref="Encounter.Execute"/>.
    /// Only the fields relevant to <see cref="Kind"/> are read.
    /// </summary>
    public struct Command
    {
        /// <summary>What to do.</summary>
        public CommandKind Kind;
        /// <summary>The unit acting; must be the active unit.</summary>
        public UnitId Actor;
        /// <summary>Move: the world-space point (x, z on the ground plane) to move to. UseAbility: the target point.</summary>
        public Vector2 Destination;
        /// <summary>Attack: the unit being attacked. Trade: the ally receiving the rounds. UseAbility: the target unit.</summary>
        public UnitId Target;
        /// <summary>PickUp only: the id of the ammo pile.</summary>
        public int PileId;
        /// <summary>Trade only: the number of rounds handed over.</summary>
        public int Count;
        /// <summary>UseAbility only: index of the ability in <see cref="EncounterSetup.Abilities"/>.</summary>
        public int Ability;

        /// <summary>A move command.</summary>
        public static Command Move(UnitId actor, Vector2 destination) =>
            new Command { Kind = CommandKind.Move, Actor = actor, Destination = destination };

        /// <summary>An attack command.</summary>
        public static Command Attack(UnitId actor, UnitId target) =>
            new Command { Kind = CommandKind.Attack, Actor = actor, Target = target };

        /// <summary>A pick-up command.</summary>
        public static Command PickUp(UnitId actor, int pileId) =>
            new Command { Kind = CommandKind.PickUp, Actor = actor, PileId = pileId };

        /// <summary>An end-turn command.</summary>
        public static Command EndTurn(UnitId actor) =>
            new Command { Kind = CommandKind.EndTurn, Actor = actor };

        /// <summary>A trade command: <paramref name="actor"/> hands <paramref name="count"/> rounds of its weapon's ammo to <paramref name="receiver"/>.</summary>
        public static Command Trade(UnitId actor, UnitId receiver, int count) =>
            new Command { Kind = CommandKind.Trade, Actor = actor, Target = receiver, Count = count };

        /// <summary>
        /// An ability command. Pass the target unit for Unit targeting and the target point for Point targeting;
        /// the other is ignored.
        /// </summary>
        public static Command UseAbility(UnitId actor, int ability, UnitId target, Vector2 point) =>
            new Command { Kind = CommandKind.UseAbility, Actor = actor, Ability = ability, Target = target, Destination = point };
    }

    /// <summary>Why a command was rejected. A rejected command never changes any state.</summary>
    public enum RejectReason
    {
        /// <summary>Not rejected.</summary>
        None,
        /// <summary>The encounter has already ended.</summary>
        EncounterOver,
        /// <summary>The actor is not the unit whose turn it is.</summary>
        NotActiveUnit,
        /// <summary>The actor has fewer action points left this turn than the command costs.</summary>
        NotEnoughActionPoints,
        /// <summary>The destination is outside the map, in a blocked cell, or occupied by another unit.</summary>
        DestinationBlocked,
        /// <summary>No path leads to the destination.</summary>
        Unreachable,
        /// <summary>
        /// The destination is reachable but farther than twice the actor's movement budget. When it lies beyond the
        /// distance field's range, its exact distance is not known and <see cref="MovePlan.Cost"/> is infinite.
        /// </summary>
        InsufficientMovement,
        /// <summary>The target is not a registered unit still in action (or, for a trade, is the actor itself).</summary>
        InvalidTarget,
        /// <summary>The target is on the actor's own team.</summary>
        TargetNotHostile,
        /// <summary>The target is not on the actor's team.</summary>
        TargetNotFriendly,
        /// <summary>The trade's receiver is not inside the giver's zone of control.</summary>
        ReceiverOutsideZone,
        /// <summary>The trade's count is less than 1 or more than the actor carries.</summary>
        InvalidAmount,
        /// <summary>The actor does not know the ability.</summary>
        AbilityNotAvailable,
        /// <summary>The ability's target unit or point is farther away than its range.</summary>
        OutOfRange,
        /// <summary>The mover is suppressed and cannot move this turn.</summary>
        Pinned,
        /// <summary>The actor cannot see the target.</summary>
        NoLineOfSight,
        /// <summary>The actor has no ammunition for its weapon.</summary>
        NoAmmo,
        /// <summary>No ammo pile has the given id.</summary>
        PileNotFound,
        /// <summary>The ammo pile is farther away than the pick-up radius.</summary>
        OutOfPickupRange,
    }

    /// <summary>Outcome of <see cref="Encounter.Execute"/>.</summary>
    public readonly struct CommandResult
    {
        /// <summary><see cref="RejectReason.None"/> if the command was executed.</summary>
        public readonly RejectReason Reason;

        private CommandResult(RejectReason reason) { Reason = reason; }

        /// <summary>True if the command was executed.</summary>
        public bool Accepted => Reason == RejectReason.None;

        /// <summary>An accepted result.</summary>
        public static CommandResult Ok => new CommandResult(RejectReason.None);

        /// <summary>A rejected result with the given reason.</summary>
        public static CommandResult Reject(RejectReason reason) => new CommandResult(reason);

        /// <inheritdoc/>
        public override string ToString() => Accepted ? "Accepted" : $"Rejected({Reason})";
    }
}
