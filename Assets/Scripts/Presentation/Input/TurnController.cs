// Written by Claude (Anthropic), October 2026; the AI lock-out added for step 7 and the pause lock-out for step 8c;
// to be reviewed by Alexander Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using Game.Rules;
using NumericsVector2 = System.Numerics.Vector2;

namespace Game.Presentation
{
    /// <summary>What the cursor would do if clicked now.</summary>
    public enum PreviewKind
    {
        /// <summary>Nothing: no hover, playback running, or the encounter is over.</summary>
        None,
        /// <summary>A unit that cannot be acted on is hovered; the HUD shows its details.</summary>
        Inspect,
        /// <summary>Move the active unit to <see cref="TurnPreview.Point"/>.</summary>
        Move,
        /// <summary>Attack the hostile <see cref="TurnPreview.Unit"/>.</summary>
        Attack,
        /// <summary>Pick up the pile <see cref="TurnPreview.PileId"/>.</summary>
        PickUp,
        /// <summary>Hand <see cref="TurnPreview.Rounds"/> rounds to the ally <see cref="TurnPreview.Unit"/>.</summary>
        Trade,
        /// <summary>Use the ability being targeted on <see cref="TurnPreview.Unit"/> or <see cref="TurnPreview.Point"/>.</summary>
        Ability,
    }

    /// <summary>
    /// The current preview: the command a click would submit and everything the rules layer says about it. Filled by
    /// <see cref="TurnController"/>; read by the HUD and <see cref="PreviewView"/>.
    /// </summary>
    public sealed class TurnPreview
    {
        /// <summary>What a click would do.</summary>
        public PreviewKind Kind;
        /// <summary>The rules layer's verdict on <see cref="Command"/>; <see cref="RejectReason.None"/> if it would be accepted.</summary>
        public RejectReason Verdict;
        /// <summary>The command a click would submit.</summary>
        public Command Command;
        /// <summary>The hovered or targeted unit, or <see cref="UnitId.None"/>.</summary>
        public UnitId Unit = UnitId.None;
        /// <summary>The hovered pile, or -1.</summary>
        public int PileId = -1;
        /// <summary>The ground point under the cursor (rules coordinates).</summary>
        public NumericsVector2 Point;
        /// <summary>Move: the planned path, its cost and the zone-of-control entries on it.</summary>
        public readonly MovePlan Move = new MovePlan();
        /// <summary>Attack, or an ability with an Attack effect on a unit: the hit chance per round.</summary>
        public HitChanceBreakdown HitChance;
        /// <summary>True if <see cref="HitChance"/> is filled.</summary>
        public bool HasHitChance;
        /// <summary>Attack: rounds that would be fired. Trade: rounds that would be handed over.</summary>
        public int Rounds;
        /// <summary>Ability: its index in the encounter's ability table; otherwise -1.</summary>
        public int Ability = -1;
        /// <summary>Ability: the units its effects would apply to.</summary>
        public readonly List<UnitId> Affected = new List<UnitId>();

        /// <summary>True if a click would submit an accepted command.</summary>
        public bool CanSubmit => Kind != PreviewKind.None && Kind != PreviewKind.Inspect && Verdict == RejectReason.None;

        internal void Clear()
        {
            Kind = PreviewKind.None;
            Verdict = RejectReason.None;
            Command = default;
            Unit = UnitId.None;
            PileId = -1;
            Point = default;
            Move.Clear();
            HitChance = default;
            HasHitChance = false;
            Rounds = 0;
            Ability = -1;
            Affected.Clear();
        }
    }

    /// <summary>
    /// Turns pointer and key input into previews and commands for the active unit, with no device code of its own
    /// (<see cref="EncounterInput"/> feeds it), so tests can drive it directly. Hovering previews; one click submits
    /// the previewed command through <see cref="EncounterRoot.TrySubmit"/>. In the default mode the hovered thing
    /// decides the command: a hostile unit is attacked, a pile picked up, an ally Shift-hovered is handed rounds, and
    /// open ground is moved to. Selecting an ability switches to targeting it until it is used or cancelled. Once
    /// playback has caught up, a unit with no points and no free action left ends its turn by itself.
    /// </summary>
    public sealed class TurnController
    {
        private readonly EncounterRoot root;
        private readonly TurnPreview preview = new TurnPreview();

        private bool hasHover, hoverShift;
        private UnitId hoverUnit = UnitId.None;
        private int hoverPile = -1;
        private NumericsVector2 hoverPoint;
        private bool dirty = true;
        private int seenEvents = -1;
        private bool wasPlayingBack;

