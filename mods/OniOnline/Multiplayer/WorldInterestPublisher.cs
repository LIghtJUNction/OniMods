using System;
using System.Collections.Generic;

namespace OniOnline.Multiplayer
{
    // One instance per observer. A single bounded in-flight delta replaces an outbound history queue.
    // Commit only after transport/application acknowledgement; reconnect can explicitly request a reset.
    public sealed class WorldInterestPublisher
    {
        public const int MaxVisibleCells = 4096, MaxVisibleEntities = 1024, MaxDeltaBytes = 65536;
        private WorldInterestSubscription subscription;
        private readonly int width, height;
        private readonly bool latestMotion;
        private string epoch;
        private long authorizationRevision, nextSequence, acknowledgedSequence, numericTick = -1, observedTick = -1;
        private bool reset = true;
        private int cellCursor, entityCursor;
        private Dictionary<int, WorldInterestCell> cells = new Dictionary<int, WorldInterestCell>();
        private Dictionary<int, WorldInterestEntity> entities = new Dictionary<int, WorldInterestEntity>();
        private Dictionary<int, WorldInterestCell> pendingCells;
        private Dictionary<int, WorldInterestEntity> pendingEntities;
        private WorldInterestDelta pending;
        private readonly HashSet<int> unconfirmedEntities = new HashSet<int>();

        public WorldInterestPublisher(int width, int height, WorldInterestSubscription subscription, bool latestMotion = false)
        {
            this.width = width; this.height = height; this.latestMotion = latestMotion;
            UpdateSubscription(subscription);
        }

        public long SubscriptionRevision { get { return subscription.Revision; } }
        public int CachedCellCount { get { return cells.Count; } }
        public int CachedEntityCount { get { return entities.Count; } }
        public bool HasPending { get { return pending != null; } }
        public int UnconfirmedEntityCount { get { return unconfirmedEntities.Count; } }

        public bool UpdateSubscription(WorldInterestSubscription value)
        {
            if (value == null) throw new ArgumentNullException("value");
            var validated = value.ValidatedCopy(width, height);
            if (latestMotion && validated.Channels != WorldInterestChannels.Entities) throw new ArgumentException("motion_lane_channels");
            if (subscription != null && validated.Revision <= subscription.Revision) return false;
            subscription = validated;
            RequireResync();
            return true;
        }

        public void RequireResync()
        {
            reset = true; pending = null; pendingCells = null; pendingEntities = null;
            cells.Clear(); entities.Clear(); numericTick = -1; cellCursor = entityCursor = 0;
            unconfirmedEntities.Clear();
        }

        public bool Acknowledge(string valueEpoch, long revision, long sequence)
        {
            if (pending == null || pending.Epoch != valueEpoch || pending.SubscriptionRevision != revision || pending.Sequence != sequence)
                return false;
            cells = pendingCells; entities = pendingEntities;
            foreach (var entity in pending.Entities) unconfirmedEntities.Remove(entity.Id);
            foreach (var id in pending.RemovedEntities) unconfirmedEntities.Remove(id);
            acknowledgedSequence = pending.Sequence;
            pending = null; pendingCells = null; pendingEntities = null; reset = false;
            return true;
        }

