// Written by Claude (Anthropic), October 2026; a profiler marker added before step 8, and waiting while paused for
// step 8c; to be reviewed by Alexander Stangl per the course syllabus.

using Game.Rules;
using Unity.Profiling;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Plays the turns of AI-controlled teams. Whenever an AI-controlled unit is active and playback has caught up,
    /// it waits <see cref="ThinkDelay"/> so the player can follow, then submits the rules layer's choice
    /// (<see cref="AiSystem.ChooseCommand"/>) through <see cref="EncounterRoot.TrySubmit"/>, the same route as the
    /// player's clicks. By default it controls the enemies; turn both teams on to watch AI against AI, or both off
    /// for hot seat. A refused command, or a turn that runs past <see cref="MaxCommandsPerTurn"/>, ends the turn
    /// with a warning, so an AI turn can never hang.
    /// </summary>
    public sealed class EncounterAi : MonoBehaviour
    {
        /// <summary>The encounter being played; found on this object or in the scene if left empty.</summary>
        [SerializeField] private EncounterRoot encounter;
        /// <summary>True if the AI plays the squad's units.</summary>
        [SerializeField] private bool controlsSquad;
        /// <summary>True if the AI plays the enemy units.</summary>
        [SerializeField] private bool controlsEnemies = true;
        /// <summary>Pause before each AI command, in seconds, so the player can follow what happens.</summary>
        [SerializeField, Min(0f)] private float thinkDelay = 0.4f;
        /// <summary>Commands one unit may give in a turn before the AI ends it.</summary>
        [SerializeField, Min(1)] private int maxCommandsPerTurn = 8;

        private static readonly ProfilerMarker chooseMarker = new ProfilerMarker("Encounter.AI.Choose");
        private float readySince = -1f;
        private UnitId turnUnit = UnitId.None;
        private int turnRound = -1, commandsThisTurn;

        /// <summary>The encounter being played.</summary>
        public EncounterRoot Root { get => encounter; set => encounter = value; }
        /// <summary>True if the AI plays the squad's units.</summary>
        public bool ControlsSquad { get => controlsSquad; set => controlsSquad = value; }
        /// <summary>True if the AI plays the enemy units.</summary>
        public bool ControlsEnemies { get => controlsEnemies; set => controlsEnemies = value; }
        /// <summary>Pause before each AI command, in seconds.</summary>
        public float ThinkDelay { get => thinkDelay; set => thinkDelay = value; }
        /// <summary>Commands one unit may give in a turn before the AI ends it.</summary>
        public int MaxCommandsPerTurn { get => maxCommandsPerTurn; set => maxCommandsPerTurn = value; }

        /// <summary>True if the AI plays <paramref name="team"/>.</summary>
        public bool ControlsTeam(Team team) => team == Team.Player ? controlsSquad : controlsEnemies;

        /// <summary>True while the encounter is waiting for a command from a unit the AI plays.</summary>
        public bool ControlsActiveUnit
        {
            get
            {
                Encounter rules = encounter != null ? encounter.Encounter : null;
                if (rules == null || rules.Flow.Phase != EncounterPhase.AwaitingCommand) return false;
                UnitId active = rules.Flow.ActiveUnit;
                return !active.IsNone && ControlsTeam(rules.Units.GetTeam(active));
            }
        }

        /// <summary>
        /// Chooses and submits one command for the active unit, if the AI plays it and playback has caught up.
        /// Returns true if a command was submitted. <see cref="Update"/> calls it after the think delay; tests call it
        /// directly.
        /// </summary>
        public bool Step()
        {
            if (!ControlsActiveUnit || encounter.IsPlayingBack) return false;
            Encounter rules = encounter.Encounter;
            UnitId active = rules.Flow.ActiveUnit;
            if (active != turnUnit || rules.Flow.Round != turnRound)
            {
                turnUnit = active;
                turnRound = rules.Flow.Round;
                commandsThisTurn = 0;
            }

            Command command;
            using (chooseMarker.Auto()) command = rules.Ai.ChooseCommand(active);
            if (++commandsThisTurn > maxCommandsPerTurn)
            {
                Debug.LogWarning($"[Encounter AI] Unit {active} gave {maxCommandsPerTurn} commands in one turn; ending it.", this);
                command = Command.EndTurn(active);
            }
            if (!encounter.TrySubmit(command, out CommandResult result)) return false;
            if (!result.Accepted)
            {
                Debug.LogWarning($"[Encounter AI] The rules refused unit {active}'s {command.Kind} ({result.Reason}); ending its turn.", this);
                encounter.TrySubmit(Command.EndTurn(active), out _);
            }
            return true;
        }

        private void Awake()
        {
            if (encounter == null) encounter = GetComponent<EncounterRoot>();
            if (encounter == null) encounter = FindAnyObjectByType<EncounterRoot>();
        }

        private void Update()
        {
            if (!ControlsActiveUnit || encounter.IsPlayingBack || encounter.IsPaused)
            {
                readySince = -1f;
                return;
            }
            if (readySince < 0f) readySince = Time.unscaledTime;
            if (Time.unscaledTime - readySince < thinkDelay) return;
            readySince = -1f;
            Step();
        }
    }
}
