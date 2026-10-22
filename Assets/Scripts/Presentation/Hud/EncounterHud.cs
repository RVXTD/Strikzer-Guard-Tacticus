// Written by Claude (Anthropic), October 2026; the AI note added for step 7 and a profiler marker before step 8;
// to be reviewed by Alexander Stangl per the course syllabus.

using System.Collections.Generic;
using System.Text;
using Game.Rules;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.Presentation
{
    /// <summary>
    /// A minimal combat HUD, built in code on a screen-space canvas: the rest of the round's turn order, the active
    /// unit's numbers, the current preview (move cost, hit chance with its breakdown, or the hovered unit), a button
    /// per ability plus End Turn, the last refusal, and a banner when the encounter ends. Unit numbers are those of
    /// the last time playback caught up, so they never run ahead of what is shown. Placeholder art until step 8.
    /// </summary>
    public sealed class EncounterHud : MonoBehaviour
    {
        /// <summary>The input whose encounter and preview are shown; found on this object or in the scene if left empty.</summary>
        [SerializeField] private EncounterInput encounterInput;
        /// <summary>The HUD font; Unity's built-in font if left empty.</summary>
        [SerializeField] private Font font;
        /// <summary>Turn-strip colour for player units.</summary>
        [SerializeField] private Color playerColour = new Color(0.25f, 0.55f, 1f);
        /// <summary>Turn-strip colour for enemy units.</summary>
        [SerializeField] private Color enemyColour = new Color(0.95f, 0.3f, 0.25f);
        /// <summary>Background of the panels.</summary>
        [SerializeField] private Color panelColour = new Color(0f, 0f, 0f, 0.65f);
        /// <summary>Highlight of the ability being targeted.</summary>
        [SerializeField] private Color targetingColour = new Color(1f, 0.8f, 0.2f);
        /// <summary>How long a refusal stays on screen, in seconds.</summary>
        [SerializeField, Min(0.1f)] private float messageSeconds = 2f;

        private static readonly ProfilerMarker refreshMarker = new ProfilerMarker("Encounter.Hud");
        private readonly List<Image> tiles = new List<Image>();
        private readonly List<Button> abilityButtons = new List<Button>();
        private readonly StringBuilder text = new StringBuilder();
        private RectTransform canvasRoot, turnStrip, abilityBar;
        private Text roundText, unitText, previewText, messageText, bannerText;
        private Image previewPanel;
        private Button endTurnButton;
        private int seenRejections;
        private float messageShownAt = float.NegativeInfinity;

        /// <summary>The input whose encounter and preview are shown.</summary>
        public EncounterInput EncounterInput { get => encounterInput; set => encounterInput = value; }

        private void Start()
        {
            if (encounterInput == null) encounterInput = GetComponent<EncounterInput>();
            if (encounterInput == null) encounterInput = FindAnyObjectByType<EncounterInput>();
            build();
        }

        private void LateUpdate() => Refresh();

        /// <summary>Updates every element from the encounter and the input's preview. Called every frame; tests call it directly.</summary>
        public void Refresh()
        {
            using ProfilerMarker.AutoScope scope = refreshMarker.Auto();
            if (canvasRoot == null) build();
            EncounterRoot root = encounterInput != null ? encounterInput.Root : null;
            Encounter encounter = root != null ? root.Encounter : null;
            canvasRoot.gameObject.SetActive(encounter != null);
            if (encounter == null) return;

            TurnController controller = encounterInput.Controller;
            refreshMessage(controller);
            if (root.IsPlayingBack)
            {
                previewPanel.gameObject.SetActive(false);
                setButtonsInteractable(false);
                return; // keep the numbers of the last caught-up state until playback finishes
            }
            refreshTurnStrip(root, encounter);
            unitText.text = describeUnit(root, encounter, encounter.Flow.ActiveUnit) +
                            (controller != null && controller.AiControlsActiveUnit ? "\nAI is acting" : "");
            refreshPreview(root, encounter, controller);
            refreshAbilityBar(root, encounter, controller);
            bannerText.gameObject.SetActive(encounter.Flow.Phase == EncounterPhase.Ended);
            bannerText.text = encounter.Flow.Winner == Team.Player ? "Squad wins" : "Squad lost";
        }

        // ------------------------------------------------------------------------------------------ refresh

        private void refreshTurnStrip(EncounterRoot root, Encounter encounter)
        {
            roundText.text = $"Round {encounter.Flow.Round}";
            IReadOnlyList<UnitId> order = encounter.Flow.TurnOrder;
            int shown = 0;
            for (int i = encounter.Flow.TurnIndex; i < order.Count; i++)
            {
                UnitId unit = order[i];
                if (encounter.Combat.IsOutOfAction(unit)) continue;
                Image tile = shown < tiles.Count ? tiles[shown] : addTile();
                tile.gameObject.SetActive(true);
                bool active = unit == encounter.Flow.ActiveUnit;
                Color colour = encounter.Units.GetTeam(unit) == Team.Player ? playerColour : enemyColour;
                tile.color = active ? colour : Color.Lerp(colour, Color.black, 0.45f);
                tile.GetComponent<Outline>().enabled = active;
                tile.GetComponentInChildren<Text>().text = nameOf(root, unit);
                shown++;
            }
            for (int i = shown; i < tiles.Count; i++) tiles[i].gameObject.SetActive(false);
        }

        private void refreshPreview(EncounterRoot root, Encounter encounter, TurnController controller)
        {
            TurnPreview preview = controller?.Preview;
            string description = preview == null ? "" : describePreview(root, encounter, preview);
            previewPanel.gameObject.SetActive(description.Length > 0);
            previewText.text = description;
        }

        private void refreshAbilityBar(EncounterRoot root, Encounter encounter, TurnController controller)
        {
            bool canAct = controller != null && controller.CanAct;
            UnitId active = encounter.Flow.ActiveUnit;
            IReadOnlyList<int> known = active.IsNone ? (IReadOnlyList<int>)new int[0] : encounter.Abilities.GetAbilities(active);
            for (int slot = 0; slot < known.Count; slot++)
            {
                Button button = slot < abilityButtons.Count ? abilityButtons[slot] : addAbilityButton(slot);
                button.gameObject.SetActive(true);
                button.interactable = canAct;
                AbilityDefinition definition = encounter.Abilities.GetDefinition(known[slot]);
                string key = encounterInput.AbilityKeyLabel(slot);
                button.GetComponentInChildren<Text>().text =
                    $"{(key.Length > 0 ? $"[{key}] " : "")}{root.AbilityAt(known[slot]).name} · {points(definition.ActionPointCost)}";
                bool targeting = controller != null && controller.TargetingAbility == known[slot];
                button.GetComponent<Image>().color = targeting ? targetingColour : Color.white;
            }
            for (int slot = known.Count; slot < abilityButtons.Count; slot++) abilityButtons[slot].gameObject.SetActive(false);
            endTurnButton.interactable = canAct;
            endTurnButton.transform.SetAsLastSibling();
        }

        private void setButtonsInteractable(bool interactable)
        {
            foreach (Button button in abilityButtons) button.interactable = interactable;
            endTurnButton.interactable = interactable;
        }

        private void refreshMessage(TurnController controller)
        {
            if (controller != null && controller.RejectionCount != seenRejections)
            {
                seenRejections = controller.RejectionCount;
                messageText.text = describe(controller.LastRejection);
                messageShownAt = Time.unscaledTime;
            }
            float age = Time.unscaledTime - messageShownAt;
            Color colour = messageText.color;
            colour.a = Mathf.Clamp01(1f - age / messageSeconds);
            messageText.color = colour;
        }

        // ------------------------------------------------------------------------------------------ text

        private string describeUnit(EncounterRoot root, Encounter encounter, UnitId unit)
        {
            text.Clear();
            if (unit.IsNone || !encounter.Units.IsValid(unit)) return "";
            bool isActive = unit == encounter.Flow.ActiveUnit;
            text.Append(nameOf(root, unit)).Append(encounter.Units.GetTeam(unit) == Team.Player ? "  (squad)" : "  (enemy)");
            if (encounter.Combat.IsDefeated(unit)) text.Append("  defeated");
            else if (encounter.Combat.IsDowned(unit)) text.Append("  downed");
            text.Append('\n').Append($"Health {encounter.Combat.GetHealth(unit)}/{encounter.Combat.GetMaxHealth(unit)}");
            int perTurn = encounter.Flow.GetActionPointsPerTurn(unit);
            text.Append('\n').Append($"Action points {(isActive ? encounter.Flow.ActiveUnitActionPoints : perTurn)}/{perTurn}");
            text.Append($"  ·  move {encounter.Movement.GetBudget(unit):0.#} m per point");
            WeaponStats weapon = encounter.Combat.GetWeapon(unit);
            text.Append('\n').Append($"Ammo {encounter.Resources.GetCarried(unit, weapon.AmmoType)}");
            text.Append($"  ·  {weapon.RoundsPerAttack} per attack");
            text.Append('\n').Append(encounter.Flow.HasReaction(unit) ? "Reaction ready" : "Reaction spent");
            for (int i = 0; i < encounter.Status.Count; i++)
            {
                StatusEffect effect = encounter.Status[i];
                if (effect.Unit != unit) continue;
                text.Append('\n').Append(effect.Kind == StatusKind.Suppressed ? "Suppressed" : $"{effect.Kind} {signed(Mathf.RoundToInt(effect.Magnitude))}");
                text.Append($" ({effect.TurnsRemaining} turn{(effect.TurnsRemaining == 1 ? "" : "s")})");
            }
            return text.ToString();
        }

        private string describePreview(EncounterRoot root, Encounter encounter, TurnPreview preview)
        {
            bool ok = preview.Verdict == RejectReason.None;
            switch (preview.Kind)
            {
                case PreviewKind.Inspect:
                    return describeUnit(root, encounter, preview.Unit);

                case PreviewKind.Move:
                {
                    var line = new StringBuilder(ok
                        ? $"Move {preview.Move.Cost:0.0} m · {points(preview.Move.ActionPointCost)}"
                        : $"Move: {describe(preview.Verdict)}");
                    if (preview.Move.Interrupts.Count > 0)
                    {
                        line.Append("\nReaction risk: ");
                        for (int i = 0; i < preview.Move.Interrupts.Count; i++)
                            line.Append(i > 0 ? ", " : "").Append(nameOf(root, preview.Move.Interrupts[i].Owner));
                    }
                    return line.ToString();
                }

                case PreviewKind.Attack:
                    return $"Attack {nameOf(root, preview.Unit)} · 1 point, ends turn" +
                           (ok ? "" : $"\n{describe(preview.Verdict)}") + hitChance(preview) +
                           $"\nTarget health {encounter.Combat.GetHealth(preview.Unit)}/{encounter.Combat.GetMaxHealth(preview.Unit)}";

                case PreviewKind.PickUp:
                {
                    bool found = encounter.Resources.TryGetPile(preview.PileId, out AmmoPile pile);
                    string cost = found && pile.Origin == PileOrigin.Released ? "1 point, refunded" : "1 point";
                    return $"Pick up {(found ? pile.Count : 0)} rounds · {cost}" + (ok ? "" : $"\n{describe(preview.Verdict)}");
                }

                case PreviewKind.Trade:
                    return $"Hand {preview.Rounds} rounds to {nameOf(root, preview.Unit)} · free" + (ok ? "" : $"\n{describe(preview.Verdict)}");

                case PreviewKind.Ability:
                {
                    AbilityDefinition definition = encounter.Abilities.GetDefinition(preview.Ability);
                    var line = new StringBuilder($"{root.AbilityAt(preview.Ability).name} · {points(definition.ActionPointCost)}");
                    if (definition.EndsTurn) line.Append(", ends turn");
                    if (!ok) line.Append('\n').Append(describe(preview.Verdict));
                    else if (preview.Affected.Count > 0)
                    {
                        line.Append("\nAffects ");
                        for (int i = 0; i < preview.Affected.Count; i++)
                            line.Append(i > 0 ? ", " : "").Append(nameOf(root, preview.Affected[i]));
                    }
                    line.Append(hitChance(preview));
                    return line.ToString();
                }

                default:
                    return "";
            }
        }

        private static string hitChance(TurnPreview preview)
        {
            if (!preview.HasHitChance) return "";
            HitChanceBreakdown chance = preview.HitChance;
            return $"\nHit {chance.Total}% × {preview.Rounds} round{(preview.Rounds == 1 ? "" : "s")}" +
                   $"\nbase {chance.Base} · cover {signed(chance.Cover)} · range {signed(chance.Range)} · " +
                   $"zone {signed(chance.Zone)} · status {signed(chance.Status)}";
        }

        private static string nameOf(EncounterRoot root, UnitId unit) =>
            root.TryGetView(unit, out UnitView view) && view != null ? view.name : $"Unit {unit}";

        private static string points(int count) => count == 1 ? "1 point" : $"{count} points";

        private static string signed(int value) => value > 0 ? $"+{value}" : value.ToString();

        private static string describe(RejectReason reason)
        {
            switch (reason)
            {
                case RejectReason.EncounterOver: return "The encounter is over";
                case RejectReason.NotActiveUnit: return "Not this unit's turn";
                case RejectReason.NotEnoughActionPoints: return "Not enough action points";
                case RejectReason.DestinationBlocked: return "Blocked";
                case RejectReason.Unreachable: return "No way there";
                case RejectReason.InsufficientMovement: return "Too far";
                case RejectReason.InvalidTarget: return "Not a valid target";
                case RejectReason.TargetNotHostile: return "Not an enemy";
                case RejectReason.TargetNotFriendly: return "Not an ally";
                case RejectReason.ReceiverOutsideZone: return "Too far away to hand over";
                case RejectReason.InvalidAmount: return "No rounds to hand over";
                case RejectReason.AbilityNotAvailable: return "Ability not available";
                case RejectReason.OutOfRange: return "Out of range";
                case RejectReason.Pinned: return "Pinned: cannot move this turn";
                case RejectReason.NoLineOfSight: return "No line of sight";
                case RejectReason.NoAmmo: return "No ammo";
                case RejectReason.PileNotFound: return "Pile is gone";
                case RejectReason.OutOfPickupRange: return "Too far to pick up";
                default: return reason.ToString();
            }
        }

        // ------------------------------------------------------------------------------------------ building

        private void build()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasObject = new GameObject("HUD canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            canvasRoot = (RectTransform)canvasObject.transform;

            if (Application.isPlaying && FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }

            // Turn strip along the top, with the round number at its left.
            turnStrip = rect("Turn strip", canvasRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1200f, 44f));
            var stripLayout = turnStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
            stripLayout.spacing = 8f;
            stripLayout.childAlignment = TextAnchor.MiddleCenter;
            stripLayout.childControlWidth = stripLayout.childControlHeight = true;
            stripLayout.childForceExpandWidth = stripLayout.childForceExpandHeight = false;
            roundText = label(panel("Round", turnStrip, 110f, 40f).transform, 20, TextAnchor.MiddleCenter);

            // Active unit (bottom left) and preview (bottom right).
            Image unitPanel = panel("Active unit", canvasRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(460f, 190f));
            unitText = label(unitPanel.transform, 20, TextAnchor.UpperLeft);
            previewPanel = panel("Preview", canvasRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 16f), new Vector2(560f, 190f));
            previewText = label(previewPanel.transform, 20, TextAnchor.UpperLeft);

            // Abilities and End Turn (bottom centre).
            abilityBar = rect("Abilities", canvasRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(760f, 48f));
            var barLayout = abilityBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 8f;
            barLayout.childAlignment = TextAnchor.MiddleCenter;
            barLayout.childControlWidth = barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = barLayout.childForceExpandHeight = false;
            endTurnButton = button("End Turn", abilityBar, "End Turn [Enter]");
            endTurnButton.onClick.AddListener(() => encounterInput?.Controller?.EndTurn());

            // Refusals just above the abilities, and the end banner in the middle.
            RectTransform message = rect("Message", canvasRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(760f, 36f));
            messageText = label(message, 24, TextAnchor.MiddleCenter);
            messageText.color = new Color(1f, 0.55f, 0.45f, 0f);
            RectTransform banner = rect("Banner", canvasRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 120f));
            bannerText = label(banner, 72, TextAnchor.MiddleCenter);
            bannerText.gameObject.SetActive(false);
        }

        private Image addTile()
        {
            Image tile = panel("Turn", turnStrip, 140f, 40f);
            Outline outline = tile.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(3f, 3f);
            label(tile.transform, 18, TextAnchor.MiddleCenter);
            tiles.Add(tile);
            return tile;
        }

        private Button addAbilityButton(int slot)
        {
            Button created = button($"Ability {slot + 1}", abilityBar, "");
            created.onClick.AddListener(() => encounterInput?.Controller?.SelectAbility(slot));
            abilityButtons.Add(created);
            return created;
        }

        private Button button(string name, Transform parent, string caption)
        {
            Image background = panel(name, parent, 240f, 44f);
            background.color = Color.white;
            var created = background.gameObject.AddComponent<Button>();
            created.targetGraphic = background;
            Text captionText = label(background.transform, 18, TextAnchor.MiddleCenter);
            captionText.color = Color.black;
            captionText.text = caption;
            return created;
        }

        private static RectTransform rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                          Vector2 position, Vector2 size)
        {
            var area = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            area.SetParent(parent, false);
            area.anchorMin = anchorMin;
            area.anchorMax = anchorMax;
            area.pivot = pivot;
            area.anchoredPosition = position;
            area.sizeDelta = size;
            return area;
        }

        // A panel anchored to a screen corner (pivot = anchor).
        private Image panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            Image image = rect(name, parent, anchor, anchor, pivot, position, size).gameObject.AddComponent<Image>();
            image.color = panelColour;
            return image;
        }

        // A panel sized by a layout group.
        private Image panel(string name, Transform parent, float width, float height)
        {
            Image image = panel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var element = image.gameObject.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = height;
            return image;
        }

        // A text filling its parent, with padding.
        private Text label(Transform parent, int size, TextAnchor alignment)
        {
            RectTransform area = rect("Text", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, -12f));
            var created = area.gameObject.AddComponent<Text>();
            created.font = font;
            created.fontSize = size;
            created.alignment = alignment;
            created.color = Color.white;
            created.horizontalOverflow = HorizontalWrapMode.Wrap;
            created.verticalOverflow = VerticalWrapMode.Overflow;
            created.raycastTarget = false;
            return created;
        }
    }
}
