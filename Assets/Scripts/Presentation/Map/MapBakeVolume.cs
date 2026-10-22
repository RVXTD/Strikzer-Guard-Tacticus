// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The area the map bake samples: an axis-aligned box centred on the transform (rotation ignored) whose bottom
    /// face is the floor. Holds the grid's cell size, the sampling heights and the asset the bake writes to. While
    /// this object (not the asset) is selected, it draws the baked cells just above the box's top face in the scene
    /// view, so cells inside walls are not hidden by the wall geometry and the result can be checked by eye.
    /// </summary>
    public sealed class MapBakeVolume : MonoBehaviour
    {
        /// <summary>
        /// Size of the baked area in metres; the bottom face is the floor. The height only places the floor and draws
        /// the box: the bake samples a thin slab at the floor and the obstacle band above it, whatever the height.
        /// </summary>
        [SerializeField] private Vector3 size = new Vector3(20f, 3f, 20f);
        /// <summary>Edge length of one grid cell in metres.</summary>
        [SerializeField, Min(0.05f)] private float cellSize = 0.5f;
        /// <summary>Half the thickness of the slab, centred on the floor, in which floor geometry is looked for.</summary>
        [SerializeField, Min(0.001f)] private float floorProbeHalfThickness = 0.05f;
        /// <summary>Height above the floor where the obstacle band starts (above kerbs and floor seams).</summary>
        [SerializeField, Min(0f)] private float obstacleBandBottom = 0.1f;
        /// <summary>Height above the floor where the obstacle band ends (below overhangs and signage).</summary>
        [SerializeField, Min(0f)] private float obstacleBandTop = 2f;
        /// <summary>The asset the bake writes to.</summary>
        [SerializeField] private MapGridAsset target;

        /// <summary>Size of the baked area in metres.</summary>
        public Vector3 Size { get => size; set => size = value; }
        /// <summary>Edge length of one grid cell in metres.</summary>
        public float CellSize { get => cellSize; set => cellSize = value; }
        /// <summary>Half the thickness of the floor probe slab.</summary>
        public float FloorProbeHalfThickness { get => floorProbeHalfThickness; set => floorProbeHalfThickness = value; }
        /// <summary>Height above the floor where the obstacle band starts.</summary>
        public float ObstacleBandBottom { get => obstacleBandBottom; set => obstacleBandBottom = value; }
        /// <summary>Height above the floor where the obstacle band ends.</summary>
        public float ObstacleBandTop { get => obstacleBandTop; set => obstacleBandTop = value; }
        /// <summary>The asset the bake writes to.</summary>
        public MapGridAsset Target { get => target; set => target = value; }

        /// <summary>World height of the floor: the box's bottom face.</summary>
        public float FloorHeight => transform.position.y - 0.5f * size.y;

        /// <summary>World position (X, Z) of the corner of cell (0, 0): the box's minimum X and Z.</summary>
        public Vector2 GridOrigin => new Vector2(transform.position.x - 0.5f * size.x, transform.position.z - 0.5f * size.z);

        /// <summary>Number of cells along world X (the box is rounded up to whole cells).</summary>
        public int GridWidth => Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize - 1e-4f));

        /// <summary>Number of cells along world Z.</summary>
        public int GridHeight => Mathf.Max(1, Mathf.CeilToInt(size.z / cellSize - 1e-4f));

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position, size);
        }

        private void OnDrawGizmosSelected()
        {
            if (target == null || target.CellCount == 0) return;
            Vector3 cube = new Vector3(target.CellSize * 0.9f, 0.02f, target.CellSize * 0.9f);
            float markerHeight = transform.position.y + 0.5f * size.y + 0.02f; // above the geometry, not inside it
            for (int y = 0; y < target.Height; y++)
            {
                for (int x = 0; x < target.Width; x++)
                {
                    bool hasFloor = target.HasFloor(x, y);
                    var obstacle = target.GetObstacle(x, y);
                    if (hasFloor && !obstacle.BlocksMovement && !obstacle.BlocksSight) continue; // open floor stays clear
                    Gizmos.color = MapCellClassifier.ColourOf(hasFloor, obstacle);
                    Vector3 centre = target.CellCenter(x, y);
                    centre.y = markerHeight;
                    Gizmos.DrawCube(centre, cube);
                }
            }
        }
    }
}
