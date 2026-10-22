// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>How fast the game renders.</summary>
    public enum FrameRateMode
    {
        /// <summary>At most 30 frames per second.</summary>
        Fps30,
        /// <summary>At most 60 frames per second.</summary>
        Fps60,
        /// <summary>At most 120 frames per second.</summary>
        Fps120,
        /// <summary>As fast as the machine can (a turn-based scene gains nothing from it, and laptops warm up).</summary>
        Uncapped,
        /// <summary>The display's own refresh rate (VSync).</summary>
        MatchDisplay,
    }

    /// <summary>
    /// The player's settings: frame rate, volumes and display. They are saved per player with PlayerPrefs and applied
    /// to the engine by <see cref="Apply"/>. <see cref="Shared"/> is the app's one instance, applied at start-up so any
    /// scene played directly is capped; this is presentation-only app state (the rules layer keeps no statics).
    /// Music and effects volumes are read by <see cref="EncounterAudio"/>; the master volume goes to the audio listener.
    /// </summary>
    public sealed class GameSettings
    {
        /// <summary>The key prefix the shared settings are saved under.</summary>
        public const string DefaultKeyPrefix = "AutoBuster.Settings.";

        private static GameSettings shared;
        private readonly string keyPrefix;

        /// <summary>Settings at their defaults, saved and loaded under <paramref name="keyPrefix"/>.</summary>
        public GameSettings(string keyPrefix = DefaultKeyPrefix)
        {
            this.keyPrefix = keyPrefix;
        }

        /// <summary>Raised by <see cref="Apply"/>, so open menus and audio can follow changes.</summary>
        public event Action Changed;

        /// <summary>The app's settings, loaded from PlayerPrefs on first use.</summary>
        public static GameSettings Shared
        {
            get
            {
                if (shared == null)
                {
                    shared = new GameSettings();
                    shared.Load();
                }
                return shared;
            }
        }

        /// <summary>How fast the game renders.</summary>
        public FrameRateMode FrameRate { get; set; } = FrameRateMode.Fps60;
        /// <summary>Overall volume, 0 to 1.</summary>
        public float MasterVolume { get; set; } = 1f;
        /// <summary>Music volume, 0 to 1, relative to the master volume.</summary>
        public float MusicVolume { get; set; } = 0.8f;
        /// <summary>Sound-effect volume, 0 to 1, relative to the master volume.</summary>
        public float EffectsVolume { get; set; } = 1f;
        /// <summary>True for full screen, false for a window.</summary>
        public bool Fullscreen { get; set; } = true;
        /// <summary>Index into <see cref="Screen.resolutions"/>, or -1 for the current resolution.</summary>
        public int ResolutionIndex { get; set; } = -1;

        /// <summary>Reads the saved settings, keeping the defaults for any not yet saved.</summary>
        public void Load()
        {
            FrameRate = (FrameRateMode)Mathf.Clamp(PlayerPrefs.GetInt(keyPrefix + "FrameRate", (int)FrameRate), 0, (int)FrameRateMode.MatchDisplay);
            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(keyPrefix + "MasterVolume", MasterVolume));
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(keyPrefix + "MusicVolume", MusicVolume));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(keyPrefix + "EffectsVolume", EffectsVolume));
            Fullscreen = PlayerPrefs.GetInt(keyPrefix + "Fullscreen", Fullscreen ? 1 : 0) != 0;
            ResolutionIndex = PlayerPrefs.GetInt(keyPrefix + "Resolution", ResolutionIndex);
        }

        /// <summary>Saves the settings.</summary>
        public void Save()
        {
            PlayerPrefs.SetInt(keyPrefix + "FrameRate", (int)FrameRate);
            PlayerPrefs.SetFloat(keyPrefix + "MasterVolume", MasterVolume);
            PlayerPrefs.SetFloat(keyPrefix + "MusicVolume", MusicVolume);
            PlayerPrefs.SetFloat(keyPrefix + "EffectsVolume", EffectsVolume);
            PlayerPrefs.SetInt(keyPrefix + "Fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(keyPrefix + "Resolution", ResolutionIndex);
            PlayerPrefs.Save();
        }

        /// <summary>Deletes the saved settings (tests clean up with it).</summary>
        public void DeleteSaved()
        {
            foreach (string key in new[] { "FrameRate", "MasterVolume", "MusicVolume", "EffectsVolume", "Fullscreen", "Resolution" })
                PlayerPrefs.DeleteKey(keyPrefix + key);
        }

        /// <summary>
        /// Applies the settings to the engine (frame cap or VSync, master volume, and, outside the editor, display mode
        /// and resolution) and raises <see cref="Changed"/>.
        /// </summary>
        public void Apply()
        {
            QualitySettings.vSyncCount = FrameRate == FrameRateMode.MatchDisplay ? 1 : 0;
            Application.targetFrameRate = TargetFrameRate(FrameRate);
            AudioListener.volume = MasterVolume;
            if (!Application.isEditor)
            {
                Resolution[] resolutions = Screen.resolutions;
                FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (ResolutionIndex >= 0 && ResolutionIndex < resolutions.Length)
                    Screen.SetResolution(resolutions[ResolutionIndex].width, resolutions[ResolutionIndex].height, mode);
                else
                    Screen.fullScreenMode = mode;
            }
            Changed?.Invoke();
        }

        /// <summary>The <see cref="Application.targetFrameRate"/> for a mode (-1 lets the platform decide).</summary>
        public static int TargetFrameRate(FrameRateMode mode)
        {
            switch (mode)
            {
                case FrameRateMode.Fps30: return 30;
                case FrameRateMode.Fps60: return 60;
                case FrameRateMode.Fps120: return 120;
                default: return -1;
            }
        }

        /// <summary>How a mode is shown in the settings menu.</summary>
        public static string Describe(FrameRateMode mode)
        {
            switch (mode)
            {
                case FrameRateMode.Fps30: return "30 fps";
                case FrameRateMode.Fps60: return "60 fps";
                case FrameRateMode.Fps120: return "120 fps";
                case FrameRateMode.Uncapped: return "Uncapped";
                default: return "Match display";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void applyAtStartUp()
        {
            shared = null; // re-read when entering play mode, even with domain reload off
            Shared.Apply();
        }
    }
}
