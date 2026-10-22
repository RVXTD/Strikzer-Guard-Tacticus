// Written by Claude (Anthropic), October 2026; profiler markers added before step 8, the EventPlayed feed for step
// 8b, and pausing for step 8c (the frame cap moved to GameSettings); to be reviewed by Alexander Stangl per the course
// syllabus.

using System.Collections.Generic;
using Game.Rules;
using Unity.Profiling;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The composition root of a combat scene. At Start it builds the rules layer's <see cref="Rules.Encounter"/> from
    /// the baked map, the tuning and every <see cref="UnitAuthoring"/> in the scene (in hierarchy order, so a seed
    /// always replays the same way), binds each unit's view and starts the encounter. Its one Update plays the event
    /// log back. Commands go in only through <see cref="TrySubmit"/>, which refuses them while playback runs.
    /// </summary>
    public sealed class EncounterRoot : MonoBehaviour
    {
        /// <summary>The baked map the encounter is played on.</summary>
        [SerializeField] private MapGridAsset map;
        /// <summary>Hit-chance, round-fate and pickup numbers, and the seed.</summary>
        [SerializeField] private EncounterTuning tuning;
        /// <summary>How fast events are played back.</summary>
        [SerializeField] private PlaybackSettings playback = PlaybackSettings.Default;
        /// <summary>If set, every event is printed to the Console as it plays.</summary>
        [SerializeField] private bool logEvents = true;

        private static readonly ProfilerMarker buildMarker = new ProfilerMarker("Encounter.Build");
        private static readonly ProfilerMarker executeMarker = new ProfilerMarker("Encounter.Execute");
        private static readonly ProfilerMarker playbackMarker = new ProfilerMarker("Encounter.Playback");
        private readonly Dictionary<UnitId, UnitView> views = new Dictionary<UnitId, UnitView>();
        private AbilityAsset[] abilities = new AbilityAsset[0];
        private EventPlayback eventPlayback;

        /// <summary>The baked map the encounter is played on.</summary>
        public MapGridAsset Map { get => map; set => map = value; }
        /// <summary>The encounter's tuning.</summary>
        public EncounterTuning Tuning { get => tuning; set => tuning = value; }
        /// <summary>How fast events are played back.</summary>
        public PlaybackSettings Playback { get => playback; set => playback = value; }

        /// <summary>
        /// Raised for each event as playback reaches it (its beat starts), in log order: the one feed camera and audio
        /// react to, so presentation never reads the log ahead of what is shown.
        /// </summary>
        public event System.Action<GameEvent> EventPlayed;

        /// <summary>The running encounter, for read-only queries (HUD, previews); null before it begins.</summary>
        public Encounter Encounter { get; private set; }

        /// <summary>Event playback; null before the encounter begins.</summary>
        public EventPlayback EventPlayback => eventPlayback;

        /// <summary>
        /// True while the game is paused (set by <see cref="PauseMenu"/>): input, the AI and the camera stand still, and
        /// <see cref="TrySubmit"/> refuses commands.
        /// </summary>
        public bool IsPaused { get; set; }

        /// <summary>True while events are still being played back; commands are refused meanwhile.</summary>
        public bool IsPlayingBack => eventPlayback != null && eventPlayback.IsPlaying;

        /// <summary>The view of a unit, if it has one.</summary>
        public bool TryGetView(UnitId id, out UnitView view) => views.TryGetValue(id, out view);

        /// <summary>The ability asset at an index of the encounter's ability table.</summary>
        public AbilityAsset AbilityAt(int index) => abilities[index];

        /// <summary>The index of an ability asset in the encounter's ability table, or -1.</summary>
        public int IndexOf(AbilityAsset ability) => System.Array.IndexOf(abilities, ability);

        private void Start()
        {
            var found = FindObjectsByType<UnitAuthoring>(); // unordered; StableOrder sorts them
            Begin(EncounterBuilder.StableOrder(found));
        }

        private void Update()
        {
            using (playbackMarker.Auto()) eventPlayback?.Tick(Time.deltaTime);
        }

        /// <summary>
        /// Builds and starts the encounter with the given units, in that order, and binds their views. Called by
        /// Start with every unit in the scene; tests call it directly.
        /// </summary>
        public void Begin(IReadOnlyList<UnitAuthoring> units)
        {
            using ProfilerMarker.AutoScope scope = buildMarker.Auto();
            Encounter = EncounterBuilder.Build(map, tuning, units, out UnitId[] ids, out abilities);
            views.Clear();
            for (int i = 0; i < units.Count; i++)
            {
                var view = units[i].GetComponent<UnitView>();
                view.Bind(ids[i], units[i].Team, GridSpace.ToWorld(GridSpace.ToRules(units[i].transform.position), map.FloorHeight));
                views[ids[i]] = view;
            }
            eventPlayback = new EventPlayback(Encounter, id => views.TryGetValue(id, out UnitView view) ? view : null, playback,
                                              map.FloorHeight, transform, played);
            Encounter.Start();
        }

        /// <summary>
        /// Sends a command to the rules layer. Returns false, without sending it, while playback is still running;
        /// otherwise returns true with the rules layer's verdict in <paramref name="result"/>.
        /// </summary>
        public bool TrySubmit(in Command command, out CommandResult result)
        {
            result = default;
            if (Encounter == null || IsPlayingBack || IsPaused) return false;
            using (executeMarker.Auto()) result = Encounter.Execute(command);
            return true;
        }

        private void played(GameEvent e)
        {
            if (logEvents) logEvent(e);
            EventPlayed?.Invoke(e);
        }

        private void logEvent(GameEvent e)
        {
            Debug.Log($"[Encounter] {e.Kind} unit {e.Unit} other {e.Other} position ({e.Position.X:F2}, {e.Position.Y:F2}) " +
                      $"value {e.Value} pile {e.PileId}{(e.IsReaction ? " (reaction)" : "")}", this);
        }
    }
}
