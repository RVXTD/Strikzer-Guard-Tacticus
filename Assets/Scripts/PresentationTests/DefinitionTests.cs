// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using Game.Rules;
using NUnit.Framework;
using UnityEngine;
using NumericsVector2 = System.Numerics.Vector2;

namespace Game.Presentation.Tests
{
    /// <summary>Tests: ScriptableObject definitions turn into the rules layer's setup data unchanged.</summary>
    public class DefinitionTests
    {
        private readonly List<ScriptableObject> created = new List<ScriptableObject>();

        private T make<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            created.Add(asset);
            return asset;
        }

        [TearDown]
        public void DestroyAssets()
        {
            foreach (ScriptableObject asset in created) UnityEngine.Object.DestroyImmediate(asset);
            created.Clear();
        }

        [Test]
        public void UnitDefinitionMapsEveryField()
        {
            var weapon = make<WeaponDefinition>();
            weapon.Stats = new WeaponStats { RoundsPerAttack = 4, DamagePerRound = 3, OptimalRange = 12f };
            var first = make<AbilityAsset>();
            var second = make<AbilityAsset>();
            var unit = make<UnitDefinition>();
            unit.MaxHealth = 14;
            unit.Accuracy = 65;
            unit.MovementBudget = 5.5f;
            unit.ActionPoints = 3;
            unit.Initiative = 7;
            unit.Weapon = weapon;
            unit.StartingAmmo = 20;
            unit.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            unit.Abilities = new[] { second, first };

            UnitSetup setup = unit.ToUnitSetup(Team.Enemy, new NumericsVector2(3f, 4f), new NumericsVector2(0f, 1f), true,
                                               ability => ability == first ? 10 : 11);

            Assert.That(setup.Team, Is.EqualTo(Team.Enemy));
            Assert.That(setup.Position, Is.EqualTo(new NumericsVector2(3f, 4f)));
            Assert.That(setup.Facing, Is.EqualTo(new NumericsVector2(0f, 1f)));
            Assert.That(setup.MaxHealth, Is.EqualTo(14));
            Assert.That(setup.Accuracy, Is.EqualTo(65));
            Assert.That(setup.MovementBudget, Is.EqualTo(5.5f));
            Assert.That(setup.ActionPoints, Is.EqualTo(3));
            Assert.That(setup.Initiative, Is.EqualTo(7));
            Assert.That(setup.Weapon.RoundsPerAttack, Is.EqualTo(4));
            Assert.That(setup.Weapon.DamagePerRound, Is.EqualTo(3));
            Assert.That(setup.StartingAmmo, Is.EqualTo(20));
            Assert.That(setup.Zone.Radius, Is.EqualTo(2f));
            Assert.That(setup.IsObjective, Is.True);
            Assert.That(setup.Abilities, Is.EqualTo(new[] { 11, 10 }));
        }

        [Test]
        public void AUnitWithoutAWeaponIsReported()
        {
            var unit = make<UnitDefinition>();
            Assert.Throws<InvalidOperationException>(() => unit.ToUnitSetup(Team.Player, default, default, false, _ => 0));
        }

        [Test]
        public void TuningCopiesIntoTheEncounterSetup()
        {
            var tuning = make<EncounterTuning>();
            HitChanceTuning hit = HitChanceTuning.Default;
            hit.FullCoverPenalty = 55;
            tuning.HitChance = hit;
            tuning.PickupRadius = 2.5f;
            tuning.Seed = 99;
            var setup = new EncounterSetup();
            tuning.ApplyTo(setup);
            Assert.That(setup.HitTuning.FullCoverPenalty, Is.EqualTo(55));
            Assert.That(setup.PickupRadius, Is.EqualTo(2.5f));
            Assert.That(setup.Seed, Is.EqualTo(99UL));
        }

        [Test]
        public void ABakedGridAppliesItsSizeOriginAndCells()
        {
            var grid = make<MapGridAsset>();
            grid.Resize(3, 2, 0.5f, new Vector2(10f, -4f), 1.25f);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 3; x++)
                    grid.SetCell(x, y, true, CellObstacle.None);
            grid.SetCell(2, 1, true, CellObstacle.Glass);
            grid.SetCell(0, 1, false, CellObstacle.None);

            var setup = new EncounterSetup();
            grid.ApplyTo(setup);
            Assert.That(setup.Width, Is.EqualTo(3));
            Assert.That(setup.Height, Is.EqualTo(2));
            Assert.That(setup.CellSize, Is.EqualTo(0.5f));
            Assert.That(setup.Origin, Is.EqualTo(new NumericsVector2(10f, -4f)));
            Assert.That(setup.Obstacles[1 * 3 + 2].BlocksMovement, Is.True, "index y * width + x");
            Assert.That(setup.Obstacles[1 * 3 + 2].BlocksSight, Is.False);
            Assert.That(setup.Floor[1 * 3 + 0], Is.False);
            Assert.That(grid.CellCenter(2, 1), Is.EqualTo(new Vector3(11.25f, 1.25f, -3.25f)));

            setup.Floor[0] = false;
            Assert.That(grid.HasFloor(0, 0), Is.True, "the setup gets copies");
            Assert.DoesNotThrow(() => new Encounter(setup));
        }

        [Test]
        public void AnUnbakedGridIsReported()
        {
            Assert.Throws<InvalidOperationException>(() => make<MapGridAsset>().ApplyTo(new EncounterSetup()));
        }
    }
}
