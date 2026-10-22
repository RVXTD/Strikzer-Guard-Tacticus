// Written by Claude (Anthropic), October 2026; a profiler marker added before step 8 and the hover highlight for
// step 8a; to be reviewed by Alexander Stangl per the course syllabus.

using System.Collections.Generic;
using Game.Rules;
using Unity.Profiling;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws the current <see cref="TurnPreview"/> on the floor with line renderers: a move's path coloured by its
    /// cost with rings where it enters a zone of control, a line to an attack, pickup or trade target, and an
    /// ability's radius and affected units. A refused preview is drawn in the rejected colour. The unit the preview
    /// is about (the hovered unit, or an ability's target) gets a fresnel rim (<see cref="RimShaderName"/>) coloured
    /// by what a click would do: attack, trade, or just inspect. Nothing is drawn or highlighted while events play
    /// back or the AI acts. The movement locus and zones are drawn by <see cref="EncounterOverlay"/>.
    /// </summary>
    [RequireComponent(typeof(EncounterInput))]
    public sealed class PreviewView : MonoBehaviour
    {
        /// <summary>A move costing one point, or any other accepted preview.</summary>
        [SerializeField] private Color onePointColour = new Color(0.3f, 0.8f, 1f);
        /// <summary>A move costing two points.</summary>
        [SerializeField] private Color twoPointColour = new Color(1f, 0.8f, 0.2f);
        /// <summary>A preview the rules layer would refuse.</summary>
        [SerializeField] private Color rejectedColour = new Color(1f, 0.25f, 0.2f);
        /// <summary>Where a move enters an enemy's zone of control.</summary>
        [SerializeField] private Color warningColour = new Color(1f, 0.1f, 0.6f);
        /// <summary>Line width in metres.</summary>
        [SerializeField, Min(0.001f)] private float lineWidth = 0.06f;
        /// <summary>Height of the lines above the floor, in metres.</summary>
        [SerializeField, Min(0f)] private float heightAboveFloor = 0.05f;
        /// <summary>Material for the lines; a vertex-coloured default is used if left empty.</summary>
        [SerializeField] private Material lineMaterial;
        /// <summary>The hover rim shader; Set Up Scene assigns it, so builds include it.</summary>
        [SerializeField] private Shader rimShader;
        /// <summary>Rim of a unit a click would attack (or hit with an ability).</summary>
        [SerializeField] private Color attackColour = new Color(1f, 0.35f, 0.15f);
        /// <summary>Rim of an ally a click would hand rounds to (or help with an ability).</summary>
        [SerializeField] private Color tradeColour = new Color(0.3f, 1f, 0.45f);
        /// <summary>Rim of a unit that is only being inspected.</summary>
        [SerializeField] private Color inspectColour = new Color(0.9f, 0.9f, 0.9f);

        /// <summary>Name of the hover rim shader, used when <see cref="RimShader"/> is left empty.</summary>
        public const string RimShaderName = "Auto Buster/Hover Rim";

        private static readonly ProfilerMarker drawMarker = new ProfilerMarker("Encounter.PreviewView");
        private const int ringSegments = 32;
        private readonly List<LineRenderer> pool = new List<LineRenderer>();
        private readonly List<Vector3> points = new List<Vector3>();
        private EncounterInput input;
        private int used;
        private Material rimMaterial;
        private bool warnedAboutRim;
        private UnitView highlighted;

        /// <summary>The hover rim shader.</summary>
        public Shader RimShader { get => rimShader; set => rimShader = value; }

        /// <summary>The view currently highlighted, or null.</summary>
        public UnitView Highlighted => highlighted;

        /// <summary>Redraws the preview and updates the highlight. Called every frame; tests call it directly.</summary>
        public void Refresh()
        {
            using ProfilerMarker.AutoScope scope = drawMarker.Auto();
            if (input == null) input = GetComponent<EncounterInput>();
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
            used = 0;
            draw();
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
            refreshHighlight();
        }

        private void LateUpdate() => Refresh();

        private void refreshHighlight()
        {
            TurnController controller = input.Controller;
            EncounterRoot root = input.Root;
            UnitView target = null;
            Color colour = inspectColour;
            if (controller != null && root != null && controller.CanAct && !controller.Preview.Unit.IsNone)
            {
                TurnPreview preview = controller.Preview;
                Encounter encounter = root.Encounter;
                bool accepted = preview.Verdict == RejectReason.None;
                bool hostile = encounter.Units.AreHostile(encounter.Flow.ActiveUnit, preview.Unit);
                switch (preview.Kind)
                {
                    case PreviewKind.Inspect: colour = inspectColour; break;
                    case PreviewKind.Attack: colour = accepted ? attackColour : rejectedColour * 0.6f; break;
                    case PreviewKind.Trade: colour = accepted ? tradeColour : rejectedColour * 0.6f; break;
                    case PreviewKind.Ability: colour = !accepted ? rejectedColour * 0.6f : hostile ? attackColour : tradeColour; break;
                    default: preview = null; break;
                }
                if (preview != null) root.TryGetView(preview.Unit, out target);
            }

            if (target != highlighted && highlighted != null) highlighted.ClearHighlight();
            highlighted = target;
            if (target == null) return;
            Material rim = rimMaterialOrNull();
            if (rim != null) target.SetHighlight(rim, colour);
        }

        private Material rimMaterialOrNull()
        {
            if (rimMaterial != null) return rimMaterial;
            Shader shader = rimShader != null ? rimShader : Shader.Find(RimShaderName);
            if (shader == null)
            {
                if (!warnedAboutRim) Debug.LogWarning($"[Preview] Shader '{RimShaderName}' not found; run Tools ▸ Encounter ▸ Set Up Scene.", this);
                warnedAboutRim = true;
                return null;
            }
            rimMaterial = new Material(shader) { name = "Hover rim" };
            return rimMaterial;
        }

        private void draw()
        {
            TurnController controller = input.Controller;
            EncounterRoot root = input.Root;
            if (controller == null || root == null || root.Map == null) return;
            TurnPreview preview = controller.Preview;
            if (preview.Kind == PreviewKind.None || preview.Kind == PreviewKind.Inspect) return;

            Encounter encounter = root.Encounter;
            float height = root.Map.FloorHeight + heightAboveFloor;
            bool accepted = preview.Verdict == RejectReason.None;
            Color colour = accepted ? onePointColour : rejectedColour;
            System.Numerics.Vector2 from = encounter.Spatial.GetPosition(encounter.Flow.ActiveUnit);

            switch (preview.Kind)
            {
                case PreviewKind.Move:
                    if (accepted && preview.Move.ActionPointCost >= 2) colour = twoPointColour;
                    points.Clear();
                    foreach (System.Numerics.Vector2 corner in preview.Move.Path) points.Add(GridSpace.ToWorld(corner, height));
                    if (points.Count >= 2) polyline(colour, false);
                    ring(preview.Point, 0.35f, height, colour);
                    foreach (ZoneEntry entry in preview.Move.Interrupts) ring(entry.Position, 0.25f, height, warningColour);
                    break;

                case PreviewKind.Attack:
                case PreviewKind.Trade:
                    segment(from, encounter.Spatial.GetPosition(preview.Unit), height, colour);
                    ring(encounter.Spatial.GetPosition(preview.Unit), 0.6f, height, colour);
                    break;

                case PreviewKind.PickUp:
                    if (encounter.Resources.TryGetPile(preview.PileId, out AmmoPile pile))
                    {
                        segment(from, pile.Position, height, colour);
                        ring(pile.Position, 0.3f, height, colour);
                    }
                    break;

                case PreviewKind.Ability:
                    AbilityDefinition definition = encounter.Abilities.GetDefinition(preview.Ability);
                    segment(from, preview.Point, height, colour);
                    ring(preview.Point, definition.Radius > 0f ? definition.Radius : 0.35f, height, colour);
                    foreach (UnitId affected in preview.Affected) ring(encounter.Spatial.GetPosition(affected), 0.6f, height, colour);
                    break;
            }
        }

        private void segment(System.Numerics.Vector2 from, System.Numerics.Vector2 to, float height, Color colour)
        {
            points.Clear();
            points.Add(GridSpace.ToWorld(from, height));
            points.Add(GridSpace.ToWorld(to, height));
            polyline(colour, false);
        }

        private void ring(System.Numerics.Vector2 centre, float radius, float height, Color colour)
        {
            points.Clear();
            Vector3 middle = GridSpace.ToWorld(centre, height);
            for (int i = 0; i < ringSegments; i++)
            {
                float angle = i * 2f * Mathf.PI / ringSegments;
                points.Add(middle + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
            polyline(colour, true);
        }

        private void polyline(Color colour, bool loop)
        {
            LineRenderer line = next();
            line.loop = loop;
            line.startColor = line.endColor = colour;
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        }

        private LineRenderer next()
        {
            if (used == pool.Count)
            {
                var holder = new GameObject("Preview line");
                holder.transform.SetParent(transform, false);
                var created = holder.AddComponent<LineRenderer>();
                created.useWorldSpace = true;
                created.sharedMaterial = lineMaterial;
                created.widthMultiplier = lineWidth;
                created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                created.receiveShadows = false;
                pool.Add(created);
            }
            LineRenderer line = pool[used++];
            line.enabled = true;
            return line;
        }
    }
}
