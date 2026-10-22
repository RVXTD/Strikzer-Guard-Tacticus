// Action-point move pricing and the windowed field by Claude (Anthropic), October 2026; to be reviewed
// by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>A planned move, filled by <see cref="MovementSystem.PlanMove"/>. Reuse one instance to avoid allocations.</summary>
    public sealed class MovePlan
    {
        /// <summary>
        /// Distance to the destination: the distance field's value at the destination cell, in metres;
        /// <see cref="float.PositiveInfinity"/> if the destination is unreachable or beyond the field's range.
        /// </summary>
        public float Cost;
        /// <summary>Action points the move costs: 1 within the mover's budget, 2 within twice it.</summary>
        public int ActionPointCost;
        /// <summary>
        /// Length of <see cref="Path"/> in metres. The path follows the field's gradient and is pulled tight round
        /// wall corners, so it stays close to <see cref="Cost"/> (which is measured to the destination cell's centre).
        /// This is measured, not proven: across tens of thousands of random moves no path exceeded the distance the
        /// move's action points buy, and fewer than 1% exceeded <see cref="Cost"/> (by at most about 0.4 m).
        /// </summary>
        public float PathLength;
        /// <summary>Waypoints from the mover's position to the destination.</summary>
        public readonly List<Vector2> Path = new List<Vector2>(16);
        /// <summary>Where hostile reactions would trigger along <see cref="Path"/>, nearest first.</summary>
        public readonly List<ZoneEntry> Interrupts = new List<ZoneEntry>(4);

        /// <summary>Empties the plan.</summary>
        public void Clear()
        {
            Cost = 0f;
            ActionPointCost = 0;
            PathLength = 0f;
            Path.Clear();
            Interrupts.Clear();
        }
    }

    /// <summary>
    /// Owns each unit's movement budget and prices moves in action points: a destination whose distance
    /// field value is within the budget costs 1 point, within twice the budget 2 points. Distance a move
    /// leaves unused is lost. The field is only built out to twice the budget plus one cell, which is all a
    /// move can use and enough for the movement locus to draw its outer ring.
    /// </summary>
    public sealed class MovementSystem
    {
        private const float tolerance = 1e-4f;

        private readonly UnitRegistry units;
        private readonly SpatialSystem spatial;
        private readonly float[] baseBudget;
        private readonly float[] budget;

        /// <summary>Creates the system for the units in <paramref name="units"/>, using <paramref name="spatial"/> for geometry.</summary>
        public MovementSystem(UnitRegistry units, SpatialSystem spatial)
        {
            this.units = units;
            this.spatial = spatial;
            baseBudget = new float[units.Capacity];
            budget = new float[units.Capacity];
        }

        /// <summary>Records a new unit's base movement budget: metres covered by one action point of movement.</summary>
        public void AddUnit(UnitId id, float baseMovementBudget)
        {
            units.Require(id);
            baseBudget[id.Index] = baseMovementBudget > 0f ? baseMovementBudget : 0f; // NaN counts as zero
            budget[id.Index] = baseBudget[id.Index];
        }

        /// <summary>Base movement budget, before status modifiers.</summary>
        public float GetBaseBudget(UnitId id)
        {
            units.Require(id);
            return baseBudget[id.Index];
        }

        /// <summary>Movement budget for the unit's current (or most recent) turn, in metres per action point.</summary>
        public float GetBudget(UnitId id)
        {
            units.Require(id);
            return budget[id.Index];
        }

        /// <summary>The distance the unit's field is built out to: twice its budget plus one cell, in metres.</summary>
        public float GetFieldRange(UnitId id)
        {
            units.Require(id);
            return 2f * budget[id.Index] + spatial.CellSize;
        }

        /// <summary>Rebuilds the distance field for the unit at its current position, out to <see cref="GetFieldRange"/>.</summary>
        public void RebuildField(UnitId id) => spatial.RebuildField(id, GetFieldRange(id));

        /// <summary>Sets a unit's budget for the turn it is starting (negative or NaN counts as zero).</summary>
        public void BeginTurn(UnitId id, float turnBudget)
        {
            units.Require(id);
            budget[id.Index] = turnBudget > 0f ? turnBudget : 0f;
        }

        /// <summary>
        /// Plans a move without changing any state. On success returns <see cref="RejectReason.None"/> with
        /// the path, its interrupts and its <see cref="MovePlan.ActionPointCost"/> in <paramref name="plan"/>.
        /// A destination farther than twice the budget returns <see cref="RejectReason.InsufficientMovement"/>;
        /// one that costs more points than <paramref name="availableActionPoints"/> returns
        /// <see cref="RejectReason.NotEnoughActionPoints"/>. In both cases <see cref="MovePlan.Cost"/> is still set
        /// (to infinity when the destination lies beyond the field's range). A destination the field does not reach
        /// is <see cref="RejectReason.Unreachable"/> if walls, gaps or other units cut it off from the mover, or if the
        /// field was not cut off at its range; otherwise it is reachable by a longer walk, and the move is
        /// <see cref="RejectReason.InsufficientMovement"/>.
        /// </summary>
        public RejectReason PlanMove(UnitId mover, Vector2 destination, int availableActionPoints, MovePlan plan)
        {
            units.Require(mover);
            plan.Clear();
            if (availableActionPoints <= 0)
                return RejectReason.NotEnoughActionPoints;
            if (spatial.FieldOwner != mover || spatial.FieldRange < GetFieldRange(mover))
                RebuildField(mover);

            if (!spatial.TryGetCell(destination, out int cell) || !spatial.IsPassableForFieldOwner(cell))
                return RejectReason.DestinationBlocked;

            plan.Cost = spatial.Field[cell];
            if (float.IsPositiveInfinity(plan.Cost))
            {
                bool mayBeFarther = spatial.FieldIsTruncated && spatial.IsConnectedToFieldOwner(cell);
                return mayBeFarther ? RejectReason.InsufficientMovement : RejectReason.Unreachable;
            }
            float turnBudget = budget[mover.Index];
            if (plan.Cost <= turnBudget + tolerance) plan.ActionPointCost = 1;
            else if (plan.Cost <= 2f * turnBudget + tolerance) plan.ActionPointCost = 2;
            else return RejectReason.InsufficientMovement;
            if (plan.ActionPointCost > availableActionPoints)
                return RejectReason.NotEnoughActionPoints;
            if (!spatial.TryBuildPath(destination, plan.Path))
                return RejectReason.Unreachable;

            plan.PathLength = SpatialSystem.PathLength(plan.Path);
            spatial.FindZoneEntries(mover, plan.Path, plan.Interrupts);
            return RejectReason.None;
        }
    }
}
