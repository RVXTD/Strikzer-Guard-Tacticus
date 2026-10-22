// Written by Claude (Anthropic), October 2026; AI driver and archetype tests added for step 7, the offset-model test
// after a unit's model was moved off its unit, overlay and highlight tests for step 8a, framing and audio tests for
// step 8b, and settings, pause and menu tests for step 8c; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Linq;
using Game.Presentation.Tooling;
using Game.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using NumericsVector2 = System.Numerics.Vector2;

namespace Game.Presentation.Tests
{
    /// <summary>
    /// Tests: Building an encounter from scene objects, playing its events back onto unit views, turning hovers and
    /// clicks into previews and commands, and the HUD. Everything is built under one temporary root in the open scene
    /// and destroyed afterwards.
    /// </summary>
    public class EncounterPresentationTests
    {
        private GameObject root;
        private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

        [SetUp]
        public void CreateRoot() => root = new GameObject("EncounterTest (temporary)");

        [TearDown]
        public void DestroyEverything()
        {
            Time.timeScale = 1f; // a pause test must never leave the editor frozen
            AudioListener.pause = false;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            foreach (UnityEngine.Object asset in assets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            assets.Clear();
        }

        private T make<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }

        // A 6 x 6 grid of 1 m open cells with its corner at the world origin, optionally with one wall.
        private MapGridAsset map((int x, int y)? wall = null)
        {
            var grid = make<MapGridAsset>();
            grid.Resize(6, 6, 1f, Vector2.zero, 0f);
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++)
                    grid.SetCell(x, y, true, wall.HasValue && wall.Value == (x, y) ? CellObstacle.Wall : CellObstacle.None);
            return grid;
        }

        private UnitDefinition definition(int initiative, int maxHealth = 10, int accuracy = 70, params AbilityAsset[] abilities)
        {
            var weapon = make<WeaponDefinition>();
            var unit = make<UnitDefinition>();
            unit.Weapon = weapon;
            unit.Initiative = initiative;
            unit.MaxHealth = maxHealth;
            unit.Accuracy = accuracy;
            unit.Abilities = abilities;
            return unit;
        }

