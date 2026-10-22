// Written by Claude (Anthropic), October 2026; names from the mall graybox (family and type names) added after
// a first run; role prefixes (the art team's naming convention, step 9a) added October 21, 2026; to be reviewed by
// Alexander Stangl per the course syllabus.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Game.Presentation.Tooling
{
    /// <summary>
    /// Tags imported level geometry and props for the map bake from their object names. A name that starts with a
    /// role prefix from the art team's naming convention (see Docs/ArtAssetGuide) is tagged by that prefix:
    /// <c>W_</c> Wall, <c>G_</c> Glass, <c>L_</c> LowObstacle, <c>F_</c> Floor and <c>D_</c> decoration (Ignore).
    /// Revit element ids (<c>" [123]_[456] Geometry"</c>) and Blender or Unity duplicate suffixes (<c>.001</c>,
    /// <c>" (1)"</c>) are stripped first. Other names fall back to the Revit family and type names: glazed curtain
    /// panels and mullions become Glass; basic walls, columns and the store block become Wall; kiosks, food-court
    /// furniture and generic models become LowObstacle; floors become Floor. Objects it does not recognise are
    /// listed, never guessed. Designers can change any tag afterwards.
    /// </summary>
    /// <remarks>
    /// The store ("store store") is a single mesh holding both its walls and its furniture, so it is tagged Wall
    /// as a whole; mark its benches and desk with a <see cref="MapOverrideVolume"/> (LowObstacle) if they should
    /// give half cover instead.
    /// </remarks>
    public static class GrayboxTagger
    {
        /// <summary>Tags every mesh under the selected objects whose name names a known Revit category.</summary>
        [MenuItem("Tools/Map/Auto-tag Revit Graybox")]
        public static void TagSelection()
        {
            int tagged = 0;
            var unknown = new SortedSet<string>();
            foreach (GameObject root in Selection.gameObjects)
                tagged += Tag(root, unknown);
            Debug.Log($"Auto-tagged {tagged} objects for the map bake." +
                      (unknown.Count == 0 ? "" : $" Not recognised (tag these by hand): {string.Join(", ", unknown.Take(40))}" +
                                                 (unknown.Count > 40 ? $" and {unknown.Count - 40} more" : "")));
        }

        /// <summary>Validates the menu item: something must be selected.</summary>
        [MenuItem("Tools/Map/Auto-tag Revit Graybox", true)]
        public static bool CanTagSelection() => Selection.gameObjects.Length > 0;

        /// <summary>
        /// Adds or updates a <see cref="MapSurface"/> on every object with a mesh under <paramref name="root"/> whose
        /// name (or an ancestor's, up to the root) names a known category, with undo. Adds the names of meshes it
        /// could not classify to <paramref name="unknown"/>. Returns how many objects it tagged.
        /// </summary>
        public static int Tag(GameObject root, ISet<string> unknown)
        {
            int tagged = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!TryClassify(filter.transform, root.transform, out SurfaceKind kind))
                {
                    unknown.Add(filter.name);
                    continue;
                }
                MapSurface surface = filter.GetComponent<MapSurface>();
                if (surface == null) surface = Undo.AddComponent<MapSurface>(filter.gameObject);
                else Undo.RecordObject(surface, "Auto-tag graybox");
                surface.Kind = kind;
                tagged++;
            }
            return tagged;
        }

        /// <summary>
        /// The surface kind an object name implies: its role prefix if it has one, otherwise the Revit category it
        /// names. False if it has neither. A <c>D_</c> name is recognised and gives Ignore.
        /// </summary>
        public static bool TryKindFromName(string objectName, out SurfaceKind kind)
        {
            string name = objectName ?? "";
            if (tryKindFromPrefix(name, out kind)) return true;
            // Curtain walls first: "Curtain Wall Mullions" also contains "Wall".
            if (contains(name, "Curtain Wall") || contains(name, "Curtain Panel") || contains(name, "Mullion") || contains(name, "Glazed"))
                kind = SurfaceKind.Glass;
            else if (contains(name, "Basic Wall") || contains(name, "Walls") || contains(name, "Column") || contains(name, "store"))
                kind = SurfaceKind.Wall;
            else if (contains(name, "Generic Model") || contains(name, "kiosk") || contains(name, "food court"))
                kind = SurfaceKind.LowObstacle;
            else if (contains(name, "Floor"))
                kind = SurfaceKind.Floor;
            else
            {
                kind = SurfaceKind.Ignore;
                return false;
            }
            return true;
        }

        // A role prefix is one uppercase letter and an underscore at the start of the cleaned name.
        private static bool tryKindFromPrefix(string name, out SurfaceKind kind)
        {
            kind = SurfaceKind.Ignore;
            string cleaned = cleanName(name);
            if (cleaned.Length < 2 || cleaned[1] != '_') return false;
            switch (cleaned[0])
            {
                case 'W': kind = SurfaceKind.Wall; return true;
                case 'G': kind = SurfaceKind.Glass; return true;
                case 'L': kind = SurfaceKind.LowObstacle; return true;
                case 'F': kind = SurfaceKind.Floor; return true;
                case 'D': kind = SurfaceKind.Ignore; return true;
                default: return false;
            }
        }

        // The name without Revit element ids (from the first " [") and without a trailing Blender (".003") or Unity
        // (" (1)") duplicate suffix.
        private static string cleanName(string name)
        {
            int revitIds = name.IndexOf(" [", System.StringComparison.Ordinal);
            if (revitIds >= 0) name = name.Substring(0, revitIds);
            return duplicateSuffix.Replace(name.Trim(), "");
        }

        private static readonly Regex duplicateSuffix = new Regex(@"(\.\d+| \(\d+\))$");

        private static bool TryClassify(Transform transform, Transform root, out SurfaceKind kind)
        {
            for (Transform current = transform; current != null; current = current == root ? null : current.parent)
                if (TryKindFromName(current.name, out kind)) return true;
            kind = SurfaceKind.Ignore;
            return false;
        }

        private static bool contains(string name, string part) => name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
