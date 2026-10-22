// Written by Claude (Anthropic), October 2026; the offset-model warning added after a unit's model was moved off its
// unit; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Builds an <see cref="Encounter"/> from a scene's authored data: the baked map, the tuning, and every placed
    /// unit. Kept apart from <see cref="EncounterRoot"/> so it can be tested without entering play mode.
    /// </summary>
    public static class EncounterBuilder
    {
        /// <summary>How far, in metres, a unit's model may sit from the unit's own position before a warning.</summary>
        public const float ModelOffsetTolerance = 0.1f;

        /// <summary>
        /// Builds the encounter (not yet started) and registers <paramref name="units"/> in the order given.
        /// <paramref name="ids"/> receives each unit's id, in the same order; <paramref name="abilities"/> the
        /// encounter's ability table (every distinct ability the units know, in first-seen order). Throws
        /// <see cref="InvalidOperationException"/>, naming the object, for a unit with no definition or one standing
        /// off the map or in a blocked cell.
        /// </summary>
        public static Encounter Build(MapGridAsset map, EncounterTuning tuning, IReadOnlyList<UnitAuthoring> units,
                                      out UnitId[] ids, out AbilityAsset[] abilities)
        {
            if (map == null) throw new InvalidOperationException("The encounter has no map grid.");
            var setup = new EncounterSetup();
            map.ApplyTo(setup);
            if (tuning != null) tuning.ApplyTo(setup);
            setup.UnitCapacity = Math.Max(setup.UnitCapacity, units.Count);

            var table = new List<AbilityAsset>();
            foreach (UnitAuthoring unit in units)
            {
                if (unit.Definition == null) throw new InvalidOperationException($"Unit '{unit.name}' has no unit definition.");
                foreach (AbilityAsset ability in unit.Definition.Abilities ?? Array.Empty<AbilityAsset>())
                    if (ability != null && !table.Contains(ability)) table.Add(ability);
            }
            abilities = table.ToArray();
            setup.Abilities = new AbilityDefinition[abilities.Length];
            for (int i = 0; i < abilities.Length; i++) setup.Abilities[i] = abilities[i].Definition;

            var encounter = new Encounter(setup);
            ids = new UnitId[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                UnitAuthoring unit = units[i];
                System.Numerics.Vector2 position = GridSpace.ToRules(unit.transform.position);
                requireOpenFloor(setup, position, unit.name);
                warnIfModelIsOffset(unit);
                UnitSetup unitSetup = unit.Definition.ToUnitSetup(unit.Team, position, GridSpace.ToRules(unit.transform.forward),
                                                                  unit.IsObjective, ability => table.IndexOf(ability));
                ids[i] = encounter.AddUnit(unitSetup);
            }
            return encounter;
        }

        /// <summary>
        /// The units sorted by their place in the hierarchy (scene, then root order, then sibling order down the
        /// tree), so registration order, and with it every seeded roll, never depends on Unity's object order.
        /// </summary>
        public static List<UnitAuthoring> StableOrder(IEnumerable<UnitAuthoring> units)
        {
            var sorted = new List<UnitAuthoring>(units);
            sorted.Sort((a, b) => compare(hierarchyPath(a.transform), hierarchyPath(b.transform)));
            return sorted;
        }

        private static List<int> hierarchyPath(Transform transform)
        {
            var path = new List<int>();
            for (Transform current = transform; current != null; current = current.parent) path.Add(current.GetSiblingIndex());
            path.Add(transform.gameObject.scene.buildIndex);
            path.Reverse();
            return path;
        }

        private static int compare(List<int> a, List<int> b)
        {
            for (int i = 0; i < a.Count && i < b.Count; i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }

        // The rules place a unit at its own object's position, and its model turns round that point. A model moved off
        // it (by dragging the child instead of the unit) is drawn somewhere the unit is not, and swings when it turns.
        private static void warnIfModelIsOffset(UnitAuthoring unit)
        {
            Vector3 pivot = unit.transform.position;
            foreach (Renderer model in unit.GetComponentsInChildren<Renderer>())
            {
                Vector3 offset = model.bounds.center - pivot;
                offset.y = 0f;
                if (offset.magnitude <= ModelOffsetTolerance) continue;
                Debug.LogWarning($"Unit '{unit.name}': its model '{model.name}' sits {offset.magnitude:0.##} m from the unit's own " +
                                 "position, which is where the rules place it, and will swing round it as the unit turns. Move the " +
                                 "unit object rather than its model, and set the model's local X and Z to 0.", unit);
            }
        }

        private static void requireOpenFloor(EncounterSetup setup, System.Numerics.Vector2 position, string unitName)
        {
            int x = Mathf.FloorToInt((position.X - setup.Origin.X) / setup.CellSize);
            int y = Mathf.FloorToInt((position.Y - setup.Origin.Y) / setup.CellSize);
            if (x < 0 || y < 0 || x >= setup.Width || y >= setup.Height)
                throw new InvalidOperationException($"Unit '{unitName}' stands outside the baked map.");
            int cell = y * setup.Width + x;
            if (!setup.Floor[cell] || setup.Obstacles[cell].BlocksMovement)
                throw new InvalidOperationException($"Unit '{unitName}' stands in a cell with no floor or an obstacle ({x}, {y}).");
        }
    }
}
