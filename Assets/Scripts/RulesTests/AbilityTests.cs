// Written by Claude (Anthropic), October 2026: tests for overwatch, suppression, revive, the boss blast
// and ability validation; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Numerics;
using NUnit.Framework;

namespace Game.Rules.Tests
{
    /// <summary>Tests: Abilities used through commands and as reactions, and their validation.</summary>
    public class AbilityTests
    {
        private const int overwatch = 0, suppression = 1, revive = 2, blast = 3, suppressionReaction = 4, shortSuppression = 5;

        private static EncounterSetup withAbilities(EncounterSetup setup)
        {
            AbilityDefinition shortRange = TestWorld.Suppression();
            shortRange.Range = 3f;
            setup.Abilities = new[]
            {
                TestWorld.Overwatch(), TestWorld.Suppression(), TestWorld.Revive(), TestWorld.Blast(),
                TestWorld.Suppression(isReaction: true), shortRange,
            };
            return setup;
        }

        private static int countEvents(Encounter encounter, EventKind kind)
        {
            int total = 0;
            for (int i = 0; i < encounter.Events.Count; i++)
                if (encounter.Events[i].Kind == kind) total++;
            return total;
        }

        [Test]
        public void OverwatchConeReactsToMovementAndClearsAfterFiring()
        {
            var setup = withAbilities(TestWorld.OpenMap(12, 8));
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup watcherSetup = TestWorld.Soldier(Team.Player, 1, 4);
            watcherSetup.Accuracy = 100;
            watcherSetup.Abilities = new[] { overwatch };
            UnitId watcher = encounter.AddUnit(watcherSetup);
            UnitId runner = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 6, 0));
            encounter.Start();

