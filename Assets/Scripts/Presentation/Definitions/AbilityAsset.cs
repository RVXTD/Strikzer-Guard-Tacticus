// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// An ability as designer-editable data: the rules layer's <see cref="AbilityDefinition"/> (cost, targeting
    /// and a list of effects from a closed set). Units list the abilities they know as references to these assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Auto Buster/Ability", fileName = "Ability")]
    public sealed class AbilityAsset : ScriptableObject
    {
        /// <summary>The ability's cost, targeting and effects.</summary>
        [SerializeField] private AbilityDefinition definition = new AbilityDefinition { ActionPointCost = 1, Effects = new AbilityEffect[0] };

        /// <summary>The ability as the rules layer uses it.</summary>
        public AbilityDefinition Definition
        {
            get => definition;
            set => definition = value;
        }
    }
}
