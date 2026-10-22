// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using UnityEngine;

namespace Game.Presentation
{
    /// <summary>What a <see cref="MapOverrideVolume"/> sets its cells to.</summary>
    public enum OverrideKind
    {
        /// <summary>Open floor: clears any obstacle.</summary>
        OpenFloor,
        /// <summary>A wall.</summary>
        Wall,
        /// <summary>Glass.</summary>
        Glass,
        /// <summary>A low obstacle.</summary>
        LowObstacle,
        /// <summary>No floor: a gap, pit or out-of-bounds area.</summary>
        NoFloor,
    }

    /// <summary>
    /// A designer override for the map bake: every cell whose centre lies inside this axis-aligned box (centred on
    /// the transform, rotation ignored) is set to <see cref="Kind"/> after the geometry has been sampled. Later
    /// volumes in the hierarchy win where they overlap.
    /// </summary>
    public sealed class MapOverrideVolume : MonoBehaviour
    {
        /// <summary>Size of the box in metres.</summary>
        [SerializeField] private Vector3 size = new Vector3(1f, 2f, 1f);
        /// <summary>What the cells inside become.</summary>
        [SerializeField] private OverrideKind kind = OverrideKind.OpenFloor;

        /// <summary>Size of the box in metres.</summary>
        public Vector3 Size { get => size; set => size = value; }
        /// <summary>What the cells inside become.</summary>
        public OverrideKind Kind { get => kind; set => kind = value; }

        /// <summary>The box in world space.</summary>
        public Bounds WorldBounds => new Bounds(transform.position, size);

        /// <summary>True if a cell centre (world X, Z) lies inside the box's footprint.</summary>
        public bool Covers(Vector3 cellCentre)
        {
            Bounds bounds = WorldBounds;
            return cellCentre.x >= bounds.min.x && cellCentre.x <= bounds.max.x && cellCentre.z >= bounds.min.z && cellCentre.z <= bounds.max.z;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = MapCellClassifier.ColourOf(kind);
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
