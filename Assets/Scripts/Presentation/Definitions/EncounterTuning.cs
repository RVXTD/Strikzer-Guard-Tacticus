// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>Designer-tunable encounter numbers: hit chance, round fates, ammo pickup and the random seed.</summary>
    [CreateAssetMenu(menuName = "Auto Buster/Encounter Tuning", fileName = "EncounterTuning")]
    public sealed class EncounterTuning : ScriptableObject
    {
        /// <summary>Cover, range and zone penalties, and the hit-chance limits.</summary>
        [SerializeField] private HitChanceTuning hitChance = HitChanceTuning.Default;
        /// <summary>Embed, glance and scatter odds, glance damage and scatter distances.</summary>
        [SerializeField] private AmmoFateTuning ammoFate = AmmoFateTuning.Default;
        /// <summary>Maximum distance in metres between a unit and a pile it picks up.</summary>
        [SerializeField, Min(0f)] private float pickupRadius = 1.5f;
        /// <summary>Rounds landing within this many metres of a pile of the same type and origin join it.</summary>
        [SerializeField, Min(0f)] private float pileMergeRadius = 1f;
        /// <summary>Seed for every random roll; the same seed and commands replay the same encounter.</summary>
        [SerializeField] private int seed = 1;

        /// <summary>Hit-chance numbers.</summary>
        public HitChanceTuning HitChance { get => hitChance; set => hitChance = value; }
        /// <summary>Round-fate numbers.</summary>
        public AmmoFateTuning AmmoFate { get => ammoFate; set => ammoFate = value; }
        /// <summary>Pickup radius in metres.</summary>
        public float PickupRadius { get => pickupRadius; set => pickupRadius = value; }
        /// <summary>Pile merge radius in metres.</summary>
        public float PileMergeRadius { get => pileMergeRadius; set => pileMergeRadius = value; }
        /// <summary>Random seed.</summary>
        public int Seed { get => seed; set => seed = value; }

        /// <summary>Copies these numbers into an encounter setup.</summary>
        public void ApplyTo(EncounterSetup setup)
        {
            setup.HitTuning = hitChance;
            setup.FateTuning = ammoFate;
            setup.PickupRadius = pickupRadius;
            setup.PileMergeRadius = pileMergeRadius;
            setup.Seed = unchecked((ulong)seed);
        }

        // Called by Unity when the asset is created or reset from the Inspector.
        private void Reset()
        {
            hitChance = HitChanceTuning.Default;
            ammoFate = AmmoFateTuning.Default;
            pickupRadius = 1.5f;
            pileMergeRadius = 1f;
            seed = 1;
        }
    }
}