            Assert.That(encounter.Execute(Command.UseAbility(watcher, overwatch, UnitId.None, TestWorld.At(8, 4))).Accepted, Is.True);
            Assert.That(countEvents(encounter, EventKind.ZonePlaced), Is.EqualTo(1));
            Assert.That(encounter.Spatial.HasTemporaryZone(watcher), Is.True);
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(runner), "overwatch ends the turn");

            var plan = new MovePlan();
            Assert.That(encounter.PlanMove(runner, TestWorld.At(6, 7), plan), Is.EqualTo(RejectReason.None));
            Assert.That(plan.Interrupts.Count, Is.EqualTo(1), "the preview warns about the cone");
            Assert.That(encounter.Execute(Command.Move(runner, TestWorld.At(6, 7))).Accepted, Is.True);
            Assert.That(countEvents(encounter, EventKind.MoveInterrupted), Is.EqualTo(1));
            Assert.That(encounter.Combat.GetHealth(runner), Is.EqualTo(4));
            Assert.That(encounter.Spatial.HasTemporaryZone(watcher), Is.False, "the cone is spent once it fires");
            Assert.That(countEvents(encounter, EventKind.ZoneRemoved), Is.EqualTo(1));
            Assert.That(encounter.Flow.HasReaction(watcher), Is.False);
        }

        [Test]
        public void UnusedOverwatchExpiresAtTheOwnersNextTurn()
        {
            var encounter = new Encounter(withAbilities(TestWorld.OpenMap(12, 8)));
            UnitSetup watcherSetup = TestWorld.Soldier(Team.Player, 1, 4);
            watcherSetup.Abilities = new[] { overwatch };
            UnitId watcher = encounter.AddUnit(watcherSetup);
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 11, 0));
            encounter.Start();

            Assert.That(encounter.Execute(Command.UseAbility(watcher, overwatch, UnitId.None, TestWorld.At(8, 4))).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.EndTurn(enemy)).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(watcher));
            Assert.That(encounter.Spatial.HasTemporaryZone(watcher), Is.False);
            Assert.That(countEvents(encounter, EventKind.ZoneRemoved), Is.EqualTo(1));
        }

        [Test]
        public void SuppressionPinsAndLowersAccuracyUntilTheTargetsNextTurnEnds()
        {
            var encounter = new Encounter(withAbilities(TestWorld.OpenMap(8, 8)));
            UnitSetup suppressorSetup = TestWorld.Soldier(Team.Player, 0, 0);
            suppressorSetup.Abilities = new[] { suppression };
            UnitId suppressor = encounter.AddUnit(suppressorSetup);
            UnitSetup tough = TestWorld.Soldier(Team.Enemy, 4, 0);
            tough.MaxHealth = 1000;
            UnitId enemy = encounter.AddUnit(tough);
            encounter.Start();

            Assert.That(encounter.Execute(Command.UseAbility(suppressor, suppression, enemy, default)).Accepted, Is.True);
            Assert.That(encounter.Resources.GetCarried(suppressor, 0), Is.EqualTo(27), "suppression fires a normal burst");
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(enemy));
            Assert.That(encounter.Status.IsSuppressed(enemy), Is.True);
            Assert.That(encounter.Execute(Command.Move(enemy, TestWorld.At(4, 3))).Reason, Is.EqualTo(RejectReason.Pinned));
            Assert.That(encounter.PreviewAttack(enemy, suppressor).Status, Is.EqualTo(-20));

            Assert.That(encounter.Execute(Command.EndTurn(enemy)).Accepted, Is.True);
            Assert.That(encounter.Status.IsSuppressed(enemy), Is.False);
            Assert.That(encounter.Execute(Command.EndTurn(suppressor)).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.Move(enemy, TestWorld.At(4, 3))).Accepted, Is.True);
        }

        [Test]
        public void SuppressingReactionStopsAMoveEvenOnAMiss()
        {
            var setup = withAbilities(TestWorld.OpenMap(12, 5));
            setup.HitTuning.Minimum = 0;
            setup.HitTuning.Maximum = 0; // every round misses
            var encounter = new Encounter(setup);
            UnitSetup runnerSetup = TestWorld.Soldier(Team.Player, 0, 2);
            runnerSetup.MovementBudget = 12f;
            UnitId runner = encounter.AddUnit(runnerSetup);
            UnitSetup sentry = TestWorld.Soldier(Team.Enemy, 6, 2);
            sentry.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            sentry.Abilities = new[] { suppressionReaction };
            encounter.AddUnit(sentry);
            encounter.Start();

            Assert.That(encounter.Execute(Command.Move(runner, TestWorld.At(11, 2))).Accepted, Is.True);
            Assert.That(countEvents(encounter, EventKind.RoundHit), Is.EqualTo(0));
            Assert.That(countEvents(encounter, EventKind.MoveInterrupted), Is.EqualTo(1));
            Assert.That(Vector2.Distance(encounter.Spatial.GetPosition(runner), TestWorld.At(6, 2)), Is.EqualTo(2f).Within(1e-3f), "stopped at the zone's edge");
            Assert.That(encounter.Status.IsSuppressed(runner), Is.True);
            Assert.That(encounter.Execute(Command.Move(runner, TestWorld.At(2, 2))).Reason, Is.EqualTo(RejectReason.Pinned));

            bool reactionLogged = false;
            for (int i = 0; i < encounter.Events.Count; i++)
                reactionLogged |= encounter.Events[i].Kind == EventKind.AbilityUsed && encounter.Events[i].IsReaction;
            Assert.That(reactionLogged, Is.True);
        }

        private static Encounter downOneOfTwoPlayers(out UnitId downed, out UnitId medic, out UnitId enemy)
        {
            var setup = withAbilities(TestWorld.OpenMap(8, 8));
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup fragile = TestWorld.Soldier(Team.Player, 0, 0);
            fragile.MaxHealth = 6; // one 3-round burst of 2 damage downs it
            fragile.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            downed = encounter.AddUnit(fragile);
            UnitSetup medicSetup = TestWorld.Soldier(Team.Player, 1, 0);
            medicSetup.Abilities = new[] { revive };
            medic = encounter.AddUnit(medicSetup);
            UnitSetup deadeye = TestWorld.Soldier(Team.Enemy, 4, 0);
            deadeye.Accuracy = 100;
            deadeye.Initiative = 200;
            enemy = encounter.AddUnit(deadeye);
            encounter.Start();
            Assert.That(encounter.Execute(Command.Attack(enemy, downed)).Accepted, Is.True);
            Assert.That(encounter.Combat.IsDowned(downed), Is.True);
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(medic));
            return encounter;
        }

        [Test]
        public void ReviveRestoresADownedAllyAndItsZone()
        {
            Encounter encounter = downOneOfTwoPlayers(out UnitId downed, out UnitId medic, out _);

            Assert.That(encounter.Execute(Command.UseAbility(medic, revive, downed, default)).Accepted, Is.True);
            Assert.That(encounter.Combat.IsDowned(downed), Is.False);
            Assert.That(encounter.Combat.GetHealth(downed), Is.EqualTo(5));
            Assert.That(encounter.Spatial.HasActiveZone(downed), Is.True);
            Assert.That(countEvents(encounter, EventKind.UnitRevived), Is.EqualTo(1));
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(1), "revive does not end the turn");

            Assert.That(encounter.Execute(Command.EndTurn(medic)).Accepted, Is.True);
            Assert.That(encounter.Flow.TurnOrder, Has.Member(downed), "the revived unit is back in the next round");
        }

        [Test]
        public void ReviveNeedsADownedAllyInRange()
        {
            Encounter encounter = downOneOfTwoPlayers(out UnitId downed, out UnitId medic, out UnitId enemy);

            Assert.That(encounter.Execute(Command.UseAbility(medic, revive, medic, default)).Reason, Is.EqualTo(RejectReason.InvalidTarget), "not downed");
            Assert.That(encounter.Execute(Command.UseAbility(medic, revive, enemy, default)).Reason, Is.EqualTo(RejectReason.TargetNotFriendly));
            Assert.That(encounter.Execute(Command.Move(medic, TestWorld.At(1, 4))).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.UseAbility(medic, revive, downed, default)).Reason, Is.EqualTo(RejectReason.OutOfRange));
            Assert.That(encounter.Combat.IsDowned(downed), Is.True);
        }

        [Test]
        public void BlastDamagesVisiblePlayersInTheRadiusOnly()
        {
            var setup = withAbilities(TestWorld.Map(
                "..........",
                "..........",
                "....#.....",
                "..........",
                ".........."));
            var encounter = new Encounter(setup);
            UnitSetup bossSetup = TestWorld.Soldier(Team.Enemy, 9, 2);
            bossSetup.Initiative = 200;
            bossSetup.Abilities = new[] { blast };
            UnitId boss = encounter.AddUnit(bossSetup);
            UnitId nearby = encounter.AddUnit(TestWorld.Soldier(Team.Player, 5, 3));
            UnitSetup fragile = TestWorld.Soldier(Team.Player, 5, 1);
            fragile.MaxHealth = 4;
            UnitId downed = encounter.AddUnit(fragile);
            UnitId behindWall = encounter.AddUnit(TestWorld.Soldier(Team.Player, 3, 2));
            UnitId farAway = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 4));
            encounter.Start();

            Assert.That(encounter.Execute(Command.UseAbility(boss, blast, UnitId.None, TestWorld.At(5, 2))).Accepted, Is.True);
            Assert.That(encounter.Combat.GetHealth(nearby), Is.EqualTo(6));
            Assert.That(encounter.Combat.IsDowned(downed), Is.True);
            Assert.That(encounter.Combat.GetHealth(behindWall), Is.EqualTo(10), "the wall shields it from the blast");
            Assert.That(encounter.Combat.GetHealth(farAway), Is.EqualTo(10));
            Assert.That(countEvents(encounter, EventKind.UnitDamaged), Is.EqualTo(2));
            Assert.That(encounter.Resources.GetCarried(boss, 0), Is.EqualTo(30), "a blast fires no rounds");
            Assert.That(encounter.Flow.ActiveUnit, Is.Not.EqualTo(boss));
        }

        [Test]
        public void AbilityUseIsValidated()
        {
            var setup = withAbilities(TestWorld.Map(
                ".........",
                ".........",
                "....#....",
                ".........",
                "........."));
            var encounter = new Encounter(setup);
            UnitSetup playerSetup = TestWorld.Soldier(Team.Player, 0, 2);
            playerSetup.Abilities = new[] { suppression, shortSuppression };
            UnitId player = encounter.AddUnit(playerSetup);
            UnitId hidden = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 8, 2));
            UnitId visible = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 8, 4));
            encounter.Start();
            int eventsBefore = encounter.Events.Count;

            Assert.That(encounter.Execute(Command.UseAbility(player, blast, UnitId.None, TestWorld.At(8, 4))).Reason, Is.EqualTo(RejectReason.AbilityNotAvailable));
            Assert.That(encounter.Execute(Command.UseAbility(player, suppression, hidden, default)).Reason, Is.EqualTo(RejectReason.NoLineOfSight));
            Assert.That(encounter.Execute(Command.UseAbility(player, suppression, player, default)).Reason, Is.EqualTo(RejectReason.TargetNotHostile));
            Assert.That(encounter.Execute(Command.UseAbility(player, shortSuppression, visible, default)).Reason, Is.EqualTo(RejectReason.OutOfRange));
            Assert.That(encounter.Events.Count, Is.EqualTo(eventsBefore), "rejected abilities change nothing");

            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(0, 0))).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(0, 4))).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.UseAbility(player, suppression, visible, default)).Reason, Is.EqualTo(RejectReason.NotEnoughActionPoints));
        }

        [Test]
        public void AttackAbilitiesNeedAmmo()
        {
            var encounter = new Encounter(withAbilities(TestWorld.OpenMap(8, 8)));
            UnitSetup empty = TestWorld.Soldier(Team.Player, 0, 0);
            empty.StartingAmmo = 0;
            empty.Abilities = new[] { suppression };
            UnitId player = encounter.AddUnit(empty);
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 4, 0));
            encounter.Start();
            Assert.That(encounter.Execute(Command.UseAbility(player, suppression, enemy, default)).Reason, Is.EqualTo(RejectReason.NoAmmo));
        }

        [Test]
        public void UnknownAbilityIndicesAreRejectedAtSetup()
        {
            var encounter = new Encounter(withAbilities(TestWorld.OpenMap(4, 4)));
            UnitSetup bad = TestWorld.Soldier(Team.Player, 0, 0);
            bad.Abilities = new[] { 99 };
            Assert.Throws<ArgumentOutOfRangeException>(() => encounter.AddUnit(bad));
            Assert.That(encounter.Units.Count, Is.EqualTo(0), "nothing is registered");
        }
    }
}
