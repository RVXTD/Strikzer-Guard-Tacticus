// Written by Claude (Anthropic), October 2026; the AI profile added for step 7; to be reviewed by Alexander Stangl
// per the course syllabus.

using System;
using Game.Rules;
using UnityEngine;
using NumericsVector2 = System.Numerics.Vector2;

namespace Game.Presentation
{
    /// <summary>
    /// A unit type as designer-editable data (melee bot, sentry, trooper, boss, squad member). Variety comes from
    /// these assets, not from subclasses. Per-placement data (team, position, objective) lives on the scene's
    /// unit authoring component.
    /// </summary>
    [CreateAssetMenu(menuName = "Auto Buster/Unit", fileName = "Unit")]
    public sealed class UnitDefinition : ScriptableObject
    {
        /// <summary>Name shown in the HUD.</summary>
        [SerializeField] private string displayName = "Unit";
        /// <summary>Maximum and starting health.</summary>
        [SerializeField, Min(1)] private int maxHealth = 10;
        /// <summary>Base chance to hit, in percent, before modifiers.</summary>
        [SerializeField, Range(0, 100)] private int accuracy = 70;
        /// <summary>Metres one action point of movement covers.</summary>
        [SerializeField, Min(0f)] private float movementBudget = 6f;
        /// <summary>Action points per turn.</summary>
        [SerializeField, Min(1)] private int actionPoints = 2;
        /// <summary>Base initiative; higher acts earlier in a round.</summary>
        [SerializeField] private int initiative = 10;
        /// <summary>The unit's weapon.</summary>
        [SerializeField] private WeaponDefinition weapon;
        /// <summary>Rounds carried at the start of an encounter.</summary>
        [SerializeField, Min(0)] private int startingAmmo = 30;
        /// <summary>The unit's base zone of control (radius 0 for none).</summary>
        [SerializeField] private ZoneSetup zone;
        /// <summary>The abilities the unit knows.</summary>
        [SerializeField] private AbilityAsset[] abilities = new AbilityAsset[0];
        /// <summary>
        /// Radius of the unit's body in metres. The unit keeps its centre at least this far from walls and other
        /// units' bodies, so a wide unit cannot fit through gaps a slim one can. Match it to the model's footprint.
        /// </summary>
        [SerializeField, Min(0f)] private float bodyRadius = 0.35f;
        /// <summary>How the AI weighs this unit's options when it controls the unit's team.</summary>
        [SerializeField] private AiProfile ai = AiProfile.Default;

        /// <summary>Name shown in the HUD.</summary>
        public string DisplayName { get => displayName; set => displayName = value; }
        /// <summary>Maximum and starting health.</summary>
        public int MaxHealth { get => maxHealth; set => maxHealth = value; }
        /// <summary>Base chance to hit, in percent.</summary>
        public int Accuracy { get => accuracy; set => accuracy = value; }
        /// <summary>Metres one action point of movement covers.</summary>
        public float MovementBudget { get => movementBudget; set => movementBudget = value; }
        /// <summary>Action points per turn.</summary>
        public int ActionPoints { get => actionPoints; set => actionPoints = value; }
        /// <summary>Base initiative.</summary>
        public int Initiative { get => initiative; set => initiative = value; }
        /// <summary>The unit's weapon.</summary>
        public WeaponDefinition Weapon { get => weapon; set => weapon = value; }
        /// <summary>Rounds carried at the start of an encounter.</summary>
        public int StartingAmmo { get => startingAmmo; set => startingAmmo = value; }
        /// <summary>The unit's base zone of control.</summary>
        public ZoneSetup Zone { get => zone; set => zone = value; }
        /// <summary>The abilities the unit knows.</summary>
        public AbilityAsset[] Abilities { get => abilities; set => abilities = value; }
        /// <summary>Radius of the unit's body in metres.</summary>
        public float BodyRadius { get => bodyRadius; set => bodyRadius = value; }
        /// <summary>How the AI weighs this unit's options.</summary>
        public AiProfile Ai { get => ai; set => ai = value; }

        /// <summary>
        /// Builds the rules layer's setup for one placed unit. <paramref name="abilityIndexOf"/> maps each of this
        /// unit's ability assets to its index in the encounter's ability table. Throws
        /// <see cref="InvalidOperationException"/> if the definition has no weapon.
        /// </summary>
        public UnitSetup ToUnitSetup(Team team, NumericsVector2 position, NumericsVector2 facing, bool isObjective,
                                     Func<AbilityAsset, int> abilityIndexOf)
        {
            if (weapon == null) throw new InvalidOperationException($"Unit definition '{name}' has no weapon.");
            var abilityIndices = new int[abilities == null ? 0 : abilities.Length];
            for (int i = 0; i < abilityIndices.Length; i++) abilityIndices[i] = abilityIndexOf(abilities[i]);
            return new UnitSetup
            {
                Team = team,
                Position = position,
                Facing = facing,
                MaxHealth = maxHealth,
                Accuracy = accuracy,
                MovementBudget = movementBudget,
                ActionPoints = actionPoints,
                Initiative = initiative,
                Weapon = weapon.Stats,
                StartingAmmo = startingAmmo,
                Zone = zone,
                IsObjective = isObjective,
                Abilities = abilityIndices,
                BodyRadius = bodyRadius,
                Ai = ai,
            };
        }
    }
}
