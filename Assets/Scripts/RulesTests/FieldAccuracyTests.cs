// Written by Claude (Anthropic), October 2026: accuracy of the distance field against exact geodesic
// distances around a wall block; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Numerics;
using NUnit.Framework;

namespace Game.Rules.Tests
{
    /// <summary>
    /// Tests: How closely the distance field matches exact distances: the shortest walk around a wall (cells
    /// in straight-line reach are seeded exactly, so only cells hidden behind the wall are measured), and a
    /// solve from a single point source on an open grid.
    /// </summary>
    public class FieldAccuracyTests
    {
        private const int mapWidth = 40, mapHeight = 30;
        // The wall block covers cells x in [18, 21] and y in [8, 21], i.e. world [18, 22] x [8, 22].
        private const int wallMinX = 18, wallMaxX = 21, wallMinY = 8, wallMaxY = 21;

        private static readonly Vector2[] unitPositions =
        {
            new Vector2(10.5f, 15.5f), // facing the middle of the wall
            new Vector2(14.3f, 9.7f),  // close to its lower-left corner
            new Vector2(5.5f, 3.5f),   // far from it, at an angle
            new Vector2(19.9f, 4.2f),  // just below it
        };

        /// <summary>Error statistics of a field against exact distances.</summary>
        public struct FieldError
        {
            /// <summary>Number of hidden cells measured.</summary>
            public int Count;
            /// <summary>Mean of |field - exact|, in metres.</summary>
            public double MeanAbsolute;
            /// <summary>Largest |field - exact|, in metres.</summary>
            public double MaxAbsolute;
            /// <summary>Mean of (field - exact): positive when the field overestimates.</summary>
            public double MeanSigned;
        }

        /// <summary>Measures the field built from every test position, over all cells hidden behind the wall.</summary>
        public static FieldError Measure(IDistanceFieldSolver solver)
        {
            EncounterSetup setup = TestWorld.OpenMap(mapWidth, mapHeight);
            for (int y = wallMinY; y <= wallMaxY; y++)
                for (int x = wallMinX; x <= wallMaxX; x++)
                    setup.Obstacles[y * mapWidth + x] = CellObstacle.Wall;
            var registry = new UnitRegistry(4);
            var spatial = new SpatialSystem(registry, setup.Width, setup.Height, setup.CellSize, setup.Origin,
                                            setup.Floor, setup.Obstacles, solver);
            UnitId unit = registry.Create(Team.Player);
            spatial.AddUnit(unit, unitPositions[0], Vector2.UnitX, default);

            var error = new FieldError();
            double sumAbsolute = 0, sumSigned = 0;
            foreach (Vector2 position in unitPositions)
            {
                spatial.SetPosition(unit, position);
                spatial.RebuildField(unit);
                for (int cell = 0; cell < spatial.CellCount; cell++)
                {
                    if (!spatial.IsOpenFloor(cell)) continue;
                    Vector2 target = spatial.CellCenter(cell);
                    double exact = geodesic(position, target);
                    if (exact <= Vector2.Distance(position, target) + 1e-4) continue; // in straight-line reach
                    Assert.That(float.IsPositiveInfinity(spatial.Field[cell]), Is.False, "every open cell is reachable");
                    double difference = spatial.Field[cell] - exact;
                    sumAbsolute += Math.Abs(difference);
                    sumSigned += difference;
                    error.MaxAbsolute = Math.Max(error.MaxAbsolute, Math.Abs(difference));
                    error.Count++;
                }
            }
            error.MeanAbsolute = sumAbsolute / error.Count;
            error.MeanSigned = sumSigned / error.Count;
            return error;
        }

        /// <summary>
        /// Measures a solve from a single seed in the middle of an open 61 x 61 grid against straight-line
        /// distances: the solver's own error, without the exact seeding <see cref="SpatialSystem"/> adds.
        /// </summary>
        public static FieldError MeasurePointSource(IDistanceFieldSolver solver)
        {
            const int size = 61, centre = size / 2;
            var passable = new bool[size * size];
            for (int i = 0; i < passable.Length; i++) passable[i] = true;
            var distances = new float[size * size];
            solver.Solve(size, size, 1f, passable, new[] { new DistanceSeed(centre * size + centre, 0f) }, float.PositiveInfinity, distances);

            var error = new FieldError();
            double sumAbsolute = 0, sumSigned = 0;
            for (int cell = 0; cell < distances.Length; cell++)
            {
                if (cell == centre * size + centre) continue;
                double exact = Math.Sqrt(Math.Pow(cell % size - centre, 2) + Math.Pow(cell / size - centre, 2));
                double difference = distances[cell] - exact;
                sumAbsolute += Math.Abs(difference);
                sumSigned += difference;
                error.MaxAbsolute = Math.Max(error.MaxAbsolute, Math.Abs(difference));
                error.Count++;
            }
            error.MeanAbsolute = sumAbsolute / error.Count;
            error.MeanSigned = sumSigned / error.Count;
            return error;
        }

