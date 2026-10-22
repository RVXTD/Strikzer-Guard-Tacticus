// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.Presentation
{
    /// <summary>
    /// Builds the menus' placeholder uGUI widgets in code, in one consistent style: an overlay canvas, a full-screen
    /// backdrop, a centred column, titles, buttons, sliders and "&lt; value &gt;" option cyclers. Built in code like the HUD,
    /// until the art pass decides between restyling these and moving to UI Toolkit.
    /// </summary>
    public static class MenuWidgets
    {
        /// <summary>Colour of the backdrop behind a menu.</summary>
        public static readonly Color BackdropColour = new Color(0f, 0f, 0f, 0.55f);
        /// <summary>Colour of a menu's column.</summary>
        public static readonly Color PanelColour = new Color(0.08f, 0.08f, 0.1f, 0.92f);
        /// <summary>Colour of text on a panel.</summary>
        public static readonly Color TextColour = new Color(0.95f, 0.95f, 0.95f);

        private static Font font;

        /// <summary>The menus' font: Unity's built-in one.</summary>
        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>A screen-space overlay canvas under <paramref name="parent"/>, drawn above canvases with a lower <paramref name="sortingOrder"/> (the HUD is 0).</summary>
        public static RectTransform Canvas(string name, Transform parent, int sortingOrder)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform));
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            return (RectTransform)canvasObject.transform;
        }

        /// <summary>In play mode, adds an EventSystem driven by the Input System if the scene has none.</summary>
        public static void EnsureEventSystem(Transform parent)
        {
            if (!Application.isPlaying || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(parent, false);
        }

        /// <summary>A full-screen image that also blocks clicks to anything beneath it.</summary>
        public static Image Backdrop(Transform parent, Color colour)
        {
            Image image = fill("Backdrop", parent).gameObject.AddComponent<Image>();
            image.color = colour;
            return image;
        }

        /// <summary>A centred column that stacks its children from the top.</summary>
        public static RectTransform Column(string name, Transform parent, float width)
        {
            var column = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            column.SetParent(parent, false);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(width, 0f);
            column.gameObject.AddComponent<Image>().color = PanelColour;
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return column;
        }

        /// <summary>A line of text in a column.</summary>
        public static Text Label(Transform parent, string text, int size, TextAnchor alignment = TextAnchor.MiddleCenter, float height = 0f)
        {
            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<Text>();
            label.transform.SetParent(parent, false);
            label.font = Font;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = TextColour;
            label.text = text;
            label.raycastTarget = false;
            sized(label.gameObject, height > 0f ? height : size * 1.6f);
            return label;
        }

        /// <summary>A button in a column.</summary>
        public static Button Button(Transform parent, string caption, Action onClick)
        {
            var background = new GameObject(caption, typeof(RectTransform)).AddComponent<Image>();
            background.transform.SetParent(parent, false);
            background.color = Color.white;
            sized(background.gameObject, 48f);
            var button = background.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = background;
            Text text = textIn(background.transform, caption, 22);
            text.color = Color.black;
            button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>A labelled slider from 0 to 1 in a column; <paramref name="onChange"/> is called with each new value.</summary>
        public static Slider Slider(Transform parent, string caption, float value, Action<float> onChange)
        {
            RectTransform row = Row(parent, caption);
            var track = new GameObject("Slider", typeof(RectTransform)).AddComponent<Image>();
            track.transform.SetParent(row, false);
            track.color = new Color(1f, 1f, 1f, 0.25f);
            var trackLayout = track.gameObject.AddComponent<LayoutElement>();
            trackLayout.flexibleWidth = 1f;
            trackLayout.preferredHeight = 18f;

            RectTransform fillArea = fill("Fill area", track.transform);
            Image fillImage = fill("Fill", fillArea).gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.8f, 1f);
            RectTransform handleArea = fill("Handle area", track.transform);
            Image handle = new GameObject("Handle", typeof(RectTransform)).AddComponent<Image>();
            handle.transform.SetParent(handleArea, false);
            handle.rectTransform.sizeDelta = new Vector2(16f, 26f);

            var slider = track.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fillImage.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(changed => onChange(changed));
            return slider;
        }

        /// <summary>
        /// A labelled "&lt; value &gt;" cycler in a column (plain arrows, which the built-in font has).
        /// <paramref name="step"/> is called with -1 or +1 and returns the text to show; returns the value text so the
        /// caller can refresh it.
        /// </summary>
        public static Text Cycler(Transform parent, string caption, string value, Func<int, string> step)
        {
            RectTransform row = Row(parent, caption);
            Text shown = null;
            arrow(row, "<", () => shown.text = step(-1));
            shown = textIn(row, value, 22);
            var shownLayout = shown.gameObject.AddComponent<LayoutElement>();
            shownLayout.flexibleWidth = 1f;
            arrow(row, ">", () => shown.text = step(+1));
            return shown;
        }

        /// <summary>A row with a caption on the left; widgets added to it line up after the caption.</summary>
        public static RectTransform Row(Transform parent, string caption)
        {
            var row = (RectTransform)new GameObject(caption, typeof(RectTransform)).transform;
            row.SetParent(parent, false);
            sized(row.gameObject, 44f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Text label = textIn(row, caption, 20);
            label.alignment = TextAnchor.MiddleLeft;
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
            return row;
        }

        /// <summary>Selects <paramref name="target"/> for keyboard and gamepad navigation, if an EventSystem exists.</summary>
        public static void Select(Selectable target)
        {
            if (target != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private static void arrow(Transform row, string glyph, Action onClick)
        {
            var background = new GameObject(glyph, typeof(RectTransform)).AddComponent<Image>();
            background.transform.SetParent(row, false);
            background.color = Color.white;
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 44f;
            layout.preferredHeight = 40f;
            var button = background.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = background;
            textIn(background.transform, glyph, 20).color = Color.black;
            button.onClick.AddListener(() => onClick());
        }

        private static Text textIn(Transform parent, string text, int size)
        {
            var label = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            label.transform.SetParent(parent, false);
            label.font = Font;
            label.fontSize = size;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = TextColour;
            label.text = text;
            label.raycastTarget = false;
            RectTransform area = label.rectTransform;
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = area.offsetMax = Vector2.zero;
            return label;
        }

        private static RectTransform fill(string name, Transform parent)
        {
            var area = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            area.SetParent(parent, false);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = area.offsetMax = Vector2.zero;
            return area;
        }

        private static void sized(GameObject target, float height)
        {
            var layout = target.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
        }
    }
}
