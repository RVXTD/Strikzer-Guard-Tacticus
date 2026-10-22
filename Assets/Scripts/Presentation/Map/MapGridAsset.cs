// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using System;
using Game.Rules;
using UnityEngine;
using NumericsVector2 = System.Numerics.Vector2;

namespace Game.Presentation
{
    /// <summary>
    /// A baked map grid: which cells have floor and what stands on each, as the rules layer reads it. Produced by
    /// the map bake tool from tagged level geometry; <c>EncounterRoot</c> loads it into an <see cref="EncounterSetup"/>.
    /// Cell (x, y) covers world X from Origin.x + x * CellSize and world Z from Origin.y + y * CellSize, with index
    /// y * Width + x, exactly as in <see cref="SpatialSystem"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Auto Buster/Map Grid", fileName = "MapGrid")]
    public sealed class MapGridAsset : ScriptableObject
    {
        /// <summary>Number of cells along world X.</summary>
        [SerializeField] private int width;
        /// <summary>Number of cells along world Z.</summary>
        [SerializeField] private int height;
        /// <summary>Edge length of one cell in metres.</summary>
        [SerializeField] private float cellSize = 0.5f;
        /// <summary>World position (X, Z) of the corner of cell (0, 0).</summary>
        [SerializeField] private Vector2 origin;
        /// <summary>World height (Y) of the floor the grid was baked at.</summary>
        [SerializeField] private float floorHeight;
        /// <summary>Per cell: true if it has floor.</summary>
        [SerializeField] private bool[] floor = new bool[0];
        /// <summary>Per cell: what stands on it.</summary>
        [SerializeField] private CellObstacle[] obstacles = new CellObstacle[0];

        /// <summary>Number of cells along world X.</summary>
        public int Width => width;
        /// <summary>Number of cells along world Z.</summary>
        public int Height => height;
        /// <summary>Edge length of one cell in metres.</summary>
        public float CellSize => cellSize;
        /// <summary>World position (X, Z) of the corner of cell (0, 0).</summary>
        public Vector2 Origin => origin;
        /// <summary>World height (Y) of the floor the grid was baked at.</summary>
        public float FloorHeight => floorHeight;
        /// <summary>Total number of cells.</summary>
        public int CellCount => width * height;

        /// <summary>Clears the grid to the given size: no floor and no obstacles anywhere.</summary>
        public void Resize(int newWidth, int newHeight, float newCellSize, Vector2 newOrigin, float newFloorHeight)
        {
            if (newWidth <= 0 || newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth), "A map grid needs at least one cell.");
            if (!(newCellSize > 0f)) throw new ArgumentOutOfRangeException(nameof(newCellSize));
            width = newWidth;
            height = newHeight;
            cellSize = newCellSize;
            origin = newOrigin;
            floorHeight = newFloorHeight;
            floor = new bool[width * height];
            obstacles = new CellObstacle[width * height];
        }

        /// <summary>True if the cell has floor.</summary>
        public bool HasFloor(int x, int y) => floor[index(x, y)];

        /// <summary>What stands on the cell.</summary>
        public CellObstacle GetObstacle(int x, int y) => obstacles[index(x, y)];

        /// <summary>Sets whether a cell has floor and what stands on it.</summary>
        public void SetCell(int x, int y, bool hasFloor, CellObstacle obstacle)
        {
            int cell = index(x, y);
            floor[cell] = hasFloor;
            obstacles[cell] = obstacle;
        }

        /// <summary>World centre of a cell, at floor height.</summary>
        public Vector3 CellCenter(int x, int y) =>
            new Vector3(origin.x + (x + 0.5f) * cellSize, floorHeight, origin.y + (y + 0.5f) * cellSize);

        /// <summary>
        /// Copies the grid into an encounter setup (size, cell size, origin, floor and obstacles; the arrays are
        /// copied). Throws <see cref="InvalidOperationException"/> if the grid has not been baked.
        /// </summary>
        public void ApplyTo(EncounterSetup setup)
        {
            if (CellCount == 0 || floor.Length != CellCount || obstacles.Length != CellCount)
                throw new InvalidOperationException($"Map grid '{name}' has not been baked.");
            setup.Width = width;
            setup.Height = height;
            setup.CellSize = cellSize;
            setup.Origin = new NumericsVector2(origin.x, origin.y);
            setup.Floor = (bool[])floor.Clone();
            setup.Obstacles = (CellObstacle[])obstacles.Clone();
        }

        private int index(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {y}) is outside the {width} x {height} grid.");
            return y * width + x;
        }
    }
}