        /// <summary>Creates a controller for the encounter run by <paramref name="root"/>.</summary>
        public TurnController(EncounterRoot root, int tradeCount = 5)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            TradeCount = tradeCount;
        }

        /// <summary>Rounds handed over by a trade (fewer if the giver carries fewer).</summary>
        public int TradeCount { get; set; }

        /// <summary>The ability being targeted (an index into the encounter's ability table), or -1 in the default mode.</summary>
        public int TargetingAbility { get; private set; } = -1;

        /// <summary>True while an ability is being targeted.</summary>
        public bool IsTargeting => TargetingAbility >= 0;

        /// <summary>The current preview; refreshed by <see cref="Tick"/> and before every click.</summary>
        public TurnPreview Preview => preview;

        /// <summary>Why the last submitted or clicked command was refused.</summary>
        public RejectReason LastRejection { get; private set; }

        /// <summary>Counts refusals, so the HUD can tell a new one from a repeat of the same reason.</summary>
        public int RejectionCount { get; private set; }

        /// <summary>
        /// True while the AI controls the active unit (set each frame by <see cref="EncounterInput"/>); the player's
        /// clicks, keys and buttons are then ignored.
        /// </summary>
        public bool AiControlsActiveUnit { get; set; }

        /// <summary>
        /// True when the player can give the active unit a command: the encounter is running and not paused, playback
        /// has caught up, and the AI does not control the unit.
        /// </summary>
        public bool CanAct => root.Encounter != null && !root.IsPlayingBack && !root.IsPaused && !AiControlsActiveUnit &&
                              root.Encounter.Flow.Phase == EncounterPhase.AwaitingCommand;

        /// <summary>
        /// Reports what is under the cursor: a unit (or none), a pile (or -1), the ground point, and whether Shift is held.
        /// </summary>
        public void Hover(UnitId unit, int pileId, NumericsVector2 groundPoint, bool shift)
        {
            if (hasHover && unit == hoverUnit && pileId == hoverPile && groundPoint == hoverPoint && shift == hoverShift) return;
            hasHover = true;
            hoverUnit = unit;
            hoverPile = pileId;
            hoverPoint = groundPoint;
            hoverShift = shift;
            dirty = true;
        }

        /// <summary>Reports that the cursor is over nothing that can be previewed (off the floor or over the HUD).</summary>
        public void ClearHover()
        {
            if (!hasHover) return;
            hasHover = false;
            dirty = true;
        }

        /// <summary>Submits the previewed command. Does nothing while playback runs; a refused preview is reported instead.</summary>
        public void Click()
        {
            refresh();
            if (!CanAct || preview.Kind == PreviewKind.None || preview.Kind == PreviewKind.Inspect) return;
            if (preview.Verdict != RejectReason.None)
            {
                reject(preview.Verdict);
                return;
            }
            if (submit(preview.Command) && preview.Kind == PreviewKind.Ability) TargetingAbility = -1;
        }

        /// <summary>Leaves ability targeting.</summary>
        public void Cancel()
        {
            if (!IsTargeting) return;
            TargetingAbility = -1;
            dirty = true;
        }

        /// <summary>
        /// Selects the active unit's ability in <paramref name="slot"/> (its position in the unit's ability list).
        /// Self-targeted abilities are used at once; others start targeting. Selecting the ability being targeted
        /// cancels it.
        /// </summary>
        public void SelectAbility(int slot)
        {
            if (!CanAct) return;
            Encounter encounter = root.Encounter;
            UnitId active = encounter.Flow.ActiveUnit;
            IReadOnlyList<int> known = encounter.Abilities.GetAbilities(active);
            if (slot < 0 || slot >= known.Count) return;
            int ability = known[slot];
            if (ability == TargetingAbility)
            {
                Cancel();
                return;
            }
            if (encounter.Abilities.GetDefinition(ability).Targeting == AbilityTargeting.Self)
            {
                TargetingAbility = -1;
                submit(Command.UseAbility(active, ability, UnitId.None, encounter.Spatial.GetPosition(active)));
                return;
            }
            TargetingAbility = ability;
            dirty = true;
        }

        /// <summary>Ends the active unit's turn.</summary>
        public void EndTurn()
        {
            if (CanAct) submit(Command.EndTurn(root.Encounter.Flow.ActiveUnit));
        }

