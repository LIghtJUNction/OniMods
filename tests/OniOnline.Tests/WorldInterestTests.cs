using System;
using System.Collections.Generic;
using OniOnline.Multiplayer;

internal static class WorldInterestTests
{
    public static void Run(Action<bool, string> check)
    {
        BasicPrivacyAndDiff(check);
        NumericalAndMotionCadence(check);
        RevisionsAndRecovery(check);
        BudgetAndCoalescing(check);
        IndependentLatestMotion(check);
        WireFieldsAndBounds(check);
        Validation(check);
    }

    private static WorldInterestSubscription Subscription(long revision = 1)
    {
        return new WorldInterestSubscription { Revision = revision,
            Region = new WorldInterestRegion { X = 0, Y = 0, Width = 4, Height = 4 } };
    }

    private static WorldInterestFrame Frame(long tick = 0)
    {
        var values = new List<WorldInterestCell>();
        for (int index = 0; index < 64; index++) values.Add(new WorldInterestCell
        { Index = index, ElementHash = 100 + index, Color = 0xffabcdef, Temperature = 300 + index, Mass = index + 1, Explored = index != 9,
            Fields = WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass });
        return new WorldInterestFrame { Epoch = "epoch-A", Tick = tick, Width = 8, Height = 8, Cells = values,
            Entities = new[] { new WorldInterestEntity { Id = 1, CellIndex = 0, X = .2f, Y = .2f },
                new WorldInterestEntity { Id = 2, CellIndex = 9, X = 1.2f, Y = 1.2f, Kind = 1 },
                new WorldInterestEntity { Id = 3, CellIndex = 63, X = 7.2f, Y = 7.2f } } };
    }

