// Written by Claude (Anthropic), October 2026; the AI lock-out added for step 7, profiler markers before step 8, and
// the pause lock-out for step 8c; to be reviewed by Alexander Stangl per the course syllabus.

using Game.Rules;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>
    /// Reads the mouse and keyboard through the Input System and feeds a <see cref="TurnController"/>. Each frame it
    /// finds what is under the cursor (a unit by a raycast against its colliders, the ground point on the floor plane,
    /// and the nearest ammo pile to that point) unless the pointer is over the HUD. Physics is used here only to pick
    /// what the player points at; the rules layer never sees it. The bindings are Inspector-editable actions.
    /// </summary>
    public sealed class EncounterInput : MonoBehaviour
    {
        /// <summary>The encounter being played; found in the scene if left empty.</summary>
        [SerializeField] private EncounterRoot encounter;
        /// <summary>The camera the cursor looks through; the main camera if left empty.</summary>
        [SerializeField] private Camera viewCamera;
        /// <summary>Rounds handed over by a Shift-click on an ally.</summary>
        [SerializeField, Min(1)] private int tradeCount = 5;
        /// <summary>How close to a pile the cursor must be to pick it, in metres.</summary>
        [SerializeField, Min(0f)] private float pilePickRadius = 0.5f;
        /// <summary>Layers whose colliders can be clicked as units (or block the view of them).</summary>
        [SerializeField] private LayerMask pickLayers = ~0;
        /// <summary>The AI driver; while it plays the active unit, the player's input is ignored. Found on this object if left empty.</summary>
        [SerializeField] private EncounterAi ai;

        /// <summary>The pointer's screen position.</summary>
        [SerializeField] private InputAction point = action("Point", InputActionType.Value, "<Pointer>/position");
        /// <summary>Submits the previewed command.</summary>
        [SerializeField] private InputAction click = action("Click", InputActionType.Button, "<Mouse>/leftButton");
        /// <summary>Cancels ability targeting.</summary>
        [SerializeField] private InputAction cancel = action("Cancel", InputActionType.Button, "<Mouse>/rightButton", "<Keyboard>/escape");
        /// <summary>Ends the active unit's turn.</summary>
        [SerializeField] private InputAction endTurn = action("End Turn", InputActionType.Button, "<Keyboard>/enter", "<Keyboard>/space");
        /// <summary>While held, hovering an ally previews a trade.</summary>
        [SerializeField] private InputAction trade = action("Trade", InputActionType.Button, "<Keyboard>/shift");
        /// <summary>Selects the active unit's abilities by position in its list.</summary>
        [SerializeField] private InputAction[] abilityKeys =
        {
            action("Ability 1", InputActionType.Button, "<Keyboard>/1"),
            action("Ability 2", InputActionType.Button, "<Keyboard>/2"),
            action("Ability 3", InputActionType.Button, "<Keyboard>/3"),
            action("Ability 4", InputActionType.Button, "<Keyboard>/4"),
        };

        private static readonly ProfilerMarker pickMarker = new ProfilerMarker("Encounter.Input.Pick");
        private static readonly ProfilerMarker previewMarker = new ProfilerMarker("Encounter.Input.Preview");
        private TurnController controller;

        /// <summary>The encounter being played.</summary>
        public EncounterRoot Root { get => encounter; set => encounter = value; }

        /// <summary>The camera the cursor looks through; null uses the main camera.</summary>
        public Camera ViewCamera { get => viewCamera; set => viewCamera = value; }

        /// <summary>The controller, created once the encounter has begun; null before.</summary>
        public TurnController Controller
        {
            get
            {
                if (controller == null && encounter != null && encounter.Encounter != null)
                    controller = new TurnController(encounter, tradeCount);
                return controller;
            }
        }

        /// <summary>The key shown for the ability in <paramref name="slot"/>, or an empty string if it has none.</summary>
        public string AbilityKeyLabel(int slot) =>
            slot >= 0 && slot < abilityKeys.Length && abilityKeys[slot] != null ? abilityKeys[slot].GetBindingDisplayString() : "";

        private static InputAction action(string name, InputActionType type, params string[] bindings)
        {
            var created = new InputAction(name, type);
            foreach (string binding in bindings) created.AddBinding(binding);
            return created;
        }

        private System.Collections.Generic.IEnumerable<InputAction> actions()
        {
            yield return point;
            yield return click;
            yield return cancel;
            yield return endTurn;
            yield return trade;
            foreach (InputAction key in abilityKeys) yield return key;
        }

        private void Awake()
        {
            if (encounter == null) encounter = FindAnyObjectByType<EncounterRoot>();
            if (ai == null) ai = GetComponent<EncounterAi>();
        }

        private void OnEnable()
        {
            foreach (InputAction each in actions()) each?.Enable();
        }

        private void OnDisable()
        {
            foreach (InputAction each in actions()) each?.Disable();
        }

        private void Update()
        {
            TurnController turn = Controller;
            if (turn == null) return;
            if (encounter.IsPaused)
            {
                turn.ClearHover(); // the pause menu has the input
                return;
            }
            turn.TradeCount = tradeCount;
            turn.AiControlsActiveUnit = ai != null && ai.ControlsActiveUnit;

            if (cancel.WasPressedThisFrame()) turn.Cancel();
            if (endTurn.WasPressedThisFrame()) turn.EndTurn();
            for (int slot = 0; slot < abilityKeys.Length; slot++)
                if (abilityKeys[slot] != null && abilityKeys[slot].WasPressedThisFrame()) turn.SelectAbility(slot);

            bool overHud = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overHud && tryPick(out UnitId unit, out int pile, out System.Numerics.Vector2 ground))
                turn.Hover(unit, pile, ground, trade.IsPressed());
            else
                turn.ClearHover();
            if (!overHud && click.WasPressedThisFrame()) turn.Click();
            using (previewMarker.Auto()) turn.Tick();
        }

        private bool tryPick(out UnitId unit, out int pile, out System.Numerics.Vector2 ground)
        {
            using ProfilerMarker.AutoScope scope = pickMarker.Auto();
            unit = UnitId.None;
            pile = -1;
            ground = default;
            Camera eye = viewCamera != null ? viewCamera : Camera.main;
            if (eye == null || encounter.Map == null) return false;

            Ray ray = eye.ScreenPointToRay(point.ReadValue<Vector2>());
            var floor = new Plane(Vector3.up, new Vector3(0f, encounter.Map.FloorHeight, 0f));
            if (!floor.Raycast(ray, out float distance)) return false;
            ground = GridSpace.ToRules(ray.GetPoint(distance));

            if (Physics.Raycast(ray, out RaycastHit hit, float.PositiveInfinity, pickLayers, QueryTriggerInteraction.Ignore))
            {
                UnitView view = hit.collider.GetComponentInParent<UnitView>();
                if (view != null && !view.IsDefeated && !view.Id.IsNone) unit = view.Id;
            }

            ResourceSystem resources = encounter.Encounter.Resources;
            float best = pilePickRadius;
            for (int i = 0; i < resources.PileCount; i++)
            {
                AmmoPile candidate = resources.GetPileAt(i);
                float away = System.Numerics.Vector2.Distance(candidate.Position, ground);
                if (away > best) continue;
                best = away;
                pile = candidate.Id;
            }
            return true;
        }
    }
}
