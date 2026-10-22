// Written by Claude (Anthropic), October 2026: tests of the utility-scoring AI; to be reviewed by Alexander Stangl
// per the course syllabus.

using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace Game.Rules.Tests
{
    /// <summary>Tests: The AI's choices, and whole encounters played by the AI on both sides.</summary>
    public class AiTests
    {
        // An encounter whose first unit acts first; returns it with the units' ids in order.
        private static Encounter begin(EncounterSetup setup, out UnitId[] ids, params UnitSetup[] units)
        {
            var encounter = new Encounter(setup);
            ids = new UnitId[units.Length];
            for (int i = 0; i < units.Length; i++)
            {
                UnitSetup unit = units[i];
                unit.Initiative = i == 0 ? 1000 : 0;
                ids[i] = encounter.AddUnit(unit);
            }
            encounter.Start();
            return encounter;
        }

        private static Command choose(Encounter encounter, UnitId unit)
        {
            Command command = encounter.Ai.ChooseCommand(unit);
            Assert.That(encounter.Validate(command), Is.EqualTo(RejectReason.None), $"the AI chose a rejected {command.Kind}");
            return command;
        }

        [Test]
        public void AttacksAVisibleHostileRatherThanEndingTheTurn()
        {
            Encounter encounter = begin(TestWorld.OpenMap(10, 10), out UnitId[] ids,
                                        TestWorld.Soldier(Team.Enemy, 2, 2), TestWorld.Soldier(Team.Player, 6, 5));
            Command command = choose(encounter, ids[0]);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.Attack));
            Assert.That(command.Target, Is.EqualTo(ids[1]));
        }

        [Test]
        public void PrefersTheTargetItExpectsToTakeOut()
        {
            UnitSetup weak = TestWorld.Soldier(Team.Player, 6, 2);
            weak.MaxHealth = 2;
            Encounter encounter = begin(TestWorld.OpenMap(10, 10), out UnitId[] ids,
                                        TestWorld.Soldier(Team.Enemy, 2, 2), TestWorld.Soldier(Team.Player, 6, 6), weak);
            Command command = choose(encounter, ids[0]);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.Attack));
            Assert.That(command.Target, Is.EqualTo(ids[2]));
        }

        [Test]
        public void MovesIntoCoverBeforeShooting()
        {
            var setup = TestWorld.Map(
                "............",
                "....h.......",
                "............");
            Encounter encounter = begin(setup, out UnitId[] ids,
                                        TestWorld.Soldier(Team.Enemy, 1, 1), TestWorld.Soldier(Team.Player, 11, 1));
            Command command = choose(encounter, ids[0]);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.Move));
            Vector2 hostile = encounter.Spatial.GetPosition(ids[1]);
            Assert.That(encounter.Spatial.CoverAgainst(command.Destination, hostile), Is.Not.EqualTo(CoverLevel.None));

            encounter.Execute(command);
            Assert.That(choose(encounter, ids[0]).Kind, Is.EqualTo(CommandKind.Attack));
        }

        [Test]
        public void ClosesInWhenNoHostileIsInSight()
        {
            var setup = TestWorld.Map(
                "..........#.........",
                "..........#.........",
                "..........#.........",
                "..........#.........",
                "....................");
            UnitSetup hunter = TestWorld.Soldier(Team.Enemy, 2, 3);
            hunter.Ai.Approach = 1f;
            Encounter encounter = begin(setup, out UnitId[] ids, hunter, TestWorld.Soldier(Team.Player, 17, 3));
            Vector2 start = encounter.Spatial.GetPosition(ids[0]), target = encounter.Spatial.GetPosition(ids[1]);

            Command command = choose(encounter, ids[0]);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.Move));
            Assert.That(Vector2.Distance(command.Destination, target), Is.LessThan(Vector2.Distance(start, target) - 3f));
        }

        [Test]
        public void ASentryWithNoMovementNeverMoves()
        {
            var setup = TestWorld.Map(
                ".....#....",
                ".....#....",
                "..........");
            UnitSetup sentry = TestWorld.Soldier(Team.Enemy, 2, 2);
            sentry.MovementBudget = 0f;
            Encounter encounter = begin(setup, out UnitId[] ids, sentry, TestWorld.Soldier(Team.Player, 8, 2));
            Assert.That(choose(encounter, ids[0]).Kind, Is.EqualTo(CommandKind.EndTurn), "nothing in sight");

            encounter = begin(setup, out ids, sentry, TestWorld.Soldier(Team.Player, 4, 0));
            Assert.That(choose(encounter, ids[0]).Kind, Is.EqualTo(CommandKind.Attack), "a hostile in sight");
        }

        [Test]
        public void ABadlyHurtUnitFallsBackOutOfSight()
        {
            var setup = TestWorld.Map(
                "..........",
                "...#......",
                "...#......",
                "...#......",
                "..........");
            UnitSetup hurt = TestWorld.Soldier(Team.Enemy, 4, 2);
            hurt.Ai.RetreatWhenHurt = 3f;
            Encounter encounter = begin(setup, out UnitId[] ids, hurt, TestWorld.Soldier(Team.Player, 9, 2));
            encounter.Combat.ApplyDamage(ids[1], ids[0], 8, encounter.Events);

            Command command = choose(encounter, ids[0]);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.Move));
            Assert.That(encounter.Spatial.HasLineOfSight(encounter.Spatial.GetPosition(ids[1]), command.Destination), Is.False);
        }

        [Test]
        public void WithNoAmmoWalksToAPileAndPicksItUp()
        {
            var setup = TestWorld.Map(
                "..........#.....",
                "..........#.....",
                "..........#.....",
                "..........#.....",
                "..........#.....");
            UnitSetup empty = TestWorld.Soldier(Team.Enemy, 2, 2);
            empty.StartingAmmo = 0;
            var encounter = new Encounter(setup);
            empty.Initiative = 1000;
            UnitId unit = encounter.AddUnit(empty);
            encounter.AddUnit(TestWorld.Soldier(Team.Player, 14, 2));
            encounter.Resources.AddToPile(0, 10, TestWorld.At(7, 2), PileOrigin.Placed);
            encounter.Start();

            Command command = choose(encounter, unit);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.Move));
            encounter.Execute(command);
            command = choose(encounter, unit);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.PickUp));
            encounter.Execute(command);
            Assert.That(encounter.Resources.GetCarried(unit, 0), Is.GreaterThan(0));
        }

        [TestCase(0f, true)]
        [TestCase(3f, false)]
        public void ZoneAvoidanceKeepsMovesOutOfReactionZones(float zoneAvoidance, bool expectInterrupt)
        {
            // A brawler that wants to stand next to the hostile, whose 2.5 m circle reacts to anyone entering it.
            UnitSetup brawler = TestWorld.Soldier(Team.Enemy, 6, 0);
            brawler.Weapon.OptimalRange = 1f;
            brawler.Ai.Approach = 1f;
            brawler.Ai.ZoneAvoidance = zoneAvoidance;
            UnitSetup guard = TestWorld.Soldier(Team.Player, 6, 6);
            guard.Zone = new ZoneSetup { Radius = 2.5f, HalfAngleDegrees = 180f };
            Encounter encounter = begin(TestWorld.OpenMap(13, 13), out UnitId[] ids, brawler, guard);

            Command command = choose(encounter, ids[0]);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.Move));
            var plan = new MovePlan();
            encounter.PlanMove(ids[0], command.Destination, plan);
            Assert.That(plan.Interrupts.Count > 0, Is.EqualTo(expectInterrupt));
        }

        [Test]
        public void UsesAnAreaAbilityOnHostilesItCanSee()
        {
            var setup = TestWorld.Map(
                "..........",
                "..........",
                "......#...",
                "..........",
                "..........");
            setup.Abilities = new[] { TestWorld.Blast() };
            UnitSetup boss = TestWorld.Soldier(Team.Enemy, 2, 2);
            boss.MovementBudget = 0f;
            boss.Abilities = new[] { 0 };
            Encounter encounter = begin(setup, out UnitId[] ids, boss,
                                        TestWorld.Soldier(Team.Player, 6, 0), TestWorld.Soldier(Team.Player, 7, 1));
            Command command = choose(encounter, ids[0]);
            Assert.That(command.Kind, Is.EqualTo(CommandKind.UseAbility));
            var affected = new List<UnitId>();
            encounter.PreviewAbility(ids[0], 0, command.Target, command.Destination, affected);
            Assert.That(affected, Is.EquivalentTo(new[] { ids[1], ids[2] }));

            encounter = begin(setup, out ids, boss, TestWorld.Soldier(Team.Player, 8, 2));
            Assert.That(choose(encounter, ids[0]).Kind, Is.EqualTo(CommandKind.EndTurn), "nobody in sight");
        }

        [Test]
        public void AiAgainstAiFinishesAndReplaysIdentically()
        {
            List<string> first = playAiAgainstAi(7UL), second = playAiAgainstAi(7UL);
            Assert.That(second, Is.EqualTo(first));
        }

        // Plays a whole encounter with the AI on both sides; every command it chooses must be accepted.
        private static List<string> playAiAgainstAi(ulong seed)
        {
            var setup = TestWorld.Map(
                "................",
                "....h......h....",
                "................",
                ".......##.......",
                "................",
                "....h......h....",
                "................");
            setup.Seed = seed;
            var encounter = new Encounter(setup);
            encounter.AddUnit(TestWorld.Soldier(Team.Player, 1, 1));
            encounter.AddUnit(TestWorld.Soldier(Team.Player, 1, 5));
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 14, 1));
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 14, 5));
            encounter.Start();

            const int commandsPerTurn = 8;
            int sameUnitCommands = 0;
            UnitId last = UnitId.None;
            for (int i = 0; i < 2000 && encounter.Flow.Phase != EncounterPhase.Ended; i++)
            {
                UnitId active = encounter.Flow.ActiveUnit;
                sameUnitCommands = active == last ? sameUnitCommands + 1 : 0;
                last = active;
                Command command = sameUnitCommands >= commandsPerTurn ? Command.EndTurn(active) : encounter.Ai.ChooseCommand(active);
                Assert.That(encounter.Execute(command).Reason, Is.EqualTo(RejectReason.None), $"command {i}: {command.Kind}");
            }
            Assert.That(encounter.Flow.Phase, Is.EqualTo(EncounterPhase.Ended), "the encounter should finish");

            var log = new List<string>(encounter.Events.Count);
            for (int i = 0; i < encounter.Events.Count; i++)
            {
                GameEvent e = encounter.Events[i];
                log.Add($"{e.Kind} {e.Unit} {e.Other} {e.Position} {e.Value} {e.AmmoType} {e.PileId} {e.IsReaction}");
            }
            return log;
        }
    }
}
