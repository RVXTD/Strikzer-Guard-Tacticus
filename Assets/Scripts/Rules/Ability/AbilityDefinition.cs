// Written by Claude (Anthropic), October 2026: ability definitions as plain, serializable data; to be
// reviewed by Alexander Stangl per the course syllabus.

namespace Game.Rules
{
    /// <summary>What an ability is aimed at.</summary>
    public enum AbilityTargeting
    {
        /// <summary>No target; effects apply to the user.</summary>
        Self,
        /// <summary>One unit, given as <see cref="Command.Target"/>.</summary>
        Unit,
        /// <summary>A point on the map, given as <see cref="Command.Destination"/>; affects units within <see cref="AbilityDefinition.Radius"/> of it.</summary>
        Point,
    }

    /// <summary>Which units an ability may target or affect, relative to the user's team.</summary>
    public enum AbilityTeam
    {
        /// <summary>Units on the other team.</summary>
        Hostile,
        /// <summary>Units on the user's team, including the user.</summary>
        Friendly,
    }

    /// <summary>
    /// The closed set of things an ability can do. New behaviour is a new case here, handled in one
    /// place by <see cref="Encounter"/>, never a new class.
    /// </summary>
    public enum AbilityEffectKind
    {
        /// <summary>Fires the user's weapon burst at each affected unit, as a normal attack (uses ammo).</summary>
        Attack,
        /// <summary>Deals <see cref="AbilityEffect.Amount"/> damage to each affected unit without firing rounds.</summary>
        Damage,
        /// <summary>Applies <see cref="AbilityEffect.Status"/> with <see cref="AbilityEffect.Magnitude"/> for <see cref="AbilityEffect.Turns"/> turns to each affected unit.</summary>
        ApplyStatus,
        /// <summary>Gives the user the temporary zone <see cref="AbilityEffect.Zone"/>, facing the target point (overwatch).</summary>
        PlaceZone,
        /// <summary>Revives each affected (downed) unit with <see cref="AbilityEffect.Amount"/> health.</summary>
        Revive,
    }

    /// <summary>One effect of an ability. Only the fields relevant to <see cref="Kind"/> are read.</summary>
    [System.Serializable]
    public struct AbilityEffect
    {
        /// <summary>What the effect does.</summary>
        public AbilityEffectKind Kind;
        /// <summary>Damage: damage dealt. Revive: health restored.</summary>
        public int Amount;
        /// <summary>ApplyStatus: the status kind.</summary>
        public StatusKind Status;
        /// <summary>ApplyStatus: the status magnitude (ignored for <see cref="StatusKind.Suppressed"/>).</summary>
        public float Magnitude;
        /// <summary>ApplyStatus: how many of the affected unit's own turn ends the status lasts.</summary>
        public int Turns;
        /// <summary>PlaceZone: the zone placed.</summary>
        public ZoneSetup Zone;
    }

    /// <summary>
    /// An ability as plain data: what it costs, what it can target, and the effects it applies in order.
    /// Presentation fills these from ScriptableObject definitions.
    /// </summary>
    [System.Serializable]
    public struct AbilityDefinition
    {
        /// <summary>Action points the ability costs when used on the user's turn (reactions cost none).</summary>
        public int ActionPointCost;
        /// <summary>True if using the ability ends the user's turn.</summary>
        public bool EndsTurn;
        /// <summary>
        /// True if the ability is a reaction: a unit that knows one uses its first reaction ability, instead of
        /// a plain burst, when a hostile unit enters its zone of control.
        /// </summary>
        public bool IsReaction;
        /// <summary>What the ability is aimed at.</summary>
        public AbilityTargeting Targeting;
        /// <summary>Which units it may target (Unit) or affect (Point).</summary>
        public AbilityTeam TargetTeam;
        /// <summary>Maximum distance in metres from the user to the target unit or point; 0 means unlimited.</summary>
        public float Range;
        /// <summary>Point only: units within this many metres of the point are affected; 0 affects no units (e.g. overwatch).</summary>
        public float Radius;
        /// <summary>True if the user must see the target unit or point (and, for an area, the point must see each affected unit).</summary>
        public bool RequiresLineOfSight;
        /// <summary>The effects, applied in order.</summary>
        public AbilityEffect[] Effects;
    }
}
