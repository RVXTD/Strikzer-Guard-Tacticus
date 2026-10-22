using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Rules.Tests
{
    /// <summary>Tests: Handle issuing, retiring and stale-handle detection.</summary>
    public class UnitRegistryTests
    {
        [Test]
        public void DefaultHandleIsNoneAndNeverValid()
        {
            var registry = new UnitRegistry(4);
            Assert.That(default(UnitId).IsNone, Is.True);
            Assert.That(registry.IsValid(UnitId.None), Is.False);
        }

        [Test]
        public void CreateIssuesDistinctValidHandles()
        {
            var registry = new UnitRegistry(4);
            UnitId a = registry.Create(Team.Player), b = registry.Create(Team.Enemy);
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(registry.IsValid(a) && registry.IsValid(b), Is.True);
            Assert.That(registry.GetTeam(b), Is.EqualTo(Team.Enemy));
            Assert.That(registry.Count, Is.EqualTo(2));
        }

        [Test]
        public void ReusedSlotGetsNewGenerationAndOldHandleGoesStale()
        {
            var registry = new UnitRegistry(4);
            UnitId first = registry.Create(Team.Player);
            registry.Retire(first);
            UnitId second = registry.Create(Team.Enemy);

            Assert.That(second.Index, Is.EqualTo(first.Index));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(registry.IsValid(first), Is.False);
            Assert.Throws<InvalidOperationException>(() => registry.GetTeam(first));
        }

        [Test]
        public void CreateThrowsWhenFull()
        {
            var registry = new UnitRegistry(1);
            registry.Create(Team.Player);
            Assert.Throws<InvalidOperationException>(() => registry.Create(Team.Player));
        }
    }

    /// <summary>Tests: Reproducibility and ranges of the seeded random generator.</summary>
    public class GameRandomTests
    {
        [Test]
        public void MatchesPcg32ReferenceOutput()
        {
            // First outputs of pcg32-demo (seed 42, sequence 54) from the PCG reference implementation.
            var random = new GameRandom(42UL, 54UL);
            uint[] expected = { 0xa15c02b7u, 0x7b47f409u, 0xba1d3330u, 0x83d2f293u, 0xbfa4784bu, 0xcbed606eu };
            foreach (uint value in expected) Assert.That(random.NextUInt(), Is.EqualTo(value));
        }

        [Test]
        public void SameSeedGivesSameSequence()
        {
            var a = new GameRandom(7UL);
            var b = new GameRandom(7UL);
            for (int i = 0; i < 100; i++) Assert.That(a.NextInt(0, 1000), Is.EqualTo(b.NextInt(0, 1000)));
        }

        [Test]
        public void NextIntCoversExactlyItsRange()
        {
            var random = new GameRandom(3UL);
            var seen = new HashSet<int>();
            for (int i = 0; i < 10000; i++)
            {
                int value = random.NextInt(3, 7);
                Assert.That(value, Is.InRange(3, 6));
                seen.Add(value);
            }
            Assert.That(seen.Count, Is.EqualTo(4));
        }

        [Test]
        public void NextFloatStaysInUnitInterval()
        {
            var random = new GameRandom(5UL);
            for (int i = 0; i < 10000; i++)
            {
                float value = random.NextFloat();
                Assert.That(value >= 0f && value < 1f, Is.True);
            }
        }

        [Test]
        public void RestoredStateReplaysTheSameValues()
        {
            var random = new GameRandom(11UL);
            random.NextUInt();
            var saved = random.SaveState();
            uint first = random.NextUInt(), second = random.NextUInt();
            random.RestoreState(saved);
            Assert.That(random.NextUInt(), Is.EqualTo(first));
            Assert.That(random.NextUInt(), Is.EqualTo(second));
        }
    }

    /// <summary>Tests: The hit-chance formula and its breakdown.</summary>
    public class HitChanceTests
    {
        [Test]
        public void NoModifiersGivesBaseAccuracy()
        {
            var inputs = new HitChanceInputs { AttackerAccuracy = 70, Distance = 5f, OptimalRange = 10f };
            HitChanceBreakdown result = HitChance.Compute(inputs, HitChanceTuning.Default);
            Assert.That(result.Total, Is.EqualTo(70));
            Assert.That(result.Cover + result.Range + result.Zone + result.Status, Is.EqualTo(0));
        }

        [Test]
        public void EachFactorContributesItsOwnLine()
        {
            var inputs = new HitChanceInputs
            {
                AttackerAccuracy = 70,
                TargetCover = CoverLevel.Half,
                Distance = 15f,
                OptimalRange = 10f,
                AttackerInHostileZone = true,
                StatusModifier = 5,
            };
            HitChanceBreakdown result = HitChance.Compute(inputs, HitChanceTuning.Default);
            Assert.That(result.Cover, Is.EqualTo(-20));
            Assert.That(result.Range, Is.EqualTo(-15));
            Assert.That(result.Zone, Is.EqualTo(-15));
            Assert.That(result.Status, Is.EqualTo(5));
            Assert.That(result.Total, Is.EqualTo(25));
        }

        [TestCase(200, 95)]
        [TestCase(-50, 5)]
        public void TotalIsClampedToTuningLimits(int accuracy, int expected)
        {
            var inputs = new HitChanceInputs { AttackerAccuracy = accuracy, OptimalRange = 10f };
            Assert.That(HitChance.Compute(inputs, HitChanceTuning.Default).Total, Is.EqualTo(expected));
        }
    }
}
