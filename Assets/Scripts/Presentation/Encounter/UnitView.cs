// Written by Claude (Anthropic), October 2026; the hover highlight added for step 8a; to be reviewed by Alexander
// Stangl per the course syllabus.

using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Shows one unit: where it stands, which way it faces, its team colour, and whether it is hit, downed, defeated
    /// or taking its turn. <see cref="EventPlayback"/> drives everything that follows the event log; the only other
    /// caller is <see cref="PreviewView"/>, for the hover highlight, which shows no game state. It holds the unit's <see cref="UnitId"/> and never touches another unit's components. Colours are
    /// set through a <see cref="MaterialPropertyBlock"/>, so shared materials are left untouched.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        private static readonly int baseColourId = Shader.PropertyToID("_BaseColor");
        private static readonly int colourId = Shader.PropertyToID("_Color");
        private static readonly int rimColourId = Shader.PropertyToID("_RimColor");

        /// <summary>Renderers to colour; found among the children when left empty.</summary>
        [SerializeField] private Renderer[] renderers = new Renderer[0];
        /// <summary>Colour of player units.</summary>
        [SerializeField] private Color playerColour = new Color(0.25f, 0.55f, 1f);
        /// <summary>Colour of enemy units.</summary>
        [SerializeField] private Color enemyColour = new Color(0.95f, 0.3f, 0.25f);
        /// <summary>Colour while downed.</summary>
        [SerializeField] private Color downedColour = new Color(0.35f, 0.35f, 0.35f);
        /// <summary>Colour flashed when hit.</summary>
        [SerializeField] private Color hitColour = Color.white;
        /// <summary>How much lighter the unit whose turn it is looks (0 to 1).</summary>
        [SerializeField, Range(0f, 1f)] private float activeHighlight = 0.35f;

        private MaterialPropertyBlock block;
        private Color teamColour;
        private bool hit, active;
        private Quaternion standingRotation;
        private Material highlight;
        private Color highlightColour;

        /// <summary>The unit this view shows; <see cref="UnitId.None"/> until bound.</summary>
        public UnitId Id { get; private set; }
        /// <summary>True while the unit is downed.</summary>
        public bool IsDowned { get; private set; }
        /// <summary>True once the unit is defeated.</summary>
        public bool IsDefeated { get; private set; }
        /// <summary>True while the hover highlight is shown.</summary>
        public bool IsHighlighted => highlight != null;
        /// <summary>The colour last given to <see cref="SetHighlight"/>.</summary>
        public Color HighlightColour => highlightColour;

        /// <summary>Ties the view to its unit and shows it in its team colour at its starting position.</summary>
        public void Bind(UnitId id, Team team, Vector3 position)
        {
            Id = id;
            teamColour = team == Team.Player ? playerColour : enemyColour;
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
            transform.position = position;
            standingRotation = transform.rotation;
            refreshColour();
        }

        /// <summary>Moves the unit to a world position.</summary>
        public void SetPosition(Vector3 position) => transform.position = position;

        /// <summary>Turns the unit to face along a world direction (its height is ignored).</summary>
        public void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-8f || IsDowned) return;
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            standingRotation = transform.rotation;
        }

        /// <summary>Shows or clears the hit flash.</summary>
        public void ShowHit(bool on)
        {
            hit = on;
            refreshColour();
        }

        /// <summary>Marks the unit whose turn it is.</summary>
        public void SetActiveTurn(bool on)
        {
            active = on;
            refreshColour();
        }

        /// <summary>Tips the unit over and greys it out, or stands it back up when revived.</summary>
        public void SetDowned(bool downed)
        {
            IsDowned = downed;
            transform.rotation = downed ? standingRotation * Quaternion.Euler(90f, 0f, 0f) : standingRotation;
            refreshColour();
        }

        /// <summary>Hides the unit for good.</summary>
        public void SetDefeated()
        {
            ClearHighlight();
            IsDefeated = true;
            foreach (Renderer target in renderers) if (target != null) target.enabled = false;
        }

        /// <summary>
        /// Shows the hover highlight: <paramref name="rim"/> (a material with the hover rim shader, shared by every
        /// unit) is drawn over the unit's own materials in <paramref name="colour"/>. Ignored for a defeated unit.
        /// </summary>
        public void SetHighlight(Material rim, Color colour)
        {
            if (rim == null || IsDefeated) return;
            if (highlight != rim)
            {
                ClearHighlight();
                highlight = rim;
                foreach (Renderer target in renderers)
                {
                    if (target == null) continue;
                    Material[] own = target.sharedMaterials;
                    var withRim = new Material[own.Length + 1];
                    own.CopyTo(withRim, 0);
                    withRim[own.Length] = rim;
                    target.sharedMaterials = withRim;
                }
            }
            highlightColour = colour;
            refreshColour();
        }

        /// <summary>Removes the hover highlight, leaving the unit's own materials as they were.</summary>
        public void ClearHighlight()
        {
            if (highlight == null) return;
            foreach (Renderer target in renderers)
            {
                if (target == null) continue;
                Material[] withRim = target.sharedMaterials;
                int kept = 0;
                foreach (Material material in withRim) if (material != highlight) kept++;
                var own = new Material[kept];
                kept = 0;
                foreach (Material material in withRim) if (material != highlight) own[kept++] = material;
                target.sharedMaterials = own;
            }
            highlight = null;
        }

        private void refreshColour()
        {
            if (renderers == null) return;
            Color colour = hit ? hitColour : IsDowned ? downedColour : teamColour;
            if (active && !hit) colour = Color.Lerp(colour, Color.white, activeHighlight);
            if (block == null) block = new MaterialPropertyBlock();
            foreach (Renderer target in renderers)
            {
                if (target == null) continue;
                target.GetPropertyBlock(block);
                block.SetColor(baseColourId, colour);
                block.SetColor(colourId, colour);
                block.SetColor(rimColourId, highlightColour);
                target.SetPropertyBlock(block);
            }
        }
    }
}
