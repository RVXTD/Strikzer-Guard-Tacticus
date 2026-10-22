// Written by Claude (Anthropic), October 2026; split into rule and scene fixtures and graybox names added after a
// first Test Runner run, and a flat-volume bake test added after the graybox bake missed walls; to be reviewed
// by Alexander Stangl per the course syllabus; role-prefix name cases added October 21, 2026 (step 9a).

using System.Collections.Generic;
using Game.Presentation.Tooling;
using Game.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Game.Presentation.Tests
{
    /// <summary>Tests: The map bake's classification rules and Revit name tagging (no scene needed).</summary>
    public class MapBakeRuleTests
    {
        [Test]
        public void WallBeatsGlassBeatsLowObstacle()
        {
            Assert.That(MapCellClassifier.Stronger(SurfaceKind.Glass, SurfaceKind.Wall), Is.EqualTo(SurfaceKind.Wall));
            Assert.That(MapCellClassifier.Stronger(SurfaceKind.Glass, SurfaceKind.LowObstacle), Is.EqualTo(SurfaceKind.Glass));
            Assert.That(MapCellClassifier.Stronger(SurfaceKind.Floor, SurfaceKind.LowObstacle), Is.EqualTo(SurfaceKind.LowObstacle));
            Assert.That(MapCellClassifier.Stronger(SurfaceKind.Ignore, SurfaceKind.Floor), Is.Not.EqualTo(SurfaceKind.Wall));
        }

        [Test]
        public void AnObstacleImpliesFloorAndOverridesReplaceTheCell()
        {
            MapCellClassifier.Classify(false, SurfaceKind.Wall, out bool hasFloor, out CellObstacle obstacle);
            Assert.That(hasFloor, Is.True);
            Assert.That(obstacle.BlocksSight, Is.True);
            MapCellClassifier.Classify(false, SurfaceKind.Ignore, out hasFloor, out _);
            Assert.That(hasFloor, Is.False);

            MapCellClassifier.Apply(OverrideKind.NoFloor, out hasFloor, out obstacle);
            Assert.That(hasFloor, Is.False);
            MapCellClassifier.Apply(OverrideKind.OpenFloor, out hasFloor, out obstacle);
            Assert.That(hasFloor && !obstacle.BlocksMovement, Is.True);
            MapCellClassifier.Apply(OverrideKind.LowObstacle, out _, out obstacle);
            Assert.That(obstacle.Cover, Is.EqualTo(CoverLevel.Half));
        }

        [TestCase("Basic Wall Generic - 6\" [348811]", SurfaceKind.Wall)]
        [TestCase("Curtain Wall Mullions [1]", SurfaceKind.Glass)]
        [TestCase("Curtain Panels [2]", SurfaceKind.Glass)]
        [TestCase("Floor Generic - 12\" [3]", SurfaceKind.Floor)]
        [TestCase("Generic Models Kiosk [4]", SurfaceKind.LowObstacle)]
        [TestCase("System Panel Glazed [1414270]", SurfaceKind.Glass)]
        [TestCase("Rectangular Mullion 2.5\" x 5\" rectangular [1414271]", SurfaceKind.Glass)]
        [TestCase("Concrete-Rectangular-Column 12\" x 24\" [1430102]", SurfaceKind.Wall)]
        [TestCase("food court 1 food court 2 [1437035]", SurfaceKind.LowObstacle)]
        [TestCase("food court16 food court [1391297]", SurfaceKind.LowObstacle)]
        [TestCase("kiosk kiosk [1439001]", SurfaceKind.LowObstacle)]
        [TestCase("store store [1439841]", SurfaceKind.Wall)]
        public void RevitNamesMapToSurfaceKinds(string objectName, SurfaceKind expected)
        {
            Assert.That(GrayboxTagger.TryKindFromName(objectName, out SurfaceKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(expected));
        }

        [Test]
        public void UnknownRevitNamesAreNotGuessed()
        {
            Assert.That(GrayboxTagger.TryKindFromName("Model Lines [5]", out _), Is.False);
        }

        [TestCase("L_Bench L_Bench [1509840]_[1509838] Geometry", SurfaceKind.LowObstacle)]
        [TestCase("W_Shelf_Tall_A.003", SurfaceKind.Wall)]
        [TestCase("G_Storefront_A (1)", SurfaceKind.Glass)]
        [TestCase("F_Tile_A", SurfaceKind.Floor)]
        [TestCase("D_Plant_Potted_B", SurfaceKind.Ignore)]
        public void PrefixedNamesMapToSurfaceKinds(string objectName, SurfaceKind expected)
        {
            Assert.That(GrayboxTagger.TryKindFromName(objectName, out SurfaceKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(expected));
        }

        [Test]
        public void APrefixBeatsTheRevitHeuristics()
        {
            Assert.That(GrayboxTagger.TryKindFromName("L_Store_Counter_A", out SurfaceKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(SurfaceKind.LowObstacle));
        }

        [Test]
        public void LowercaseOrLongPrefixesAreNotRoles()
        {
            Assert.That(GrayboxTagger.TryKindFromName("l_bench", out _), Is.False);
            Assert.That(GrayboxTagger.TryKindFromName("WL_Thing", out _), Is.False);
        }
    }

    /// <summary>
    /// Tests: A bake of tagged cubes. The cubes are built under one temporary root in the open scene, far from
    /// anything already there, and destroyed afterwards. (Unity cannot open a second scene while the current
    /// one is untitled and unsaved, so the test does not create its own scene.)
    /// </summary>
    public class MapBakeSceneTests
    {
        private GameObject root;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void CreateRoot()
        {
            root = new GameObject("MapBakeTest (temporary)");
        }

        [TearDown]
        public void DestroyEverything()
        {
            if (root != null) Object.DestroyImmediate(root);
            foreach (Object item in created) if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }

        // A box primitive under the test root.
        private GameObject box(Vector3 centre, Vector3 size, SurfaceKind? kind)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(root.transform, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            if (kind.HasValue) go.AddComponent<MapSurface>().Kind = kind.Value;
            return go;
        }

        [Test]
        public void BakingTaggedCubesGivesTheExpectedGrid()
        {
            // A 4 x 4 grid of 1 m cells with its corner at (9998, 0, -2) and the floor at y = 0.
            const float x0 = 9998f, z0 = -2f;
            Vector3 at(int x, int z, float y) => new Vector3(x0 + x + 0.5f, y, z0 + z + 0.5f);
            box(new Vector3(x0 + 1.5f, -0.5f, 0f), new Vector3(3f, 1f, 4f), SurfaceKind.Floor); // floor under columns 0-2
            box(at(0, 0, 1f), new Vector3(1f, 2f, 1f), SurfaceKind.Wall);
            GameObject glass = box(at(1, 0, 1f), new Vector3(1f, 2f, 1f), SurfaceKind.Glass);
            Object.DestroyImmediate(glass.GetComponent<Collider>()); // a mesh with no collider gets a temporary one
            box(at(2, 0, 0.4f), new Vector3(1f, 0.8f, 1f), SurfaceKind.LowObstacle);
            box(at(3, 1, 1f), new Vector3(1f, 2f, 1f), null); // untagged: ignored
            var overrideObject = new GameObject("Override");
            overrideObject.transform.SetParent(root.transform, false);
            overrideObject.transform.position = at(1, 2, 1f);
            var overrideVolume = overrideObject.AddComponent<MapOverrideVolume>();
            overrideVolume.Size = new Vector3(0.5f, 2f, 0.5f);
            overrideVolume.Kind = OverrideKind.Wall;

            var volumeObject = new GameObject("Volume");
            volumeObject.transform.SetParent(root.transform, false);
            volumeObject.transform.position = new Vector3(x0 + 2f, 1.5f, z0 + 2f);
            var volume = volumeObject.AddComponent<MapBakeVolume>();
            volume.Size = new Vector3(4f, 3f, 4f);
            volume.CellSize = 1f;
            var grid = ScriptableObject.CreateInstance<MapGridAsset>();
            created.Add(grid);

            MapBakeReport report = MapBaker.Bake(volume, grid);

            Assert.That(grid.Width, Is.EqualTo(4));
            Assert.That(grid.Height, Is.EqualTo(4));
            Assert.That(grid.GetObstacle(0, 0).BlocksSight, Is.True, "wall");
            Assert.That(grid.GetObstacle(1, 0).BlocksMovement && !grid.GetObstacle(1, 0).BlocksSight, Is.True, "glass");
            Assert.That(grid.GetObstacle(1, 0).Cover, Is.EqualTo(CoverLevel.None));
            Assert.That(grid.GetObstacle(2, 0).Cover, Is.EqualTo(CoverLevel.Half), "low obstacle");
            Assert.That(grid.HasFloor(3, 0), Is.False, "no floor geometry under column 3");
            Assert.That(grid.GetObstacle(1, 2).BlocksSight, Is.True, "override");
            Assert.That(grid.HasFloor(0, 1) && !grid.GetObstacle(0, 1).BlocksMovement, Is.True, "open floor");
            Assert.That(report.UntaggedHits, Is.GreaterThan(0));
            Assert.That(report.Overridden, Is.EqualTo(1));
            Assert.That(glass.GetComponent<Collider>(), Is.Null, "temporary colliders are removed");
        }

        [Test]
        public void AFlatVolumeStillFindsColliderlessWallsAboveTheFloor()
        {
            // A flat (zero-height) volume on the floor, and a wall with no collider whose base is 2 cm above it.
            const float x0 = 9998f, z0 = 10f;
            box(new Vector3(x0 + 1f, -0.5f, z0 + 0.5f), new Vector3(2f, 1f, 1f), SurfaceKind.Floor);
            GameObject wall = box(new Vector3(x0 + 0.5f, 1.02f, z0 + 0.5f), new Vector3(1f, 2f, 1f), SurfaceKind.Wall);
            Object.DestroyImmediate(wall.GetComponent<Collider>());

            var volumeObject = new GameObject("Volume");
            volumeObject.transform.SetParent(root.transform, false);
            volumeObject.transform.position = new Vector3(x0 + 1f, 0f, z0 + 0.5f);
            var volume = volumeObject.AddComponent<MapBakeVolume>();
            volume.Size = new Vector3(2f, 0f, 1f);
            volume.CellSize = 1f;
            var grid = ScriptableObject.CreateInstance<MapGridAsset>();
            created.Add(grid);

            MapBaker.Bake(volume, grid);

            Assert.That(grid.GetObstacle(0, 0).BlocksSight, Is.True, "wall");
            Assert.That(grid.HasFloor(1, 0) && !grid.GetObstacle(1, 0).BlocksMovement, Is.True, "open floor");
            Assert.That(wall.GetComponent<Collider>(), Is.Null, "temporary colliders are removed");
        }
    }
}
