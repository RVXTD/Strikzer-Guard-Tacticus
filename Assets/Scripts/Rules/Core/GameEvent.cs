// ActionPointsChanged, UnitDowned, AmmoTraded and ability events by Claude (Anthropic), October 2026;
// to be reviewed by Alexander Stangl per the course syllabus.

using System.Collections.Generic;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>What happened in a <see cref="GameEvent"/>. Each value documents which fields it sets.</summary>
    public enum EventKind
    {
        /// <summary>A new round of turns began. Value = round number (1-based).</summary>
        RoundStarted,
        /// <summary>Unit's turn began. Value = action points for the turn.</summary>
        TurnStarted,
        /// <summary>Unit's turn ended.</summary>
        TurnEnded,
        /// <summary>Unit spent action points. Value = points left this turn.</summary>
        ActionPointsChanged,
        /// <summary>Unit moved in a straight line to Position. A move emits one event per waypoint.</summary>
        UnitMoved,
        /// <summary>Unit's move was stopped at Position by Other's reaction.</summary>
        MoveInterrupted,
        /// <summary>Unit attacked Other. Value = rounds fired; IsReaction marks zone-of-control reactions.</summary>
        AttackStarted,
        /// <summary>One round from Unit hit Other. Value = damage dealt (reduced for a glancing round).</summary>
        RoundHit,
        /// <summary>One round from Unit missed Other.</summary>
        RoundMissed,
        /// <summary>Unit (an enemy) was reduced to zero health and is out of the encounter.</summary>
        UnitDefeated,
        /// <summary>Unit (a player unit) was reduced to zero health at Position and is downed until revived.</summary>
        UnitDowned,
        /// <summary>Unit took Value damage from Other without a round being fired (e.g. a blast).</summary>
        UnitDamaged,
        /// <summary>Downed Unit was revived by Other with Value health.</summary>
        UnitRevived,
        /// <summary>
        /// Unit used ability Value (an index into the ability table), aimed at Other (Unit targeting) or Position
        /// (Point targeting). IsReaction marks abilities used as zone-of-control reactions.
        /// </summary>
        AbilityUsed,
        /// <summary>Unit gained a temporary zone of control (overwatch) facing Position.</summary>
        ZonePlaced,
        /// <summary>Unit's temporary zone of control ended (it fired, was downed, or its next turn began).</summary>
        ZoneRemoved,
        /// <summary>One round of AmmoType embedded in Unit; recoverable once Unit is defeated.</summary>
        AmmoEmbedded,
        /// <summary>Value rounds of AmmoType landed at Position and joined pile PileId.</summary>
        AmmoScattered,
        /// <summary>One round of AmmoType was lost for good.</summary>
        AmmoLost,
        /// <summary>Value rounds of AmmoType embedded in defeated Unit dropped into pile PileId at Position.</summary>
        AmmoReleased,
        /// <summary>Unit picked up pile PileId: Value rounds of AmmoType.</summary>
        AmmoPickedUp,
        /// <summary>Unit handed Value rounds of AmmoType to its ally Other.</summary>
        AmmoTraded,
        /// <summary>A status effect was applied to Unit. Value = (int)StatusKind.</summary>
        StatusApplied,
        /// <summary>A status effect on Unit expired. Value = (int)StatusKind.</summary>
        StatusExpired,
        /// <summary>The encounter ended. Value = (int)Team of the winning side.</summary>
        EncounterEnded,
    }

    /// <summary>
    /// One state change produced by the rules layer. Presentation plays events back in order;
    /// fields not listed for an <see cref="EventKind"/> are left at their defaults.
    /// </summary>
    public struct GameEvent
    {
        /// <summary>What happened.</summary>
        public EventKind Kind;
        /// <summary>The unit the event is about (the actor, the mover, the attacker).</summary>
        public UnitId Unit;
        /// <summary>The other unit involved (the target, the reacting unit).</summary>
        public UnitId Other;
        /// <summary>A world-space position on the ground plane.</summary>
        public Vector2 Position;
        /// <summary>A count, amount or enum value, as documented per kind.</summary>
        public int Value;
        /// <summary>Ammo type index, for ammo events.</summary>
        public int AmmoType;
        /// <summary>Ammo pile id, for pile events.</summary>
        public int PileId;
        /// <summary>True for attacks and abilities made as zone-of-control reactions.</summary>
        public bool IsReaction;
    }

    /// <summary>
    /// Append-only, ordered record of everything that happened in an encounter. Presentation keeps
    /// its own read cursor and consumes events from it; the rules layer never reads the log back.
    /// </summary>
    public sealed class EventLog
    {
        private readonly List<GameEvent> events = new List<GameEvent>(256);

        /// <summary>Number of events recorded so far.</summary>
        public int Count => events.Count;

        /// <summary>The event at a position in the log (0 = oldest).</summary>
        public GameEvent this[int index] => events[index];

        /// <summary>Appends an event to the end of the log.</summary>
        public void Add(in GameEvent e) => events.Add(e);
    }
}
