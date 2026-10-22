// Written by Claude (Anthropic), October 2026; a profiler marker added before step 8, attack framing for step 8b, and
// ignoring input while paused for step 8c; to be reviewed by Alexander Stangl per the course syllabus.

using Game.Rules;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>
    /// A simple tactics camera on the main camera: it orbits a focus point on the floor at a fixed pitch. WASD or the
    /// arrows pan, the scroll wheel zooms, Q and E turn in 45-degree steps, and F jumps to the active unit. Whenever a
    /// new unit's turn starts and playback has caught up, it glides to that unit. When an attack or ability plays back
    /// (reactions included), it frames the attacker and its target or aim point, then, once playback has caught up and
    /// a short hold has passed, returns to its earlier zoom and the active unit. The focus stays on the baked map.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class EncounterCamera : MonoBehaviour
    {
        /// <summary>The encounter being watched; found in the scene if left empty.</summary>
        [SerializeField] private EncounterRoot encounter;
        /// <summary>Angle of the view below the horizon, in degrees.</summary>
        [SerializeField, Range(20f, 89f)] private float pitch = 55f;
        /// <summary>Starting heading, in degrees around the vertical.</summary>
        [SerializeField] private float yaw = 45f;
        /// <summary>Starting distance from the focus point, in metres.</summary>
        [SerializeField, Min(1f)] private float distance = 18f;
        /// <summary>Closest zoom, in metres.</summary>
        [SerializeField, Min(1f)] private float minDistance = 6f;
        /// <summary>Farthest zoom, in metres.</summary>
        [SerializeField, Min(1f)] private float maxDistance = 45f;
        /// <summary>Pan speed at a distance of 10 m, in metres per second; scales with the distance.</summary>
        [SerializeField, Min(0f)] private float panSpeed = 12f;
        /// <summary>Change in distance per scroll notch, as a fraction of the distance.</summary>
        [SerializeField, Range(0f, 0.5f)] private float zoomStep = 0.1f;
        /// <summary>How quickly the camera catches up with its target; higher is snappier.</summary>
        [SerializeField, Min(0.1f)] private float sharpness = 8f;
        /// <summary>Frame attacks and abilities as they play back.</summary>
        [SerializeField] private bool frameAttacks = true;
        /// <summary>Room left around a framed attack, as a multiple of what just fits it.</summary>
        [SerializeField, Min(1f)] private float framingMargin = 1.4f;
        /// <summary>Seconds a framing is held after the last framed event before the camera returns.</summary>
        [SerializeField, Min(0f)] private float framingHold = 0.8f;

        /// <summary>Pans the focus point, relative to the camera's heading.</summary>
        [SerializeField] private InputAction pan = panAction();
        /// <summary>Zooms in and out.</summary>
        [SerializeField] private InputAction zoom = new InputAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
        /// <summary>Turns the camera 45 degrees to the left.</summary>
        [SerializeField] private InputAction turnLeft = new InputAction("Turn Left", InputActionType.Button, "<Keyboard>/q");
        /// <summary>Turns the camera 45 degrees to the right.</summary>
        [SerializeField] private InputAction turnRight = new InputAction("Turn Right", InputActionType.Button, "<Keyboard>/e");
        /// <summary>Jumps to the active unit.</summary>
        [SerializeField] private InputAction focus = new InputAction("Focus", InputActionType.Button, "<Keyboard>/f");

        private static readonly ProfilerMarker updateMarker = new ProfilerMarker("Encounter.Camera");
        private Vector3 focusPoint, shownFocus;
        private float shownYaw, shownDistance;
        private bool placed;
        private UnitId followed = UnitId.None;
        private bool framing;
        private float restoreDistance, framedAt;
        private Camera eye;

        /// <summary>The encounter being watched.</summary>
        public EncounterRoot Root { get => encounter; set => encounter = value; }

        /// <summary>The point on the floor the camera is heading for.</summary>
        public Vector3 FocusPoint => focusPoint;

        /// <summary>The distance from the focus point the camera is heading for, in metres.</summary>
        public float TargetDistance => distance;

        /// <summary>True while an attack is framed.</summary>
        public bool IsFraming => framing;

        /// <summary>
        /// Frames an attack or ability event: the attacker and its target (or the aim point, with room for the
        /// ability's radius). Returns false for any other event, or one with nothing to frame. Playback calls it
        /// through <see cref="EncounterRoot.EventPlayed"/>; tests call it directly.
        /// </summary>
        public bool Frame(GameEvent played)
        {
            if (encounter == null || encounter.Encounter == null || played.Unit.IsNone) return false;
            if (played.Kind != EventKind.AttackStarted && played.Kind != EventKind.AbilityUsed) return false;
            if (!encounter.TryGetView(played.Unit, out UnitView attacker) || attacker == null) return false;

            float padding = 1f;
            Vector3 target;
            if (!played.Other.IsNone && encounter.TryGetView(played.Other, out UnitView targetView) && targetView != null)
                target = targetView.transform.position;
            else if (played.Kind == EventKind.AbilityUsed)
            {
                AbilityDefinition definition = encounter.Encounter.Abilities.GetDefinition(played.Value);
                if (definition.Targeting == AbilityTargeting.Self) return false;
                target = GridSpace.ToWorld(played.Position, floorHeight());
                padding = Mathf.Max(padding, definition.Radius);
            }
            else return false;

            Vector3 from = attacker.transform.position;
            float halfSpan = 0.5f * Vector2.Distance(new Vector2(from.x, from.z), new Vector2(target.x, target.z)) + padding;
            if (eye == null) eye = GetComponent<Camera>();
            float halfVertical = 0.5f * eye.fieldOfView * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * eye.aspect);
            float fit = halfSpan * framingMargin / Mathf.Tan(Mathf.Min(halfVertical, halfHorizontal));

            if (!framing) restoreDistance = distance;
            framing = true;
            framedAt = Time.unscaledTime;
            distance = Mathf.Clamp(fit, minDistance, maxDistance);
            LookAt(0.5f * (from + target));
            return true;
        }

        /// <summary>Sets the point on the floor to look at; the camera glides there.</summary>
        public void LookAt(Vector3 point) => focusPoint = clampToMap(point);

        /// <summary>Jumps straight to the target view, without gliding.</summary>
        public void SnapToTarget()
        {
            shownFocus = focusPoint;
            shownYaw = yaw;
            shownDistance = distance;
            apply();
        }

        private static InputAction panAction()
        {
            var created = new InputAction("Pan", InputActionType.Value);
            created.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                   .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            created.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                   .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            return created;
        }

        private void Awake()
        {
            if (encounter == null) encounter = FindAnyObjectByType<EncounterRoot>();
            eye = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (encounter != null) encounter.EventPlayed += onEventPlayed;
            pan.Enable();
            zoom.Enable();
            turnLeft.Enable();
            turnRight.Enable();
            focus.Enable();
        }

        private void OnDisable()
        {
            if (encounter != null) encounter.EventPlayed -= onEventPlayed;
            pan.Disable();
            zoom.Disable();
            turnLeft.Disable();
            turnRight.Disable();
            focus.Disable();
        }

        private void LateUpdate()
        {
            using ProfilerMarker.AutoScope scope = updateMarker.Auto();
            if (!placed)
            {
                // Start by looking at whatever the camera was pointed at in the scene.
                Ray view = new Ray(transform.position, transform.forward);
                var floor = new Plane(Vector3.up, new Vector3(0f, floorHeight(), 0f));
                LookAt(floor.Raycast(view, out float hit) ? view.GetPoint(hit) : transform.position);
                SnapToTarget();
                placed = true;
            }

            float dt = Time.unscaledDeltaTime;
            bool paused = encounter != null && encounter.IsPaused;
            Vector2 move = paused ? Vector2.zero : pan.ReadValue<Vector2>();
            if (move.sqrMagnitude > 0f)
            {
                Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
                Vector3 direction = heading * new Vector3(move.x, 0f, move.y);
                LookAt(focusPoint + direction * (panSpeed * distance / 10f * dt));
            }
            bool overHud = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            float scroll = overHud || paused ? 0f : zoom.ReadValue<float>();
            if (scroll != 0f) distance = Mathf.Clamp(distance * (1f - zoomStep * Mathf.Sign(scroll)), minDistance, maxDistance);
            if (!paused && turnLeft.WasPressedThisFrame()) yaw += 45f;
            if (!paused && turnRight.WasPressedThisFrame()) yaw -= 45f;
            endFramingWhenDone();
            follow(!paused && focus.WasPressedThisFrame());

            float blend = 1f - Mathf.Exp(-sharpness * dt);
            shownFocus = Vector3.Lerp(shownFocus, focusPoint, blend);
            shownYaw = Mathf.LerpAngle(shownYaw, yaw, blend);
            shownDistance = Mathf.Lerp(shownDistance, distance, blend);
            apply();
        }

        private void onEventPlayed(GameEvent played)
        {
            if (frameAttacks) Frame(played);
        }

        // Once playback has caught up and the hold has passed, goes back to the zoom from before the framing and to
        // the active unit.
        private void endFramingWhenDone()
        {
            if (!framing || encounter == null || encounter.IsPlayingBack || Time.unscaledTime - framedAt < framingHold) return;
            framing = false;
            distance = restoreDistance;
            follow(true);
        }

        // Glides to the active unit when its turn has started and playback has caught up, or when asked to. A framing
        // in progress keeps the camera until it ends.
        private void follow(bool forced)
        {
            if (encounter == null || encounter.Encounter == null || encounter.IsPlayingBack || (framing && !forced)) return;
            UnitId active = encounter.Encounter.Flow.ActiveUnit;
            if (active.IsNone || (!forced && active == followed)) return;
            followed = active;
            if (encounter.TryGetView(active, out UnitView view) && view != null) LookAt(view.transform.position);
        }

        private void apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, shownYaw, 0f);
            transform.SetPositionAndRotation(shownFocus - rotation * Vector3.forward * shownDistance, rotation);
        }

        private float floorHeight() => encounter != null && encounter.Map != null ? encounter.Map.FloorHeight : 0f;

        private Vector3 clampToMap(Vector3 point)
        {
            point.y = floorHeight();
            MapGridAsset map = encounter != null ? encounter.Map : null;
            if (map == null || map.CellCount == 0) return point;
            point.x = Mathf.Clamp(point.x, map.Origin.x, map.Origin.x + map.Width * map.CellSize);
            point.z = Mathf.Clamp(point.z, map.Origin.y, map.Origin.y + map.Height * map.CellSize);
            return point;
        }
    }
}
