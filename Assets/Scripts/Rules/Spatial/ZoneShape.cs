using System;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>
    /// The region a zone of control covers at one moment: a disc of <see cref="Radius"/> around
    /// <see cref="Center"/>, optionally narrowed to a sector of half-angle <see cref="HalfAngleRadians"/>
    /// around <see cref="Facing"/>. A circle is the special case of a half-angle of pi or more.
    /// </summary>
    public readonly struct ZoneShape
    {
        /// <summary>The owner's position.</summary>
        public readonly Vector2 Center;
        /// <summary>Unit-length direction the sector opens around.</summary>
        public readonly Vector2 Facing;
        /// <summary>Reach in metres.</summary>
        public readonly float Radius;
        /// <summary>Half the opening angle, in radians; pi or more means a full circle.</summary>
        public readonly float HalfAngleRadians;
        /// <summary>True if the owner must also see a point for the zone to trigger there.</summary>
        public readonly bool RequiresLineOfSight;

        private readonly float cosHalfAngle;

        /// <summary>Builds the shape of a zone for an owner at <paramref name="center"/> facing <paramref name="facing"/>.</summary>
        public ZoneShape(Vector2 center, Vector2 facing, in ZoneSetup setup)
        {
            Center = center;
            Facing = facing.LengthSquared() > 1e-12f ? Vector2.Normalize(facing) : Vector2.UnitX;
            Radius = setup.Radius;
            HalfAngleRadians = MathF.Min(setup.HalfAngleDegrees, 180f) * (MathF.PI / 180f);
            RequiresLineOfSight = setup.RequiresLineOfSight;
            cosHalfAngle = MathF.Cos(HalfAngleRadians);
        }

        /// <summary>True if the zone covers the whole disc.</summary>
        public bool IsFullCircle => HalfAngleRadians >= MathF.PI - 1e-6f;

        /// <summary>True if the point lies inside the zone (boundary included).</summary>
        public bool Contains(Vector2 point)
        {
            Vector2 v = point - Center;
            float lengthSquared = v.LengthSquared();
            if (lengthSquared > Radius * Radius) return false;
            if (IsFullCircle || lengthSquared < 1e-12f) return true;
            return Vector2.Dot(v, Facing) >= cosHalfAngle * MathF.Sqrt(lengthSquared);
        }

        /// <summary>
        /// Finds where the segment from <paramref name="a"/> to <paramref name="b"/> first enters the zone,
        /// as the parameter t in [0, 1] of the point a + t (b - a). Returns false if the segment starts
        /// inside the zone or never enters it.
        /// </summary>
        public bool TryFindEntry(Vector2 a, Vector2 b, out float t)
        {
            t = 0f;
            if (Contains(a)) return false;

            Span<float> candidates = stackalloc float[4];
            int count = 0;

            // Crossings of the circle |a + t d - c| = r.
            Vector2 d = b - a;
            Vector2 f = a - Center;
            float qa = Vector2.Dot(d, d);
            if (qa < 1e-12f) return false;
            float qb = 2f * Vector2.Dot(f, d);
            float qc = Vector2.Dot(f, f) - Radius * Radius;
            float discriminant = qb * qb - 4f * qa * qc;
            if (discriminant >= 0f)
            {
                float root = MathF.Sqrt(discriminant);
                addCandidate(candidates, ref count, (-qb - root) / (2f * qa));
                addCandidate(candidates, ref count, (-qb + root) / (2f * qa));
            }

            // Crossings of the sector's two straight edges.
            if (!IsFullCircle)
            {
                addEdgeCrossing(candidates, ref count, a, d, rotate(Facing, HalfAngleRadians));
                addEdgeCrossing(candidates, ref count, a, d, rotate(Facing, -HalfAngleRadians));
            }

            // Sort the few candidates, then take the first crossing after which the segment is inside.
            for (int i = 1; i < count; i++)
            {
                float key = candidates[i];
                int j = i - 1;
                while (j >= 0 && candidates[j] > key) { candidates[j + 1] = candidates[j]; j--; }
                candidates[j + 1] = key;
            }
            for (int i = 0; i < count; i++)
            {
                float next = i + 1 < count ? candidates[i + 1] : 1f;
                float probe = next > candidates[i] ? 0.5f * (candidates[i] + next) : candidates[i];
                if (Contains(a + probe * d))
                {
                    t = candidates[i];
                    return true;
                }
            }
            return false;
        }

        private void addEdgeCrossing(Span<float> candidates, ref int count, Vector2 a, Vector2 d, Vector2 edgeDirection)
        {
            Vector2 edge = edgeDirection * Radius;
            float denominator = cross(d, edge);
            if (MathF.Abs(denominator) < 1e-12f) return;
            Vector2 offset = Center - a;
            float t = cross(offset, edge) / denominator;
            float u = cross(offset, d) / denominator;
            if (u >= 0f && u <= 1f) addCandidate(candidates, ref count, t);
        }

        private static void addCandidate(Span<float> candidates, ref int count, float t)
        {
            if (t >= 0f && t <= 1f && count < candidates.Length) candidates[count++] = t;
        }

        private static float cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

        private static Vector2 rotate(Vector2 v, float radians)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            return new Vector2(c * v.X - s * v.Y, s * v.X + c * v.Y);
        }
    }

    /// <summary>The point where a planned path first enters one hostile unit's zone of control.</summary>
    public struct ZoneEntry
    {
        /// <summary>The unit whose zone is entered; it gets to react there.</summary>
        public UnitId Owner;
        /// <summary>Where the mover is when the reaction triggers.</summary>
        public Vector2 Position;
        /// <summary>Distance along the path from its start to <see cref="Position"/>, in metres.</summary>
        public float PathDistance;
        /// <summary>Index of the path segment containing <see cref="Position"/> (segment k runs from point k to k + 1).</summary>
        public int SegmentIndex;
    }
}
