// Written by Claude (Anthropic), October 2026; EncounterAi added for step 7, the overlay and shaders for step 8a,
// audio for step 8b, and the pause menu and its dimming material for step 8c; to be reviewed by Alexander Stangl per
// the course syllabus.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Presentation.Tooling
{
    /// <summary>
    /// Tools ▸ Encounter ▸ Set Up Scene: adds what a combat scene needs to the open scene. An "Encounter" object gets
    /// <see cref="EncounterRoot"/>, <see cref="EncounterInput"/>, <see cref="PreviewView"/>, <see cref="EncounterHud"/>,
    /// <see cref="EncounterAi"/>, <see cref="EncounterOverlay"/>, <see cref="EncounterAudio"/> and <see cref="PauseMenu"/>;
    /// the overlay and preview get their shaders from Assets/Materials/Shaders, where the pause-dim material is created
    /// if it is missing; the root is given the scene's baked map and the project's tuning,
    /// and the audio the project's sound dictionary, when there is exactly one of each; the main camera gets an
    /// <see cref="EncounterCamera"/>. Anything already there is left alone, so it is safe to run again.
    /// Units are still placed by hand.
    /// </summary>
    public static class EncounterSceneSetup
    {
        private const string undoName = "Set up encounter scene";
        /// <summary>Where the overlay shader lives.</summary>
        public const string OverlayShaderPath = "Assets/Materials/Shaders/EncounterOverlay.shader";
        /// <summary>Where the hover rim shader lives.</summary>
        public const string RimShaderPath = "Assets/Materials/Shaders/HoverRim.shader";
        /// <summary>Where the pause-dim shader lives.</summary>
        public const string PauseDimShaderPath = "Assets/Materials/Shaders/PauseDim.shader";
        /// <summary>The material URP's Full Screen Pass feature runs for the pause dimming.</summary>
        public const string PauseDimMaterialPath = "Assets/Materials/Shaders/PauseDim.mat";

        /// <summary>Sets up the open scene and logs what was added.</summary>
        [MenuItem("Tools/Encounter/Set Up Scene")]
        public static void SetUpScene()
        {
            var added = new List<string>();
            EncounterRoot root = Object.FindAnyObjectByType<EncounterRoot>();
            GameObject host = root != null ? root.gameObject : null;
            if (host == null)
            {
                host = new GameObject("Encounter");
                Undo.RegisterCreatedObjectUndo(host, undoName);
                added.Add("an Encounter object");
            }
            added.AddRange(AddEncounterComponents(host));
            added.AddRange(AssignShaders(host));
            if (EnsurePauseDimMaterial()) added.Add("the pause-dim material (add it to the renderers' Full Screen Pass)");

            root = host.GetComponent<EncounterRoot>();
            if (root.Map == null && Object.FindObjectsByType<MapBakeVolume>().Length == 1)
            {
                MapGridAsset map = Object.FindAnyObjectByType<MapBakeVolume>().Target;
                if (map != null)
                {
                    Undo.RecordObject(root, undoName);
                    root.Map = map;
                    added.Add($"map {map.name}");
                }
            }
            EncounterAudio audio = host.GetComponent<EncounterAudio>();
            if (audio.Dictionary == null)
            {
                string[] dictionaries = AssetDatabase.FindAssets("t:" + nameof(SoundDictionary));
                if (dictionaries.Length == 1)
                {
                    Undo.RecordObject(audio, undoName);
                    audio.Dictionary = AssetDatabase.LoadAssetAtPath<SoundDictionary>(AssetDatabase.GUIDToAssetPath(dictionaries[0]));
                    added.Add($"sound dictionary {audio.Dictionary.name}");
                }
            }
            if (root.Tuning == null)
            {
                string[] tunings = AssetDatabase.FindAssets("t:" + nameof(EncounterTuning));
                if (tunings.Length == 1)
                {
                    Undo.RecordObject(root, undoName);
                    root.Tuning = AssetDatabase.LoadAssetAtPath<EncounterTuning>(AssetDatabase.GUIDToAssetPath(tunings[0]));
                    added.Add($"tuning {root.Tuning.name}");
                }
            }

            Camera view = Camera.main;
            if (view == null)
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
                Undo.RegisterCreatedObjectUndo(cameraObject, undoName);
                view = cameraObject.GetComponent<Camera>();
                added.Add("a main camera");
            }
            if (view.GetComponent<EncounterCamera>() == null)
            {
                Undo.AddComponent<EncounterCamera>(view.gameObject);
                added.Add("EncounterCamera on " + view.name);
            }

            EditorSceneManager.MarkSceneDirty(host.scene);
            Selection.activeGameObject = host;
            string missing = (root.Map == null ? " Assign the baked map on EncounterRoot." : "") +
                             (root.Tuning == null ? " Assign an EncounterTuning (or leave it empty for the defaults)." : "");
            Debug.Log(added.Count > 0
                ? $"[Encounter setup] Added {string.Join(", ", added)}.{missing}"
                : $"[Encounter setup] The scene was already set up.{missing}", host);
        }

        /// <summary>
        /// Adds whichever of <see cref="EncounterRoot"/>, <see cref="EncounterInput"/>, <see cref="PreviewView"/>,
        /// <see cref="EncounterHud"/>, <see cref="EncounterAi"/>, <see cref="EncounterOverlay"/>, <see cref="EncounterAudio"/>
        /// and <see cref="PauseMenu"/> <paramref name="host"/> lacks, with undo, and returns the names of those added.
        /// </summary>
        public static List<string> AddEncounterComponents(GameObject host)
        {
            var added = new List<string>();
            addIfMissing<EncounterRoot>(host, added);
            addIfMissing<EncounterInput>(host, added);
            addIfMissing<PreviewView>(host, added);
            addIfMissing<EncounterHud>(host, added);
            addIfMissing<EncounterAi>(host, added);
            addIfMissing<EncounterOverlay>(host, added);
            addIfMissing<EncounterAudio>(host, added);
            addIfMissing<PauseMenu>(host, added);
            return added;
        }

        /// <summary>Creates the pause-dim material from its shader if it does not exist yet; true if it was created.</summary>
        public static bool EnsurePauseDimMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(PauseDimMaterialPath) != null) return false;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(PauseDimShaderPath);
            if (shader == null) return false;
            AssetDatabase.CreateAsset(new Material(shader), PauseDimMaterialPath);
            return true;
        }

        /// <summary>
        /// Gives <paramref name="host"/>'s <see cref="EncounterOverlay"/> and <see cref="PreviewView"/> their shaders
        /// where those are empty, with undo, and returns what was assigned.
        /// </summary>
        public static List<string> AssignShaders(GameObject host)
        {
            var assigned = new List<string>();
            EncounterOverlay overlay = host.GetComponent<EncounterOverlay>();
            if (overlay != null && overlay.Shader == null)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(OverlayShaderPath);
                if (shader != null)
                {
                    Undo.RecordObject(overlay, undoName);
                    overlay.Shader = shader;
                    assigned.Add("the overlay shader");
                }
            }
            PreviewView preview = host.GetComponent<PreviewView>();
            if (preview != null && preview.RimShader == null)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(RimShaderPath);
                if (shader != null)
                {
                    Undo.RecordObject(preview, undoName);
                    preview.RimShader = shader;
                    assigned.Add("the hover rim shader");
                }
            }
            return assigned;
        }

        private static void addIfMissing<T>(GameObject host, List<string> added) where T : Component
        {
            if (host.GetComponent<T>() != null) return;
            Undo.AddComponent<T>(host);
            added.Add(typeof(T).Name);
        }
    }
}
