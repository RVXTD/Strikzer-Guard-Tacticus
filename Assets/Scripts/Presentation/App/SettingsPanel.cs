// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Presentation
{
    /// <summary>
    /// The settings menu, shared by the main and pause menus: frame rate, master, music and effects volume, display
    /// mode and resolution. Every change applies at once (<see cref="GameSettings.Apply"/>); Back saves and returns.
    /// </summary>
    public sealed class SettingsPanel
    {
        private readonly GameSettings settings;
        private readonly Action back;
        private readonly Text frameRateText;

        /// <summary>Builds the panel under <paramref name="parent"/>, hidden; <paramref name="back"/> is called after Back saves.</summary>
        public SettingsPanel(Transform parent, GameSettings settings, Action back)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.back = back;
            Root = MenuWidgets.Column("Settings", parent, 640f);
            MenuWidgets.Label(Root, "Settings", 34, height: 60f);
            frameRateText = MenuWidgets.Cycler(Root, "Frame rate", GameSettings.Describe(settings.FrameRate), StepFrameRate);
            FirstSelectable = frameRateText.transform.parent.GetComponentInChildren<Selectable>();
            MenuWidgets.Slider(Root, "Master volume", settings.MasterVolume, value => change(() => settings.MasterVolume = value));
            MenuWidgets.Slider(Root, "Music", settings.MusicVolume, value => change(() => settings.MusicVolume = value));
            MenuWidgets.Slider(Root, "Effects", settings.EffectsVolume, value => change(() => settings.EffectsVolume = value));
            MenuWidgets.Cycler(Root, "Display", displayText(), _ =>
            {
                change(() => settings.Fullscreen = !settings.Fullscreen);
                return displayText();
            });
            MenuWidgets.Cycler(Root, "Resolution", resolutionText(), StepResolution);
            MenuWidgets.Button(Root, "Back", Back);
            Show(false);
        }

        /// <summary>The panel's column.</summary>
        public RectTransform Root { get; }

        /// <summary>The first control, selected when the panel opens.</summary>
        public Selectable FirstSelectable { get; }

        /// <summary>The frame-rate value as shown.</summary>
        public string FrameRateShown => frameRateText.text;

        /// <summary>Shows or hides the panel, selecting its first control when shown.</summary>
        public void Show(bool shown)
        {
            Root.gameObject.SetActive(shown);
            if (shown) MenuWidgets.Select(FirstSelectable);
        }

        /// <summary>Moves the frame-rate setting one option either way, applies it, and returns the new value's text.</summary>
        public string StepFrameRate(int direction)
        {
            int count = Enum.GetValues(typeof(FrameRateMode)).Length;
            change(() => settings.FrameRate = (FrameRateMode)(((int)settings.FrameRate + direction + count) % count));
            frameRateText.text = GameSettings.Describe(settings.FrameRate);
            return frameRateText.text;
        }

        /// <summary>Moves the resolution one option either way (through "current" and every supported one) and returns its text.</summary>
        public string StepResolution(int direction)
        {
            int count = Screen.resolutions.Length + 1; // index -1 is "current"
            change(() => settings.ResolutionIndex = (settings.ResolutionIndex + 1 + direction + count) % count - 1);
            return resolutionText();
        }

        /// <summary>Saves the settings and returns to the menu that opened the panel.</summary>
        public void Back()
        {
            settings.Save();
            Show(false);
            back?.Invoke();
        }

        private void change(Action set)
        {
            set();
            settings.Apply();
        }

        private string displayText() => settings.Fullscreen ? "Fullscreen" : "Windowed";

        private string resolutionText()
        {
            Resolution[] resolutions = Screen.resolutions;
            if (settings.ResolutionIndex < 0 || settings.ResolutionIndex >= resolutions.Length) return $"Current ({Screen.width} x {Screen.height})";
            Resolution chosen = resolutions[settings.ResolutionIndex];
            return $"{chosen.width} x {chosen.height}";
        }
    }
}
