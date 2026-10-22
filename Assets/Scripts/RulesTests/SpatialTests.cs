// Glass and independent obstacle property tests, windowed-field tests and the cover-reach test by Claude
// (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace Game.Rules.Tests
{
    /// <summary>Tests: Line of sight, cover, distance fields and path building.</summary>
    public class SpatialTests
    {
        private static SpatialSystem build(EncounterSetup setup, UnitRegistry registry) =>
            new SpatialSystem(registry, setup.Width, setup.Height, setup.CellSize, setup.Origin,
                              setup.Floor, setup.Obstacles, new EikonalDistanceFieldSolver());

        [Test]
        public void FullCoverBlocksSightButHalfCoverDoesNot()
        {
            var setup = TestWorld.Map(
                ".....",
                "..#..",
                ".....",
                "..h..",
                ".....");
            var spatial = build(setup, new UnitRegistry(4));
            Assert.That(spatial.HasLineOfSight(TestWorld.At(0, 3), TestWorld.At(4, 3)), Is.False);
            Assert.That(spatial.HasLineOfSight(TestWorld.At(0, 1), TestWorld.At(4, 1)), Is.True);
            Assert.That(spatial.HasLineOfSight(TestWorld.At(0, 2), TestWorld.At(4, 2)), Is.True);
        }

        [Test]
        public void SightCannotSqueezeBetweenDiagonallyTouchingWalls()
        {
            var squeezed = build(TestWorld.Map(
                "#.",
                ".#"), new UnitRegistry(4));
            var open = build(TestWorld.OpenMap(2, 2), new UnitRegistry(4));
            Assert.That(squeezed.HasLineOfSight(TestWorld.At(0, 0), TestWorld.At(1, 1)), Is.False);
            Assert.That(open.HasLineOfSight(TestWorld.At(0, 0), TestWorld.At(1, 1)), Is.True);
        }

        [Test]
        public void DistanceFieldDetoursAroundWalls()
        {
            var setup = TestWorld.Map(
                "......",
                "..#...",
                "..#...",
                "..#...",
                "......");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(0, 2), Vector2.UnitX, default);
            spatial.RebuildField(unit);

            Assert.That(spatial.TryGetCell(TestWorld.At(4, 2), out int target), Is.True);
            float distance = spatial.Field[target];
            Assert.That(distance, Is.GreaterThan(4f));
            Assert.That(float.IsPositiveInfinity(distance), Is.False);
            Assert.That(spatial.Field[spatial.TryGetCell(TestWorld.At(0, 2), out int source) ? source : -1], Is.EqualTo(0f));
        }

        [Test]
        public void PathRunsFromUnitToDestinationThroughOpenCellsOnly()
        {
            var setup = TestWorld.Map(
                "......",
                "..#...",
                "..#...",
                "..#...",
                "......");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(0, 2), Vector2.UnitX, default);
            spatial.RebuildField(unit);

            var path = new List<Vector2>();
            Vector2 destination = TestWorld.At(4, 2);
            Assert.That(spatial.TryBuildPath(destination, path), Is.True);
            Assert.That(path[0], Is.EqualTo(TestWorld.At(0, 2)));
            Assert.That(path[path.Count - 1], Is.EqualTo(destination));

            // Sample every segment densely: no sample may fall in a blocked cell.
            for (int k = 0; k + 1 < path.Count; k++)
                for (int s = 0; s <= 20; s++)
                    Assert.That(spatial.IsOpenFloor(Vector2.Lerp(path[k], path[k + 1], s / 20f)), Is.True);

            // The shortest walk runs over the wall's top corners, (2, 4) and (3, 4): about 5.24 m. The path can be no
            // shorter, and no longer than the field's estimate that prices the move.
            spatial.TryGetCell(destination, out int cell);
            float shortest = Vector2.Distance(TestWorld.At(0, 2), new Vector2(2f, 4f)) + 1f + Vector2.Distance(new Vector2(3f, 4f), destination);
            Assert.That(SpatialSystem.PathLength(path), Is.GreaterThanOrEqualTo(shortest - 1e-3f));
            Assert.That(SpatialSystem.PathLength(path), Is.LessThanOrEqualTo(spatial.Field[cell] + 1e-3f));
            Assert.That(SpatialSystem.PathLength(path), Is.LessThan(shortest + 0.05f), "the path hugs the wall's corners");
        }

        [Test]
        public void EnclosedCellsAndPitsAreUnreachable()
        {
            var setup = TestWorld.Map(
                ".....",
                ".###.",
                ".#.#.",
                ".###.",
                "..___");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(0, 4), Vector2.UnitX, default);
            spatial.RebuildField(unit);

            spatial.TryGetCell(TestWorld.At(2, 2), out int enclosed);
            spatial.TryGetCell(TestWorld.At(3, 0), out int pit);
            Assert.That(float.IsPositiveInfinity(spatial.Field[enclosed]), Is.True);
            Assert.That(float.IsPositiveInfinity(spatial.Field[pit]), Is.True);
            Assert.That(spatial.TryBuildPath(TestWorld.At(2, 2), new List<Vector2>()), Is.False);
        }

        [Test]
        public void OtherUnitsBlockTheirCellUntilTheyStopOccupyingSpace()
        {
            var setup = TestWorld.Map(
                "###",
                "...",
                "###");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId mover = registry.Create(Team.Player), blocker = registry.Create(Team.Enemy);
            spatial.AddUnit(mover, TestWorld.At(0, 1), Vector2.UnitX, default);
            spatial.AddUnit(blocker, TestWorld.At(1, 1), Vector2.UnitX, default);
            spatial.TryGetCell(TestWorld.At(2, 1), out int beyond);

            spatial.RebuildField(mover);
            Assert.That(float.IsPositiveInfinity(spatial.Field[beyond]), Is.True);

            spatial.SetOccupiesSpace(blocker, false);
            spatial.RebuildField(mover);
            Assert.That(spatial.Field[beyond], Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void DiagonalStepsCannotCutWallCorners()
        {
            var setup = TestWorld.Map(
                ".#",
                "..");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(0, 1), Vector2.UnitX, default);
            spatial.RebuildField(unit);
            spatial.TryGetCell(TestWorld.At(1, 0), out int corner);
            Assert.That(spatial.Field[corner], Is.EqualTo(2f).Within(1e-4f)); // around, not sqrt(2) through the corner
        }

        [Test]
        public void FieldIsExactWhereverTheUnitCanWalkInAStraightLine()
        {
            var registry = new UnitRegistry(4);
            var spatial = build(TestWorld.OpenMap(9, 7), registry);
            UnitId unit = registry.Create(Team.Player);
            var position = new Vector2(1.9f, 1.2f); // off its cell's centre
            spatial.AddUnit(unit, position, Vector2.UnitX, default);
            spatial.RebuildField(unit);
            for (int cell = 0; cell < spatial.CellCount; cell++)
                Assert.That(spatial.Field[cell], Is.EqualTo(Vector2.Distance(position, spatial.CellCenter(cell))).Within(1e-4f));
        }

        [Test]
        public void FieldIsNotSeededThroughWalls()
        {
            var setup = TestWorld.Map(
                "......",
                "..#...",
                "..#...",
                "..#...",
                "......");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(0, 2), Vector2.UnitX, default);
            spatial.RebuildField(unit);
            spatial.TryGetCell(TestWorld.At(3, 2), out int behind);
            // 3 m in a straight line; the shortest walk around the wall's corners is about 4.70 m.
            Assert.That(spatial.Field[behind], Is.GreaterThan(4.5f));
        }

        [Test]
        public void PathFromAnOffCentreUnitStartsAtItsPositionAndAvoidsWalls()
        {
            var setup = TestWorld.Map(
                "......",
                "..#...",
                "..#...",
                "..#...",
                "......");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId unit = registry.Create(Team.Player);
            var position = new Vector2(0.8f, 2.3f);
            spatial.AddUnit(unit, position, Vector2.UnitX, default);
            spatial.RebuildField(unit);

            var path = new List<Vector2>();
            Vector2 destination = new Vector2(4.2f, 2.9f);
            Assert.That(spatial.TryBuildPath(destination, path), Is.True);
            Assert.That(path[0], Is.EqualTo(position));
            Assert.That(path[path.Count - 1], Is.EqualTo(destination));
            for (int k = 0; k + 1 < path.Count; k++)
                for (int s = 0; s <= 20; s++)
                    Assert.That(spatial.IsOpenFloor(Vector2.Lerp(path[k], path[k + 1], s / 20f)), Is.True);
        }

        [Test]
        public void WithTheDijkstraSolverPathsExceedTheFieldByAtMostHalfACellDiagonal()
        {
            var random = new Random(286);
            var path = new List<Vector2>();
            for (int trial = 0; trial < 10; trial++)
            {
                var rows = new string[12];
                for (int r = 0; r < rows.Length; r++)
                {
                    var row = new char[14];
                    for (int c = 0; c < row.Length; c++) row[c] = random.NextDouble() < 0.25 ? '#' : '.';
                    rows[r] = new string(row);
                }
                EncounterSetup setup = TestWorld.Map(rows);
                var registry = new UnitRegistry(4);
                var spatial = new SpatialSystem(registry, setup.Width, setup.Height, setup.CellSize, setup.Origin,
                                                setup.Floor, setup.Obstacles, new DijkstraDistanceFieldSolver());
                UnitId unit = registry.Create(Team.Player);
                Vector2 position;
                do position = new Vector2((float)random.NextDouble() * setup.Width, (float)random.NextDouble() * setup.Height);
                while (!spatial.IsOpenFloor(position));
                spatial.AddUnit(unit, position, Vector2.UnitX, default);
                spatial.RebuildField(unit);

                for (int cell = 0; cell < spatial.CellCount; cell++)
                {
                    if (float.IsPositiveInfinity(spatial.Field[cell])) continue;
                    Vector2 destination = spatial.CellCenter(cell) +
                        new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.98f;
                    Assert.That(spatial.TryBuildPath(destination, path), Is.True);
                    Assert.That(SpatialSystem.PathLength(path), Is.LessThanOrEqualTo(spatial.Field[cell] + 0.5f * MathF.Sqrt(2f) + 1e-3f));
                }
            }
        }

        [Test]
        public void CoverCountsOnlyWhenItFacesTheAttacker()
        {
            var setup = TestWorld.Map(
                "......",
                ".#....",
                "......");
            var spatial = build(setup, new UnitRegistry(4));
            Vector2 unit = TestWorld.At(2, 1);
            Assert.That(spatial.CoverAgainst(unit, TestWorld.At(0, 1)), Is.EqualTo(CoverLevel.Full));
            Assert.That(spatial.CoverAgainst(unit, TestWorld.At(5, 1)), Is.EqualTo(CoverLevel.None));
            Assert.That(spatial.CoverAgainst(unit, TestWorld.At(2, 2)), Is.EqualTo(CoverLevel.None));
        }

        [Test]
        public void CoverReachesTheNearestCellABodyCanStandIn()
        {
            // 0.5 m cells: a 0.35 m body cannot stand in the cell beside the wall, only in the one after it.
            var setup = TestWorld.Map(
                "......",
                "..#...",
                "......");
            setup.CellSize = 0.5f;
            var spatial = build(setup, new UnitRegistry(4));
            var unit = new Vector2(2.25f, 0.75f);
            var attacker = new Vector2(0.25f, 0.75f);
            Assert.That(spatial.CoverAgainst(unit, attacker, 0.35f), Is.EqualTo(CoverLevel.Full));
            Assert.That(spatial.CoverAgainst(unit, attacker), Is.EqualTo(CoverLevel.None), "a point body must stand beside the wall");
        }

        [Test]
        public void GlassBlocksMovementButNotSight()
        {
            var setup = TestWorld.Map(
                "..g..",
                "..g..",
                "..g..");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(0, 1), Vector2.UnitX, default);
            spatial.RebuildField(unit);

            Assert.That(spatial.HasLineOfSight(TestWorld.At(0, 1), TestWorld.At(4, 1)), Is.True);
            Assert.That(spatial.IsOpenFloor(TestWorld.At(2, 1)), Is.False);
            Assert.That(spatial.TryGetCell(TestWorld.At(4, 1), out int beyond), Is.True);
            Assert.That(float.IsPositiveInfinity(spatial.Field[beyond]), Is.True);
        }

        [Test]
        public void GlassGivesNoCover()
        {
            var setup = TestWorld.Map(
                "......",
                ".g....",
                "......");
            var spatial = build(setup, new UnitRegistry(4));
            Assert.That(spatial.CoverAgainst(TestWorld.At(2, 1), TestWorld.At(0, 1)), Is.EqualTo(CoverLevel.None));
        }

        [Test]
        public void ObstaclePropertiesAreIndependent()
        {
            // A hanging banner: blocks sight, but units can walk underneath it.
            var setup = TestWorld.OpenMap(5, 3);
            setup.Obstacles[1 * 5 + 2] = new CellObstacle { BlocksSight = true };
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(0, 1), Vector2.UnitX, default);
            spatial.RebuildField(unit);

            Assert.That(spatial.HasLineOfSight(TestWorld.At(0, 1), TestWorld.At(4, 1)), Is.False);
            Assert.That(spatial.IsOpenFloor(TestWorld.At(2, 1)), Is.True);
            var path = new List<Vector2>();
            Assert.That(spatial.TryBuildPath(TestWorld.At(4, 1), path), Is.True);
            Assert.That(SpatialSystem.PathLength(path), Is.EqualTo(4f).Within(1e-4f));
        }
        [Test]
        public void WindowedFieldMatchesTheFullFieldWithinItsRange()
        {
            var random = new Random(288);
            for (int trial = 0; trial < 10; trial++)
            {
                var rows = new string[16];
                for (int r = 0; r < rows.Length; r++)
                {
                    var row = new char[20];
                    for (int c = 0; c < row.Length; c++) row[c] = random.NextDouble() < 0.2 ? '#' : '.';
                    rows[r] = new string(row);
                }
                EncounterSetup setup = TestWorld.Map(rows);
                var registry = new UnitRegistry(4);
                var spatial = build(setup, registry);
                UnitId unit = registry.Create(Team.Player);
                Vector2 position;
                do position = new Vector2((float)random.NextDouble() * setup.Width, (float)random.NextDouble() * setup.Height);
                while (!spatial.IsOpenFloor(position));
                spatial.AddUnit(unit, position, Vector2.UnitX, default);

                spatial.RebuildField(unit);
                float[] full = spatial.Field.ToArray();
                Assert.That(spatial.FieldIsTruncated, Is.False);
                const float range = 6.5f;
                spatial.RebuildField(unit, range);
                Assert.That(spatial.FieldRange, Is.EqualTo(range));
                bool anyBeyond = false;
                for (int cell = 0; cell < full.Length; cell++)
                {
                    if (full[cell] <= range)
                        Assert.That(spatial.Field[cell], Is.EqualTo(full[cell]).Within(1e-5f), $"trial {trial}, cell {cell}");
                    else
                        Assert.That(float.IsPositiveInfinity(spatial.Field[cell]), Is.True, $"trial {trial}, cell {cell}");
                    anyBeyond |= full[cell] > range && !float.IsPositiveInfinity(full[cell]);
                }
                Assert.That(spatial.FieldIsTruncated, Is.EqualTo(anyBeyond), $"trial {trial}");
            }
        }

        [Test]
        public void WindowedRebuildOnAGrayboxSizedMap()
        {
            // The largest graybox piece: 150 x 50 m at 0.5 m cells, with scattered walls; a 6 m movement budget.
            const int width = 300, height = 100;
            var random = new Random(289);
            var setup = TestWorld.OpenMap(width, height);
            setup.CellSize = 0.5f;
            for (int cell = 0; cell < setup.Obstacles.Length; cell++)
                if (random.NextDouble() < 0.08) setup.Obstacles[cell] = CellObstacle.Wall;
            var position = new Vector2(75.25f, 25.25f); // the centre of cell (150, 50), kept clear
            setup.Obstacles[50 * width + 150] = CellObstacle.None;
            var registry = new UnitRegistry(4);
            var spatial = new SpatialSystem(registry, width, height, setup.CellSize, setup.Origin, setup.Floor, setup.Obstacles,
                                            new EikonalDistanceFieldSolver());
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, position, Vector2.UnitX, default);
            const float range = 2f * 6f + 0.5f;
            spatial.RebuildField(unit, range); // warm up, so neither timing includes compilation

            var clock = System.Diagnostics.Stopwatch.StartNew();
            spatial.RebuildField(unit);
            double fullMs = clock.Elapsed.TotalMilliseconds;
            float[] full = spatial.Field.ToArray();
            clock.Restart();
            spatial.RebuildField(unit, range);
            double windowedMs = clock.Elapsed.TotalMilliseconds;
            TestContext.WriteLine($"300 x 100 cells at 0.5 m: whole-map rebuild {fullMs:F1} ms, 12.5 m window {windowedMs:F2} ms");

            for (int cell = 0; cell < full.Length; cell++)
            {
                if (full[cell] <= range) Assert.That(spatial.Field[cell], Is.EqualTo(full[cell]).Within(1e-5f));
                else Assert.That(float.IsPositiveInfinity(spatial.Field[cell]), Is.True);
            }
            Assert.That(spatial.FieldIsTruncated, Is.True);
        }

        [Test]
        public void ConnectivityFollowsWallsGlassAndPits()
        {
            var registry = new UnitRegistry(4);
            var spatial = build(TestWorld.Map(
                ".....",
                ".###.",
                ".#.#.",
                ".###.",
                "..g__"), registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(0, 4), Vector2.UnitX, default);
            spatial.RebuildField(unit, 1f); // connectivity ignores the field's range
            int cellAt(int x, int y) => spatial.TryGetCell(TestWorld.At(x, y), out int cell) ? cell : -1;
            Assert.That(spatial.IsConnectedToFieldOwner(cellAt(4, 1)), Is.True, "around the room");
            Assert.That(spatial.IsConnectedToFieldOwner(cellAt(2, 2)), Is.False, "sealed inside the room");
            Assert.That(spatial.IsConnectedToFieldOwner(cellAt(3, 0)), Is.False, "a pit is not floor");
            Assert.That(spatial.IsConnectedToFieldOwner(cellAt(2, 0)), Is.False, "glass is not open floor");
        }

        [Test]
        public void ConnectivityCountsOtherUnitsBodies()
        {
            var setup = TestWorld.Map(
                "#####",
                "#...#",
                "##.##",
                ".....");
            var registry = new UnitRegistry(4);
            var spatial = build(setup, registry);
            UnitId mover = registry.Create(Team.Player), blocker = registry.Create(Team.Enemy);
            spatial.AddUnit(mover, TestWorld.At(0, 0), Vector2.UnitX, default);
            spatial.AddUnit(blocker, TestWorld.At(2, 1), Vector2.UnitX, default); // standing in the room's only doorway
            spatial.TryGetCell(TestWorld.At(2, 2), out int room);

            spatial.RebuildField(mover);
            Assert.That(spatial.IsConnectedToFieldOwner(room), Is.False);
            spatial.SetOccupiesSpace(blocker, false);
            spatial.RebuildField(mover);
            Assert.That(spatial.IsConnectedToFieldOwner(room), Is.True);
        }

        [Test]
        public void TheUnitsOwnCellIsAlwaysInItsField()
        {
            var registry = new UnitRegistry(4);
            var spatial = build(TestWorld.OpenMap(5, 5), registry);
            UnitId unit = registry.Create(Team.Player);
            var position = new Vector2(1.9f, 1.2f); // about 0.5 m from its cell's centre
            spatial.AddUnit(unit, position, Vector2.UnitX, default);
            spatial.RebuildField(unit, 0f);
            spatial.TryGetCell(position, out int own);
            Assert.That(spatial.Field[own], Is.EqualTo(Vector2.Distance(position, spatial.CellCenter(own))).Within(1e-5f));
            Assert.That(spatial.FieldRange, Is.EqualTo(spatial.Field[own]).Within(1e-5f), "the range stretches to cover it");
        }

        [Test]
        public void AHugeFiniteRangeStillCoversTheWholeMap()
        {
            var registry = new UnitRegistry(4);
            var spatial = build(TestWorld.OpenMap(6, 5), registry);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, TestWorld.At(2, 2), Vector2.UnitX, default);
            spatial.RebuildField(unit, 1e9f); // must not overflow the window bounds
            for (int cell = 0; cell < spatial.CellCount; cell++)
                Assert.That(spatial.Field[cell], Is.EqualTo(Vector2.Distance(TestWorld.At(2, 2), spatial.CellCenter(cell))).Within(1e-4f));
            Assert.That(spatial.FieldIsTruncated, Is.False);
        }
    }

    /// <summary>Tests: Zone-of-control geometry: containment and first entry of a segment.</summary>
    public class ZoneShapeTests
    {
        private static ZoneShape makeCircle(float radius) =>
            new ZoneShape(Vector2.Zero, Vector2.UnitX, new ZoneSetup { Radius = radius, HalfAngleDegrees = 180f });

        private static ZoneShape makeCone(float radius, float halfAngleDegrees) =>
            new ZoneShape(Vector2.Zero, Vector2.UnitX, new ZoneSetup { Radius = radius, HalfAngleDegrees = halfAngleDegrees });

        [Test]
        public void CircleEntryIsTheFirstBoundaryCrossing()
        {
            Assert.That(makeCircle(1f).TryFindEntry(new Vector2(-3f, 0f), new Vector2(3f, 0f), out float t), Is.True);
            Assert.That(t, Is.EqualTo(1f / 3f).Within(1e-5f));
        }

        [Test]
        public void SegmentStartingInsideHasNoEntry()
        {
            Assert.That(makeCircle(1f).TryFindEntry(Vector2.Zero, new Vector2(3f, 0f), out _), Is.False);
        }

        [Test]
        public void SegmentPassingOutsideHasNoEntry()
        {
            Assert.That(makeCircle(1f).TryFindEntry(new Vector2(-3f, 2f), new Vector2(3f, 2f), out _), Is.False);
        }

        [Test]
        public void ConeIsEnteredThroughItsStraightEdge()
        {
            // Crossing x = 1 from y = -3 to y = 3 enters the 45-degree cone where its lower edge passes y = -1.
            Assert.That(makeCone(5f, 45f).TryFindEntry(new Vector2(1f, -3f), new Vector2(1f, 3f), out float t), Is.True);
            Assert.That(t, Is.EqualTo(1f / 3f).Within(1e-4f));
        }

        [Test]
        public void ConeIsEnteredThroughItsArc()
        {
            Assert.That(makeCone(5f, 30f).TryFindEntry(new Vector2(9f, 0f), Vector2.Zero, out float t), Is.True);
            Assert.That(t, Is.EqualTo(4f / 9f).Within(1e-4f));
        }

        [Test]
        public void PointsBehindTheConeAreOutside()
        {
            ZoneShape cone = makeCone(5f, 45f);
            Assert.That(cone.Contains(new Vector2(2f, 0f)), Is.True);
            Assert.That(cone.Contains(new Vector2(-2f, 0f)), Is.False);
            Assert.That(cone.TryFindEntry(new Vector2(-1f, -3f), new Vector2(-1f, 3f), out _), Is.False);
        }
    }
}