        public WorldInterestDelta Build(WorldInterestFrame frame, int byteBudget, Func<int, bool> authorized)
        {
            Validate(frame, byteBudget, authorized);
            if (epoch != frame.Epoch || authorizationRevision != frame.AuthorizationRevision)
            {
                if (epoch != frame.Epoch) observedTick = -1;
                epoch = frame.Epoch; authorizationRevision = frame.AuthorizationRevision; RequireResync();
            }
            if (frame.Tick < observedTick) throw new ArgumentException("regressing_interest_tick");
            observedTick = frame.Tick;
            // Permissions/exploration may narrow even before the caller increments its authorization revision.
            var explored = new HashSet<int>();
            foreach (var cell in frame.Cells)
                if (Visible(cell.Index) && cell.Explored && authorized(cell.Index)) explored.Add(cell.Index);
            foreach (var pair in cells)
                if (!explored.Contains(pair.Key)) { RequireResync(); break; }
            if (pending != null)
            {
                bool safe = pending.EstimatedBytes <= byteBudget;
                foreach (var cell in pending.Cells) if (!explored.Contains(cell.Index)) safe = false;
                foreach (var entity in pending.Entities) if (!explored.Contains(entity.CellIndex)) safe = false;
                // Motion supersedes its pending packet after the reset baseline is acknowledged.
                // Unacknowledged entity IDs remain dirty, including disappearances and return-to-baseline positions.
                bool partialMotionReset = reset && WorldInterestDelta.HeaderBytes + (long)frame.Entities.Count * WorldInterestDelta.EntityBytes > byteBudget;
                if (safe && (!latestMotion || partialMotionReset)) return pending;
                if (!safe) RequireResync();
                else { pending = null; pendingCells = null; pendingEntities = null; }
            }
            bool numericalDue = numericTick < 0 || frame.Tick - numericTick >= subscription.NumericIntervalTicks;
            var desiredCells = new Dictionary<int, WorldInterestCell>();
            foreach (var source in frame.Cells)
            {
                if (!explored.Contains(source.Index)) continue;
                if (latestMotion) continue;
                if (desiredCells.ContainsKey(source.Index)) throw new ArgumentException("duplicate_interest_cell");
                WorldInterestCell previous;
                cells.TryGetValue(source.Index, out previous);
                var fields = subscription.Channels & (WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass);
                if (source.Index == subscription.HoverIndex) fields |= WorldInterestChannels.Temperature | WorldInterestChannels.Mass;
                if ((source.Fields & fields) != fields) throw new ArgumentException("missing_interest_source_fields");
                var current = new WorldInterestCell { Index = source.Index, Explored = true, Fields = fields,
                    ElementHash = (fields & WorldInterestChannels.Visual) != 0 ? source.ElementHash : 0,
                    Color = (fields & WorldInterestChannels.Visual) != 0 ? source.Color : 0 };
                bool detail = source.Index == subscription.HoverIndex;
                current.Temperature = (fields & WorldInterestChannels.Temperature) != 0 ?
                    (numericalDue || detail || (previous.Fields & WorldInterestChannels.Temperature) == 0 ? source.Temperature : previous.Temperature) : 0;
                current.Mass = (fields & WorldInterestChannels.Mass) != 0 ?
                    (numericalDue || detail || (previous.Fields & WorldInterestChannels.Mass) == 0 ? source.Mass : previous.Mass) : 0;
                desiredCells.Add(source.Index, current);
            }
            var desiredEntities = new Dictionary<int, WorldInterestEntity>();
            if ((subscription.Channels & WorldInterestChannels.Entities) != 0)
                foreach (var source in frame.Entities)
                    if (explored.Contains(source.CellIndex))
                    {
                        if (desiredEntities.Count >= MaxVisibleEntities || desiredEntities.ContainsKey(source.Id))
                            throw new ArgumentException("invalid_interest_entities");
                        desiredEntities.Add(source.Id, source);
                    }
            if (latestMotion)
            {
                var debt = new HashSet<int>(unconfirmedEntities); debt.UnionWith(desiredEntities.Keys);
                // Endless churn without ACKs must not accumulate an unbounded tombstone history.
                if (debt.Count > MaxVisibleEntities * 2) RequireResync();
            }
            var changedCells = new List<WorldInterestCell>();
            var removedCells = new List<int>();
            var changedEntities = new List<WorldInterestEntity>();
            var removedEntities = new List<int>();
            int remaining = byteBudget - WorldInterestDelta.HeaderBytes;
            pendingCells = new Dictionary<int, WorldInterestCell>(cells);
            pendingEntities = new Dictionary<int, WorldInterestEntity>(entities);
            // Revocations/removals precede additions. Motion has its own cadence, independent of numerical sampling.
            foreach (var pair in cells)
                if (!desiredCells.ContainsKey(pair.Key) && remaining >= WorldInterestDelta.RemovalBytes)
                { removedCells.Add(pair.Key); pendingCells.Remove(pair.Key); remaining -= WorldInterestDelta.RemovalBytes; }
            var entityRemovals = new HashSet<int>(entities.Keys);
            entityRemovals.UnionWith(unconfirmedEntities);
            foreach (var id in entityRemovals)
                if (!desiredEntities.ContainsKey(id) && remaining >= WorldInterestDelta.RemovalBytes && removedEntities.Count < MaxVisibleEntities)
                { removedEntities.Add(id); pendingEntities.Remove(id); remaining -= WorldInterestDelta.RemovalBytes; }
            int entityBudget = latestMotion ? remaining : Math.Max(WorldInterestDelta.EntityBytes, remaining / 2);
            var entityKeys = new List<int>(desiredEntities.Keys); entityKeys.Sort();
            int consideredEntities = 0;
            while (consideredEntities < entityKeys.Count && remaining >= WorldInterestDelta.EntityBytes && entityBudget >= WorldInterestDelta.EntityBytes)
            {
                int id = entityKeys[(entityCursor + consideredEntities) % entityKeys.Count]; consideredEntities++;
                WorldInterestEntity previous;
                if (!unconfirmedEntities.Contains(id) && entities.TryGetValue(id, out previous) && Same(previous, desiredEntities[id])) continue;
                if (!pendingEntities.ContainsKey(id) && pendingEntities.Count >= MaxVisibleEntities) continue;
                changedEntities.Add(desiredEntities[id]); pendingEntities[id] = desiredEntities[id];
                remaining -= WorldInterestDelta.EntityBytes; entityBudget -= WorldInterestDelta.EntityBytes;
            }
            if (entityKeys.Count > 0) entityCursor = (entityCursor + consideredEntities) % entityKeys.Count;
            var keys = new List<int>(desiredCells.Keys);
            keys.Sort();
            int considered = 0;
            while (considered < keys.Count && remaining >= WorldInterestDelta.CellBytes)
            {
                int key = keys[(cellCursor + considered) % keys.Count]; considered++;
                WorldInterestCell previous;
                if (cells.TryGetValue(key, out previous) && Same(previous, desiredCells[key])) continue;
                changedCells.Add(desiredCells[key]); pendingCells[key] = desiredCells[key]; remaining -= WorldInterestDelta.CellBytes;
            }
            if (keys.Count > 0) cellCursor = (cellCursor + considered) % keys.Count;
            if (!reset && changedCells.Count + removedCells.Count + changedEntities.Count + removedEntities.Count == 0)
            { pendingCells = null; pendingEntities = null; return null; }
            if (numericalDue) numericTick = frame.Tick;
            pending = new WorldInterestDelta { Epoch = epoch, SubscriptionRevision = subscription.Revision,
                Sequence = ++nextSequence, BaseSequence = acknowledgedSequence, Tick = frame.Tick, Reset = reset,
                Cells = changedCells.ToArray(), RemovedCells = removedCells.ToArray(),
                Entities = changedEntities.ToArray(), RemovedEntities = removedEntities.ToArray() };
            foreach (var entity in pending.Entities) unconfirmedEntities.Add(entity.Id);
            foreach (var id in pending.RemovedEntities) unconfirmedEntities.Add(id);
            return pending;
        }

