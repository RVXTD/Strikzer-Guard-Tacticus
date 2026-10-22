// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>How one kind of event sounds.</summary>
    [Serializable]
    public struct SoundEntry
    {
        /// <summary>The event kind this entry voices.</summary>
        public EventKind Kind;
        /// <summary>Clips to pick from at random; empty uses a generated placeholder.</summary>
        public AudioClip[] Clips;
        /// <summary>Playback volume, 0 to 1.</summary>
        [Range(0f, 1f)] public float Volume;
        /// <summary>Random pitch change either way, as a fraction (0.1 means ±10%).</summary>
        [Range(0f, 0.5f)] public float PitchVariance;
        /// <summary>True to play in 3D at the unit or point the event concerns; false for a 2D (interface) sound.</summary>
        public bool Positional;
    }

    /// <summary>
    /// The encounter's sound: which clips voice which events, and the music. Designers and the audio team fill it in the
    /// Inspector; a new one starts with an entry for every event that makes a sound, with no clips, so generated
    /// placeholders (<see cref="PlaceholderSounds"/>) play until real clips are assigned. Event kinds without an entry
    /// (moves, bookkeeping) are silent. Music is two loops crossfaded by combat intensity, and a stinger at the end.
    /// </summary>
    [CreateAssetMenu(menuName = "Auto Buster/Sound Dictionary", fileName = "Sound Dictionary")]
    public sealed class SoundDictionary : ScriptableObject
    {
        /// <summary>One entry per event kind that makes a sound.</summary>
        [SerializeField] private SoundEntry[] entries = DefaultEntries();
        /// <summary>Music loop while things are quiet.</summary>
        [SerializeField] private AudioClip calmLoop;
        /// <summary>Music loop faded in as combat intensity rises.</summary>
        [SerializeField] private AudioClip combatLoop;
        /// <summary>Played once when the squad wins.</summary>
        [SerializeField] private AudioClip winStinger;
        /// <summary>Played once when the squad loses.</summary>
        [SerializeField] private AudioClip loseStinger;
        /// <summary>Music volume, 0 to 1.</summary>
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.6f;
        /// <summary>Seconds a music layer takes to fade fully in or out.</summary>
        [SerializeField, Min(0.1f)] private float crossfadeSeconds = 2f;
        /// <summary>Intensity added by each shot, hit, blast, down or interrupted move (intensity runs from 0 to 1).</summary>
        [SerializeField, Range(0f, 1f)] private float intensityPerEvent = 0.15f;
        /// <summary>Seconds for intensity to halve once fighting stops.</summary>
        [SerializeField, Min(0.5f)] private float intensityHalfLife = 8f;

        /// <summary>The entries, one per sounding event kind.</summary>
        public SoundEntry[] Entries { get => entries; set => entries = value; }
        /// <summary>Music loop while things are quiet.</summary>
        public AudioClip CalmLoop { get => calmLoop; set => calmLoop = value; }
        /// <summary>Music loop faded in as combat intensity rises.</summary>
        public AudioClip CombatLoop { get => combatLoop; set => combatLoop = value; }
        /// <summary>Played once when the squad wins.</summary>
        public AudioClip WinStinger { get => winStinger; set => winStinger = value; }
        /// <summary>Played once when the squad loses.</summary>
        public AudioClip LoseStinger { get => loseStinger; set => loseStinger = value; }
        /// <summary>Music volume.</summary>
        public float MusicVolume { get => musicVolume; set => musicVolume = value; }
        /// <summary>Seconds a music layer takes to fade fully in or out.</summary>
        public float CrossfadeSeconds { get => crossfadeSeconds; set => crossfadeSeconds = value; }
        /// <summary>Intensity added by each combat event.</summary>
        public float IntensityPerEvent { get => intensityPerEvent; set => intensityPerEvent = value; }
        /// <summary>Seconds for intensity to halve.</summary>
        public float IntensityHalfLife { get => intensityHalfLife; set => intensityHalfLife = value; }

        /// <summary>The entry for an event kind, if it has one.</summary>
        public bool TryGetEntry(EventKind kind, out SoundEntry entry)
        {
            if (entries != null)
                foreach (SoundEntry candidate in entries)
                    if (candidate.Kind == kind)
                    {
                        entry = candidate;
                        return true;
                    }
            entry = default;
            return false;
        }

        /// <summary>An entry, with no clips, for every event kind that makes a sound, at starting volumes.</summary>
        public static SoundEntry[] DefaultEntries() => new[]
        {
            entry(EventKind.RoundHit, 0.8f, 0.1f, true),
            entry(EventKind.RoundMissed, 0.7f, 0.1f, true),
            entry(EventKind.UnitDamaged, 0.9f, 0.05f, true),
            entry(EventKind.UnitDowned, 0.8f, 0f, true),
            entry(EventKind.UnitDefeated, 0.9f, 0.05f, true),
            entry(EventKind.UnitRevived, 0.7f, 0f, true),
            entry(EventKind.AbilityUsed, 0.6f, 0.05f, true),
            entry(EventKind.ZonePlaced, 0.5f, 0f, true),
            entry(EventKind.MoveInterrupted, 0.7f, 0f, false),
            entry(EventKind.StatusApplied, 0.5f, 0.05f, true),
            entry(EventKind.AmmoPickedUp, 0.6f, 0.05f, true),
            entry(EventKind.AmmoTraded, 0.6f, 0.05f, true),
            entry(EventKind.AmmoScattered, 0.25f, 0.2f, true),
            entry(EventKind.TurnStarted, 0.35f, 0f, false),
        };

        private static SoundEntry entry(EventKind kind, float volume, float pitchVariance, bool positional) =>
            new SoundEntry { Kind = kind, Clips = new AudioClip[0], Volume = volume, PitchVariance = pitchVariance, Positional = positional };
    }
}
