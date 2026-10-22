// Cell obstacle model (CellObstacle), UnitSetup.ActionPoints, IsObjective, Abilities, BodyRadius, the AI profile
// and [Serializable] setup types by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the
// course syllabus.

using System.Numerics;

namespace Game.Rules
{
    /// <summary>
    /// The cover a cell provides to a unit standing next to it. Whether the cell also blocks
    /// movement or sight is recorded separately in <see cref="CellObstacle"/>.
    /// </summary>
    public enum CoverLevel : byte
    {
        /// <summary>No cover.</summary>
        None,
        /// <summary>Half cover.</summary>
        Half,
        /// <summary>Full cover.</summary>
        Full,
    }

    /// <summary>
    /// What stands on one map cell. Each property is independent, so for example glass can block
    /// movement without blocking sight. The default value is an empty cell.
    /// </summary>
    [System.Serializable]
    public struct CellObstacle
    {
        /// <summary>No unit may stand in or walk through the cell.</summary>
        public bool BlocksMovement;
        /// <summary>Line of sight cannot pass through the cell.</summary>
        public bool BlocksSight;
        /// <summary>Cover the cell gives a unit in an adjacent cell, against attackers on the cell's side.</summary>
        public CoverLevel Cover;

        /// <summary>An empty cell.</summary>
        public static CellObstacle None => default;

        /// <summary>A wall: blocks movement and sight and gives full cover.</summary>
        public static CellObstacle Wall => new CellObstacle { BlocksMovement = true, BlocksSight = true, Cover = CoverLevel.Full };

        /// <summary>A low obstacle such as a bench or planter: blocks movement and gives half cover, but not sight.</summary>
        public static CellObstacle LowObstacle => new CellObstacle { BlocksMovement = true, Cover = CoverLevel.Half };

        /// <summary>A glass storefront: blocks movement but not sight, and gives no cover.</summary>
        public static CellObstacle Glass => new CellObstacle { BlocksMovement = true };
    }

    /// <summary>A unit's weapon, as plain data. Presentation fills this from a ScriptableObject definition.</summary>
    [System.Serializable]
    public struct WeaponStats
    {
        /// <summary>Index of the ammo type this weapon fires.</summary>
        public int AmmoType;
        /// <summary>Rounds fired per attack (burst size); each round rolls to hit separately.</summary>
        public int RoundsPerAttack;
        /// <summary>Damage dealt by each round that hits.</summary>
        public int DamagePerRound;
        /// <summary>Distance in metres up to which there is no range penalty.</summary>
        public float OptimalRange;
    }

    /// <summary>A unit's zone of control. A <see cref="Radius"/> of zero means the unit has none.</summary>
    [System.Serializable]
    public struct ZoneSetup
    {
        /// <summary>Reach of the zone in metres.</summary>
        public float Radius;
        /// <summary>Half the opening angle around the unit's facing; 180 or more is a full circle.</summary>
        public float HalfAngleDegrees;
        /// <summary>If true (sentry cones), the zone triggers only where the owner can see the mover.</summary>
        public bool RequiresLineOfSight;
    }

    /// <summary>
    /// How an AI-controlled unit weighs its options (see <see cref="AiSystem"/>). Values are in health points of
    /// expected damage, so 1 means "as much as the damage itself". Variety between enemy types comes from these
    /// weights, not from code.
    /// </summary>
    [System.Serializable]
    public struct AiProfile
    {
        /// <summary>Weight on the expected damage (and other effects) of an attack or ability.</summary>
        public float Aggression;
        /// <summary>Weight on the expected damage hostiles could deal the unit where it stands.</summary>
        public float Caution;
        /// <summary>Penalty per metre the unit stands beyond its weapon's optimal range from the nearest hostile.</summary>
        public float Approach;
        /// <summary>Raises <see cref="Caution"/> as health drops: Caution × (1 + this × the fraction of health lost).</summary>
        public float RetreatWhenHurt;
        /// <summary>Weight on picking up rounds when the unit carries less than one burst.</summary>
        public float AmmoGreed;
        /// <summary>Weight on the expected reaction damage from hostile zones a move would enter.</summary>
        public float ZoneAvoidance;

        /// <summary>Balanced starting values; expected to change with playtesting.</summary>
        public static AiProfile Default => new AiProfile
        {
            Aggression = 1f,
            Caution = 1f,
            Approach = 0.3f,
            RetreatWhenHurt = 1f,
            AmmoGreed = 1f,
            ZoneAvoidance = 1f,
        };
    }

    /// <summary>Everything needed to add one unit to an encounter.</summary>
    public struct UnitSetup
    {
        /// <summary>Side the unit fights for.</summary>
        public Team Team;
        /// <summary>Starting world-space position on the ground plane.</summary>
        public Vector2 Position;
        /// <summary>Starting facing; need not be normalised. Zero faces +X.</summary>
        public Vector2 Facing;
        /// <summary>Maximum and starting health.</summary>
        public int MaxHealth;
        /// <summary>Base chance to hit, in percent, before modifiers.</summary>
        public int Accuracy;
        /// <summary>Distance in metres one action point of movement covers, before status modifiers.</summary>
        public float MovementBudget;
        /// <summary>Base initiative; higher acts earlier in a round.</summary>
        public int Initiative;
        /// <summary>Action points per turn (2 by default in the slice); at least 1.</summary>
        public int ActionPoints;
        /// <summary>The unit's weapon.</summary>
        public WeaponStats Weapon;
        /// <summary>Rounds of the weapon's ammo type carried at the start.</summary>
        public int StartingAmmo;
        /// <summary>The unit's zone of control.</summary>
        public ZoneSetup Zone;
        /// <summary>
        /// Enemy units only: defeating every objective unit wins the encounter, whatever enemies remain.
        /// With no objective units, defeating every enemy wins.
        /// </summary>
        public bool IsObjective;
        /// <summary>Indices into <see cref="EncounterSetup.Abilities"/> of the abilities the unit knows (null for none).</summary>
        public int[] Abilities;
        /// <summary>
        /// Radius of the unit's body in metres (0 for a point). The unit only stands in, and walks through, cells
        /// whose centre is at least this far from anything that blocks movement and from other units' bodies.
        /// </summary>
        public float BodyRadius;
        /// <summary>How the AI weighs this unit's options when it controls it; all zeros never attacks or moves.</summary>
        public AiProfile Ai;
    }
}
