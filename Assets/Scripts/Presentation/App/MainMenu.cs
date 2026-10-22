// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Presentation
{
    /// <summary>Which screen the main menu shows.</summary>
    public enum MainMenuState
    {
        /// <summary>The title, then "Press any button".</summary>
        Splash,
        /// <summary>Start, Settings and Quit.</summary>
        Main,
        /// <summary>The settings panel.</summary>
        Settings,
    }

    /// <summary>
    /// The MainMenu scene's screens: a splash whose title fades in before "Press any button" pulses (any key, button or
    /// click goes on), then the main menu (Start loads the encounter scene, Settings, Quit), and the shared
    /// <see cref="SettingsPanel"/>. Built in code with uGUI like the HUD; keyboard and gamepad navigation start on each
    /// screen's first button.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        /// <summary>The scene Start loads; it must be in the build list.</summary>
        [SerializeField] private string encounterScene = "MallGraybox";
        /// <summary>The game's title on the splash.</summary>
        [SerializeField] private string title = "Auto Buster Tacticus";
        /// <summary>The line under the title.</summary>
        [SerializeField] private string subtitle = "The robots have taken the mall. Take it back.";
        /// <summary>Seconds the title takes to fade in.</summary>
        [SerializeField, Min(0.1f)] private float titleFadeSeconds = 1.5f;

        private RectTransform canvas, splash, main;
        private SettingsPanel settings;
        private Text titleText, prompt;
        private Button startButton;
        private IDisposable anyButton;
        private float shownAt;

        /// <summary>The screen being shown.</summary>
        public MainMenuState State { get; private set; } = MainMenuState.Splash;

        /// <summary>Builds the screens if they are not built yet; Start calls it, and tests call it directly.</summary>
        public void EnsureBuilt()
        {
            if (canvas != null) return;
            canvas = MenuWidgets.Canvas("Main menu", transform, 10);
            MenuWidgets.EnsureEventSystem(transform);
            MenuWidgets.Backdrop(canvas, new Color(0.05f, 0.05f, 0.08f, 1f));

            splash = MenuWidgets.Column("Splash", canvas, 900f);
            splash.GetComponent<Image>().color = Color.clear;
            titleText = MenuWidgets.Label(splash, title, 64, height: 110f);
            MenuWidgets.Label(splash, subtitle, 24, height: 50f);
            prompt = MenuWidgets.Label(splash, "Press any button", 26, height: 90f);

            main = MenuWidgets.Column("Main", canvas, 420f);
            MenuWidgets.Label(main, title, 34, height: 70f);
            startButton = MenuWidgets.Button(main, "Start", StartEncounter);
            MenuWidgets.Button(main, "Settings", OpenSettings);
            MenuWidgets.Button(main, "Quit", Quit);
            settings = new SettingsPanel(canvas, GameSettings.Shared, Back);
            showState();
        }

        /// <summary>Leaves the splash for the main menu.</summary>
        public void AdvanceFromSplash()
        {
            if (State != MainMenuState.Splash) return;
            State = MainMenuState.Main;
            showState();
        }

        /// <summary>Opens the settings panel.</summary>
        public void OpenSettings()
        {
            State = MainMenuState.Settings;
            showState();
        }

        /// <summary>Returns from the settings panel to the main menu.</summary>
        public void Back()
        {
            State = MainMenuState.Main;
            showState();
        }

        /// <summary>Loads the encounter scene, if it is in the build list.</summary>
        public void StartEncounter()
        {
            if (!Application.CanStreamedLevelBeLoaded(encounterScene))
            {
                Debug.LogWarning($"[Main menu] Scene '{encounterScene}' is not in the build list; run Tools ▸ App ▸ Set Up Main Menu and Build List.", this);
                return;
            }
            SceneManager.LoadScene(encounterScene);
        }

        /// <summary>Quits the game (stops play mode in the editor).</summary>
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Start()
        {
            EnsureBuilt();
            shownAt = Time.unscaledTime;
            anyButton = InputSystem.onAnyButtonPress.CallOnce(_ => AdvanceFromSplash());
        }

        private void OnDestroy() => anyButton?.Dispose();

        private void Update()
        {
            if (State != MainMenuState.Splash || titleText == null) return;
            float age = Time.unscaledTime - shownAt;
            setAlpha(titleText, Mathf.Clamp01(age / titleFadeSeconds));
            setAlpha(prompt, age < titleFadeSeconds ? 0f : 0.55f + 0.45f * Mathf.Sin(3f * (age - titleFadeSeconds)));
        }

        private void showState()
        {
            splash.gameObject.SetActive(State == MainMenuState.Splash);
            main.gameObject.SetActive(State == MainMenuState.Main);
            if (State == MainMenuState.Settings) settings.Show(true);
            else settings.Root.gameObject.SetActive(false);
            if (State == MainMenuState.Main) MenuWidgets.Select(startButton);
        }

        private static void setAlpha(Graphic graphic, float alpha)
        {
            Color colour = graphic.color;
            colour.a = alpha;
            graphic.color = colour;
        }
    }
}
