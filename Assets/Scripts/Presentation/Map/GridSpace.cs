// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using UnityEngine;
using NumericsVector2 = System.Numerics.Vector2;

namespace Game.Presentation
{
    /// <summary>
    /// Converts between the rules layer's ground-plane positions and Unity world space: rules (x, y) is world
    /// (x, floor height, z = y). The one place this mapping is written down.
    /// </summary>
    public static class GridSpace
    {
        /// <summary>The world position of a rules-layer point, standing on the floor at <paramref name="floorHeight"/>.</summary>
        public static Vector3 ToWorld(NumericsVector2 point, float floorHeight) => new Vector3(point.X, floorHeight, point.Y);

        /// <summary>The rules-layer point under a world position (its height is dropped).</summary>
        public static NumericsVector2 ToRules(Vector3 world) => new NumericsVector2(world.x, world.z);
    }
}