        // A unit standing at the centre of cell (x, z), under the test root (so in hierarchy order of creation).
        private UnitAuthoring unit(string name, int x, int z, Team team, UnitDefinition type, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : root.transform, false);
            go.transform.position = new Vector3(x + 0.5f, 0f, z + 0.5f);
            var authoring = go.AddComponent<UnitAuthoring>(); // brings its UnitView with it
            authoring.Definition = type;
            authoring.Team = team;
            return authoring;
        }

        private EncounterTuning alwaysHitAndEmbed()
        {
            var tuning = make<EncounterTuning>();
            HitChanceTuning hit = HitChanceTuning.Default;
            hit.Maximum = 100;
            tuning.HitChance = hit;
            AmmoFateTuning fate = AmmoFateTuning.Default;
            fate.HitEmbedPercent = 100;
            tuning.AmmoFate = fate;
            return tuning;
        }

        private EncounterRoot encounterRoot(MapGridAsset grid, EncounterTuning tuning)
        {
            var encounter = root.AddComponent<EncounterRoot>();
            encounter.Map = grid;
            encounter.Tuning = tuning;
            return encounter;
        }

        // Builds and starts an encounter with the given units, with its opening events already played.
        private EncounterRoot begin(MapGridAsset grid, params UnitAuthoring[] units)
        {
            EncounterRoot encounter = encounterRoot(grid, null);
            encounter.Begin(units);
            encounter.EventPlayback.SkipToEnd();
            return encounter;
        }

        private static UnitId idOf(UnitAuthoring unit) => unit.GetComponent<UnitView>().Id;

        private static int countEvents(Encounter encounter, EventKind kind)
        {
            int total = 0;
            for (int i = 0; i < encounter.Events.Count; i++) if (encounter.Events[i].Kind == kind) total++;
            return total;
        }

        [Test]
        public void StableOrderFollowsTheHierarchyNotTheInputOrder()
        {
            UnitDefinition type = definition(10);
            UnitAuthoring a = unit("A", 0, 0, Team.Player, type);
            var group = new GameObject("Group").transform;
            group.SetParent(root.transform, false);
            UnitAuthoring b = unit("B", 1, 0, Team.Player, type, group);
            UnitAuthoring c = unit("C", 2, 0, Team.Enemy, type);
            Assert.That(EncounterBuilder.StableOrder(new[] { c, b, a }), Is.EqualTo(new[] { a, b, c }));
        }

        [Test]
        public void TheBuilderPlacesUnitsAndSharesTheAbilityTable()
        {
            AbilityAsset shared = make<AbilityAsset>(), onlyEnemy = make<AbilityAsset>();
            UnitAuthoring player = unit("Player", 1, 2, Team.Player, definition(10, abilities: shared));
            UnitAuthoring enemy = unit("Enemy", 4, 4, Team.Enemy, definition(0, abilities: new[] { onlyEnemy, shared }));
            enemy.IsObjective = true;
            enemy.transform.forward = Vector3.left;

            Encounter encounter = EncounterBuilder.Build(map(), null, new[] { player, enemy }, out UnitId[] ids, out AbilityAsset[] abilities);

            Assert.That(abilities, Is.EqualTo(new[] { shared, onlyEnemy }));
            Assert.That(encounter.Spatial.GetPosition(ids[0]), Is.EqualTo(new System.Numerics.Vector2(1.5f, 2.5f)));
            Assert.That(encounter.Units.GetTeam(ids[1]), Is.EqualTo(Team.Enemy));
            Assert.That(encounter.Spatial.GetFacing(ids[1]).X, Is.EqualTo(-1f).Within(1e-5f));
            Assert.That(encounter.Abilities.GetAbilities(ids[1]), Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void AUnitStandingInAWallIsReportedByName()
        {
            UnitAuthoring stuck = unit("Stuck Trooper", 3, 3, Team.Enemy, definition(0));
            var error = Assert.Throws<InvalidOperationException>(() =>
                EncounterBuilder.Build(map(wall: (3, 3)), null, new[] { stuck }, out _, out _));
            Assert.That(error.Message, Does.Contain("Stuck Trooper"));
        }

        [Test]
        public void AModelMovedOffItsUnitIsReportedByName()
        {
            UnitAuthoring moved = unit("Offset Trooper", 2, 2, Team.Enemy, definition(0));
            GameObject model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            model.transform.SetParent(moved.transform, false);
            model.transform.localPosition = new Vector3(-4.6f, 1f, 3.08f);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Offset Trooper"));
            EncounterBuilder.Build(map(), null, new[] { moved }, out _, out _);

            model.transform.localPosition = new Vector3(0f, 1f, 0f);
            EncounterBuilder.Build(map(), null, new[] { moved }, out _, out _);
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MovesPlayBackAtWalkingSpeedAndLockInputUntilDone()
        {
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100));
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = encounterRoot(map(), null);
            encounter.Begin(new[] { player, enemy });
            encounter.EventPlayback.SkipToEnd(); // the opening round and turn events

            UnitId mover = player.GetComponent<UnitView>().Id;
            Assert.That(encounter.TrySubmit(Command.Move(mover, new System.Numerics.Vector2(3.5f, 0.5f)), out CommandResult result), Is.True);
            Assert.That(result.Accepted, Is.True);

            // 3 m at the default 6 m/s takes 0.5 s.
            encounter.EventPlayback.Tick(0.4f);
            float x = player.transform.position.x;
            Assert.That(x, Is.GreaterThan(0.6f).And.LessThan(3.4f), "part-way there");
            Assert.That(encounter.IsPlayingBack, Is.True);
            Assert.That(encounter.TrySubmit(Command.EndTurn(mover), out _), Is.False, "commands wait for playback");

            encounter.EventPlayback.Tick(0.2f);
            Assert.That(player.transform.position.x, Is.EqualTo(3.5f).Within(1e-4f));
            Assert.That(encounter.IsPlayingBack, Is.False);
            Assert.That(encounter.TrySubmit(Command.EndTurn(mover), out result), Is.True);
            Assert.That(result.Accepted, Is.True);
        }

        [Test]
        public void DownedAndDefeatedUnitsAndAmmoPilesAreShown()
        {
            UnitAuthoring fragileEnemy = unit("Fragile Enemy", 3, 0, Team.Enemy, definition(200, maxHealth: 2, accuracy: 100));
            UnitAuthoring fragilePlayer = unit("Fragile Player", 0, 0, Team.Player, definition(100, maxHealth: 2, accuracy: 100));
            UnitAuthoring secondPlayer = unit("Second Player", 0, 5, Team.Player, definition(50, accuracy: 100));
            EncounterRoot encounter = encounterRoot(map(), alwaysHitAndEmbed());
            encounter.Begin(new[] { fragileEnemy, fragilePlayer, secondPlayer });
            encounter.EventPlayback.SkipToEnd();

            UnitView enemyView = fragileEnemy.GetComponent<UnitView>(), playerView = fragilePlayer.GetComponent<UnitView>();
            Assert.That(encounter.TrySubmit(Command.Attack(enemyView.Id, playerView.Id), out CommandResult result), Is.True);
            Assert.That(result.Accepted, Is.True);
            encounter.EventPlayback.SkipToEnd();
            Assert.That(playerView.IsDowned, Is.True);

            UnitView secondView = secondPlayer.GetComponent<UnitView>();
            Assert.That(encounter.Encounter.Flow.ActiveUnit, Is.EqualTo(secondView.Id));
            Assert.That(encounter.TrySubmit(Command.Attack(secondView.Id, enemyView.Id), out result), Is.True);
            Assert.That(result.Accepted, Is.True);
            encounter.EventPlayback.SkipToEnd();
            Assert.That(enemyView.IsDefeated, Is.True);
            Assert.That(encounter.EventPlayback.Piles.Count, Is.GreaterThan(0), "the defeated enemy's embedded rounds drop as a pile");
        }

        [Test]
        public void HoveringGroundPreviewsAMoveAndAClickMovesThere()
        {
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100));
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), player, enemy);
            var controller = new TurnController(encounter);

            controller.Hover(UnitId.None, -1, new NumericsVector2(3.5f, 0.5f), false);
            controller.Tick();
            TurnPreview preview = controller.Preview;
            Assert.That(preview.Kind, Is.EqualTo(PreviewKind.Move));
            Assert.That(preview.CanSubmit, Is.True);
            Assert.That(preview.Move.ActionPointCost, Is.EqualTo(1));
            Assert.That(preview.Move.Path.Count, Is.GreaterThanOrEqualTo(2));

            controller.Click();
            Assert.That(encounter.IsPlayingBack, Is.True);
            int events = encounter.Encounter.Events.Count;
            controller.Hover(UnitId.None, -1, new NumericsVector2(0.5f, 3.5f), false);
            controller.Click();
            Assert.That(encounter.Encounter.Events.Count, Is.EqualTo(events), "clicks wait for playback");

            encounter.EventPlayback.SkipToEnd();
            Assert.That(player.transform.position.x, Is.EqualTo(3.5f).Within(1e-4f));
        }

        [Test]
        public void HoveringAnEnemyPreviewsTheAttackAndOneOutOfSightIsRefused()
        {
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100));
            UnitAuthoring hidden = unit("Hidden", 4, 0, Team.Enemy, definition(0));
            UnitAuthoring visible = unit("Visible", 4, 3, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(wall: (2, 0)), player, hidden, visible);
            var controller = new TurnController(encounter);

            controller.Hover(idOf(visible), -1, new NumericsVector2(4.5f, 3.5f), false);
            controller.Tick();
            TurnPreview preview = controller.Preview;
            Assert.That(preview.Kind, Is.EqualTo(PreviewKind.Attack));
            Assert.That(preview.Verdict, Is.EqualTo(RejectReason.None));
            Assert.That(preview.HasHitChance, Is.True);
            Assert.That(preview.HitChance.Total, Is.EqualTo(encounter.Encounter.PreviewAttack(idOf(player), idOf(visible)).Total));
            Assert.That(preview.Rounds, Is.EqualTo(3));

            controller.Hover(idOf(hidden), -1, new NumericsVector2(4.5f, 0.5f), false);
            controller.Tick();
            Assert.That(controller.Preview.Verdict, Is.EqualTo(RejectReason.NoLineOfSight));
            int events = encounter.Encounter.Events.Count;
            controller.Click();
            Assert.That(encounter.Encounter.Events.Count, Is.EqualTo(events), "a refused preview submits nothing");
            Assert.That(controller.LastRejection, Is.EqualTo(RejectReason.NoLineOfSight));
            Assert.That(controller.RejectionCount, Is.EqualTo(1));
        }

        [Test]
        public void AnAbilityIsTargetedThenUsedAndTargetingCanBeCancelled()
        {
            var overwatch = make<AbilityAsset>();
            overwatch.Definition = new AbilityDefinition
            {
                ActionPointCost = 1,
                EndsTurn = true,
                Targeting = AbilityTargeting.Point,
                Effects = new[] { new AbilityEffect { Kind = AbilityEffectKind.PlaceZone, Zone = new ZoneSetup { Radius = 6f, HalfAngleDegrees = 45f } } },
            };
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100, abilities: overwatch));
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), player, enemy);
            var controller = new TurnController(encounter);

            controller.SelectAbility(0);
            Assert.That(controller.IsTargeting, Is.True);
            controller.Cancel();
            Assert.That(controller.IsTargeting, Is.False);

            controller.SelectAbility(0);
            controller.Hover(UnitId.None, -1, new NumericsVector2(5.5f, 5.5f), false);
            controller.Tick();
            Assert.That(controller.Preview.Kind, Is.EqualTo(PreviewKind.Ability));
            Assert.That(controller.Preview.Verdict, Is.EqualTo(RejectReason.None));
            controller.Click();
            Assert.That(controller.IsTargeting, Is.False);
            Assert.That(countEvents(encounter.Encounter, EventKind.AbilityUsed), Is.EqualTo(1));
            Assert.That(encounter.Encounter.Flow.ActiveUnit, Is.EqualTo(idOf(enemy)), "overwatch ends the turn");
        }

        [Test]
        public void ATurnEndsByItselfOnlyOnceNoFreeActionIsLeft()
        {
            var zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            UnitDefinition firstType = definition(100), secondType = definition(50);
            firstType.ActionPoints = secondType.ActionPoints = 1;
            firstType.Zone = secondType.Zone = zone;
            UnitAuthoring first = unit("First", 0, 0, Team.Player, firstType);
            UnitAuthoring second = unit("Second", 5, 0, Team.Player, secondType);
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), first, second, enemy);
            var controller = new TurnController(encounter);

            controller.Hover(UnitId.None, -1, new NumericsVector2(4.5f, 0.5f), false);
            controller.Click();
            encounter.EventPlayback.SkipToEnd();
            controller.Tick();
            Assert.That(encounter.Encounter.Flow.ActiveUnitActionPoints, Is.EqualTo(0));
            Assert.That(encounter.Encounter.Flow.ActiveUnit, Is.EqualTo(idOf(first)), "an ally inside the zone can still be handed rounds");

            controller.EndTurn();
            encounter.EventPlayback.SkipToEnd();
            Assert.That(encounter.Encounter.Flow.ActiveUnit, Is.EqualTo(idOf(second)));
            controller.Hover(UnitId.None, -1, new NumericsVector2(5.5f, 3.5f), false);
            controller.Click();
            encounter.EventPlayback.SkipToEnd();
            controller.Tick();
            Assert.That(encounter.Encounter.Flow.ActiveUnit, Is.EqualTo(idOf(enemy)), "nothing free is left, so the turn ends");
        }

        [Test]
        public void TheHudShowsTheActiveUnit()
        {
            UnitAuthoring player = unit("Squad Leader", 0, 0, Team.Player, definition(100));
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), player, enemy);
            var input = root.AddComponent<EncounterInput>();
            input.Root = encounter;
            var hud = root.AddComponent<EncounterHud>();
            hud.EncounterInput = input;

            hud.Refresh();
            string shown = string.Join("\n", root.GetComponentsInChildren<Text>(true).Select(text => text.text));
            Assert.That(shown, Does.Contain("Squad Leader"));
            Assert.That(shown, Does.Contain("Action points 2/2"));
            Assert.That(shown, Does.Contain("Round 1"));
        }

        // Checks that the overlay's soft masks give the rules' own answers at every cell centre: the unit can stand in a
        // cell exactly when it is clear of walls and of other bodies, and a zone covers a cell exactly when it contains
        // the centre and its owner sees it.
        private static void assertOverlayAgreesWithTheRules(EncounterOverlay overlay, Encounter rules, UnitId zoneOwner)
        {
            SpatialSystem spatial = rules.Spatial;
            spatial.TryGetCell(spatial.GetPosition(rules.Flow.ActiveUnit), out int own);
            for (int cell = 0; cell < spatial.CellCount; cell++)
            {
                int x = cell % spatial.Width, y = cell / spatial.Width;
                Color field = overlay.FieldAt(x, y);
                if (cell != own)
                    Assert.That(field.g >= 0.5f && field.b <= 0.5f, Is.EqualTo(spatial.IsPassableForFieldOwner(cell)), $"standing in ({x}, {y})");
                if (zoneOwner.IsNone) continue;
                NumericsVector2 centre = spatial.CellCenter(cell);
                bool covered = spatial.IsInsideZone(zoneOwner, centre) && spatial.HasLineOfSight(spatial.GetPosition(zoneOwner), centre);
                Assert.That(overlay.ZonesAt(x, y).x >= 0.5f || overlay.ZonesAt(x, y).y >= 0.5f, Is.EqualTo(covered), $"zone at ({x}, {y})");
            }
        }

        [Test]
        public void TheOverlayCarriesTheFieldPassabilityAndZones()
        {
            UnitDefinition guardType = definition(0);
            guardType.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100));
            UnitAuthoring guard = unit("Guard", 5, 5, Team.Enemy, guardType);
            EncounterRoot encounter = begin(map(wall: (1, 4)), player, guard);
            root.AddComponent<EncounterInput>().Root = encounter;
            var overlay = root.AddComponent<EncounterOverlay>();
            overlay.Refresh();

            SpatialSystem spatial = encounter.Encounter.Spatial;
            Assert.That(spatial.TryGetCell(new NumericsVector2(2.5f, 0.5f), out int open), Is.True);
            Assert.That(overlay.FieldAt(2, 0).r, Is.EqualTo(spatial.Field[open]).Within(0.01f), "the field distance");
            Assert.That(overlay.FieldAt(2, 0).g, Is.EqualTo(1f), "clear of walls");
            Assert.That(overlay.FieldAt(1, 4).g, Is.EqualTo(0f), "the wall");
            Assert.That(overlay.FieldAt(5, 5).b, Is.EqualTo(1f), "the guard's own cell");
            Assert.That(overlay.ZonesAt(5, 4).x, Is.EqualTo(1f), "inside the guard's zone");
            Assert.That(overlay.ZonesAt(0, 0).x + overlay.ZonesAt(0, 0).y, Is.EqualTo(0f), "outside every zone");
            assertOverlayAgreesWithTheRules(overlay, encounter.Encounter, idOf(guard));

            // Walking into the zone spends the guard's reaction, so its zone is drawn as spent.
            Assert.That(encounter.TrySubmit(Command.Move(idOf(player), new NumericsVector2(4.5f, 4.5f)), out CommandResult result), Is.True);
            Assert.That(result.Accepted, Is.True);
            encounter.EventPlayback.SkipToEnd();
            Assert.That(encounter.Encounter.Flow.HasReaction(idOf(guard)), Is.False);
            overlay.Refresh();
            Assert.That(overlay.ZonesAt(5, 4).x, Is.EqualTo(0f));
            Assert.That(overlay.ZonesAt(5, 4).y, Is.EqualTo(1f));
        }

        [Test]
        public void TheOverlayFieldRisesIntoWallClearanceAndRunsUnderOtherUnits()
        {
            // A wide unit (0.8 m) next to a wall cell, with an ally in its way.
            UnitDefinition wide = definition(100);
            wide.BodyRadius = 0.8f;
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, wide);
            UnitAuthoring ally = unit("Ally", 2, 0, Team.Player, definition(50));
            UnitAuthoring guard = unit("Guard", 5, 5, Team.Enemy, definition(0)); // without an enemy the encounter is won at once
            EncounterRoot encounter = begin(map(wall: (3, 3)), player, ally, guard);
            Assert.That(encounter.Encounter.Flow.Phase, Is.EqualTo(EncounterPhase.AwaitingCommand));
            root.AddComponent<EncounterInput>().Root = encounter;
            var overlay = root.AddComponent<EncounterOverlay>();
            overlay.Refresh();
            assertOverlayAgreesWithTheRules(overlay, encounter.Encounter, UnitId.None);

            // (2, 3) sits in the wall's clearance: it is closed, and its distance keeps rising past its open neighbours'.
            SpatialSystem spatial = encounter.Encounter.Spatial;
            Assert.That(overlay.FieldAt(2, 3).g, Is.LessThan(0.5f));
            float nearestOpen = float.PositiveInfinity;
            for (int y = 2; y <= 4; y++)
                for (int x = 1; x <= 3; x++)
                    if (spatial.IsPassableForFieldOwner(y * spatial.Width + x)) nearestOpen = Mathf.Min(nearestOpen, overlay.FieldAt(x, y).r);
            Assert.That(overlay.FieldAt(2, 3).r, Is.GreaterThanOrEqualTo(nearestOpen + 1f - 1e-3f));

            // The ally's cell is blocked by its body, but the field carries on underneath it.
            Assert.That(overlay.FieldAt(2, 0).b, Is.GreaterThan(0.5f));
            Assert.That(overlay.FieldAt(2, 0).r, Is.LessThan(spatial.FieldRange));
        }

        [Test]
        public void HoveringAnEnemyHighlightsItAndClearingRestoresItsMaterials()
        {
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100));
            UnitAuthoring enemy = unit("Enemy", 3, 0, Team.Enemy, definition(0));
            GameObject model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            model.transform.SetParent(enemy.transform, false);
            Renderer body = model.GetComponent<Renderer>();
            Material[] original = body.sharedMaterials;
            EncounterRoot encounter = begin(map(), player, enemy);
            var input = root.AddComponent<EncounterInput>();
            input.Root = encounter;
            var preview = root.AddComponent<PreviewView>();
            UnitView view = enemy.GetComponent<UnitView>();

            input.Controller.Hover(idOf(enemy), -1, new NumericsVector2(3.5f, 0.5f), false);
            input.Controller.Tick();
            preview.Refresh();
            Assert.That(view.IsHighlighted, Is.True);
            Assert.That(preview.Highlighted, Is.SameAs(view));
            Assert.That(body.sharedMaterials.Length, Is.EqualTo(original.Length + 1));
            Assert.That(view.HighlightColour, Is.EqualTo(new Color(1f, 0.35f, 0.15f)), "the attack colour");

            input.Controller.ClearHover();
            input.Controller.Tick();
            preview.Refresh();
            Assert.That(view.IsHighlighted, Is.False);
            Assert.That(body.sharedMaterials, Is.EqualTo(original));
        }

        [Test]
        public void TheCameraFramesAnAttackAndIgnoresOtherEvents()
        {
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100));
            UnitAuthoring enemy = unit("Enemy", 4, 4, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), player, enemy);
            var eyeObject = new GameObject("Camera", typeof(Camera));
            eyeObject.transform.SetParent(root.transform, false);
            var rig = eyeObject.AddComponent<EncounterCamera>();
            rig.Root = encounter;

            Assert.That(rig.Frame(new GameEvent { Kind = EventKind.TurnStarted, Unit = idOf(player) }), Is.False);
            Assert.That(rig.Frame(new GameEvent { Kind = EventKind.AttackStarted, Unit = idOf(player), Other = idOf(enemy) }), Is.True);
            Assert.That(rig.IsFraming, Is.True);
            Assert.That(Vector3.Distance(rig.FocusPoint, new Vector3(2.5f, 0f, 2.5f)), Is.LessThan(1e-3f), "midway between them");
            Assert.That(rig.TargetDistance, Is.GreaterThan(6f), "far enough back to fit both, past the closest zoom");
        }

        [Test]
        public void AudioVoicesEventsFromTheDictionaryAndTracksIntensity()
        {
            var audio = root.AddComponent<EncounterAudio>();
            AudioClip placeholder = audio.ClipFor(EventKind.RoundHit);
            Assert.That(placeholder, Is.Not.Null, "a generated placeholder");
            Assert.That(placeholder.samples, Is.GreaterThan(0));
            Assert.That(audio.ClipFor(EventKind.UnitMoved), Is.Null, "walking is silent");

            var sounds = make<SoundDictionary>();
            AudioClip own = AudioClip.Create("Test shot", 100, 1, 22050, false);
            SoundEntry[] entries = sounds.Entries;
            int hit = System.Array.FindIndex(entries, entry => entry.Kind == EventKind.RoundHit);
            entries[hit].Clips = new[] { own };
            sounds.Entries = entries;
            audio.Dictionary = sounds;
            Assert.That(audio.ClipFor(EventKind.RoundHit), Is.SameAs(own));

            for (int i = 0; i < 3; i++) audio.Play(new GameEvent { Kind = EventKind.RoundHit });
            Assert.That(audio.Intensity, Is.GreaterThan(0.3f));
            audio.Tick(120f);
            Assert.That(audio.Intensity, Is.LessThan(0.01f));
            UnityEngine.Object.DestroyImmediate(own);
        }

        [Test]
        public void SettingsApplyTheFrameCapAndSurviveASaveAndLoad()
        {
            int frameRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
            float volume = AudioListener.volume;
            var settings = new GameSettings("AutoBuster.Test.");
            try
            {
                settings.FrameRate = FrameRateMode.Fps30;
                settings.Apply();
                Assert.That(Application.targetFrameRate, Is.EqualTo(30));
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(0));
                settings.FrameRate = FrameRateMode.MatchDisplay;
                settings.Apply();
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
                settings.FrameRate = FrameRateMode.Uncapped;
                settings.Apply();
                Assert.That(Application.targetFrameRate, Is.EqualTo(-1));
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(0));

                settings.MusicVolume = 0.25f;
                settings.Save();
                var loaded = new GameSettings("AutoBuster.Test.");
                loaded.Load();
                Assert.That(loaded.FrameRate, Is.EqualTo(FrameRateMode.Uncapped));
                Assert.That(loaded.MusicVolume, Is.EqualTo(0.25f));
            }
            finally
            {
                settings.DeleteSaved();
                Application.targetFrameRate = frameRate;
                QualitySettings.vSyncCount = vSync;
                AudioListener.volume = volume;
            }
        }

        [Test]
        public void TheSettingsPanelCyclesTheFrameRate()
        {
            int frameRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
            var settings = new GameSettings("AutoBuster.Test.");
            try
            {
                var panel = new SettingsPanel(MenuWidgets.Canvas("Test canvas", root.transform, 0), settings, null);
                Assert.That(panel.StepFrameRate(+1), Is.EqualTo("120 fps"), "60 is the default");
                Assert.That(settings.FrameRate, Is.EqualTo(FrameRateMode.Fps120));
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));
                Assert.That(panel.StepFrameRate(-1), Is.EqualTo("60 fps"));
            }
            finally
            {
                settings.DeleteSaved();
                Application.targetFrameRate = frameRate;
                QualitySettings.vSyncCount = vSync;
            }
        }

        [Test]
        public void PausingFreezesTheEncounterAndEscCancelsTargetingFirst()
        {
            var aim = make<AbilityAsset>();
            aim.Definition = new AbilityDefinition { ActionPointCost = 1, Targeting = AbilityTargeting.Point, Effects = new AbilityEffect[0] };
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100, abilities: aim));
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), player, enemy);
            var input = root.AddComponent<EncounterInput>();
            input.Root = encounter;
            var pause = root.AddComponent<PauseMenu>();

            pause.Pause();
            Assert.That(pause.IsPaused && encounter.IsPaused, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(input.Controller.CanAct, Is.False);
            Assert.That(encounter.TrySubmit(Command.EndTurn(idOf(player)), out _), Is.False, "commands wait while paused");
            pause.Resume();
            Assert.That(pause.IsPaused || encounter.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(input.Controller.CanAct, Is.True);

            input.Controller.SelectAbility(0);
            Assert.That(input.Controller.IsTargeting, Is.True);
            pause.OnPausePressed();
            Assert.That(input.Controller.IsTargeting, Is.False, "the first press cancels the targeting");
            Assert.That(pause.IsPaused, Is.False);
            pause.OnPausePressed();
            Assert.That(pause.IsPaused, Is.True, "the next press pauses");
        }

        [Test]
        public void EscCancelsTargetingEvenWhenTheInputCancelledItEarlierInTheFrame()
        {
            var aim = make<AbilityAsset>();
            aim.Definition = new AbilityDefinition { ActionPointCost = 1, Targeting = AbilityTargeting.Point, Effects = new AbilityEffect[0] };
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100, abilities: aim));
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), player, enemy);
            var input = root.AddComponent<EncounterInput>();
            input.Root = encounter;
            var pause = root.AddComponent<PauseMenu>(); // a fresh menu that has never been pressed

            input.Controller.SelectAbility(0);
            pause.RecordTargeting(); // the frame ends with the ability targeted
            input.Controller.Cancel(); // next frame, EncounterInput handles Esc first
            pause.OnPausePressed();
            Assert.That(pause.IsPaused, Is.False, "the press only ends the targeting");
            pause.RecordTargeting();
            pause.OnPausePressed();
            Assert.That(pause.IsPaused, Is.True);
        }

        [Test]
        public void TheMainMenuGoesFromTheSplashToTheMenuAndSettings()
        {
            var menu = root.AddComponent<MainMenu>();
            menu.EnsureBuilt();
            Assert.That(menu.State, Is.EqualTo(MainMenuState.Splash));
            menu.AdvanceFromSplash();
            Assert.That(menu.State, Is.EqualTo(MainMenuState.Main));
            menu.OpenSettings();
            Assert.That(menu.State, Is.EqualTo(MainMenuState.Settings));
            menu.Back();
            Assert.That(menu.State, Is.EqualTo(MainMenuState.Main));
        }

        [Test]
        public void SceneSetupAddsTheEncounterComponentsOnlyOnce()
        {
            var host = new GameObject("Host");
            host.transform.SetParent(root.transform, false);
            Assert.That(EncounterSceneSetup.AddEncounterComponents(host),
                        Is.EqualTo(new[] { "EncounterRoot", "EncounterInput", "PreviewView", "EncounterHud", "EncounterAi", "EncounterOverlay", "EncounterAudio", "PauseMenu" }));
            Assert.That(EncounterSceneSetup.AddEncounterComponents(host), Is.Empty);
        }

        [Test]
        public void TheAiPlaysTheEnemiesAndLeavesTheSquadToThePlayer()
        {
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100));
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), player, enemy);
            var ai = root.AddComponent<EncounterAi>();
            ai.Root = encounter;

            Assert.That(ai.ControlsActiveUnit, Is.False, "the squad member's turn");
            Assert.That(ai.Step(), Is.False);
            Assert.That(encounter.TrySubmit(Command.EndTurn(idOf(player)), out _), Is.True);
            encounter.EventPlayback.SkipToEnd();

            Assert.That(ai.ControlsActiveUnit, Is.True, "the enemy's turn");
            int before = encounter.Encounter.Events.Count;
            Assert.That(ai.Step(), Is.True);
            Assert.That(encounter.Encounter.Events.Count, Is.GreaterThan(before));
        }

        [Test]
        public void ThePlayerCannotActWhileTheAiDoes()
        {
            UnitAuthoring player = unit("Player", 0, 0, Team.Player, definition(100));
            UnitAuthoring enemy = unit("Enemy", 5, 5, Team.Enemy, definition(0));
            EncounterRoot encounter = begin(map(), player, enemy);
            var controller = new TurnController(encounter) { AiControlsActiveUnit = true };

            Assert.That(controller.CanAct, Is.False);
            controller.EndTurn();
            controller.Hover(UnitId.None, -1, new NumericsVector2(3.5f, 0.5f), false);
            controller.Click();
            Assert.That(encounter.Encounter.Flow.ActiveUnit, Is.EqualTo(idOf(player)));
            Assert.That(encounter.Encounter.Spatial.GetPosition(idOf(player)), Is.EqualTo(new NumericsVector2(0.5f, 0.5f)));
        }

        [Test]
        public void EnemyArchetypesAreCreatedOnceAndNeverOverwritten()
        {
            const string folder = "Assets/EnemyArchetypesTest";
            try
            {
                List<string> created = EnemyArchetypes.CreateArchetypes(folder);
                Assert.That(created, Has.Count.EqualTo(10), "four weapons, two abilities, four units");
                var boss = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDefinition>(folder + "/Boss.asset");
                Assert.That(boss.Weapon, Is.Not.Null);
                Assert.That(boss.Abilities.Length, Is.EqualTo(1));
                Assert.That(UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDefinition>(folder + "/Sentry.asset").MovementBudget, Is.EqualTo(0f));

                boss.MaxHealth = 99;
                Assert.That(EnemyArchetypes.CreateArchetypes(folder), Is.Empty);
                Assert.That(UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDefinition>(folder + "/Boss.asset").MaxHealth, Is.EqualTo(99));
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