        private bool Visible(int index) { return index >= 0 && index < (long)width * height && subscription.Region.Contains(index, width); }
        private void Validate(WorldInterestFrame frame, int budget, Func<int, bool> authorized)
        {
            if (frame == null || authorized == null || !ValidEpoch(frame.Epoch) ||
                frame.Width != width || frame.Height != height || frame.Tick < 0 || frame.AuthorizationRevision < 0 ||
                frame.Cells == null || frame.Entities == null || frame.Cells.Count > width * (long)height ||
                frame.Entities.Count > 65536 || budget < WorldInterestDelta.HeaderBytes + WorldInterestDelta.CellBytes || budget > MaxDeltaBytes)
                throw new ArgumentException("invalid_interest_frame");
            foreach (var cell in frame.Cells)
                if (cell.Index < 0 || cell.Index >= (long)width * height || !Finite(cell.Mass) || cell.Mass < 0 ||
                    !Finite(cell.Temperature) || cell.Temperature < 0) throw new ArgumentException("invalid_interest_cell");
            foreach (var entity in frame.Entities)
                if (entity.Id < 0 || entity.CellIndex < 0 || entity.CellIndex >= (long)width * height || !Finite(entity.X) || !Finite(entity.Y) ||
                    entity.X < 0 || entity.X >= width || entity.Y < 0 || entity.Y >= height ||
                    (int)entity.X + (int)entity.Y * width != entity.CellIndex) throw new ArgumentException("invalid_interest_entity");
        }
        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        internal static bool ValidEpoch(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-' && c != '_') return false;
            return true;
        }
        internal static bool Same(WorldInterestCell a, WorldInterestCell b)
        { return a.Index == b.Index && a.Fields == b.Fields && a.Color == b.Color && a.ElementHash == b.ElementHash && a.Mass == b.Mass && a.Temperature == b.Temperature; }
        internal static bool Same(WorldInterestEntity a, WorldInterestEntity b)
        { return a.Id == b.Id && a.CellIndex == b.CellIndex && a.Kind == b.Kind && a.X == b.X && a.Y == b.Y; }
    }
}
