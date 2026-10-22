// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using UnityEngine;

namespace Game.Presentation
{
    /// <summary>What a piece of level geometry is, for the map bake.</summary>
    public enum SurfaceKind
    {
        /// <summary>Walkable floor.</summary>
        Floor,
        /// <summary>A wall: blocks movement and sight, gives full cover.</summary>
        Wall,
        /// <summary>Glass (storefronts): blocks movement but not sight, gives no cover.</summary>
        Glass,
        /// <summary>A low obstacle (bench, planter, kiosk counter): blocks movement, gives half cover.</summary>
        LowObstacle,
        /// <summary>Geometry the bake ignores (ceilings, signage, decoration).</summary>
        Ignore,
    }

    /// <summary>
    /// Tags level geometry for the map bake: the object and all of its children count as this kind, unless a
    /// child has its own <see cref="MapSurface"/>. Only used in the editor; it does nothing at runtime.
    /// </summary>
    public sealed class MapSurface : MonoBehaviour
    {
        /// <summary>What this geometry is.</summary>
        [SerializeField] private SurfaceKind kind = SurfaceKind.Wall;

        /// <summary>What this geometry is.</summary>
        public SurfaceKind Kind { get => kind; set => kind = value; }
    }
}
