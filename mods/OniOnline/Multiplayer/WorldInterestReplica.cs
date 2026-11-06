using System;
using System.Collections.Generic;

namespace OniOnline.Multiplayer
{
    // Bind only from authenticated descriptor/subscription state, never from an arriving delta.
    public sealed class WorldInterestReplica
    {
        private string epoch;
        private WorldInterestSubscription subscription;
        private readonly int width, height;
        private readonly bool latestMotion;
        private long sequence, previousTick, tick;
        private bool initialized;
        private Dictionary<int, WorldInterestCell> cells = new Dictionary<int, WorldInterestCell>();
        private Dictionary<int, WorldInterestEntity> entities = new Dictionary<int, WorldInterestEntity>();
        private Dictionary<int, WorldInterestEntity> previousEntities = new Dictionary<int, WorldInterestEntity>();
        public IReadOnlyDictionary<int, WorldInterestCell> Cells { get { return cells; } }
        public IReadOnlyDictionary<int, WorldInterestEntity> Entities { get { return entities; } }
        public long Sequence { get { return sequence; } }
        public long LatestTick { get { return tick; } }
        public long PreviousTick { get { return previousTick; } }

        public WorldInterestReplica(int width, int height, bool latestMotion = false)
        { this.width = width; this.height = height; this.latestMotion = latestMotion; }

        public void Bind(string valueEpoch, WorldInterestSubscription value)
        {
            if (!WorldInterestPublisher.ValidEpoch(valueEpoch) || value == null) throw new ArgumentException("invalid_interest_binding");
            var validated = value.ValidatedCopy(width, height);
            if (latestMotion && validated.Channels != WorldInterestChannels.Entities) throw new ArgumentException("motion_lane_channels");
            if (epoch == valueEpoch && subscription != null && subscription.Revision == validated.Revision) return;
            epoch = valueEpoch; subscription = validated; sequence = 0; tick = previousTick = 0; initialized = false;
            cells.Clear(); entities.Clear(); previousEntities.Clear();
        }

        public bool TryApply(WorldInterestDelta delta)
        {
            if (delta == null || subscription == null || delta.Epoch != epoch || delta.SubscriptionRevision != subscription.Revision ||
                delta.Sequence <= sequence || delta.Tick < 0 || (initialized && delta.Tick < tick) ||
                (!delta.Reset && (!initialized || (latestMotion ? delta.BaseSequence > sequence : delta.BaseSequence != sequence))) || delta.Cells == null || delta.Entities == null ||
                delta.RemovedCells == null || delta.RemovedEntities == null || delta.EstimatedBytes > WorldInterestPublisher.MaxDeltaBytes)
                return false;
            var nextCells = delta.Reset ? new Dictionary<int, WorldInterestCell>() : new Dictionary<int, WorldInterestCell>(cells);
            var nextEntities = delta.Reset ? new Dictionary<int, WorldInterestEntity>() : new Dictionary<int, WorldInterestEntity>(entities);
            var seenCells = new HashSet<int>();
            foreach (var index in delta.RemovedCells)
                if (!ValidIndex(index) || !seenCells.Add(index)) return false;
                else nextCells.Remove(index);
            foreach (var value in delta.Cells)
            {
                var allowed = subscription.Channels & (WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass);
                if (value.Index == subscription.HoverIndex) allowed |= WorldInterestChannels.Temperature | WorldInterestChannels.Mass;
                if (!ValidIndex(value.Index) || !subscription.Region.Contains(value.Index, width) || !value.Explored ||
                    !seenCells.Add(value.Index) || value.Fields != allowed ||
                    !WorldInterestPublisher.Finite(value.Mass) || value.Mass < 0 || !WorldInterestPublisher.Finite(value.Temperature) || value.Temperature < 0 ||
                    ((value.Fields & WorldInterestChannels.Mass) == 0 && value.Mass != 0) ||
                    ((value.Fields & WorldInterestChannels.Temperature) == 0 && value.Temperature != 0) ||
                    ((value.Fields & WorldInterestChannels.Visual) == 0 && (value.ElementHash != 0 || value.Color != 0))) return false;
                nextCells[value.Index] = value;
            }
            var seenEntities = new HashSet<int>();
            foreach (var id in delta.RemovedEntities)
                if (id < 0 || !seenEntities.Add(id)) return false;
                else nextEntities.Remove(id);
            foreach (var value in delta.Entities)
            {
                if ((subscription.Channels & WorldInterestChannels.Entities) == 0 || value.Id < 0 || !seenEntities.Add(value.Id) ||
                    !ValidIndex(value.CellIndex) || !subscription.Region.Contains(value.CellIndex, width) ||
                    !WorldInterestPublisher.Finite(value.X) || !WorldInterestPublisher.Finite(value.Y) ||
                    value.X < 0 || value.X >= width || value.Y < 0 || value.Y >= height ||
                    (int)value.X + (int)value.Y * width != value.CellIndex) return false;
                nextEntities[value.Id] = value;
            }
            if (nextCells.Count > WorldInterestPublisher.MaxVisibleCells || nextEntities.Count > WorldInterestPublisher.MaxVisibleEntities) return false;
            previousEntities = delta.Reset ? new Dictionary<int, WorldInterestEntity>(nextEntities) : entities;
            previousTick = delta.Reset ? delta.Tick : tick;
            tick = delta.Tick; cells = nextCells; entities = nextEntities; sequence = delta.Sequence; initialized = true;
            return true;
        }

        // Render behind the last two authority samples; never predict movement through hidden terrain.
        public bool SampleEntity(int id, double renderTick, out WorldInterestEntity value, Func<int, bool> visible = null)
        {
            if (!entities.TryGetValue(id, out value)) return false;
            var latest = value;
            WorldInterestEntity previous;
            if (tick <= previousTick || !previousEntities.TryGetValue(id, out previous)) return true;
            double alpha = double.IsNaN(renderTick) ? 1 : Math.Max(0, Math.Min(1, (renderTick - previousTick) / (tick - previousTick)));
            value.X = (float)(previous.X + (value.X - previous.X) * alpha);
            value.Y = (float)(previous.Y + (value.Y - previous.Y) * alpha);
            value.CellIndex = (int)value.X + (int)value.Y * width;
            bool known = visible == null ? cells.ContainsKey(value.CellIndex) : visible(value.CellIndex);
            if (!known && value.CellIndex != latest.CellIndex) value = latest;
            return true;
        }

        private bool ValidIndex(int index) { return index >= 0 && index < (long)width * height; }
    }
}
