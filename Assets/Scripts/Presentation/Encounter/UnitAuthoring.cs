// Written by Claude (Anthropic), October 2026; [SelectionBase] added after a unit's model was moved off its unit;
// to be reviewed by Alexander Stangl per the course syllabus.

using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Places a unit in a combat scene: which unit type it is, which side it fights for and whether defeating it
    /// is the encounter's objective. It holds data only; <see cref="EncounterRoot"/> gathers every one at Start and
    /// registers them with the rules layer, using the object's position and facing. Clicking the unit's model in the
    /// Scene view selects this object rather than the model under it, so moving a unit moves what the rules see.
    /// </summary>
    [SelectionBase]
    [RequireComponent(typeof(UnitView))]
    public sealed class UnitAuthoring : MonoBehaviour
    {
        /// <summary>The unit type.</summary>
        [SerializeField] private UnitDefinition definition;
        /// <summary>The side the unit fights for.</summary>
        [SerializeField] private Team team = Team.Player;
        /// <summary>Enemy units only: defeating every objective unit wins the encounter.</summary>
        [SerializeField] private bool isObjective;

        /// <summary>The unit type.</summary>
        public UnitDefinition Definition { get => definition; set => definition = value; }
        /// <summary>The side the unit fights for.</summary>
        public Team Team { get => team; set => team = value; }
        /// <summary>True if defeating this unit is (part of) the encounter's objective.</summary>
        public bool IsObjective { get => isObjective; set => isObjective = value; }
    }
}