    private static void BasicPrivacyAndDiff(Action<bool, string> check)
    {
        var publisher = new WorldInterestPublisher(8, 8, Subscription());
        var frame = Frame();
        var delta = publisher.Build(frame, 4096, index => index != 10);
        check(delta.Reset && delta.Cells.Length == 14, "viewport excludes unexplored and unauthorized cells");
        foreach (var cell in delta.Cells)
            check(cell.Temperature == 0 && cell.Mass == 0 && cell.Fields == WorldInterestChannels.Visual,
                "default visual payload omits numerical data");
        check(delta.Entities.Length == 1 && delta.Entities[0].Id == 1, "entities also require viewport exploration and authorization");
        check(publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence), "first delta acknowledged");
        check(publisher.Build(frame, 4096, index => index != 10) == null, "identical frame sends no traffic");
        var changed = (List<WorldInterestCell>)frame.Cells;
        var value = changed[0]; value.Color++; changed[0] = value;
        frame.Tick++;
        delta = publisher.Build(frame, 4096, index => index != 10);
        check(!delta.Reset && delta.Cells.Length == 1 && delta.Cells[0].Index == 0 && delta.Entities.Length == 0,
            "single visual mutation publishes one cell only");
        publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence);
        frame.Tick++;
        delta = publisher.Build(frame, 4096, index => index != 0 && index != 10);
        check(delta.Reset && Array.TrueForAll(delta.Cells, cell => cell.Index != 0), "revocation clears old cache immediately even without caller auth revision update");
    }

    private static void NumericalAndMotionCadence(Action<bool, string> check)
    {
        var sub = Subscription(); sub.Channels |= WorldInterestChannels.Temperature; sub.HoverIndex = 1; sub.NumericIntervalTicks = 10;
        var publisher = new WorldInterestPublisher(8, 8, sub);
        var replica = new WorldInterestReplica(8, 8); replica.Bind("epoch-A", sub);
        var frame = Frame();
        var delta = publisher.Build(frame, 4096, index => true);
        check(replica.TryApply(delta), "replica accepts matching subscription reset");
        publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence);
        check(replica.Cells[0].Temperature == 300 && replica.Cells[0].Mass == 0, "temperature overlay sends thermal values only");
        check(replica.Cells[1].Mass == 2 && replica.Cells[1].Temperature == 301, "hover adds exact detail for selected cell only");
        var values = (List<WorldInterestCell>)frame.Cells;
        for (int i = 0; i < values.Count; i++) { var cell = values[i]; cell.Temperature += 10; cell.Mass += 10; values[i] = cell; }
        frame.Tick = 1;
        var motion = new WorldInterestEntity { Id = 1, CellIndex = 0, X = .8f, Y = .2f };
        frame.Entities = new[] { motion };
        delta = publisher.Build(frame, 4096, index => true);
        check(delta.Cells.Length == 1 && delta.Cells[0].Index == 1, "hover updates before numerical cadence while overlay waits");
        check(delta.Entities.Length == 1, "movement fast lane does not wait for numerical cadence");
        check(replica.TryApply(delta), "replica accepts contiguous delta");
        publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence);
        WorldInterestEntity sample;
        check(replica.SampleEntity(1, .5, out sample) && Math.Abs(sample.X - .5f) < .001, "client interpolates authoritative motion samples");
        check(replica.SampleEntity(1, 1000, out sample) && sample.X == .8f, "client never extrapolates beyond latest sample");
        frame.Tick = 10;
        delta = publisher.Build(frame, 4096, index => true);
        check(delta != null && delta.Cells.Length == 14 && delta.Entities.Length == 0, "numerical cadence refreshes changed thermal cells without resending unchanged motion");
        replica.TryApply(delta); publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence);
        frame.Entities = new WorldInterestEntity[0]; frame.Tick = 11;
        delta = publisher.Build(frame, 4096, index => true);
        check(delta.RemovedEntities.Length == 1 && delta.RemovedEntities[0] == 1 && replica.TryApply(delta) && !replica.SampleEntity(1, 11, out sample),
            "entity removal clears interpolation state");
        publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence);
        var newSub = Subscription(2); publisher.UpdateSubscription(newSub); replica.Bind("epoch-A", newSub);
        delta = publisher.Build(frame, 4096, index => true); replica.TryApply(delta);
        check(replica.Cells[1].Mass == 0 && replica.Cells[1].Temperature == 0, "leaving hover and thermal overlay clears cached numerical fields");
    }

    private static void RevisionsAndRecovery(Action<bool, string> check)
    {
        var publisher = new WorldInterestPublisher(8, 8, Subscription());
        var replica = new WorldInterestReplica(8, 8); replica.Bind("epoch-A", Subscription());
        var frame = Frame();
        var old = publisher.Build(frame, 4096, index => true);
        var newer = Subscription(2); newer.Region.X = 4;
        check(publisher.UpdateSubscription(newer), "new viewport revision supersedes inflight subscription");
        check(!publisher.UpdateSubscription(Subscription()), "old viewport revision cannot replace new viewport");
        replica.Bind("epoch-A", newer);
        check(!replica.TryApply(old) && !publisher.Acknowledge(old.Epoch, old.SubscriptionRevision, old.Sequence), "late old viewport packet and ack are rejected");
        var delta = publisher.Build(frame, 4096, index => true);
        check(delta.Reset && replica.TryApply(delta) && !replica.Cells.ContainsKey(0), "new viewport reset removes old geometry");
        publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence);
        check(!replica.TryApply(delta), "duplicate delta never replays client changes");
        frame.AuthorizationRevision = 1;
        var auth = publisher.Build(frame, 4096, index => index != 4);
        check(auth.Reset && replica.TryApply(auth) && !replica.Cells.ContainsKey(4), "authorization revision rebuilds sanitized cache");
        publisher.Acknowledge(auth.Epoch, auth.SubscriptionRevision, auth.Sequence);
        frame.Epoch = "epoch-B";
        var epoch = publisher.Build(frame, 4096, index => true);
        check(epoch.Reset && !replica.TryApply(epoch), "unbound epoch cannot replace trusted replica");
        replica.Bind("epoch-B", newer);
        check(replica.TryApply(epoch) && !replica.TryApply(auth), "explicit epoch binding accepts rebuild and rejects stale authority");
        publisher.Acknowledge(epoch.Epoch, epoch.SubscriptionRevision, epoch.Sequence);
        publisher.RequireResync();
        var resync = publisher.Build(frame, 4096, index => true);
        check(resync.Reset && replica.TryApply(resync), "reconnect explicitly rebuilds state when retained cache cannot be trusted");
    }

    private static void BudgetAndCoalescing(Action<bool, string> check)
    {
        var sub = Subscription();
        var publisher = new WorldInterestPublisher(8, 8, sub);
        var replica = new WorldInterestReplica(8, 8); replica.Bind("epoch-A", sub);
        var frame = Frame(); frame.Entities = new WorldInterestEntity[0];
        int budget = WorldInterestDelta.HeaderBytes + WorldInterestDelta.CellBytes;
        var pending = publisher.Build(frame, budget, index => true);
        var values = (List<WorldInterestCell>)frame.Cells;
        for (int i = 0; i < values.Count; i++) { var cell = values[i]; cell.Color++; values[i] = cell; }
        frame.Tick = 2;
        for (int i = 0; i < 100; i++) check(ReferenceEquals(pending, publisher.Build(frame, budget, index => true)), "slow client retains one bounded inflight delta");
        check(pending.EstimatedBytes <= budget && publisher.CachedCellCount == 0, "unacknowledged payload respects byte budget and cannot commit cache");
        check(!publisher.Acknowledge(pending.Epoch, pending.SubscriptionRevision, pending.Sequence + 1), "wrong ack cannot commit cache");
        replica.TryApply(pending); publisher.Acknowledge(pending.Epoch, pending.SubscriptionRevision, pending.Sequence);
        int batches = 0;
        WorldInterestDelta delta;
        while ((delta = publisher.Build(frame, budget, index => true)) != null && batches++ < 40)
        {
            check(delta.EstimatedBytes <= budget && replica.TryApply(delta), "bounded partial deltas build coherent replica");
            publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence);
        }
        check(batches <= 16 && replica.Cells.Count == 15, "small budget eventually drains entire viewport without full resend queue");
        foreach (var cell in replica.Cells.Values) check(cell.Color == 0xffabcdf0, "latest source mutations coalesce after inflight acknowledgement");
        publisher.RequireResync();
        pending = publisher.Build(frame, 4096, index => true);
        var denied = publisher.Build(frame, 4096, index => index != pending.Cells[0].Index);
        check(denied.Sequence > pending.Sequence && denied.Reset && denied.Cells.Length == 14, "inflight privacy revocation invalidates old payload before retransmission");
    }

    private static void Validation(Action<bool, string> check)
    {
        var invalid = Subscription(); invalid.Region.X = int.MaxValue;
        check(Throws(() => new WorldInterestPublisher(8, 8, invalid)), "overflowing viewport rejected");
        invalid = Subscription(); invalid.HoverIndex = 64;
        check(Throws(() => new WorldInterestPublisher(8, 8, invalid)), "out of world hover rejected");
        var publisher = new WorldInterestPublisher(8, 8, Subscription());
        var frame = Frame();
        check(Throws(() => publisher.Build(frame, 1, index => true)), "impossible packet budget rejected");
        frame.Entities = new[] { new WorldInterestEntity { Id = 1, CellIndex = 0, X = float.NaN, Y = 0 } };
        check(Throws(() => publisher.Build(frame, 4096, index => true)), "nonfinite motion rejected before cache mutation");
        frame = Frame(5); publisher.Build(frame, 4096, index => true); frame.Tick = 4;
        check(Throws(() => publisher.Build(frame, 4096, index => true)), "regressing authority ticks rejected");
        var replica = new WorldInterestReplica(8, 8); replica.Bind("epoch-A", Subscription());
        var malformed = new WorldInterestDelta { Epoch = "epoch-A", SubscriptionRevision = 1, Sequence = 1, Reset = true,
            Cells = new[] { new WorldInterestCell { Index = 0, Explored = true, Fields = WorldInterestChannels.Visual, Mass = 1 } } };
        check(!replica.TryApply(malformed) && replica.Cells.Count == 0, "unexpected numerical field cannot leak through visual replica");
        malformed.Cells = new[] { new WorldInterestCell { Index = 0, Explored = true, Fields = WorldInterestChannels.Visual } };
        malformed.Reset = false;
        check(!replica.TryApply(malformed), "client requests reset instead of accepting deltas on missing baseline");
    }

    private static void IndependentLatestMotion(Action<bool, string> check)
    {
        var visualSub = Subscription(); visualSub.Channels = WorldInterestChannels.Visual;
        var motionSub = Subscription(); motionSub.Channels = WorldInterestChannels.Entities;
        var visual = new WorldInterestPublisher(8, 8, visualSub);
        var motion = new WorldInterestPublisher(8, 8, motionSub, latestMotion: true);
        var replica = new WorldInterestReplica(8, 8, latestMotion: true); replica.Bind("epoch-A", motionSub);
        var frame = Frame();
        var terrainPending = visual.Build(frame, 4096, index => true);
        var baseline = motion.Build(frame, 4096, index => true);
        check(baseline.Cells.Length == 0 && replica.TryApply(baseline), "motion lane baseline carries no terrain or numbers");
        motion.Acknowledge(baseline.Epoch, baseline.SubscriptionRevision, baseline.Sequence);
        frame.Tick = 1;
        frame.Entities = new[] { new WorldInterestEntity { Id = 1, CellIndex = 0, X = .4f, Y = .2f } };
        var lost = motion.Build(frame, 4096, index => true);
        check(replica.TryApply(lost), "motion applies while visual baseline remains unacknowledged");
        frame.Tick = 2;
        frame.Entities = new[] { new WorldInterestEntity { Id = 1, CellIndex = 0, X = .8f, Y = .2f } };
        var newest = motion.Build(frame, 4096, index => true);
        check(newest.Sequence > lost.Sequence && newest.BaseSequence == baseline.Sequence && replica.TryApply(newest),
            "lost motion ACK does not block newer position and cumulative base can skip unconfirmed frame");
        check(visual.HasPending && ReferenceEquals(terrainPending, visual.Build(frame, 4096, index => true)), "visual and motion ACK state remain independent");
        check(!replica.TryApply(lost) && !motion.Acknowledge(lost.Epoch, lost.SubscriptionRevision, lost.Sequence), "late obsolete motion packet and ack cannot rewind motion");
        // Move back to the acknowledged baseline. Comparing only against that baseline would incorrectly suppress this update.
        frame.Tick = 3;
        frame.Entities = new[] { new WorldInterestEntity { Id = 1, CellIndex = 0, X = .2f, Y = .2f } };
        var returned = motion.Build(frame, 4096, index => true);
        check(returned.Entities.Length == 1 && replica.TryApply(returned) && replica.Entities[1].X == .2f,
            "return-to-ACK-baseline position still overwrites previously unacknowledged movement");
        frame.Tick = 4; frame.Entities = new WorldInterestEntity[0];
        var removed = motion.Build(frame, 4096, index => true);
        check(removed.RemovedEntities.Length == 1 && replica.TryApply(removed) && replica.Entities.Count == 0,
            "disappearance supersedes unacknowledged movement with persistent tombstone");
        motion.Acknowledge(removed.Epoch, removed.SubscriptionRevision, removed.Sequence);
        check(motion.Build(frame, 4096, index => true) == null, "confirmed motion tombstone leaves no repeated traffic");
        frame.Tick = 5; frame.Entities = new[] { new WorldInterestEntity { Id = 50, CellIndex = 0, X = .5f, Y = .5f } };
        var created = motion.Build(frame, 4096, index => true); replica.TryApply(created);
        frame.Tick = 6; frame.Entities = new WorldInterestEntity[0];
        var vanished = motion.Build(frame, 4096, index => true);
        check(vanished.RemovedEntities.Length == 1 && vanished.RemovedEntities[0] == 50 && replica.TryApply(vanished) && replica.Entities.Count == 0,
            "entity created and removed before any ACK still sends removal");
        motion.Acknowledge(vanished.Epoch, vanished.SubscriptionRevision, vanished.Sequence);
        bool boundedReset = false;
        for (int i = 0; i < 2200; i++)
        {
            frame.Tick = 7 + i;
            frame.Entities = new[] { new WorldInterestEntity { Id = 10000 + i, CellIndex = 0, X = .5f, Y = .5f } };
            var churn = motion.Build(frame, WorldInterestWire.MaxPacketBytes, index => true);
            if (churn.Reset) boundedReset = true;
            if (i % 100 == 0) check(motion.UnconfirmedEntityCount <= WorldInterestPublisher.MaxVisibleEntities * 2 &&
                churn.EstimatedBytes <= WorldInterestWire.MaxPacketBytes && churn.RemovedEntities.Length <= WorldInterestPublisher.MaxVisibleEntities,
                "motion loss and entity churn keep bounded debt, packet size and removal count");
        }
        check(boundedReset, "unacknowledged lifetime churn requests explicit resync instead of retaining infinite tombstones");
    }

    private static void WireFieldsAndBounds(Action<bool, string> check)
    {
        var sub = Subscription();
        var publisher = new WorldInterestPublisher(8, 8, sub);
        var frame = Frame(); frame.Entities = new WorldInterestEntity[0];
        var delta = publisher.Build(frame, 4096, index => true);
        var visualBytes = WorldInterestWire.Encode(delta, false);
        WorldInterestDelta decoded; bool lane;
        check(WorldInterestWire.TryDecode(visualBytes, out decoded, out lane) && !lane && decoded.Cells.Length == delta.Cells.Length,
            "production visual delta wire roundtrips");
        foreach (var cell in decoded.Cells) check(cell.Temperature == 0 && cell.Mass == 0, "wire physically omits unrequested numeric fields");
        publisher.Acknowledge(delta.Epoch, delta.SubscriptionRevision, delta.Sequence);
        sub = Subscription(2); sub.Channels |= WorldInterestChannels.Temperature | WorldInterestChannels.Mass;
        publisher.UpdateSubscription(sub);
        var detailed = publisher.Build(frame, 4096, index => true);
        var detailBytes = WorldInterestWire.Encode(detailed, false);
        check(detailBytes.Length - visualBytes.Length == delta.Cells.Length * 8, "only requested float fields consume numerical payload bytes");
        check(detailBytes.Length <= detailed.EstimatedBytes && WorldInterestWire.TryDecode(detailBytes, out decoded, out lane), "wire fits conservative publisher allowance");
        for (int i = 0; i < detailBytes.Length; i++)
        {
            var truncated = new byte[i]; Array.Copy(detailBytes, truncated, i);
            check(!WorldInterestWire.TryDecode(truncated, out decoded, out lane), "truncated packet cannot publish partial cells");
        }
        var trailing = new byte[visualBytes.Length + 1]; Array.Copy(visualBytes, trailing, visualBytes.Length);
        check(!WorldInterestWire.TryDecode(trailing, out decoded, out lane), "trailing bytes rejected");
        check(!WorldInterestWire.TryDecode(new byte[WorldInterestWire.MaxPacketBytes + 1], out decoded, out lane), "oversized packet rejected before allocation");
        var motionSub = Subscription(); motionSub.Channels = WorldInterestChannels.Entities;
        var motion = new WorldInterestPublisher(8, 8, motionSub, latestMotion: true);
        var packet = WorldInterestWire.Encode(motion.Build(Frame(), 4096, index => true), true);
        check(WorldInterestWire.TryDecode(packet, out decoded, out lane) && lane && decoded.Entities.Length == 1 && decoded.Cells.Length == 0,
            "motion wire lane has independent explicit discriminator");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; } catch (ArgumentException) { return true; }
    }
}