        /// <summary>
        /// Call once per frame after the input calls. Ends the turn of a unit with no points and no free action left,
        /// and refreshes the preview if anything changed.
        /// </summary>
        public void Tick()
        {
            Encounter encounter = root.Encounter;
            if (encounter == null) return;
            if (encounter.Events.Count != seenEvents || root.IsPlayingBack != wasPlayingBack) dirty = true;
            seenEvents = encounter.Events.Count;
            wasPlayingBack = root.IsPlayingBack;

            if (CanAct && encounter.Flow.ActiveUnitActionPoints == 0 && !encounter.HasFreeAction(encounter.Flow.ActiveUnit))
                EndTurn();
            refresh();
        }

        private bool submit(in Command command)
        {
            if (!root.TrySubmit(command, out CommandResult result)) return false;
            dirty = true;
            if (result.Accepted) return true;
            reject(result.Reason);
            return false;
        }

        private void reject(RejectReason reason)
        {
            LastRejection = reason;
            RejectionCount++;
        }

        private void refresh()
        {
            Encounter encounter = root.Encounter;
            if (encounter != null && encounter.Events.Count != seenEvents) dirty = true;
            if (!dirty) return;
            dirty = false;
            preview.Clear();
            if (!CanAct || !hasHover) return;

            UnitId active = encounter.Flow.ActiveUnit;
            preview.Point = hoverPoint;
            if (IsTargeting)
            {
                previewAbility(encounter, active);
                return;
            }
            if (!hoverUnit.IsNone && encounter.Units.IsValid(hoverUnit))
            {
                preview.Unit = hoverUnit;
                bool hostile = encounter.Units.AreHostile(active, hoverUnit);
                if (hostile && !encounter.Combat.IsOutOfAction(hoverUnit)) previewAttack(encounter, active);
                else if (!hostile && hoverShift && hoverUnit != active) previewTrade(encounter, active);
                else preview.Kind = PreviewKind.Inspect;
                return;
            }
            if (hoverPile >= 0)
            {
                preview.Kind = PreviewKind.PickUp;
                preview.PileId = hoverPile;
                preview.Command = Command.PickUp(active, hoverPile);
                preview.Verdict = encounter.Validate(preview.Command);
                return;
            }
            preview.Kind = PreviewKind.Move;
            preview.Command = Command.Move(active, hoverPoint);
            preview.Verdict = encounter.PlanMove(active, hoverPoint, preview.Move);
        }

        private void previewAttack(Encounter encounter, UnitId active)
        {
            preview.Kind = PreviewKind.Attack;
            preview.Command = Command.Attack(active, hoverUnit);
            preview.Verdict = encounter.Validate(preview.Command);
            preview.HitChance = encounter.PreviewAttack(active, hoverUnit);
            preview.HasHitChance = true;
            WeaponStats weapon = encounter.Combat.GetWeapon(active);
            preview.Rounds = Math.Min(weapon.RoundsPerAttack, encounter.Resources.GetCarried(active, weapon.AmmoType));
        }

        private void previewTrade(Encounter encounter, UnitId active)
        {
            preview.Kind = PreviewKind.Trade;
            int carried = encounter.Resources.GetCarried(active, encounter.Combat.GetWeapon(active).AmmoType);
            preview.Rounds = Math.Min(TradeCount, carried);
            preview.Command = Command.Trade(active, hoverUnit, preview.Rounds);
            preview.Verdict = encounter.Validate(preview.Command);
        }

        private void previewAbility(Encounter encounter, UnitId active)
        {
            AbilityDefinition definition = encounter.Abilities.GetDefinition(TargetingAbility);
            preview.Kind = PreviewKind.Ability;
            preview.Ability = TargetingAbility;
            UnitId target = UnitId.None;
            NumericsVector2 point = hoverPoint;
            if (definition.Targeting == AbilityTargeting.Unit && !hoverUnit.IsNone && encounter.Units.IsValid(hoverUnit))
            {
                target = hoverUnit;
                point = encounter.Spatial.GetPosition(hoverUnit);
            }
            preview.Unit = target;
            preview.Point = point;
            preview.Command = Command.UseAbility(active, TargetingAbility, target, point);
            preview.Verdict = encounter.PreviewAbility(active, TargetingAbility, target, point, preview.Affected);

            if (target.IsNone || !encounter.Units.AreHostile(active, target) || encounter.Combat.IsOutOfAction(target)) return;
            if (definition.Effects == null) return;
            foreach (AbilityEffect effect in definition.Effects)
            {
                if (effect.Kind != AbilityEffectKind.Attack) continue;
                preview.HitChance = encounter.PreviewAttack(active, target);
                preview.HasHitChance = true;
                WeaponStats weapon = encounter.Combat.GetWeapon(active);
                preview.Rounds = Math.Min(weapon.RoundsPerAttack, encounter.Resources.GetCarried(active, weapon.AmmoType));
                break;
            }
        }
    }
}
