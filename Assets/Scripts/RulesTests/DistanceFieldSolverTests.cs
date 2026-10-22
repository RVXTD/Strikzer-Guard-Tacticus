// Distance-limit tests by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the
// course syllabus.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Rules.Tests
{
    /// <summary>Tests: The distance field solver contract (seeds, reachability, descent), for every solver.</summary>
    [TestFixture(typeof(EikonalDistanceFieldSolver))]
    [TestFixture(typeof(DijkstraDistanceFieldSolver))]
    public class DistanceFieldSolverTests
    {
        private readonly Type solverType;
        private IDistanceFieldSolver solver;

        public DistanceFieldSolverTests(Type solverType) { this.solverType = solverType; }

        [SetUp]
        public void CreateSolver() { solver = (IDistanceFieldSolver)Activator.CreateInstance(solverType); }

        private static bool[] open(int count)
        {
            var passable = new bool[count];
            for (int i = 0; i < passable.Length; i++) passable[i] = true;
            return passable;
        }

        private float[] solve(int width, int height, bool[] passable, params DistanceSeed[] seeds)
        {
            var distances = new float[width * height];
            solver.Solve(width, height, 1f, passable, seeds, float.PositiveInfinity, distances);
            return distances;
        }

        [Test]
        public void SeedsKeepTheirDistanceEvenNextToASmallerSeed()
        {
            float[] d = solve(4, 1, open(4), new DistanceSeed(0, 0.3f), new DistanceSeed(1, 0.8f));
            Assert.That(d[0], Is.EqualTo(0.3f));
            Assert.That(d[1], Is.EqualTo(0.8f)); // not 0.3 + 1
            Assert.That(d[2], Is.EqualTo(1.8f).Within(1e-5f));
            Assert.That(d[3], Is.EqualTo(2.8f).Within(1e-5f));
        }

        [Test]
        public void ACellSeededTwiceKeepsTheSmallerDistance()
        {
            float[] d = solve(3, 1, open(3), new DistanceSeed(1, 1.5f), new DistanceSeed(1, 0.5f), new DistanceSeed(1, 2f));
            Assert.That(d[1], Is.EqualTo(0.5f));
            Assert.That(d[0], Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void NoUsableSeedsGiveAnAllInfiniteField()
        {
            bool[] passable = open(9);
            passable[4] = false;
            foreach (float[] d in new[] { solve(3, 3, passable), solve(3, 3, passable, new DistanceSeed(4, 0f)) })
                foreach (float value in d)
                    Assert.That(float.IsPositiveInfinity(value), Is.True);
        }

        [Test]
        public void InvalidSeedsThrow()
        {
            var distances = new float[4];
            foreach (DistanceSeed seed in new[]
            {
                new DistanceSeed(-1, 0f), new DistanceSeed(4, 0f), new DistanceSeed(0, -1f),
                new DistanceSeed(0, float.NaN), new DistanceSeed(0, float.PositiveInfinity),
            })
                Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(2, 2, 1f, open(4), new[] { seed }, float.PositiveInfinity, distances));
        }

        [Test]
        public void SeparatedRegionsAreMeasuredFromTheirOwnSeeds()
        {
            // Row 1 is a wall: row 0 is seeded at its left end, row 2 at its right end.
            bool[] passable = open(15);
            for (int x = 0; x < 5; x++) passable[5 + x] = false;
            float[] d = solve(5, 3, passable, new DistanceSeed(0, 0f), new DistanceSeed(14, 0.25f));
            for (int x = 0; x < 5; x++)
            {
                Assert.That(d[x], Is.EqualTo(x).Within(1e-5f));
                Assert.That(float.IsPositiveInfinity(d[5 + x]), Is.True);
                Assert.That(d[10 + x], Is.EqualTo(0.25f + (4 - x)).Within(1e-5f));
            }
        }

        [Test]
        public void ReachableCellsAreFiniteAndEveryNonSeedCellCanDescend()
        {
            var random = new Random(286);
            for (int trial = 0; trial < 20; trial++)
            {
                int width = 12 + trial % 5, height = 10 + trial % 3;
                var passable = new bool[width * height];
                for (int i = 0; i < passable.Length; i++) passable[i] = random.NextDouble() > 0.3;
                var seeds = new List<DistanceSeed>();
                var isSeed = new bool[passable.Length];
                for (int s = 0; s < 3; s++)
                {
                    int cell = random.Next(passable.Length);
                    seeds.Add(new DistanceSeed(cell, (float)random.NextDouble() * 2f));
                    isSeed[cell] |= passable[cell];
                }
                float[] d = solve(width, height, passable, seeds.ToArray());
                bool[] reachable = floodFill(width, height, passable, isSeed);

                for (int cell = 0; cell < d.Length; cell++)
                {
                    Assert.That(float.IsPositiveInfinity(d[cell]), Is.EqualTo(!reachable[cell]), $"trial {trial}, cell {cell}");
                    if (reachable[cell] && !isSeed[cell])
                        Assert.That(hasSmallerStepNeighbour(width, height, passable, d, cell), Is.True, $"trial {trial}, cell {cell}");
                }
            }
        }

        [Test]
        public void CellsBeyondTheLimitAreInfiniteAndReportedAsCutOff()
        {
            var distances = new float[10];
            var seeds = new[] { new DistanceSeed(0, 0f) };
            Assert.That(solver.Solve(10, 1, 1f, open(10), seeds, 4.5f, distances), Is.True);
            for (int x = 0; x < 10; x++)
            {
                if (x <= 4) Assert.That(distances[x], Is.EqualTo(x).Within(1e-5f));
                else Assert.That(float.IsPositiveInfinity(distances[x]), Is.True, $"cell {x}");
            }
            Assert.That(solver.Solve(10, 1, 1f, open(10), seeds, 9f, distances), Is.False, "nothing lies beyond 9 m");
            Assert.That(distances[9], Is.EqualTo(9f).Within(1e-5f));
        }

        [Test]
        public void ALimitDoesNotChangeValuesWithinIt()
        {
            var random = new Random(287);
            for (int trial = 0; trial < 20; trial++)
            {
                int width = 14 + trial % 4, height = 11 + trial % 3;
                var passable = new bool[width * height];
                for (int i = 0; i < passable.Length; i++) passable[i] = random.NextDouble() > 0.25;
                var seeds = new[]
                {
                    new DistanceSeed(random.Next(passable.Length), (float)random.NextDouble()),
                    new DistanceSeed(random.Next(passable.Length), (float)random.NextDouble() * 3f),
                };
                float[] full = solve(width, height, passable, seeds);
                foreach (float limit in new[] { 2.5f, 5f, 9f })
                {
                    var limited = new float[full.Length];
                    bool cutOff = solver.Solve(width, height, 1f, passable, seeds, limit, limited);
                    bool anyBeyond = false;
                    for (int cell = 0; cell < full.Length; cell++)
                    {
                        if (full[cell] <= limit)
                            Assert.That(limited[cell], Is.EqualTo(full[cell]).Within(1e-5f), $"trial {trial}, limit {limit}, cell {cell}");
                        else
                            Assert.That(float.IsPositiveInfinity(limited[cell]), Is.True, $"trial {trial}, limit {limit}, cell {cell}");
                        anyBeyond |= full[cell] > limit && !float.IsPositiveInfinity(full[cell]);
                    }
                    Assert.That(cutOff, Is.EqualTo(anyBeyond), $"trial {trial}, limit {limit}");
                }
            }
        }

        [Test]
        public void InvalidLimitsThrow()
        {
            var distances = new float[4];
            var seeds = new[] { new DistanceSeed(0, 0f) };
            Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(2, 2, 1f, open(4), seeds, -1f, distances));
            Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(2, 2, 1f, open(4), seeds, float.NaN, distances));
        }

        [Test]
        public void ReusingOneSolverAcrossGridSizesGivesTheSameFieldsAsFreshSolvers()
        {
            foreach (int size in new[] { 3, 6, 3, 6 })
            {
                bool[] passable = open(size * size);
                passable[size + 1] = false;
                var seeds = new[] { new DistanceSeed(0, 0.5f) };
                float[] reused = solve(size, size, passable, seeds);
                var fresh = new float[size * size];
                ((IDistanceFieldSolver)Activator.CreateInstance(solverType)).Solve(size, size, 1f, passable, seeds, float.PositiveInfinity, fresh);
                Assert.That(reused, Is.EqualTo(fresh));
            }
        }

        // 4-connected flood fill from the passable seed cells.
        private static bool[] floodFill(int width, int height, bool[] passable, bool[] isSeed)
        {
            var reached = new bool[passable.Length];
            var stack = new Stack<int>();
            for (int i = 0; i < isSeed.Length; i++)
                if (isSeed[i]) { reached[i] = true; stack.Push(i); }
            while (stack.Count > 0)
            {
                int cell = stack.Pop(), x = cell % width, y = cell / width;
                foreach ((int nx, int ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int n = ny * width + nx;
                    if (!passable[n] || reached[n]) continue;
                    reached[n] = true;
                    stack.Push(n);
                }
            }
            return reached;
        }

        // A neighbour reachable in one legal step (no corner cutting) with a strictly smaller value.
        private static bool hasSmallerStepNeighbour(int width, int height, bool[] passable, float[] d, int cell)
        {
            int x = cell % width, y = cell / width;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    if (!passable[ny * width + nx]) continue;
                    if (dx != 0 && dy != 0 && (!passable[y * width + nx] || !passable[ny * width + x])) continue;
                    if (d[ny * width + nx] < d[cell]) return true;
                }
            return false;
        }
    }
}
