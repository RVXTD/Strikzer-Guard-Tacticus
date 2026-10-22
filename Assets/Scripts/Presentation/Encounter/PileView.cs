// Written by Claude (Anthropic), October 2026; to be reviewed by Alexander Stangl per the course syllabus.

using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Marks an ammo pile on the floor: a small cube that grows with the number of rounds in it. Created and removed
    /// by <see cref="EventPlayback"/> as rounds land and are picked up.
    /// </summary>
    public sealed class PileView : MonoBehaviour
    {
        /// <summary>The rules layer's id for the pile.</summary>
        public int PileId { get; private set; }
        /// <summary>Rounds in the pile, as far as playback has got.</summary>
        public int Count { get; private set; }

        /// <summary>Creates a pile marker under <paramref name="parent"/> at a floor position.</summary>
        public static PileView Create(int pileId, Vector3 position, Transform parent)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = $"Ammo pile {pileId}";
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null) DestroyImmediateOrLater(collider); // markers are visuals only
            marker.transform.SetParent(parent, false);
            marker.transform.position = position;
            var view = marker.AddComponent<PileView>();
            view.PileId = pileId;
            return view;
        }

        /// <summary>Adds rounds to the pile and resizes the marker.</summary>
        public void Add(int rounds)
        {
            Count += rounds;
            float size = 0.12f + 0.04f * Mathf.Sqrt(Count);
            transform.localScale = new Vector3(size, size * 0.5f, size);
        }

        // Destroy is deferred in play mode; outside it (EditMode tests) objects must go at once.
        internal static void DestroyImmediateOrLater(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
