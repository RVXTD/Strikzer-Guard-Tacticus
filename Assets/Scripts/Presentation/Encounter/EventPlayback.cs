// Written by Claude (Anthropic), October 2026; the aim beat added for step 8b; to be reviewed by Alexander Stangl
// per the course syllabus.

using System;
using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Plays the encounter's event log back at its own pace. Events are taken in order; each starts a beat (a move
    /// tween or a short pause) and the next event waits until the beat ends. Bookkeeping events (points, statuses,
    /// zones) take no time. It only drives views; it never changes rules-layer state.
    /// </summary>
    public sealed class EventPlayback
    {
        private readonly Encounter encounter;
        private readonly Func<UnitId, UnitView> viewOf;
        private readonly PlaybackSettings settings;
        private readonly float floorHeight;
        private readonly Transform pileParent;
        private readonly Action<GameEvent> onEvent;
        private readonly Dictionary<int, PileView> piles = new Dictionary<int, PileView>();

        private int cursor;
        private UnitView activeView;

        // The current beat: a pause, or a move of one view from one point to another.
        private float beatLength, beatElapsed;
        private UnitView beatView;
        private Vector3 moveFrom, moveTo;
        private bool beatMoves, beatClearsHit;

        /// <summary>
        /// Creates playback for <paramref name="encounter"/>'s log. <paramref name="viewOf"/> finds a unit's view (or
        /// null); pile markers are created under <paramref name="pileParent"/>; <paramref name="onEvent"/>, if given,
        /// is told about every event as it starts (for logging).
        /// </summary>
        public EventPlayback(Encounter encounter, Func<UnitId, UnitView> viewOf, PlaybackSettings settings, float floorHeight,
                             Transform pileParent, Action<GameEvent> onEvent = null)
        {
            this.encounter = encounter;
            this.viewOf = viewOf;
            this.settings = settings;
            this.floorHeight = floorHeight;
            this.pileParent = pileParent;
            this.onEvent = onEvent;
        }

        /// <summary>True while events are waiting or a beat is running; input stays locked meanwhile.</summary>
        public bool IsPlaying => cursor < encounter.Events.Count || beatElapsed < beatLength;

        /// <summary>The pile markers currently shown, by pile id.</summary>
        public IReadOnlyDictionary<int, PileView> Piles => piles;

        /// <summary>Advances playback by <paramref name="deltaTime"/> seconds.</summary>
        public void Tick(float deltaTime)
        {
            float remaining = Mathf.Max(0f, deltaTime);
            while (true)
            {
                if (beatElapsed < beatLength)
                {
                    float step = Mathf.Min(remaining, beatLength - beatElapsed);
                    beatElapsed += step;
                    remaining -= step;
                    applyBeat();
                    if (beatElapsed < beatLength) return; // this frame's time is used up
                    finishBeat();
                }
                if (cursor >= encounter.Events.Count) return;
                begin(encounter.Events[cursor++]);
                if (beatLength <= 0f) finishBeat(); // instant beats still finish (snap moves, clear flashes)
            }
        }

        /// <summary>Plays everything still waiting instantly, leaving every view in its final state.</summary>
        public void SkipToEnd()
        {
            while (IsPlaying) Tick(float.MaxValue);
        }

        private void begin(in GameEvent e)
        {
            onEvent?.Invoke(e);
            beatLength = 0f;
            beatElapsed = 0f;
            beatMoves = false;
            beatClearsHit = false;
            beatView = null;
            switch (e.Kind)
            {
                case EventKind.TurnStarted:
                    if (activeView != null) activeView.SetActiveTurn(false);
                    activeView = viewOf(e.Unit);
                    if (activeView != null) activeView.SetActiveTurn(true);
                    break;
                case EventKind.UnitMoved:
                    beatView = viewOf(e.Unit);
                    if (beatView == null) break;
                    moveFrom = beatView.transform.position;
                    moveTo = GridSpace.ToWorld(e.Position, floorHeight);
                    beatView.Face(moveTo - moveFrom);
                    beatMoves = true;
                    pause(Vector3.Distance(moveFrom, moveTo) / settings.MoveSpeed);
                    break;
                case EventKind.AttackStarted:
                case EventKind.AbilityUsed:
                    faceTowards(e);
                    pause(settings.ShotBeat + settings.AimBeat);
                    break;
                case EventKind.RoundHit:
                case EventKind.UnitDamaged:
                    flash(e.Kind == EventKind.RoundHit ? e.Other : e.Unit);
                    pause(settings.HitBeat);
                    break;
                case EventKind.RoundMissed:
                    pause(settings.HitBeat);
                    break;
                case EventKind.UnitDowned:
                    viewOf(e.Unit)?.SetDowned(true);
                    pause(settings.DownBeat);
                    break;
                case EventKind.UnitDefeated:
                    viewOf(e.Unit)?.SetDefeated();
                    pause(settings.DownBeat);
                    break;
                case EventKind.UnitRevived:
                    viewOf(e.Unit)?.SetDowned(false);
                    pause(settings.HitBeat);
                    break;
                case EventKind.AmmoScattered:
                case EventKind.AmmoReleased:
                    addToPile(e.PileId, e.Position, e.Value);
                    break;
                case EventKind.AmmoPickedUp:
                    if (piles.TryGetValue(e.PileId, out PileView pile))
                    {
                        piles.Remove(e.PileId);
                        if (pile != null) PileView.DestroyImmediateOrLater(pile.gameObject);
                    }
                    break;
            }
        }

        private void pause(float seconds) => beatLength = Mathf.Max(0f, seconds);

        private void applyBeat()
        {
            if (!beatMoves || beatView == null) return;
            float t = beatLength > 0f ? beatElapsed / beatLength : 1f;
            beatView.SetPosition(Vector3.Lerp(moveFrom, moveTo, t));
        }

        private void finishBeat()
        {
            if (beatMoves && beatView != null) beatView.SetPosition(moveTo);
            if (beatClearsHit && beatView != null) beatView.ShowHit(false);
            beatLength = beatElapsed = 0f;
        }

        private void flash(UnitId unit)
        {
            beatView = viewOf(unit);
            if (beatView == null) return;
            beatView.ShowHit(true);
            beatClearsHit = true;
        }

        private void faceTowards(in GameEvent e)
        {
            UnitView view = viewOf(e.Unit);
            if (view == null) return;
            UnitView target = e.Other.IsNone ? null : viewOf(e.Other);
            Vector3 point = target != null ? target.transform.position : GridSpace.ToWorld(e.Position, floorHeight);
            view.Face(point - view.transform.position);
        }

        private void addToPile(int pileId, System.Numerics.Vector2 position, int rounds)
        {
            if (!piles.TryGetValue(pileId, out PileView pile) || pile == null)
            {
                pile = PileView.Create(pileId, GridSpace.ToWorld(position, floorHeight), pileParent);
                piles[pileId] = pile;
            }
            pile.Add(rounds);
        }
    }
}
