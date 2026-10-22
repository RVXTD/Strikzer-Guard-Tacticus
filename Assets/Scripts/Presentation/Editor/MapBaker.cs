// Written by Claude (Anthropic), October 2026; the inside-a-thick-mesh test added after a Test Runner failure, and
// the temporary-collider area fixed to the sampled heights after the graybox bake missed walls;
// to be reviewed by Alexander Stangl per the course syllabus.

using System.Collections.Generic;
using System.IO;
using Game.Rules;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Presentation.Tooling
{
    /// <summary>What a bake found, for the log and for tests.</summary>
    public struct MapBakeReport
    {
        /// <summary>Cells in the grid.</summary>
        public int Cells;
        /// <summary>Cells with open floor.</summary>
        public int OpenFloor;
        /// <summary>Cells with no floor.</summary>
        public int NoFloor;
        /// <summary>Wall cells.</summary>
        public int Walls;
        /// <summary>Glass cells.</summary>
        public int Glass;
        /// <summary>Low-obstacle cells.</summary>
        public int LowObstacles;
        /// <summary>Cells changed by override volumes.</summary>
        public int Overridden;
        /// <summary>Collider hits on geometry with no <see cref="MapSurface"/> above it, which the bake ignored.</summary>
        public int UntaggedHits;

        /// <inheritdoc/>
        public override string ToString() =>
            $"{Cells} cells: {OpenFloor} open floor, {Walls} wall, {Glass} glass, {LowObstacles} low obstacle, {NoFloor} no floor; " +
            $"{Overridden} overridden; {UntaggedHits} untagged collider hits ignored";
    }

    /// <summary>
    /// Bakes tagged level geometry into a <see cref="MapGridAsset"/>. Each cell is sampled with editor-time physics
    /// overlap tests: a thin slab at floor height finds floor, and a box over the obstacle band finds walls, glass
    /// and low obstacles (Wall beats Glass beats LowObstacle). Tagged meshes without colliders get temporary mesh
    /// colliders for the bake. Mesh colliders are surfaces, so a cell lying wholly inside a thick mesh touches none
    /// of its faces; for those cells a ray cast straight up with back faces enabled finds the mesh, because the
    /// nearest face it hits from inside faces upwards. Override volumes are applied last. Physics is only used
    /// here, offline; the rules layer reads the baked asset and never the scene.
    /// </summary>
    public static class MapBaker
    {
        private const string mapsFolder = "Assets/Data/Maps";

        /// <summary>Bakes the selected <see cref="MapBakeVolume"/>, creating its asset under Assets/Data/Maps if it has none.</summary>
        [MenuItem("Tools/Map/Bake Selected Volume")]
        public static void BakeSelected()
        {
            MapBakeVolume volume = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<MapBakeVolume>() : null;
            if (volume == null)
            {
                EditorUtility.DisplayDialog("Bake map", "Select a GameObject with a MapBakeVolume first.", "OK");
                return;
            }
            if (volume.Target == null)
            {
                Directory.CreateDirectory(mapsFolder);
                var asset = ScriptableObject.CreateInstance<MapGridAsset>();
                string sceneName = string.IsNullOrEmpty(volume.gameObject.scene.name) ? "Untitled" : volume.gameObject.scene.name;
                AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{mapsFolder}/{sceneName}_{volume.name}.asset"));
                Undo.RecordObject(volume, "Assign map grid");
                volume.Target = asset;
                EditorUtility.SetDirty(volume);
            }
            Undo.RecordObject(volume.Target, "Bake map");
            MapBakeReport report = Bake(volume, volume.Target);
            EditorUtility.SetDirty(volume.Target);
            AssetDatabase.SaveAssets();
            Debug.Log($"Baked '{volume.name}' into {AssetDatabase.GetAssetPath(volume.Target)}: {report}", volume.Target);
            SceneView.RepaintAll();
        }

        /// <summary>Validates the menu item: a bake volume must be selected.</summary>
        [MenuItem("Tools/Map/Bake Selected Volume", true)]
        public static bool CanBakeSelected() =>
            Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<MapBakeVolume>() != null;

        /// <summary>
        /// Samples the scene inside <paramref name="volume"/> into <paramref name="target"/> (resized to fit) and
        /// returns what it found. Does not save the asset.
        /// </summary>
        public static MapBakeReport Bake(MapBakeVolume volume, MapGridAsset target)
        {
            Scene scene = volume.gameObject.scene;
            // Temporary colliders cover the heights actually sampled (the floor slab and the obstacle band), not the
            // volume's box: a flat box would skip every wall whose base sits a hair above the floor.
            float bottom = volume.FloorHeight - volume.FloorProbeHalfThickness;
            float top = volume.FloorHeight + Mathf.Max(volume.ObstacleBandTop, volume.FloorProbeHalfThickness);
            var sampled = new Bounds(new Vector3(volume.transform.position.x, 0.5f * (bottom + top), volume.transform.position.z),
                                     new Vector3(volume.Size.x, top - bottom, volume.Size.z));
            List<MeshCollider> temporary = addTemporaryColliders(scene, sampled);
            bool hitBackfaces = Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces = true;
                Physics.SyncTransforms();
                return sample(volume, target, scene);
            }
            finally
            {
                Physics.queriesHitBackfaces = hitBackfaces;
                foreach (MeshCollider collider in temporary)
                    if (collider != null) Object.DestroyImmediate(collider);
                Physics.SyncTransforms();
            }
        }

        private static MapBakeReport sample(MapBakeVolume volume, MapGridAsset target, Scene scene)
        {
            int width = volume.GridWidth, height = volume.GridHeight;
            float cell = volume.CellSize, floorY = volume.FloorHeight;
            target.Resize(width, height, cell, volume.GridOrigin, floorY);

            var report = new MapBakeReport { Cells = width * height };
            var hits = new Collider[64];
            var rayHits = new RaycastHit[64];
            float halfFootprint = 0.5f * cell * 0.95f;
            var floorHalfExtents = new Vector3(halfFootprint, volume.FloorProbeHalfThickness, halfFootprint);
            float bandBottom = floorY + volume.ObstacleBandBottom, bandTop = floorY + volume.ObstacleBandTop;
            var bandHalfExtents = new Vector3(halfFootprint, 0.5f * (bandTop - bandBottom), halfFootprint);
            PhysicsScene physics = scene.GetPhysicsScene();
            MapOverrideVolume[] overrides = findInScene<MapOverrideVolume>(scene);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 centre = target.CellCenter(x, y);
                    bool floorFound = false;
                    int count = physics.OverlapBox(centre, floorHalfExtents, hits, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        MapSurface surface = hits[i].GetComponentInParent<MapSurface>();
                        if (surface == null) report.UntaggedHits++;
                        else if (surface.Kind == SurfaceKind.Floor) floorFound = true;
                    }

                    SurfaceKind strongest = SurfaceKind.Ignore;
                    var bandCentre = new Vector3(centre.x, 0.5f * (bandBottom + bandTop), centre.z);
                    count = physics.OverlapBox(bandCentre, bandHalfExtents, hits, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        MapSurface surface = hits[i].GetComponentInParent<MapSurface>();
                        if (surface == null) report.UntaggedHits++;
                        else strongest = MapCellClassifier.Stronger(strongest, surface.Kind);
                    }
                    if (!MapCellClassifier.ObstacleOf(strongest).BlocksMovement)
                        strongest = MapCellClassifier.Stronger(strongest, enclosingKind(physics, bandCentre, rayHits));

                    MapCellClassifier.Classify(floorFound, strongest, out bool hasFloor, out CellObstacle obstacle);
                    for (int o = 0; o < overrides.Length; o++)
                    {
                        if (!overrides[o].Covers(centre)) continue;
                        MapCellClassifier.Apply(overrides[o].Kind, out hasFloor, out obstacle);
                        report.Overridden++;
                    }
                    target.SetCell(x, y, hasFloor, obstacle);
                    tally(ref report, hasFloor, obstacle);
                }
            }
            return report;
        }

        // The strongest tagged obstacle whose closed mesh encloses the point: a ray cast straight up (back faces
        // enabled) whose nearest hit on that collider is a back face, facing up with the ray, started inside it.
        private static SurfaceKind enclosingKind(PhysicsScene physics, Vector3 point, RaycastHit[] rayHits)
        {
            int count = physics.Raycast(point, Vector3.up, rayHits, 1000f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            SurfaceKind strongest = SurfaceKind.Ignore;
            for (int i = 0; i < count; i++)
            {
                if (Vector3.Dot(rayHits[i].normal, Vector3.up) <= 0f) continue; // a front face: the point is outside
                bool nearestOnItsCollider = true;
                for (int j = 0; j < count && nearestOnItsCollider; j++)
                    if (j != i && rayHits[j].collider == rayHits[i].collider && rayHits[j].distance < rayHits[i].distance) nearestOnItsCollider = false;
                if (!nearestOnItsCollider) continue;
                MapSurface surface = rayHits[i].collider.GetComponentInParent<MapSurface>();
                if (surface != null) strongest = MapCellClassifier.Stronger(strongest, surface.Kind);
            }
            return strongest;
        }

        private static void tally(ref MapBakeReport report, bool hasFloor, CellObstacle obstacle)
        {
            if (!hasFloor) report.NoFloor++;
            else if (obstacle.BlocksSight) report.Walls++;
            else if (obstacle.BlocksMovement && obstacle.Cover == CoverLevel.None) report.Glass++;
            else if (obstacle.BlocksMovement) report.LowObstacles++;
            else report.OpenFloor++;
        }

        // Adds a mesh collider to every tagged mesh inside the baked area that has no collider of its own, and returns
        // them for removal.
        private static List<MeshCollider> addTemporaryColliders(Scene scene, Bounds area)
        {
            var added = new List<MeshCollider>();
            foreach (MapSurface surface in findInScene<MapSurface>(scene))
            {
                if (surface.Kind == SurfaceKind.Ignore) continue;
                foreach (MeshFilter filter in surface.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                    Renderer renderer = filter.GetComponent<Renderer>();
                    if (renderer != null && !renderer.bounds.Intersects(area)) continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collider.hideFlags = HideFlags.DontSave;
                    added.Add(collider);
                }
            }
            return added;
        }

        // Components of one type in a scene, in hierarchy order (so later override volumes win consistently).
        private static T[] findInScene<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found.ToArray();
        }
    }
}