        [Test]
        public void MultiStencilEikonalIsMoreAccurateThanDijkstraAndWithinItsMeasuredBounds()
        {
            FieldError eikonalWall = Measure(new EikonalDistanceFieldSolver());
            FieldError dijkstraWall = Measure(new DijkstraDistanceFieldSolver());
            FieldError eikonalPoint = MeasurePointSource(new EikonalDistanceFieldSolver());
            FieldError dijkstraPoint = MeasurePointSource(new DijkstraDistanceFieldSolver());
            TestContext.WriteLine(describe("Eikonal, behind a wall", eikonalWall));
            TestContext.WriteLine(describe("Dijkstra, behind a wall", dijkstraWall));
            TestContext.WriteLine(describe("Eikonal, point source", eikonalPoint));
            TestContext.WriteLine(describe("Dijkstra, point source", dijkstraPoint));

            Assert.That(eikonalWall.Count, Is.GreaterThan(1000));
            Assert.That(eikonalWall.MeanAbsolute, Is.LessThan(dijkstraWall.MeanAbsolute));
            Assert.That(eikonalWall.MaxAbsolute, Is.LessThan(dijkstraWall.MaxAbsolute));
            Assert.That(eikonalPoint.MeanAbsolute, Is.LessThan(dijkstraPoint.MeanAbsolute));
            Assert.That(eikonalPoint.MaxAbsolute, Is.LessThan(dijkstraPoint.MaxAbsolute));

            // Measured when the multi-stencil scheme was introduced (October 2026), with headroom: behind a wall
            // 0.551 m mean and 1.768 m max; point source 0.244 m mean and 0.481 m max. The 4-point scheme it
            // replaced measured 0.729 / 1.774 and 0.648 / 1.151. These guard against regressions, not exact values.
            Assert.That(eikonalWall.MeanAbsolute, Is.LessThan(0.65));
            Assert.That(eikonalWall.MaxAbsolute, Is.LessThan(2.0));
            Assert.That(eikonalPoint.MeanAbsolute, Is.LessThan(0.3));
            Assert.That(eikonalPoint.MaxAbsolute, Is.LessThan(0.6));
        }

        private static string describe(string scenario, in FieldError error) =>
            $"{scenario}: {error.Count} cells, mean |error| {error.MeanAbsolute:F4} m, " +
            $"max |error| {error.MaxAbsolute:F4} m, mean signed error {error.MeanSigned:+0.0000;-0.0000} m";

        // Exact shortest distance from a to b around the wall block: a visibility graph over a, b and the
        // block's four corners, where a segment may run along the block's boundary but not through it.
        private static double geodesic(Vector2 a, Vector2 b)
        {
            var nodes = new[]
            {
                a, b,
                new Vector2(wallMinX, wallMinY), new Vector2(wallMaxX + 1, wallMinY),
                new Vector2(wallMinX, wallMaxY + 1), new Vector2(wallMaxX + 1, wallMaxY + 1),
            };
            var best = new double[nodes.Length];
            var done = new bool[nodes.Length];
            for (int i = 0; i < best.Length; i++) best[i] = double.PositiveInfinity;
            best[0] = 0;
            for (int round = 0; round < nodes.Length; round++)
            {
                int current = -1;
                for (int i = 0; i < nodes.Length; i++)
                    if (!done[i] && (current < 0 || best[i] < best[current])) current = i;
                done[current] = true;
                if (current == 1) break;
                for (int next = 0; next < nodes.Length; next++)
                {
                    if (done[next] || crossesWallInterior(nodes[current], nodes[next])) continue;
                    best[next] = Math.Min(best[next], best[current] + Vector2.Distance(nodes[current], nodes[next]));
                }
            }
            return best[1];
        }

        // True if the segment passes through the open interior of the wall block (Liang-Barsky clipping).
        private static bool crossesWallInterior(Vector2 p, Vector2 q)
        {
            double t0 = 0, t1 = 1;
            double dx = q.X - p.X, dy = q.Y - p.Y;
            if (!clip(-dx, p.X - wallMinX, ref t0, ref t1) || !clip(dx, wallMaxX + 1 - p.X, ref t0, ref t1) ||
                !clip(-dy, p.Y - wallMinY, ref t0, ref t1) || !clip(dy, wallMaxY + 1 - p.Y, ref t0, ref t1))
                return false;
            if (t1 - t0 < 1e-9) return false; // touches a single point, such as a corner
            double mx = p.X + 0.5 * (t0 + t1) * dx, my = p.Y + 0.5 * (t0 + t1) * dy;
            const double eps = 1e-6;
            return mx > wallMinX + eps && mx < wallMaxX + 1 - eps && my > wallMinY + eps && my < wallMaxY + 1 - eps;
        }

        private static bool clip(double denominator, double numerator, ref double t0, ref double t1)
        {
            if (Math.Abs(denominator) < 1e-12) return numerator >= 0;
            double t = numerator / denominator;
            if (denominator < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
    }
}
