// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The map bake's per-cell rules, kept free of scene access so they can be tested on their own: which surface
    /// kind wins when several are found in one cell, and how a kind or an override becomes rules-layer cell data.
    /// </summary>
    public static class MapCellClassifier
    {
        /// <summary>
        /// The stronger of two obstacle kinds found in the same cell: Wall beats Glass beats LowObstacle; Floor and
        /// Ignore are not obstacles and never win over one.
        /// </summary>
        public static SurfaceKind Stronger(SurfaceKind a, SurfaceKind b) => rank(a) >= rank(b) ? a : b;

        /// <summary>
        /// The cell data for a sampled cell: <paramref name="floorFound"/> if floor geometry was found at floor
        /// height, and <paramref name="strongestObstacle"/> the strongest kind found in the obstacle band. A cell with
        /// an obstacle counts as having floor, since something stands on it.
        /// </summary>
        public static void Classify(bool floorFound, SurfaceKind strongestObstacle, out bool hasFloor, out CellObstacle obstacle)
        {
            obstacle = ObstacleOf(strongestObstacle);
            hasFloor = floorFound || obstacle.BlocksMovement || obstacle.BlocksSight;
        }

        /// <summary>The rules-layer obstacle for a surface kind (<see cref="CellObstacle.None"/> for Floor and Ignore).</summary>
        public static CellObstacle ObstacleOf(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.Wall: return CellObstacle.Wall;
                case SurfaceKind.Glass: return CellObstacle.Glass;
                case SurfaceKind.LowObstacle: return CellObstacle.LowObstacle;
                default: return CellObstacle.None;
            }
        }

        /// <summary>The cell data an override sets.</summary>
        public static void Apply(OverrideKind kind, out bool hasFloor, out CellObstacle obstacle)
        {
            hasFloor = kind != OverrideKind.NoFloor;
            switch (kind)
            {
                case OverrideKind.Wall: obstacle = CellObstacle.Wall; break;
                case OverrideKind.Glass: obstacle = CellObstacle.Glass; break;
                case OverrideKind.LowObstacle: obstacle = CellObstacle.LowObstacle; break;
                default: obstacle = CellObstacle.None; break;
            }
        }

        /// <summary>The gizmo colour used to show a kind of cell in the scene view.</summary>
        public static Color ColourOf(OverrideKind kind)
        {
            switch (kind)
            {
                case OverrideKind.Wall: return new Color(0.9f, 0.25f, 0.2f);
                case OverrideKind.Glass: return new Color(0.3f, 0.8f, 1f);
                case OverrideKind.LowObstacle: return new Color(1f, 0.8f, 0.2f);
                case OverrideKind.NoFloor: return new Color(0.2f, 0.2f, 0.2f);
                default: return new Color(0.4f, 0.9f, 0.4f);
            }
        }

        /// <summary>The gizmo colour for a baked cell.</summary>
        public static Color ColourOf(bool hasFloor, CellObstacle obstacle)
        {
            if (!hasFloor) return ColourOf(OverrideKind.NoFloor);
            if (obstacle.BlocksSight) return ColourOf(OverrideKind.Wall);
            if (obstacle.BlocksMovement) return obstacle.Cover == CoverLevel.None ? ColourOf(OverrideKind.Glass) : ColourOf(OverrideKind.LowObstacle);
            return ColourOf(OverrideKind.OpenFloor);
        }

        private static int rank(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.Wall: return 3;
                case SurfaceKind.Glass: return 2;
                case SurfaceKind.LowObstacle: return 1;
                default: return 0;
            }
        }
    }
}
