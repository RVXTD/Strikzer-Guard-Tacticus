// Written by Claude (Anthropic), October 2026; references looked up in Awake after the first Esc while targeting
// paused the game; to be reviewed by Alexander Stangl per the course syllabus.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Presentation
{
    /// <summary>
    /// Pauses the encounter. While paused, <see cref="EncounterRoot.IsPaused"/> stops input, the AI and the camera,
    /// time stands still (so playback halts), sound effects pause (music plays on), and the global shader float
    /// <see cref="DimProperty"/> eases up so the Full Screen Pass with the pause-dim shader desaturates and darkens the
    /// world behind the menu: Resume, Settings, Restart, and Quit to Main Menu. Esc cancels ability targeting first, as
    /// it always has; only when nothing was being targeted does it pause. In the settings panel it goes back to the menu.
    /// </summary>
    [RequireComponent(typeof(EncounterRoot))]
    public sealed class PauseMenu : MonoBehaviour
    {
        /// <summary>Name of the global shader float the pause-dim shader reads, from 0 (none) to 1 (fully dimmed).</summary>
        public const string DimProperty = "_PauseDim";

        /// <summary>Opens and closes the menu.</summary>
        [SerializeField] private InputAction pause = pauseAction();
        /// <summary>The scene Quit to Main Menu loads; it must be in the build list.</summary>
        [SerializeField] private string menuScene = "MainMenu";
        /// <summary>Seconds the dimming takes to ease in or out.</summary>
        [SerializeField, Min(0.01f)] private float dimSeconds = 0.25f;

        private static readonly int dimId = Shader.PropertyToID(DimProperty);
        private EncounterRoot encounter;
        private EncounterInput input;
        private RectTransform canvas, menu;
        private SettingsPanel settings;
        private Button resumeButton;
        private float dim;
        private bool targetingAtFrameStart;

        /// <summary>True while the game is paused.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>True while the menu's settings panel is open.</summary>
        public bool IsInSettings => settings != null && settings.Root.gameObject.activeSelf;

        /// <summary>
        /// What the pause key does: cancel ability targeting if it was active, otherwise toggle pause. Update calls it
        /// on a press; tests call it directly.
        /// </summary>
        public void OnPausePressed()
        {
            ensureReferences();
            TurnController controller = input != null ? input.Controller : null;
            // Targeting at the start of this frame, or still: whether EncounterInput has already cancelled it this frame
            // depends on which runs first, and must not decide whether the game pauses.
            if (!IsPaused && controller != null && (targetingAtFrameStart || controller.IsTargeting))
            {
                controller.Cancel();
                return;
            }
            if (IsInSettings) settings.Back();
            else if (IsPaused) Resume();
            else Pause();
        }

        /// <summary>Pauses the encounter and opens the menu.</summary>
        public void Pause()
        {
            ensureReferences();
            if (IsPaused) return;
            IsPaused = true;
            encounter.IsPaused = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            showMenu(true);
        }

        /// <summary>Closes the menu and resumes the encounter.</summary>
        public void Resume()
        {
            ensureReferences();
            if (!IsPaused) return;
            IsPaused = false;
            encounter.IsPaused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private static InputAction pauseAction()
        {
            var created = new InputAction("Pause", InputActionType.Button);
            created.AddBinding("<Keyboard>/escape");
            created.AddBinding("<Keyboard>/p");
            created.AddBinding("<Gamepad>/start");
            return created;
        }

        /// <summary>
        /// Notes whether an ability is being targeted as the frame ends, so a press next frame cancels the targeting even
        /// if <see cref="EncounterInput"/> has already done so earlier in that frame. LateUpdate calls it; tests call it
        /// directly.
        /// </summary>
        public void RecordTargeting()
        {
            ensureReferences();
            TurnController controller = input != null ? input.Controller : null;
            targetingAtFrameStart = controller != null && controller.IsTargeting;
        }

        private void ensureReferences()
        {
            if (encounter == null) encounter = GetComponent<EncounterRoot>();
            if (input == null) input = GetComponent<EncounterInput>();
        }

        private void Awake() => ensureReferences();

        private void OnEnable() => pause.Enable();

        private void OnDisable()
        {
            pause.Disable();
            leavePause(); // never leave the game frozen or dimmed behind
        }

        private void Update()
        {
            if (pause.WasPressedThisFrame()) OnPausePressed();
            float target = IsPaused ? 1f : 0f;
            dim = Mathf.MoveTowards(dim, target, Time.unscaledDeltaTime / dimSeconds);
            Shader.SetGlobalFloat(dimId, dim);
        }

        private void LateUpdate() => RecordTargeting();

        private void showMenu(bool shown)
        {
            if (canvas == null) build();
            canvas.gameObject.SetActive(shown);
            settings.Show(false);
            menu.gameObject.SetActive(true);
            MenuWidgets.Select(resumeButton);
        }

        private void build()
        {
            canvas = MenuWidgets.Canvas("Pause menu", transform, 10);
            MenuWidgets.EnsureEventSystem(transform);
            MenuWidgets.Backdrop(canvas, MenuWidgets.BackdropColour);
            menu = MenuWidgets.Column("Paused", canvas, 420f);
            MenuWidgets.Label(menu, "Paused", 34, height: 60f);
            resumeButton = MenuWidgets.Button(menu, "Resume", Resume);
            MenuWidgets.Button(menu, "Settings", () =>
            {
                menu.gameObject.SetActive(false);
                settings.Show(true);
            });
            MenuWidgets.Button(menu, "Restart", () => load(SceneManager.GetActiveScene().name));
            MenuWidgets.Button(menu, "Quit to Main Menu", () => load(menuScene));
            settings = new SettingsPanel(canvas, GameSettings.Shared, () =>
            {
                menu.gameObject.SetActive(true);
                MenuWidgets.Select(resumeButton);
            });
        }

        private void load(string scene)
        {
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogWarning($"[Pause menu] Scene '{scene}' is not in the build list; run Tools ▸ App ▸ Set Up Main Menu and Build List.", this);
                return;
            }
            leavePause();
            SceneManager.LoadScene(scene);
        }

        private void leavePause()
        {
            if (IsPaused && encounter != null) encounter.IsPaused = false;
            IsPaused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            dim = 0f;
            Shader.SetGlobalFloat(dimId, 0f);
        }
    }
}
