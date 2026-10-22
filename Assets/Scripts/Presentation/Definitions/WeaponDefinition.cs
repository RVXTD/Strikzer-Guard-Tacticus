// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>A weapon as designer-editable data: the rules layer's <see cref="WeaponStats"/>.</summary>
    [CreateAssetMenu(menuName = "Auto Buster/Weapon", fileName = "Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        /// <summary>Rounds per attack, damage per round, optimal range and ammo type.</summary>
        [SerializeField] private WeaponStats stats = new WeaponStats { RoundsPerAttack = 3, DamagePerRound = 2, OptimalRange = 10f };

        /// <summary>The weapon's stats, as the rules layer uses them.</summary>
        public WeaponStats Stats
        {
            get => stats;
            set => stats = value;
        }
    }
}
