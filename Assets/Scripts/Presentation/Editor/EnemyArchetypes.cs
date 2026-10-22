// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System.Collections.Generic;
using Game.Rules;
using UnityEditor;
using UnityEngine;

namespace Game.Presentation.Tooling
{
    /// <summary>
    /// Tools ▸ Encounter ▸ Create Enemy Archetypes: writes placeholder definitions for the slice's four enemy types
    /// (melee bot, sentry, ranged trooper, boss), with their weapons, the sentry's reaction suppression and the boss's
    /// blast, into Assets/Data/Definitions/Enemies. Assets that already exist are left untouched (and reused), so
    /// running it again never overwrites tuning. The numbers are a starting point for playtesting.
    /// </summary>
    public static class EnemyArchetypes
    {
        /// <summary>Where the menu item writes the assets.</summary>
        public const string DefaultFolder = "Assets/Data/Definitions/Enemies";

        /// <summary>Creates the archetype assets in <see cref="DefaultFolder"/> and logs what it created.</summary>
        [MenuItem("Tools/Encounter/Create Enemy Archetypes")]
        public static void CreateInDefaultFolder()
        {
            List<string> created = CreateArchetypes(DefaultFolder);
            Debug.Log(created.Count > 0
                ? $"[Enemy archetypes] Created {string.Join(", ", created)}."
                : $"[Enemy archetypes] Every archetype already exists in {DefaultFolder}.");
        }

        /// <summary>
        /// Creates whichever archetype assets are missing from <paramref name="folder"/> (creating the folder too) and
        /// returns the paths of those it created.
        /// </summary>
        public static List<string> CreateArchetypes(string folder)
        {
            ensureFolder(folder);
            var created = new List<string>();

            WeaponDefinition claws = weapon(folder, "Bot Claws", 2, 3, 1.5f, created);
            WeaponDefinition sentryGun = weapon(folder, "Sentry Gun", 4, 1, 14f, created);
            WeaponDefinition rifle = weapon(folder, "Trooper Rifle", 3, 2, 10f, created);
            WeaponDefinition cannon = weapon(folder, "Boss Cannon", 2, 4, 8f, created);

            AbilityAsset suppression = ability(folder, "Sentry Suppression", new AbilityDefinition
            {
                ActionPointCost = 1,
                EndsTurn = true,
                IsReaction = true,
                Targeting = AbilityTargeting.Unit,
                TargetTeam = AbilityTeam.Hostile,
                RequiresLineOfSight = true,
                Effects = new[]
                {
                    new AbilityEffect { Kind = AbilityEffectKind.Attack },
                    new AbilityEffect { Kind = AbilityEffectKind.ApplyStatus, Status = StatusKind.Suppressed, Turns = 1 },
                    new AbilityEffect { Kind = AbilityEffectKind.ApplyStatus, Status = StatusKind.Accuracy, Magnitude = -20f, Turns = 1 },
                },
            }, created);
            AbilityAsset blast = ability(folder, "Boss Blast", new AbilityDefinition
            {
                ActionPointCost = 1,
                EndsTurn = true,
                Targeting = AbilityTargeting.Point,
                TargetTeam = AbilityTeam.Hostile,
                Range = 10f,
                Radius = 2.5f,
                RequiresLineOfSight = true,
                Effects = new[] { new AbilityEffect { Kind = AbilityEffectKind.Damage, Amount = 4 } },
            }, created);

            unit(folder, "Melee Bot", created, type =>
            {
                type.MaxHealth = 14;
                type.Accuracy = 75;
                type.MovementBudget = 7f;
                type.Initiative = 12;
                type.Weapon = claws;
                type.StartingAmmo = 40;
                type.Zone = new ZoneSetup { Radius = 1.5f, HalfAngleDegrees = 180f };
                type.BodyRadius = 0.4f;
                type.Ai = new AiProfile { Aggression = 1.5f, Caution = 0.3f, Approach = 1f, AmmoGreed = 0.5f, ZoneAvoidance = 0.5f };
            });
            unit(folder, "Sentry", created, type =>
            {
                type.MaxHealth = 12;
                type.MovementBudget = 0f;
                type.Initiative = 6;
                type.Weapon = sentryGun;
                type.StartingAmmo = 60;
                type.Zone = new ZoneSetup { Radius = 10f, HalfAngleDegrees = 45f, RequiresLineOfSight = true };
                type.Abilities = new[] { suppression };
                type.BodyRadius = 0.5f;
                type.Ai = new AiProfile { Aggression = 1f };
            });
            unit(folder, "Ranged Trooper", created, type =>
            {
                type.Weapon = rifle;
                type.Ai = new AiProfile { Aggression = 1f, Caution = 1.5f, Approach = 0.3f, RetreatWhenHurt = 2f, AmmoGreed = 1f, ZoneAvoidance = 1.5f };
            });
            unit(folder, "Boss", created, type =>
            {
                type.MaxHealth = 40;
                type.MovementBudget = 4f;
                type.Initiative = 8;
                type.Weapon = cannon;
                type.StartingAmmo = 40;
                type.Zone = new ZoneSetup { Radius = 2f, HalfAngleDegrees = 180f };
                type.Abilities = new[] { blast };
                type.BodyRadius = 0.7f;
                type.Ai = new AiProfile { Aggression = 1.2f, Caution = 0.5f, Approach = 0.5f, RetreatWhenHurt = 0.5f, AmmoGreed = 0.5f, ZoneAvoidance = 1f };
            });

            AssetDatabase.SaveAssets();
            return created;
        }

        private static WeaponDefinition weapon(string folder, string name, int rounds, int damage, float optimalRange, List<string> created) =>
            loadOrCreate<WeaponDefinition>(folder, name, created, asset =>
                asset.Stats = new WeaponStats { RoundsPerAttack = rounds, DamagePerRound = damage, OptimalRange = optimalRange });

        private static AbilityAsset ability(string folder, string name, AbilityDefinition definition, List<string> created) =>
            loadOrCreate<AbilityAsset>(folder, name, created, asset => asset.Definition = definition);

        private static void unit(string folder, string name, List<string> created, System.Action<UnitDefinition> configure) =>
            loadOrCreate<UnitDefinition>(folder, name, created, type =>
            {
                type.DisplayName = name;
                configure(type);
            });

        // The asset at folder/name.asset, created and configured first if it does not exist yet.
        private static T loadOrCreate<T>(string folder, string name, List<string> created, System.Action<T> configure) where T : ScriptableObject
        {
            string path = $"{folder}/{name}.asset";
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<T>();
            configure(asset);
            AssetDatabase.CreateAsset(asset, path);
            created.Add(path);
            return asset;
        }

        private static void ensureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int split = folder.LastIndexOf('/');
            string parent = folder.Substring(0, split);
            ensureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(split + 1));
        }
    }
}
