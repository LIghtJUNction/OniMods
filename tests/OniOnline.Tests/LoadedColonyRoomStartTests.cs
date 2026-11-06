using System;
using OniOnline.Core;
using OniOnline.Network;

internal static class LoadedColonyRoomStartTests
{
    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(42039);
        internal readonly RoomSession Host;
        internal readonly RoomSession Guest;

        internal Room()
        {
            Host = new RoomSession(Network.Host, Profile(), "", "Loaded colony");
            Guest = new RoomSession(Network.Connect(), Profile(), "");
        }

        private static SessionProfile Profile()
        { return new SessionProfile { Name = "Player", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }; }

        internal void Step()
        { Network.Advance(0.05); Host.Pump(Network.Now); Guest.Pump(Network.Now); }

        internal void Until(Func<bool> condition)
        {
            for (int frame = 0; frame < 200; frame++)
            { Step(); if (condition()) return; }
            throw new Exception("Loaded-colony room start did not reach the guest");
        }

        public void Dispose() { Guest.Dispose(); Host.Dispose(); Network.Dispose(); }
    }

    internal static void Run(Action<bool, string> check)
    {
        DelayedHost(check);
        SynchronousHost(check);
        MenuAndGuest(check);
        CancelAndRetry(check);
        ColonyLifetime(check);
        Console.WriteLine("Loaded-colony start: production intent and RoomSession with an in-memory transport; no Steam callback or game runtime claim.");
    }

    private static void DelayedHost(Action<bool, string> check)
    {
        var colony = new object();
        var intent = new LoadedColonyRoomStart();
        using (var room = new Room())
        {
            int starts = 0;
            room.Host.GameStartRequested += () => starts++;
            intent.Begin(colony);
            room.Until(() => room.Guest.Joined);
            check(!room.Host.Started && !room.Guest.Started, "an asynchronous host request waits for successful attachment");
            check(intent.Complete(room.Host, colony), "late host attachment starts the explicitly opened loaded colony");
            check(room.Host.Started && starts == 1, "loaded host starts exactly once after attachment");
            room.Until(() => room.Guest.Started);
            check(room.Guest.Started, "production room session announces the running colony to its guest");
            check(!intent.Complete(room.Host, colony) && starts == 1, "repeated completion does not replay the start request");
        }
    }

    private static void SynchronousHost(Action<bool, string> check)
    {
        var colony = new object();
        var intent = new LoadedColonyRoomStart();
        intent.Begin(colony);
        using (var room = new Room())
        {
            check(intent.Complete(room.Host, colony) && room.Host.Started, "synchronous LAN attachment preserves immediate loaded-colony start");
            room.Until(() => room.Guest.Joined && room.Guest.Started);
            check(room.Guest.Started, "a guest joining after the loaded host starts inherits its running state");
        }
    }

    private static void MenuAndGuest(Action<bool, string> check)
    {
        var colony = new object();
        var intent = new LoadedColonyRoomStart();
        using (var room = new Room())
        {
            intent.Begin(null);
            check(!intent.Complete(room.Host, colony) && !room.Host.Started, "ordinary menu hosting remains waiting even if a colony later becomes available");
            room.Until(() => room.Guest.Joined);
            intent.Begin(colony);
            check(!intent.Complete(room.Guest, colony) && !room.Host.Started && !room.Guest.Started,
                "joining a room cannot consume a host intent by starting either session");
            check(!intent.Complete(room.Host, colony) && !room.Host.Started, "a rejected guest completion cannot leave an intent for a later host");
            check(room.Host.StartGame(), "the ordinary host can still explicitly start from its menu");
            room.Until(() => room.Guest.Started);
            check(room.Guest.Started, "ordinary explicit start still reaches the guest");
        }
    }

    private static void CancelAndRetry(Action<bool, string> check)
    {
        var colony = new object();
        var intent = new LoadedColonyRoomStart();
        using (var room = new Room())
        {
            intent.Begin(colony);
            intent.Cancel();
            check(!intent.Complete(room.Host, colony) && !room.Host.Started, "leaving or an asynchronous creation failure clears the pending start");
            intent.Begin(colony);
            check(!intent.Complete(null, colony), "failed attachment cannot start a room");
            check(!intent.Complete(room.Host, colony) && !room.Host.Started, "failed attachment consumes its request instead of leaking it into another attempt");
            intent.Begin(colony);
            check(intent.Complete(room.Host, colony) && room.Host.Started, "an explicit retry after cancellation can start its own successful host");
        }
    }

    private static void ColonyLifetime(Action<bool, string> check)
    {
        var colony = new object();
        var intent = new LoadedColonyRoomStart();
        using (var room = new Room())
        {
            intent.Begin(colony);
            check(!intent.Complete(room.Host, null) && !room.Host.Started, "unloading the requesting colony before Steam completes cannot start a new game");
            intent.Begin(colony);
            check(!intent.Complete(room.Host, new object()) && !room.Host.Started, "a different loaded save cannot inherit the previous colony's start request");
            check(!intent.Complete(room.Host, colony) && !room.Host.Started, "a colony mismatch consumes the obsolete request");
            intent.Begin(colony);
            room.Host.Dispose();
            check(!intent.Complete(room.Host, colony), "a disposed host cannot be started by a delayed completion");
        }
    }
}
