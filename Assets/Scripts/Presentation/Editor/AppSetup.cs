// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Presentation.Tooling
{
    /// <summary>
    /// Tools ▸ App ▸ Set Up Main Menu and Build List: creates the MainMenu scene (a camera and an "App" object with
    /// <see cref="MainMenu"/>) if it does not exist, and sets the build list to MainMenu first, then the encounter scene,
    /// leaving test scenes out. Safe to run again.
    /// </summary>
    public static class AppSetup
    {
        /// <summary>Where the MainMenu scene lives.</summary>
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        /// <summary>The encounter scene the main menu starts.</summary>
        public const string EncounterScenePath = "Assets/Scenes/MallGraybox.unity";

        /// <summary>Creates the MainMenu scene if needed and sets the build list.</summary>
        [MenuItem("Tools/App/Set Up Main Menu and Build List")]
        public static void SetUp()
        {
            if (!File.Exists(MenuScenePath))
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                Camera view = Camera.main;
                if (view != null)
                {
                    view.clearFlags = CameraClearFlags.SolidColor;
                    view.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
                }
                new GameObject("App").AddComponent<MainMenu>();
                EditorSceneManager.SaveScene(scene, MenuScenePath);
                Debug.Log($"[App setup] Created {MenuScenePath}.");
            }

            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuScenePath, true) };
            if (File.Exists(EncounterScenePath)) scenes.Add(new EditorBuildSettingsScene(EncounterScenePath, true));
            else Debug.LogWarning($"[App setup] {EncounterScenePath} not found; add the encounter scene to the build list by hand.");
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[App setup] Build list: {string.Join(", ", scenes.ConvertAll(s => s.path))}.");
        }
    }
}
