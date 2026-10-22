// Pile origins and ammo trading by Claude (Anthropic), October 2026; to be reviewed by Alexander
// Stangl per the course syllabus.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Game.Rules
{
    /// <summary>How an ammo pile came to be on the floor.</summary>
    public enum PileOrigin
    {
        /// <summary>Placed by the level or a script (map pickups).</summary>
        Placed,
        /// <summary>Fired rounds that glanced or missed and landed recoverably.</summary>
        Scattered,
        /// <summary>Rounds that were embedded in a unit and dropped when it was defeated.</summary>
        Released,
    }

    /// <summary>Ammunition lying on the floor, recoverable by a unit standing close enough.</summary>
    public struct AmmoPile
    {
        /// <summary>Stable identifier; never reused within an encounter.</summary>
        public int Id;
        /// <summary>Ammo type index.</summary>
        public int AmmoType;
        /// <summary>Number of rounds in the pile (always at least 1).</summary>
        public int Count;
        /// <summary>World position on the ground plane.</summary>
        public Vector2 Position;
        /// <summary>How the pile came to be on the floor.</summary>
        public PileOrigin Origin;
    }

    /// <summary>
    /// Owns all ammunition: what each unit carries, what is embedded in each unit, and the piles on
    /// the floor. Ammunition is conserved: every round is carried, embedded, in a pile, or recorded lost.
    /// </summary>
    public sealed class ResourceSystem
    {
        private readonly UnitRegistry units;
        private readonly int ammoTypeCount;
        private readonly int[] carried;
        private readonly int[] embedded;
        private readonly List<AmmoPile> piles = new List<AmmoPile>(32);
        private int nextPileId = 1;

        /// <summary>Maximum distance in metres between a unit and a pile for the unit to pick it up.</summary>
        public float PickupRadius;

        /// <summary>Rounds landing within this many metres of a pile of the same type and origin join that pile.</summary>
        public float PileMergeRadius;

        /// <summary>Creates the system for <paramref name="ammoTypeCount"/> ammo types.</summary>
        public ResourceSystem(UnitRegistry units, int ammoTypeCount, float pickupRadius, float pileMergeRadius)
        {
            if (ammoTypeCount <= 0) throw new ArgumentOutOfRangeException(nameof(ammoTypeCount));
            this.units = units;
            this.ammoTypeCount = ammoTypeCount;
            carried = new int[units.Capacity * ammoTypeCount];
            embedded = new int[units.Capacity * ammoTypeCount];
            PickupRadius = pickupRadius;
            PileMergeRadius = pileMergeRadius;
        }

        /// <summary>Number of distinct ammo types.</summary>
        public int AmmoTypeCount => ammoTypeCount;

        /// <summary>Records a new unit carrying <paramref name="count"/> rounds of <paramref name="ammoType"/> and nothing else.</summary>
        public void AddUnit(UnitId id, int ammoType, int count)
        {
            units.Require(id);
            for (int t = 0; t < ammoTypeCount; t++)
            {
                carried[slotOf(id, t)] = 0;
                embedded[slotOf(id, t)] = 0;
            }
            carried[slotOf(id, ammoType)] = Math.Max(0, count);
        }

        /// <summary>Rounds of a type the unit carries.</summary>
        public int GetCarried(UnitId id, int ammoType) { units.Require(id); return carried[slotOf(id, ammoType)]; }

        /// <summary>Rounds of a type embedded in the unit.</summary>
        public int GetEmbedded(UnitId id, int ammoType) { units.Require(id); return embedded[slotOf(id, ammoType)]; }

        /// <summary>Removes fired rounds from a unit's inventory. Throws if it carries fewer than <paramref name="count"/>.</summary>
        public void Consume(UnitId id, int ammoType, int count)
        {
            units.Require(id);
            int slot = slotOf(id, ammoType);
            if (count < 0 || carried[slot] < count) throw new InvalidOperationException($"{id} cannot spend {count} rounds of type {ammoType}.");
            carried[slot] -= count;
        }

        /// <summary>
        /// Applies the fates of fired rounds: embeds, adds to piles, or records as lost. Appends one event
        /// per outcome (AmmoEmbedded, AmmoScattered or AmmoLost) in order.
        /// </summary>
        public void ApplyOutcomes(List<RoundOutcome> outcomes, EventLog log)
        {
            for (int i = 0; i < outcomes.Count; i++)
            {
                RoundOutcome o = outcomes[i];
                switch (o.Fate)
                {
                    case RoundFate.Embedded:
                        embedded[slotOf(o.EmbeddedIn, o.AmmoType)]++;
                        log.Add(new GameEvent { Kind = EventKind.AmmoEmbedded, Unit = o.EmbeddedIn, AmmoType = o.AmmoType, Value = 1 });
                        break;
                    case RoundFate.Scattered:
                        int pileId = AddToPile(o.AmmoType, 1, o.Position, PileOrigin.Scattered);
                        log.Add(new GameEvent { Kind = EventKind.AmmoScattered, Position = o.Position, AmmoType = o.AmmoType, Value = 1, PileId = pileId });
                        break;
                    default:
                        log.Add(new GameEvent { Kind = EventKind.AmmoLost, AmmoType = o.AmmoType, Value = 1 });
                        break;
                }
            }
        }

        /// <summary>
        /// Drops everything embedded in a (defeated) unit into piles at <paramref name="position"/> and
        /// appends AmmoReleased for each ammo type that had rounds.
        /// </summary>
        public void ReleaseEmbedded(UnitId id, Vector2 position, EventLog log)
        {
            units.Require(id);
            for (int t = 0; t < ammoTypeCount; t++)
            {
                int slot = slotOf(id, t);
                int count = embedded[slot];
                if (count == 0) continue;
                embedded[slot] = 0;
                int pileId = AddToPile(t, count, position, PileOrigin.Released);
                log.Add(new GameEvent { Kind = EventKind.AmmoReleased, Unit = id, Position = position, AmmoType = t, Value = count, PileId = pileId });
            }
        }

        /// <summary>
        /// Places ammunition on the floor and returns the id of the pile it joined: an existing pile of the
        /// same type and origin within <see cref="PileMergeRadius"/>, or a new one.
        /// </summary>
        public int AddToPile(int ammoType, int count, Vector2 position, PileOrigin origin)
        {
            if (ammoType < 0 || ammoType >= ammoTypeCount) throw new ArgumentOutOfRangeException(nameof(ammoType));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            for (int i = 0; i < piles.Count; i++)
            {
                AmmoPile pile = piles[i];
                if (pile.AmmoType != ammoType || pile.Origin != origin || Vector2.Distance(pile.Position, position) > PileMergeRadius) continue;
                pile.Count += count;
                piles[i] = pile;
                return pile.Id;
            }
            piles.Add(new AmmoPile { Id = nextPileId++, AmmoType = ammoType, Count = count, Position = position, Origin = origin });
            return nextPileId - 1;
        }

        /// <summary>Number of piles on the floor.</summary>
        public int PileCount => piles.Count;

        /// <summary>A pile by position in the list (order changes as piles are picked up).</summary>
        public AmmoPile GetPileAt(int index) => piles[index];

        /// <summary>Finds a pile by id.</summary>
        public bool TryGetPile(int pileId, out AmmoPile pile)
        {
            int index = indexOfPile(pileId);
            pile = index >= 0 ? piles[index] : default;
            return index >= 0;
        }

        /// <summary>Whether a unit at <paramref name="unitPosition"/> could pick up the pile. Changes no state.</summary>
        public RejectReason CanPickUp(Vector2 unitPosition, int pileId)
        {
            int index = indexOfPile(pileId);
            if (index < 0) return RejectReason.PileNotFound;
            return Vector2.Distance(piles[index].Position, unitPosition) <= PickupRadius ? RejectReason.None : RejectReason.OutOfPickupRange;
        }

        /// <summary>
        /// Moves a whole pile into the unit's inventory and appends AmmoPickedUp.
        /// Returns the rejection reason, if any, without changing state.
        /// </summary>
        public RejectReason PickUp(UnitId id, Vector2 unitPosition, int pileId, EventLog log)
        {
            units.Require(id);
            RejectReason reason = CanPickUp(unitPosition, pileId);
            if (reason != RejectReason.None) return reason;
            int index = indexOfPile(pileId);
            AmmoPile pile = piles[index];
            piles.RemoveAt(index);
            carried[slotOf(id, pile.AmmoType)] += pile.Count;
            log.Add(new GameEvent { Kind = EventKind.AmmoPickedUp, Unit = id, PileId = pile.Id, AmmoType = pile.AmmoType, Value = pile.Count, Position = pile.Position });
            return RejectReason.None;
        }

        /// <summary>
        /// Moves <paramref name="count"/> rounds of a type from one unit's inventory to another's and appends
        /// AmmoTraded. Throws if either handle is invalid or the giver carries fewer than <paramref name="count"/>;
        /// game-rule checks (team, zone, count of at least 1) are the caller's job.
        /// </summary>
        public void Trade(UnitId giver, UnitId receiver, int ammoType, int count, EventLog log)
        {
            units.Require(giver);
            units.Require(receiver);
            Consume(giver, ammoType, count);
            carried[slotOf(receiver, ammoType)] += count;
            log.Add(new GameEvent { Kind = EventKind.AmmoTraded, Unit = giver, Other = receiver, AmmoType = ammoType, Value = count });
        }

        private int indexOfPile(int pileId)
        {
            for (int i = 0; i < piles.Count; i++) if (piles[i].Id == pileId) return i;
            return -1;
        }

        private int slotOf(UnitId id, int ammoType)
        {
            if (ammoType < 0 || ammoType >= ammoTypeCount) throw new ArgumentOutOfRangeException(nameof(ammoType));
            return id.Index * ammoTypeCount + ammoType;
        }
    }
}
