// CanShootThroughGlassButNotWalkThrough, turn-economy, combat, resource, windowed-field, validation, body-radius
// and zone-sight tests by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Numerics;
using NUnit.Framework;

namespace Game.Rules.Tests
{
    /// <summary>Tests: Whole-encounter behaviour through commands: turns, moves, attacks, ammo, reactions, determinism.</summary>
    public class EncounterTests
    {
        private static int countEvents(Encounter encounter, EventKind kind, bool sumValues = false)
        {
            int total = 0;
            for (int i = 0; i < encounter.Events.Count; i++)
                if (encounter.Events[i].Kind == kind) total += sumValues ? encounter.Events[i].Value : 1;
            return total;
        }

        [Test]
        public void HigherInitiativeActsFirstAndTurnsAlternate()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Start();

            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(player));
            Assert.That(encounter.Execute(Command.EndTurn(player)).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(enemy));
            Assert.That(encounter.Execute(Command.EndTurn(enemy)).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(player));
            Assert.That(encounter.Flow.Round, Is.EqualTo(2));
        }

        [Test]
        public void CommandsFromTheWrongUnitAreRejected()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Start();
            Assert.That(encounter.Execute(Command.EndTurn(enemy)).Reason, Is.EqualTo(RejectReason.NotActiveUnit));
        }

        private static Encounter startDuel(out UnitId player)
        {
            var encounter = new Encounter(TestWorld.OpenMap(16, 8));
            player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 15, 7));
            encounter.Start();
            return encounter;
        }

        [Test]
        public void MovesCostOnePointWithinTheBudgetAndTwoWithinTwiceIt()
        {
            // The soldier's budget is 6 m per point.
            Encounter encounter = startDuel(out UnitId player);
            var plan = new MovePlan();
            Assert.That(encounter.PlanMove(player, TestWorld.At(4, 0), plan), Is.EqualTo(RejectReason.None));
            Assert.That(plan.ActionPointCost, Is.EqualTo(1));
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(4, 0))).Accepted, Is.True);
            Assert.That(encounter.Spatial.GetPosition(player), Is.EqualTo(TestWorld.At(4, 0)));
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(1));

            // Unused distance is lost: the last point covers one budget, not what the first move left over.
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(4, 7))).Reason, Is.EqualTo(RejectReason.NotEnoughActionPoints));
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(4, 5))).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(0));

            encounter = startDuel(out player);
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(13, 0))).Reason, Is.EqualTo(RejectReason.InsufficientMovement));
            Assert.That(encounter.PlanMove(player, TestWorld.At(10, 0), plan), Is.EqualTo(RejectReason.None));
            Assert.That(plan.ActionPointCost, Is.EqualTo(2));
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(10, 0))).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(0));
        }

        [Test]
        public void AtZeroPointsTheTurnStaysOpenUntilEndTurn()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(3, 0))).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(3, 3))).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.Attack(player, enemy)).Reason, Is.EqualTo(RejectReason.NotEnoughActionPoints));
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(3, 4))).Reason, Is.EqualTo(RejectReason.NotEnoughActionPoints));
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(player));
            Assert.That(encounter.Execute(Command.EndTurn(player)).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(enemy));
        }

        [Test]
        public void UnitsWithoutActionPointsAreRejected()
        {
            var encounter = new Encounter(TestWorld.OpenMap(4, 4));
            UnitSetup idle = TestWorld.Soldier(Team.Player, 0, 0);
            idle.ActionPoints = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => encounter.AddUnit(idle));
            Assert.That(encounter.Units.Count, Is.EqualTo(0), "nothing is registered");
        }

        [Test]
        public void RejectedMoveChangesNothing()
        {
            var setup = TestWorld.OpenMap(16, 3);
            setup.Obstacles[1 * 16 + 5] = CellObstacle.Wall;
            var encounter = new Encounter(setup);
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 1));
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 15, 2));
            encounter.Start();
            int eventsBefore = encounter.Events.Count;

            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(5, 1))).Reason, Is.EqualTo(RejectReason.DestinationBlocked));
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(14, 1))).Reason, Is.EqualTo(RejectReason.InsufficientMovement));
            Assert.That(encounter.Execute(Command.Move(player, new Vector2(-5f, 0f))).Reason, Is.EqualTo(RejectReason.DestinationBlocked));
            Assert.That(encounter.Events.Count, Is.EqualTo(eventsBefore));
            Assert.That(encounter.Spatial.GetPosition(player), Is.EqualTo(TestWorld.At(0, 1)));
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(2));
        }

        [Test]
        public void FarDestinationsAreTooFarButSealedOnesAreUnreachable()
        {
            // Two sealed rooms, one within the field's 13 m range and one beyond it, on an open map wider than the range.
            var setup = TestWorld.OpenMap(30, 7);
            foreach (int roomX in new[] { 5, 25 })
                for (int y = 2; y <= 4; y++)
                    for (int x = roomX - 1; x <= roomX + 1; x++)
                        if (x != roomX || y != 3) setup.Obstacles[y * 30 + x] = CellObstacle.Wall;
            var encounter = new Encounter(setup);
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 3));
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 29, 6));
            encounter.Start();

            var plan = new MovePlan();
            Assert.That(encounter.PlanMove(player, TestWorld.At(20, 3), plan), Is.EqualTo(RejectReason.InsufficientMovement));
            Assert.That(float.IsPositiveInfinity(plan.Cost), Is.True, "beyond the field's range");
            Assert.That(encounter.Spatial.FieldIsTruncated, Is.True);
            Assert.That(encounter.PlanMove(player, TestWorld.At(5, 3), plan), Is.EqualTo(RejectReason.Unreachable), "sealed, within range");
            Assert.That(encounter.PlanMove(player, TestWorld.At(25, 3), plan), Is.EqualTo(RejectReason.Unreachable), "sealed, beyond range");
        }

        [Test]
        public void AUnitInTheOnlyDoorwayMakesTheRoomUnreachable()
        {
            // A room at x 4-6, y 3-5 whose only way in is (5, 3), on an open map wider than the field's range.
            var setup = TestWorld.OpenMap(30, 9);
            for (int y = 3; y <= 5; y++)
                for (int x = 4; x <= 6; x++)
                    if (!(x == 5 && y == 4) && !(x == 5 && y == 3)) setup.Obstacles[y * 30 + x] = CellObstacle.Wall;

            Encounter withGuard = new Encounter(setup);
            UnitId player = withGuard.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            withGuard.AddUnit(TestWorld.Soldier(Team.Enemy, 5, 3)); // in the doorway
            withGuard.Start();
            var plan = new MovePlan();
            Assert.That(withGuard.PlanMove(player, TestWorld.At(5, 4), plan), Is.EqualTo(RejectReason.Unreachable));

            Encounter withoutGuard = new Encounter(setup);
            player = withoutGuard.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            withoutGuard.AddUnit(TestWorld.Soldier(Team.Enemy, 29, 8));
            withoutGuard.Start();
            Assert.That(withoutGuard.PlanMove(player, TestWorld.At(5, 4), plan), Is.EqualTo(RejectReason.None));
        }

        [Test]
        public void MovesNeverWalkFartherThanTheyArePaidFor()
        {
            // Random maps with 20% walls and a 6 m budget, every cell centre as a destination. No path may be longer
            // than the distance its action points buy. The path follows the field's gradient, so it also stays close
            // to the field value that priced it: within a few centimetres in almost every case.
            var random = new Random(4243);
            int accepted = 0, overPrice = 0;
            float worstExcess = 0f;
            for (int map = 0; map < 40; map++)
            {
                var setup = TestWorld.OpenMap(20, 16);
                for (int i = 0; i < setup.Obstacles.Length; i++)
                    if (random.NextDouble() < 0.2) setup.Obstacles[i] = CellObstacle.Wall;
                int start = random.Next(setup.Obstacles.Length);
                setup.Obstacles[start] = CellObstacle.None;
                setup.Obstacles[setup.Obstacles.Length - 1] = CellObstacle.None;
                var encounter = new Encounter(setup);
                UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, start % 20, start / 20));
                encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 19, 15));
                encounter.Start();
                var plan = new MovePlan();
                for (int cell = 0; cell < setup.Obstacles.Length; cell++)
                {
                    if (encounter.PlanMove(player, TestWorld.At(cell % 20, cell / 20), plan) != RejectReason.None) continue;
                    accepted++;
                    Assert.That(plan.PathLength, Is.LessThanOrEqualTo(plan.ActionPointCost * 6f + 1e-3f), $"map {map}, cell {cell}");
                    float excess = plan.PathLength - plan.Cost;
                    if (excess > 1e-3f) overPrice++;
                    worstExcess = Math.Max(worstExcess, excess);
                }
            }
            TestContext.WriteLine($"{accepted} moves; {overPrice} longer than their field value; worst by {worstExcess:F3} m");
            Assert.That(accepted, Is.GreaterThan(3000));
            Assert.That(overPrice, Is.LessThan(accepted / 100), "fewer than 1% of paths exceed the field value");
            Assert.That(worstExcess, Is.LessThan(0.5f));
        }

        [Test]
        public void ANaNMovementBudgetRejectsMovesInsteadOfFailing()
        {
            var encounter = new Encounter(TestWorld.OpenMap(6, 6));
            UnitSetup broken = TestWorld.Soldier(Team.Player, 0, 0);
            broken.MovementBudget = float.NaN;
            UnitId player = encounter.AddUnit(broken);
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 5, 5));
            Assert.DoesNotThrow(() => encounter.Start());
            Assert.That(encounter.Movement.GetBudget(player), Is.EqualTo(0f));
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(1, 0))).Reason, Is.EqualTo(RejectReason.InsufficientMovement));
        }

        [Test]
        public void AttackNeedsLineOfSightAndEndsTheTurn()
        {
            var setup = TestWorld.Map(
                ".........",
                ".........",
                "....#....",
                ".........",
                ".........");
            var encounter = new Encounter(setup);
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 2));
            UnitId hidden = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 8, 2));
            UnitId visible = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 8, 4));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Attack(player, hidden)).Reason, Is.EqualTo(RejectReason.NoLineOfSight));
            Assert.That(encounter.Execute(Command.Attack(player, player)).Reason, Is.EqualTo(RejectReason.TargetNotHostile));
            Assert.That(encounter.Execute(Command.Attack(player, visible)).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnit, Is.Not.EqualTo(player));
            Assert.That(encounter.Execute(Command.Attack(player, visible)).Reason, Is.EqualTo(RejectReason.NotActiveUnit));
        }

        [Test]
        public void CanShootThroughGlassButNotWalkThrough()
        {
            var setup = TestWorld.Map(
                "....g....",
                "....g....",
                "....g....",
                "....g....",
                "....g....");
            var encounter = new Encounter(setup);
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 2, 2));
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 6, 2));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(4, 2))).Reason, Is.EqualTo(RejectReason.DestinationBlocked));
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(5, 2))).Reason, Is.EqualTo(RejectReason.Unreachable));
            Assert.That(encounter.Execute(Command.Attack(player, enemy)).Accepted, Is.True);
        }

        [Test]
        public void EveryFiredRoundIsAccountedFor([Range(1, 20)] int seed)
        {
            var setup = TestWorld.OpenMap(12, 12);
            setup.Seed = (ulong)seed;
            var encounter = new Encounter(setup);
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 2, 2));
            UnitSetup tough = TestWorld.Soldier(Team.Enemy, 6, 5);
            tough.MaxHealth = 1000;
            UnitId enemy = encounter.AddUnit(tough);
            encounter.Start();

            encounter.Execute(Command.Attack(player, enemy));
            int fired = 30 - encounter.Resources.GetCarried(player, 0);
            int embedded = countEvents(encounter, EventKind.AmmoEmbedded);
            int scattered = countEvents(encounter, EventKind.AmmoScattered, sumValues: true);
            int lost = countEvents(encounter, EventKind.AmmoLost);
            int inPiles = 0;
            for (int i = 0; i < encounter.Resources.PileCount; i++) inPiles += encounter.Resources.GetPileAt(i).Count;

            Assert.That(fired, Is.EqualTo(3));
            Assert.That(embedded + scattered + lost, Is.EqualTo(fired));
            Assert.That(encounter.Resources.GetEmbedded(enemy, 0), Is.EqualTo(embedded));
            Assert.That(inPiles, Is.EqualTo(scattered));
            Assert.That(countEvents(encounter, EventKind.RoundHit) + countEvents(encounter, EventKind.RoundMissed), Is.EqualTo(fired));
        }

        [Test]
        public void DefeatReleasesEmbeddedAmmoAndEndsTheEncounter()
        {
            var setup = TestWorld.OpenMap(8, 8);
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup shooter = TestWorld.Soldier(Team.Player, 0, 0);
            shooter.Accuracy = 100;
            UnitId player = encounter.AddUnit(shooter);
            UnitSetup fragile = TestWorld.Soldier(Team.Enemy, 3, 0);
            fragile.MaxHealth = 2;
            UnitId enemy = encounter.AddUnit(fragile);
            encounter.Start();

            Assert.That(encounter.Execute(Command.Attack(player, enemy)).Accepted, Is.True);
            Assert.That(encounter.Combat.IsDefeated(enemy), Is.True);
            Assert.That(encounter.Resources.GetCarried(player, 0), Is.EqualTo(29), "the burst stops once the target is down");
            Assert.That(encounter.Resources.GetEmbedded(enemy, 0), Is.EqualTo(0));
            Assert.That(encounter.Resources.PileCount, Is.EqualTo(1));
            Assert.That(encounter.Resources.GetPileAt(0).Position, Is.EqualTo(TestWorld.At(3, 0)));
            Assert.That(encounter.Flow.Phase, Is.EqualTo(EncounterPhase.Ended));
            Assert.That(encounter.Flow.Winner, Is.EqualTo(Team.Player));
            Assert.That(encounter.Execute(Command.EndTurn(player)).Reason, Is.EqualTo(RejectReason.EncounterOver));
        }

        [Test]
        public void PickUpRequiresProximity()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            int near = encounter.Resources.AddToPile(0, 5, TestWorld.At(1, 0), PileOrigin.Placed);
            int alsoNear = encounter.Resources.AddToPile(0, 2, TestWorld.At(0, 1), PileOrigin.Placed);
            int far = encounter.Resources.AddToPile(0, 4, TestWorld.At(5, 5), PileOrigin.Placed);
            encounter.Start();

            Assert.That(encounter.Execute(Command.PickUp(player, far)).Reason, Is.EqualTo(RejectReason.OutOfPickupRange));
            Assert.That(encounter.Execute(Command.PickUp(player, near)).Accepted, Is.True);
            Assert.That(encounter.Resources.GetCarried(player, 0), Is.EqualTo(35));
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(1), "a pickup costs a point");
            Assert.That(encounter.Execute(Command.PickUp(player, near)).Reason, Is.EqualTo(RejectReason.PileNotFound));
            Assert.That(encounter.Execute(Command.PickUp(player, alsoNear)).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.PickUp(player, far)).Reason, Is.EqualTo(RejectReason.NotEnoughActionPoints));
        }

        [Test]
        public void NearbyLandingRoundsShareOnePile()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            int first = encounter.Resources.AddToPile(0, 1, new Vector2(3f, 3f), PileOrigin.Scattered);
            int second = encounter.Resources.AddToPile(0, 1, new Vector2(3.5f, 3f), PileOrigin.Scattered);
            int elsewhere = encounter.Resources.AddToPile(0, 1, new Vector2(6f, 6f), PileOrigin.Scattered);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(elsewhere, Is.Not.EqualTo(first));
            Assert.That(encounter.Resources.PileCount, Is.EqualTo(2));
        }

        [Test]
        public void EnteringAZoneOfControlTriggersAReactionThatStopsTheMove()
        {
            var setup = TestWorld.OpenMap(12, 5);
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup runner = TestWorld.Soldier(Team.Player, 0, 2);
            runner.MovementBudget = 12f;
            UnitId player = encounter.AddUnit(runner);
            UnitSetup guard = TestWorld.Soldier(Team.Enemy, 6, 3);
            guard.Accuracy = 100;
            guard.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            UnitId enemy = encounter.AddUnit(guard);
            encounter.Start();

            var plan = new MovePlan();
            Assert.That(encounter.PlanMove(player, TestWorld.At(11, 2), plan), Is.EqualTo(RejectReason.None));
            Assert.That(plan.Interrupts.Count, Is.EqualTo(1), "the preview warns about the reaction");

            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(11, 2))).Accepted, Is.True);
            Vector2 stoppedAt = encounter.Spatial.GetPosition(player);
            Assert.That(stoppedAt.X, Is.EqualTo(6.5f - MathF.Sqrt(3f)).Within(1e-3f));
            Assert.That(Vector2.Distance(stoppedAt, TestWorld.At(6, 3)), Is.EqualTo(2f).Within(1e-3f));
            Assert.That(countEvents(encounter, EventKind.MoveInterrupted), Is.EqualTo(1));
            Assert.That(encounter.Combat.GetHealth(player), Is.EqualTo(4));
            Assert.That(encounter.Resources.GetCarried(enemy, 0), Is.EqualTo(27));
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(1));
            Assert.That(encounter.Flow.HasReaction(enemy), Is.False);

            // Being stopped does not pin the unit: its remaining point still moves it, and the guard has no reaction left.
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(2, 2))).Accepted, Is.True);
            Assert.That(countEvents(encounter, EventKind.RoundHit), Is.EqualTo(3));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ACircleZoneOnlyWarnsAndReactsWhereItsOwnerSeesTheEntry(bool wallBetween)
        {
            var setup = wallBetween
                ? TestWorld.Map(
                    "..........",
                    "....#.....",
                    "....#.....",
                    "....#.....",
                    "..........")
                : TestWorld.OpenMap(10, 5);
            var encounter = new Encounter(setup);
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 2, 4));
            UnitSetup guard = TestWorld.Soldier(Team.Enemy, 5, 2);
            guard.Zone = new ZoneSetup { Radius = 3.5f, HalfAngleDegrees = 180f };
            encounter.AddUnit(guard);
            encounter.Start();

            // The walk down x = 2.5 enters the circle at about (2.5, 4.3), which the wall hides from the guard.
            var plan = new MovePlan();
            Assert.That(encounter.PlanMove(player, TestWorld.At(2, 0), plan), Is.EqualTo(RejectReason.None));
            Assert.That(plan.Interrupts.Count, Is.EqualTo(wallBetween ? 0 : 1));
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(2, 0))).Accepted, Is.True);
            Assert.That(countEvents(encounter, EventKind.AttackStarted), Is.EqualTo(wallBetween ? 0 : 1));
        }

        [Test]
        public void AReactionFiresOncePerRoundAndReturnsOnTheOwnersTurn()
        {
            var setup = TestWorld.OpenMap(12, 5);
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup first = TestWorld.Soldier(Team.Player, 0, 2);
            first.MovementBudget = 12f;
            UnitId firstRunner = encounter.AddUnit(first);
            UnitSetup second = TestWorld.Soldier(Team.Player, 0, 1);
            second.MovementBudget = 12f;
            second.Initiative = 50;
            UnitId secondRunner = encounter.AddUnit(second);
            UnitSetup guard = TestWorld.Soldier(Team.Enemy, 6, 2);
            guard.Accuracy = 100;
            guard.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            UnitId enemy = encounter.AddUnit(guard);
            encounter.Start();

            Assert.That(encounter.Execute(Command.Move(firstRunner, TestWorld.At(11, 2))).Accepted, Is.True);
            Assert.That(countEvents(encounter, EventKind.MoveInterrupted), Is.EqualTo(1));
            Assert.That(encounter.Execute(Command.EndTurn(firstRunner)).Accepted, Is.True);

            var plan = new MovePlan();
            Assert.That(encounter.PlanMove(secondRunner, TestWorld.At(11, 1), plan), Is.EqualTo(RejectReason.None));
            Assert.That(plan.Interrupts.Count, Is.EqualTo(0), "the preview omits a guard that has already reacted");
            Assert.That(encounter.Execute(Command.Move(secondRunner, TestWorld.At(11, 1))).Accepted, Is.True);
            Assert.That(encounter.Spatial.GetPosition(secondRunner), Is.EqualTo(TestWorld.At(11, 1)));
            Assert.That(countEvents(encounter, EventKind.RoundHit), Is.EqualTo(3), "only the first runner was shot");

            Assert.That(encounter.Execute(Command.EndTurn(secondRunner)).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(enemy));
            Assert.That(encounter.Flow.HasReaction(enemy), Is.True);
        }

        [Test]
        public void SentryConeIgnoresMovementOutOfSight()
        {
            var setup = TestWorld.Map(
                "............",
                "............",
                "....######..",
                "............",
                "............");
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup runner = TestWorld.Soldier(Team.Player, 0, 0);
            runner.MovementBudget = 12f;
            UnitId player = encounter.AddUnit(runner);
            UnitSetup sentry = TestWorld.Soldier(Team.Enemy, 7, 4);
            sentry.Facing = new Vector2(0f, -1f);
            sentry.Zone = new ZoneSetup { Radius = 6f, HalfAngleDegrees = 60f, RequiresLineOfSight = true };
            encounter.AddUnit(sentry);
            encounter.Start();

            var plan = new MovePlan();
            Assert.That(encounter.PlanMove(player, TestWorld.At(11, 0), plan), Is.EqualTo(RejectReason.None));
            Assert.That(plan.Interrupts.Count, Is.EqualTo(0), "the wall hides the whole route from the sentry");
        }

        [Test]
        public void MovementStatusAppliesEachTurnUntilItExpires()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Status.Apply(player, StatusKind.Movement, 2f, 2, encounter.Events);
            encounter.Start();

            Assert.That(encounter.Movement.GetBudget(player), Is.EqualTo(8f));
            encounter.Execute(Command.EndTurn(player));
            encounter.Execute(Command.EndTurn(enemy));
            Assert.That(encounter.Movement.GetBudget(player), Is.EqualTo(8f));
            encounter.Execute(Command.EndTurn(player));
            Assert.That(countEvents(encounter, EventKind.StatusExpired), Is.EqualTo(1));
            encounter.Execute(Command.EndTurn(enemy));
            Assert.That(encounter.Movement.GetBudget(player), Is.EqualTo(6f));
        }

        [Test]
        public void SameSeedAndCommandsGiveIdenticalEventLogs()
        {
            Encounter a = playScript(99UL), b = playScript(99UL);
            Assert.That(a.Events.Count, Is.EqualTo(b.Events.Count));
            for (int i = 0; i < a.Events.Count; i++)
            {
                GameEvent x = a.Events[i], y = b.Events[i];
                Assert.That(x.Kind == y.Kind && x.Unit == y.Unit && x.Other == y.Other && x.Position == y.Position
                            && x.Value == y.Value && x.AmmoType == y.AmmoType && x.PileId == y.PileId, Is.True, $"event {i} differs");
            }
        }

        private static Encounter playScript(ulong seed)
        {
            var setup = TestWorld.OpenMap(12, 12);
            setup.Seed = seed;
            var encounter = new Encounter(setup);
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 1, 1));
            UnitSetup enemySetup = TestWorld.Soldier(Team.Enemy, 9, 9);
            enemySetup.MaxHealth = 1000;
            UnitId enemy = encounter.AddUnit(enemySetup);
            encounter.Start();
            for (int round = 0; round < 3; round++)
            {
                encounter.Execute(Command.Move(player, encounter.Spatial.GetPosition(player) + new Vector2(1f, 1f)));
                encounter.Execute(Command.Attack(player, enemy)); // attacking ends the turn
                encounter.Execute(Command.Attack(enemy, player));
            }
            return encounter;
        }

        [Test]
        public void GlancingRoundsDealHalfDamage()
        {
            var setup = TestWorld.OpenMap(8, 8);
            setup.HitTuning.Maximum = 100;
            setup.FateTuning.HitEmbedPercent = 0;
            setup.FateTuning.HitScatterPercent = 100;
            var encounter = new Encounter(setup);
            UnitSetup shooter = TestWorld.Soldier(Team.Player, 0, 0);
            shooter.Accuracy = 100;
            UnitId player = encounter.AddUnit(shooter);
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 3, 0));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Attack(player, enemy)).Accepted, Is.True);
            Assert.That(countEvents(encounter, EventKind.RoundHit), Is.EqualTo(3));
            Assert.That(countEvents(encounter, EventKind.RoundHit, sumValues: true), Is.EqualTo(3), "each glance deals half of 2");
            Assert.That(encounter.Combat.GetHealth(enemy), Is.EqualTo(7));
        }

        [Test]
        public void RoundsDestroyedOnImpactDealFullDamage()
        {
            var setup = TestWorld.OpenMap(8, 8);
            setup.HitTuning.Maximum = 100;
            setup.FateTuning.HitEmbedPercent = 0;
            setup.FateTuning.HitScatterPercent = 0;
            var encounter = new Encounter(setup);
            UnitSetup shooter = TestWorld.Soldier(Team.Player, 0, 0);
            shooter.Accuracy = 100;
            UnitId player = encounter.AddUnit(shooter);
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 3, 0));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Attack(player, enemy)).Accepted, Is.True);
            Assert.That(countEvents(encounter, EventKind.AmmoLost), Is.EqualTo(3));
            Assert.That(encounter.Combat.GetHealth(enemy), Is.EqualTo(4));
        }

        private static UnitSetup deadeyeEnemy(int x, int y)
        {
            UnitSetup enemy = TestWorld.Soldier(Team.Enemy, x, y);
            enemy.Accuracy = 100;
            enemy.Initiative = 200; // acts before the players
            return enemy;
        }

        [Test]
        public void PlayerUnitsAreDownedNotDefeated()
        {
            var setup = TestWorld.OpenMap(8, 8);
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup fragile = TestWorld.Soldier(Team.Player, 0, 0);
            fragile.MaxHealth = 2;
            fragile.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            UnitId downedPlayer = encounter.AddUnit(fragile);
            UnitId otherPlayer = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 7));
            UnitId enemy = encounter.AddUnit(deadeyeEnemy(3, 0));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Attack(enemy, downedPlayer)).Accepted, Is.True);
            Assert.That(encounter.Combat.IsDowned(downedPlayer), Is.True);
            Assert.That(encounter.Combat.IsDefeated(downedPlayer), Is.False);
            Assert.That(countEvents(encounter, EventKind.UnitDowned), Is.EqualTo(1));
            Assert.That(encounter.Spatial.OccupiesSpace(downedPlayer), Is.True, "the body still blocks movement");
            Assert.That(encounter.Spatial.HasActiveZone(downedPlayer), Is.False);
            Assert.That(encounter.Resources.GetEmbedded(downedPlayer, 0), Is.EqualTo(1));
            Assert.That(countEvents(encounter, EventKind.AmmoReleased), Is.EqualTo(0));
            Assert.That(encounter.Flow.Phase, Is.EqualTo(EncounterPhase.AwaitingCommand));
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(otherPlayer), "the downed unit's turn is skipped");

            Assert.That(encounter.Execute(Command.EndTurn(otherPlayer)).Accepted, Is.True);
            Assert.That(encounter.Flow.TurnOrder, Has.No.Member(downedPlayer));
            Assert.That(encounter.Execute(Command.Attack(enemy, downedPlayer)).Reason, Is.EqualTo(RejectReason.InvalidTarget));
        }

        [Test]
        public void AllPlayersDownedLosesTheEncounter()
        {
            var setup = TestWorld.OpenMap(8, 8);
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup fragile = TestWorld.Soldier(Team.Player, 0, 0);
            fragile.MaxHealth = 2;
            UnitId player = encounter.AddUnit(fragile);
            UnitId enemy = encounter.AddUnit(deadeyeEnemy(3, 0));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Attack(enemy, player)).Accepted, Is.True);
            Assert.That(encounter.Flow.Phase, Is.EqualTo(EncounterPhase.Ended));
            Assert.That(encounter.Flow.Winner, Is.EqualTo(Team.Enemy));
        }

        [Test]
        public void DefeatingTheObjectiveWinsWithEnemiesLeft()
        {
            var setup = TestWorld.OpenMap(8, 8);
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup shooter = TestWorld.Soldier(Team.Player, 0, 0);
            shooter.Accuracy = 100;
            UnitId player = encounter.AddUnit(shooter);
            UnitSetup boss = TestWorld.Soldier(Team.Enemy, 3, 0);
            boss.MaxHealth = 2;
            boss.IsObjective = true;
            UnitId bossId = encounter.AddUnit(boss);
            UnitId minion = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Attack(player, bossId)).Accepted, Is.True);
            Assert.That(encounter.Combat.IsDefeated(minion), Is.False);
            Assert.That(encounter.Flow.Phase, Is.EqualTo(EncounterPhase.Ended));
            Assert.That(encounter.Flow.Winner, Is.EqualTo(Team.Player));
        }

        [Test]
        public void PlayerObjectiveUnitsAreRejected()
        {
            var encounter = new Encounter(TestWorld.OpenMap(4, 4));
            UnitSetup escort = TestWorld.Soldier(Team.Player, 0, 0);
            escort.IsObjective = true;
            Assert.Throws<ArgumentException>(() => encounter.AddUnit(escort));
            Assert.That(encounter.Units.Count, Is.EqualTo(0), "nothing is registered");
        }

        [Test]
        public void KillReleasePickupRefundsItsPoint()
        {
            var setup = TestWorld.OpenMap(8, 8);
            TestWorld.MakeEveryRoundHitAndEmbed(setup);
            var encounter = new Encounter(setup);
            UnitSetup shooter = TestWorld.Soldier(Team.Player, 0, 0);
            shooter.Accuracy = 100;
            UnitId player = encounter.AddUnit(shooter);
            UnitSetup fragile = TestWorld.Soldier(Team.Enemy, 3, 0);
            fragile.MaxHealth = 2;
            encounter.AddUnit(fragile);
            UnitId survivor = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Start();

            Assert.That(encounter.Execute(Command.Attack(player, encounter.Units.GetIdAtSlot(1))).Accepted, Is.True);
            Assert.That(encounter.Resources.PileCount, Is.EqualTo(1));
            AmmoPile released = encounter.Resources.GetPileAt(0);
            Assert.That(released.Origin, Is.EqualTo(PileOrigin.Released));
            Assert.That(encounter.Execute(Command.EndTurn(survivor)).Accepted, Is.True);

            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(2, 0))).Accepted, Is.True);
            int pointEventsBefore = countEvents(encounter, EventKind.ActionPointsChanged);
            Assert.That(encounter.Execute(Command.PickUp(player, released.Id)).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(1), "the point is refunded");
            Assert.That(countEvents(encounter, EventKind.ActionPointsChanged) - pointEventsBefore, Is.EqualTo(2));

            // The refund still needs a point to spend first.
            int another = encounter.Resources.AddToPile(0, 3, TestWorld.At(2, 1), PileOrigin.Released);
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(1, 1))).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.PickUp(player, another)).Reason, Is.EqualTo(RejectReason.NotEnoughActionPoints));
        }

        [Test]
        public void PilesOfDifferentOriginsDoNotMerge()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            Vector2 spot = TestWorld.At(3, 3);
            int placed = encounter.Resources.AddToPile(0, 1, spot, PileOrigin.Placed);
            int scattered = encounter.Resources.AddToPile(0, 1, spot, PileOrigin.Scattered);
            int released = encounter.Resources.AddToPile(0, 1, spot, PileOrigin.Released);
            Assert.That(new[] { placed, scattered, released }, Is.Unique);
            Assert.That(encounter.Resources.AddToPile(0, 1, spot, PileOrigin.Scattered), Is.EqualTo(scattered));
            Assert.That(encounter.Resources.PileCount, Is.EqualTo(3));
        }

        [Test]
        public void TradingIsFreeAndNeedsTheReceiverInsideTheGiversZone()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            UnitSetup giverSetup = TestWorld.Soldier(Team.Player, 0, 0);
            giverSetup.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            giverSetup.Initiative = 150; // acts before its allies
            UnitId giver = encounter.AddUnit(giverSetup);
            UnitId near = encounter.AddUnit(TestWorld.Soldier(Team.Player, 1, 0));
            UnitId far = encounter.AddUnit(TestWorld.Soldier(Team.Player, 5, 0));
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Start();
            Assert.That(encounter.Flow.ActiveUnit, Is.EqualTo(giver));

            Assert.That(encounter.Execute(Command.Trade(giver, near, 5)).Accepted, Is.True);
            Assert.That(encounter.Resources.GetCarried(giver, 0), Is.EqualTo(25));
            Assert.That(encounter.Resources.GetCarried(near, 0), Is.EqualTo(35));
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(2), "trading is free");
            Assert.That(countEvents(encounter, EventKind.AmmoTraded), Is.EqualTo(1));

            Assert.That(encounter.Execute(Command.Trade(giver, far, 1)).Reason, Is.EqualTo(RejectReason.ReceiverOutsideZone));
            Assert.That(encounter.Execute(Command.Trade(giver, enemy, 1)).Reason, Is.EqualTo(RejectReason.TargetNotFriendly));
            Assert.That(encounter.Execute(Command.Trade(giver, giver, 1)).Reason, Is.EqualTo(RejectReason.InvalidTarget));
            Assert.That(encounter.Execute(Command.Trade(giver, near, 0)).Reason, Is.EqualTo(RejectReason.InvalidAmount));
            Assert.That(encounter.Execute(Command.Trade(giver, near, 26)).Reason, Is.EqualTo(RejectReason.InvalidAmount));

            // With no points left the turn stays open, and trading still works.
            Assert.That(encounter.Execute(Command.Move(giver, TestWorld.At(0, 2))).Accepted, Is.True);
            Assert.That(encounter.Execute(Command.Move(giver, TestWorld.At(1, 1))).Accepted, Is.True);
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(0));
            Assert.That(encounter.Execute(Command.Trade(giver, near, 5)).Accepted, Is.True);
            Assert.That(encounter.Resources.GetCarried(near, 0), Is.EqualTo(40));
        }

        [Test]
        public void ValidateGivesTheVerdictExecuteWouldWithoutChangingAnything()
        {
            var setup = TestWorld.Map(
                ".........",
                ".........",
                "....#....",
                ".........",
                ".........");
            var encounter = new Encounter(setup);
            UnitSetup playerSetup = TestWorld.Soldier(Team.Player, 0, 2);
            playerSetup.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            playerSetup.Initiative = 150; // acts before its ally
            UnitId player = encounter.AddUnit(playerSetup);
            UnitId farAlly = encounter.AddUnit(TestWorld.Soldier(Team.Player, 3, 0));
            UnitId hidden = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 8, 2));
            int farPile = encounter.Resources.AddToPile(0, 3, TestWorld.At(6, 0), PileOrigin.Placed);
            encounter.Start();
            int eventsBefore = encounter.Events.Count;

            var rejected = new[]
            {
                (Command.Attack(player, hidden), RejectReason.NoLineOfSight),
                (Command.Attack(player, farAlly), RejectReason.TargetNotHostile),
                (Command.PickUp(player, farPile), RejectReason.OutOfPickupRange),
                (Command.Trade(player, farAlly, 1), RejectReason.ReceiverOutsideZone),
                (Command.Move(player, TestWorld.At(4, 2)), RejectReason.DestinationBlocked),
                (Command.EndTurn(farAlly), RejectReason.NotActiveUnit),
            };
            foreach ((Command command, RejectReason reason) in rejected)
                Assert.That(encounter.Validate(command), Is.EqualTo(reason), command.Kind.ToString());
            Assert.That(encounter.Validate(Command.Move(player, TestWorld.At(2, 0))), Is.EqualTo(RejectReason.None));
            Assert.That(encounter.Events.Count, Is.EqualTo(eventsBefore), "validating records nothing");
            Assert.That(encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(2));

            foreach ((Command command, RejectReason reason) in rejected)
                Assert.That(encounter.Execute(command).Reason, Is.EqualTo(reason), command.Kind.ToString());
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(2, 0))).Accepted, Is.True);
        }

        [Test]
        public void ValidateReportsAnAttackWithoutAmmo()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            UnitSetup unarmed = TestWorld.Soldier(Team.Player, 0, 0);
            unarmed.StartingAmmo = 0;
            UnitId player = encounter.AddUnit(unarmed);
            UnitId enemy = encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 4, 0));
            encounter.Start();
            Assert.That(encounter.Validate(Command.Attack(player, enemy)), Is.EqualTo(RejectReason.NoAmmo));
            Assert.That(encounter.Execute(Command.Attack(player, enemy)).Reason, Is.EqualTo(RejectReason.NoAmmo));
        }

        [Test]
        public void AFreeActionIsATradeToAnAllyInTheZoneOrAFreeAbility()
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 8));
            UnitSetup giverSetup = TestWorld.Soldier(Team.Player, 0, 0);
            giverSetup.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            giverSetup.Initiative = 150;
            UnitId giver = encounter.AddUnit(giverSetup);
            encounter.AddUnit(TestWorld.Soldier(Team.Player, 5, 0));
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Start();

            Assert.That(encounter.HasFreeAction(giver), Is.False, "the ally is outside the giver's zone");
            Assert.That(encounter.Execute(Command.Move(giver, TestWorld.At(4, 0))).Accepted, Is.True);
            Assert.That(encounter.HasFreeAction(giver), Is.True, "the ally is now inside it");

            var setup = TestWorld.OpenMap(8, 8);
            setup.Abilities = new[] { new AbilityDefinition { ActionPointCost = 0, Targeting = AbilityTargeting.Self } };
            encounter = new Encounter(setup);
            UnitSetup gifted = TestWorld.Soldier(Team.Player, 0, 0);
            gifted.Abilities = new[] { 0 };
            UnitId player = encounter.AddUnit(gifted);
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 7, 7));
            encounter.Start();
            Assert.That(encounter.HasFreeAction(player), Is.True);
        }

        // A 12 x 9 map with a wall along x = 6 from y = 0 to y = 5, and one unit at (2, 2) with the given body radius.
        private static Encounter besideAWall(float bodyRadius, int x, int y, out UnitId player)
        {
            var encounter = new Encounter(TestWorld.Map(
                "............",
                "............",
                "............",
                "......#.....",
                "......#.....",
                "......#.....",
                "......#.....",
                "......#.....",
                "......#....."));
            UnitSetup body = TestWorld.Soldier(Team.Player, x, y);
            body.BodyRadius = bodyRadius;
            body.MovementBudget = 10f;
            player = encounter.AddUnit(body);
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 11, 8));
            encounter.Start();
            return encounter;
        }

        // Closest approach of a path to the square of cell (x, y), sampled every 5 cm.
        private static float closestApproach(System.Collections.Generic.List<Vector2> path, int cellX, int minY, int maxY)
        {
            float closest = float.PositiveInfinity;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                int samples = Math.Max(1, (int)(Vector2.Distance(path[i], path[i + 1]) / 0.05f));
                for (int k = 0; k <= samples; k++)
                {
                    Vector2 point = Vector2.Lerp(path[i], path[i + 1], k / (float)samples);
                    float gapX = Math.Max(0f, Math.Max(cellX - point.X, point.X - (cellX + 1)));
                    float gapY = Math.Max(0f, Math.Max(minY - point.Y, point.Y - (maxY + 1)));
                    closest = Math.Min(closest, MathF.Sqrt(gapX * gapX + gapY * gapY));
                }
            }
            return closest;
        }

        [Test]
        public void ABodyRadiusKeepsAUnitClearOfWalls()
        {
            Encounter encounter = besideAWall(0.6f, 2, 2, out UnitId player);
            Assert.That(encounter.Validate(Command.Move(player, TestWorld.At(5, 2))), Is.EqualTo(RejectReason.DestinationBlocked));
            var plan = new MovePlan();
            Assert.That(encounter.PlanMove(player, TestWorld.At(10, 2), plan), Is.EqualTo(RejectReason.None));
            Assert.That(closestApproach(plan.Path, 6, 0, 5), Is.GreaterThanOrEqualTo(0.5f), "the path stays clear of the wall");

            encounter = besideAWall(0f, 2, 2, out player);
            Assert.That(encounter.Validate(Command.Move(player, TestWorld.At(5, 2))), Is.EqualTo(RejectReason.None));
            Assert.That(encounter.PlanMove(player, TestWorld.At(10, 2), plan), Is.EqualTo(RejectReason.None));
            Assert.That(closestApproach(plan.Path, 6, 0, 5), Is.LessThan(0.1f), "a point unit hugs the corner");
        }

        [Test]
        public void AUnitPlacedInsideItsClearanceCanStillWalkOut()
        {
            Encounter encounter = besideAWall(0.6f, 5, 2, out UnitId player);
            Assert.That(encounter.Execute(Command.Move(player, TestWorld.At(2, 2))).Accepted, Is.True);
        }

        [TestCase(0f, RejectReason.None)]
        [TestCase(0.6f, RejectReason.DestinationBlocked)]
        public void BodiesKeepUnitsApart(float bodyRadius, RejectReason nextToTheOther)
        {
            var encounter = new Encounter(TestWorld.OpenMap(8, 3));
            UnitSetup mover = TestWorld.Soldier(Team.Player, 1, 1), other = TestWorld.Soldier(Team.Enemy, 4, 1);
            mover.BodyRadius = other.BodyRadius = bodyRadius;
            UnitId player = encounter.AddUnit(mover);
            encounter.AddUnit(other);
            encounter.Start();
            Assert.That(encounter.Validate(Command.Move(player, TestWorld.At(3, 1))), Is.EqualTo(nextToTheOther));
            Assert.That(encounter.Validate(Command.Move(player, TestWorld.At(2, 1))), Is.EqualTo(RejectReason.None));
        }

        [TestCase(0.4f, RejectReason.None)]
        [TestCase(0.6f, RejectReason.Unreachable)]
        public void ANarrowGapLetsSlimUnitsThroughButNotWideOnes(float bodyRadius, RejectReason verdict)
        {
            var encounter = new Encounter(TestWorld.Map(
                ".....#.....",
                ".....#.....",
                "...........",
                ".....#.....",
                ".....#....."));
            UnitSetup body = TestWorld.Soldier(Team.Player, 1, 2);
            body.BodyRadius = bodyRadius;
            UnitId player = encounter.AddUnit(body);
            encounter.AddUnit(TestWorld.Soldier(Team.Enemy, 10, 4));
            encounter.Start();
            Assert.That(encounter.Validate(Command.Move(player, TestWorld.At(8, 2))), Is.EqualTo(verdict));
        }

        [Test]
        public void StaleHandlesFailLoudly()
        {
            var encounter = new Encounter(TestWorld.OpenMap(4, 4));
            UnitId player = encounter.AddUnit(TestWorld.Soldier(Team.Player, 0, 0));
            encounter.Units.Retire(player);
            Assert.Throws<InvalidOperationException>(() => encounter.Combat.GetHealth(player));
        }
    }
}
