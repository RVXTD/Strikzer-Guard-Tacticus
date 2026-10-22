// Glass ('g') and CellObstacle map parsing, default action points, the slice ability definitions and the
// soldier's AI profile by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Numerics;

namespace Game.Rules.Tests
{
    /// <summary>Helpers for building small maps and units in tests.</summary>
    public static class TestWorld
    {
        /// <summary>
        /// Builds a setup from rows of text, top row = highest Z. '.' floor, 'h' low obstacle (half cover),
        /// '#' wall, 'g' glass, '_' no floor. Cells are 1 m with the origin at (0, 0).
        /// </summary>
        public static EncounterSetup Map(params string[] rows)
        {
            int height = rows.Length, width = rows[0].Length;
            var setup = new EncounterSetup
            {
                Width = width,
                Height = height,
                Floor = new bool[width * height],
                Obstacles = new CellObstacle[width * height],
            };
            for (int r = 0; r < height; r++)
            {
                if (rows[r].Length != width) throw new ArgumentException("All rows must have the same length.");
                int y = height - 1 - r;
                for (int x = 0; x < width; x++)
                {
                    char c = rows[r][x];
                    int cell = y * width + x;
                    setup.Floor[cell] = c != '_';
                    setup.Obstacles[cell] = c == '#' ? CellObstacle.Wall
                                          : c == 'h' ? CellObstacle.LowObstacle
                                          : c == 'g' ? CellObstacle.Glass
                                          : CellObstacle.None;
                }
            }
            return setup;
        }

        /// <summary>An open map with no obstacles.</summary>
        public static EncounterSetup OpenMap(int width, int height)
        {
            var rows = new string[height];
            for (int i = 0; i < height; i++) rows[i] = new string('.', width);
            return Map(rows);
        }

        /// <summary>Centre of cell (x, y).</summary>
        public static Vector2 At(int x, int y) => new Vector2(x + 0.5f, y + 0.5f);

        /// <summary>A plain rifle-armed unit at the centre of cell (x, y).</summary>
        public static UnitSetup Soldier(Team team, int x, int y) => new UnitSetup
        {
            Team = team,
            Position = At(x, y),
            MaxHealth = 10,
            Accuracy = 70,
            MovementBudget = 6f,
            Initiative = team == Team.Player ? 100 : 0, // players act first unless a test says otherwise
            ActionPoints = 2,
            Weapon = new WeaponStats { AmmoType = 0, RoundsPerAttack = 3, DamagePerRound = 2, OptimalRange = 10f },
            StartingAmmo = 30,
            Ai = AiProfile.Default,
        };

        /// <summary>Overwatch: 1 point, ends the turn; a 6 m, 45-degree cone towards the target point.</summary>
        public static AbilityDefinition Overwatch() => new AbilityDefinition
        {
            ActionPointCost = 1,
            EndsTurn = true,
            Targeting = AbilityTargeting.Point,
            Effects = new[]
            {
                new AbilityEffect { Kind = AbilityEffectKind.PlaceZone, Zone = new ZoneSetup { Radius = 6f, HalfAngleDegrees = 45f, RequiresLineOfSight = true } },
            },
        };

        /// <summary>Suppression: fires a burst at a visible enemy, then pins it and lowers its accuracy by 20 for 1 turn.</summary>
        public static AbilityDefinition Suppression(bool isReaction = false) => new AbilityDefinition
        {
            ActionPointCost = 1,
            EndsTurn = true,
            IsReaction = isReaction,
            Targeting = AbilityTargeting.Unit,
            TargetTeam = AbilityTeam.Hostile,
            RequiresLineOfSight = true,
            Effects = new[]
            {
                new AbilityEffect { Kind = AbilityEffectKind.Attack },
                new AbilityEffect { Kind = AbilityEffectKind.ApplyStatus, Status = StatusKind.Suppressed, Turns = 1 },
                new AbilityEffect { Kind = AbilityEffectKind.ApplyStatus, Status = StatusKind.Accuracy, Magnitude = -20f, Turns = 1 },
            },
        };

        /// <summary>Revive: 1 point; brings a downed ally within 2 m back with 5 health.</summary>
        public static AbilityDefinition Revive() => new AbilityDefinition
        {
            ActionPointCost = 1,
            Targeting = AbilityTargeting.Unit,
            TargetTeam = AbilityTeam.Friendly,
            Range = 2f,
            Effects = new[] { new AbilityEffect { Kind = AbilityEffectKind.Revive, Amount = 5 } },
        };

        /// <summary>Blast: 1 point, ends the turn; 4 damage to every visible enemy within 3 m of a visible point.</summary>
        public static AbilityDefinition Blast() => new AbilityDefinition
        {
            ActionPointCost = 1,
            EndsTurn = true,
            Targeting = AbilityTargeting.Point,
            TargetTeam = AbilityTeam.Hostile,
            Radius = 3f,
            RequiresLineOfSight = true,
            Effects = new[] { new AbilityEffect { Kind = AbilityEffectKind.Damage, Amount = 4 } },
        };

        /// <summary>Tuning under which every round hits and embeds, for deterministic tests.</summary>
        public static void MakeEveryRoundHitAndEmbed(EncounterSetup setup)
        {
            setup.HitTuning.Maximum = 100;
            setup.FateTuning.HitEmbedPercent = 100;
        }
    }
}
