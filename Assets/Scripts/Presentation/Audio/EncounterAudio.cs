// Written by Claude (Anthropic), October 2026; settings volumes and music under the pause menu added for step 8c; to
// be reviewed by Alexander Stangl per the course syllabus.

using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Sound for the encounter, driven only by played-back events (<see cref="EncounterRoot.EventPlayed"/>), never by
    /// the rules directly. Each event plays a random clip from its <see cref="SoundDictionary"/> entry (or the entry's
    /// generated placeholder), in 3D at the unit or point it concerns or in 2D for interface sounds, from a small pool of
    /// voices. Music is two loops, calm and combat, crossfaded by an intensity that rises with shots, hits, blasts, downs
    /// and interrupted moves and decays over time; at the end of the encounter both fade out and a win or lose stinger
    /// plays. Without a dictionary, a default one (placeholders, no music) is used. Effects and music are scaled by the
    /// player's <see cref="GameSettings"/> volumes, and the music keeps playing while the game is paused.
    /// </summary>
    public sealed class EncounterAudio : MonoBehaviour
    {
        /// <summary>The encounter being played; found on this object or in the scene if left empty.</summary>
        [SerializeField] private EncounterRoot encounter;
        /// <summary>Which clips voice which events, and the music; a default (placeholders only) if left empty.</summary>
        [SerializeField] private SoundDictionary dictionary;
        /// <summary>Volume of all sound effects, 0 to 1.</summary>
        [SerializeField, Range(0f, 1f)] private float effectsVolume = 0.8f;
        /// <summary>How many sound effects can play at once.</summary>
        [SerializeField, Min(1)] private int voices = 12;
        /// <summary>Distance in metres within which a positional sound plays at full volume.</summary>
        [SerializeField, Min(0.1f)] private float fullVolumeDistance = 12f;
        /// <summary>Distance in metres beyond which a positional sound gets no quieter.</summary>
        [SerializeField, Min(0.1f)] private float farDistance = 70f;

        private readonly List<AudioSource> pool = new List<AudioSource>();
        private int nextVoice;
        private AudioSource calm, combat, stinger;
        private float intensity;
        private bool ended;
        private SoundDictionary fallback;

        /// <summary>The encounter being played.</summary>
        public EncounterRoot Root { get => encounter; set => encounter = value; }
        /// <summary>Which clips voice which events, and the music.</summary>
        public SoundDictionary Dictionary { get => dictionary; set => dictionary = value; }
        /// <summary>Combat intensity, from 0 (quiet) to 1, which crossfades the music.</summary>
        public float Intensity => intensity;

        private SoundDictionary sounds
        {
            get
            {
                if (dictionary != null) return dictionary;
                if (fallback == null)
                {
                    fallback = ScriptableObject.CreateInstance<SoundDictionary>();
                    fallback.hideFlags = HideFlags.DontSave;
                }
                return fallback;
            }
        }

        /// <summary>
        /// A clip for an event kind: one of its entry's clips at random, the generated placeholder if the entry has
        /// none, or null if the kind has no entry (it makes no sound).
        /// </summary>
        public AudioClip ClipFor(EventKind kind)
        {
            if (!sounds.TryGetEntry(kind, out SoundEntry entry)) return null;
            if (entry.Clips != null && entry.Clips.Length > 0)
            {
                AudioClip chosen = entry.Clips[Random.Range(0, entry.Clips.Length)];
                if (chosen != null) return chosen;
            }
            return PlaceholderSounds.For(kind);
        }

        /// <summary>
        /// Voices one played-back event and feeds the music's intensity. Playback calls it through
        /// <see cref="EncounterRoot.EventPlayed"/>; tests call it directly. Outside play mode the sound is chosen but not played.
        /// </summary>
        public void Play(GameEvent played)
        {
            SoundDictionary dictionaryInUse = sounds;
            if (raisesIntensity(played.Kind)) intensity = Mathf.Min(1f, intensity + dictionaryInUse.IntensityPerEvent);
            if (played.Kind == EventKind.EncounterEnded) endMusic();

            AudioClip clip = ClipFor(played.Kind);
            if (clip == null || !Application.isPlaying) return;
            dictionaryInUse.TryGetEntry(played.Kind, out SoundEntry entry);
            AudioSource voice = nextFreeVoice();
            voice.transform.position = whereItHappened(played);
            voice.spatialBlend = entry.Positional ? 1f : 0f;
            voice.pitch = 1f + Random.Range(-entry.PitchVariance, entry.PitchVariance);
            voice.volume = entry.Volume * effectsVolume * GameSettings.Shared.EffectsVolume;
            voice.clip = clip;
            voice.Play();
        }

        /// <summary>Lets intensity decay and moves the music layers towards it. Called every frame; tests call it directly.</summary>
        public void Tick(float deltaTime)
        {
            SoundDictionary dictionaryInUse = sounds;
            intensity *= Mathf.Pow(0.5f, deltaTime / dictionaryInUse.IntensityHalfLife);
            if (calm == null) return;
            float step = deltaTime / dictionaryInUse.CrossfadeSeconds;
            float music = dictionaryInUse.MusicVolume * GameSettings.Shared.MusicVolume;
            float combatTarget = ended ? 0f : Mathf.SmoothStep(0f, 1f, intensity);
            calm.volume = Mathf.MoveTowards(calm.volume, (ended ? 0f : 1f - combatTarget) * music, step);
            combat.volume = Mathf.MoveTowards(combat.volume, combatTarget * music, step);
        }

        private void Awake()
        {
            if (encounter == null) encounter = GetComponent<EncounterRoot>();
            if (encounter == null) encounter = FindAnyObjectByType<EncounterRoot>();
        }

        private void OnEnable()
        {
            if (encounter != null) encounter.EventPlayed += Play;
        }

        private void OnDisable()
        {
            if (encounter != null) encounter.EventPlayed -= Play;
        }

        private void Start()
        {
            calm = musicSource("Music (calm)", sounds.CalmLoop);
            combat = musicSource("Music (combat)", sounds.CombatLoop);
            stinger = musicSource("Music (stinger)", null);
            stinger.loop = false;
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        private static bool raisesIntensity(EventKind kind) =>
            kind == EventKind.RoundHit || kind == EventKind.RoundMissed || kind == EventKind.UnitDamaged ||
            kind == EventKind.UnitDowned || kind == EventKind.UnitDefeated || kind == EventKind.MoveInterrupted;

        private void endMusic()
        {
            if (ended) return;
            ended = true;
            if (stinger == null || encounter == null || encounter.Encounter == null) return;
            AudioClip clip = encounter.Encounter.Flow.Winner == Team.Player ? sounds.WinStinger : sounds.LoseStinger;
            if (clip == null || !Application.isPlaying) return;
            stinger.volume = sounds.MusicVolume * GameSettings.Shared.MusicVolume;
            stinger.clip = clip;
            stinger.Play();
        }

        // The unit's view if the event concerns one (the attacker, for shots), otherwise the event's point on the floor.
        private Vector3 whereItHappened(GameEvent played)
        {
            if (encounter != null && !played.Unit.IsNone && encounter.TryGetView(played.Unit, out UnitView view) && view != null)
                return view.transform.position;
            float floor = encounter != null && encounter.Map != null ? encounter.Map.FloorHeight : 0f;
            return GridSpace.ToWorld(played.Position, floor);
        }

        private AudioSource nextFreeVoice()
        {
            while (pool.Count < voices)
            {
                var holder = new GameObject($"Sound voice {pool.Count + 1}");
                holder.transform.SetParent(transform, false);
                var source = holder.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = fullVolumeDistance;
                source.maxDistance = farDistance;
                source.dopplerLevel = 0f;
                pool.Add(source);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                AudioSource candidate = pool[(nextVoice + i) % pool.Count];
                if (candidate.isPlaying) continue;
                nextVoice = (nextVoice + i + 1) % pool.Count;
                return candidate;
            }
            AudioSource oldest = pool[nextVoice]; // every voice is busy: cut the oldest
            nextVoice = (nextVoice + 1) % pool.Count;
            return oldest;
        }

        private AudioSource musicSource(string name, AudioClip clip)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            var source = holder.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.ignoreListenerPause = true; // music plays on under the pause menu
            source.clip = clip;
            if (clip != null) source.Play();
            return source;
        }
    }
}
