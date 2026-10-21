using System;
using System.IO;
using System.Linq;
using OniOnline.Core;

internal static class SharedTimeTests
{
    internal static void Run(Action<bool, string> check)
    {
        string epoch = new string('a', 32);
        string a = new string('1', 32);
        string b = new string('2', 32);
        var state = new SharedTimeState(epoch);
        state.Configure(new[] { Member(a), Member(b) });
        check(state.Paused && !state.Waiting && state.Voters == 2, "shared time starts paused with two ready voters");
        long round = state.Revision;
        check(state.Request(a, SharedTimeMessage.Resume, 1, epoch, round), "first resume vote accepted");
        check(state.Paused && state.Votes == 1 && state.Revision == round, "one vote does not resume or invalidate simultaneous votes");
        check(!state.Request(a, SharedTimeMessage.Resume, 1, epoch, round), "duplicate vote is idempotent");
        check(!state.Request(b, SharedTimeMessage.Resume, 1, new string('b', 32), round), "foreign time epoch refused");
        check(!state.Request(b, SharedTimeMessage.Resume, 1, epoch, round - 1), "old vote round refused");
        check(state.Request(b, SharedTimeMessage.Resume, 1, epoch, round) && !state.Paused, "all ready players resume together");
        check(!state.Request(a, SharedTimeMessage.Resume, 1, epoch, round), "late vote cannot affect a completed round");
        round = state.Revision;
        check(state.Request(a, SharedTimeMessage.Speed, 3, epoch, round) && state.Speed == 1, "one speed vote keeps approved speed");
        check(!state.Request(b, SharedTimeMessage.Speed, 2, epoch, round), "different speed cannot hijack pending vote");
        check(state.Request(b, SharedTimeMessage.Speed, 3, epoch, round) && state.Speed == 3 && !state.Paused, "unanimous speed changes preserve running state");
        check(state.Request(a, SharedTimeMessage.Pause, 3, epoch, state.Revision) && state.Paused, "any voter can pause immediately");
        round = state.Revision;
        state.Request(a, SharedTimeMessage.Speed, 2, epoch, round);
        state.Request(b, SharedTimeMessage.Speed, 2, epoch, round);
        check(state.Speed == 2 && state.Paused, "speed approval does not release pause");
        state.Request(a, SharedTimeMessage.Resume, 2, epoch, state.Revision);
        round = state.Revision;
        state.Configure(new[] { Member(a) });
        check(state.Paused && state.Votes == 0 && state.Revision > round, "disconnect cancels pending approvals and pauses");
        check(state.Request(a, SharedTimeMessage.Resume, 2, epoch, state.Revision) && !state.Paused, "remaining ready player can explicitly resume");
        state.Configure(new[] { Member(a), Member(b, false, false) });
        check(state.Paused && state.Waiting && state.Voters == 1, "joining player waits for real colony availability");
        check(!state.Request(a, SharedTimeMessage.Resume, 2, epoch, state.Revision), "cannot resume while a connected world is unavailable");
        state.Configure(new[] { Member(a), Member(b, true, false) });
        check(state.Waiting && state.Voters == 2, "system UI pause prevents shared resume without releasing its ticket");
        state.Configure(new[] { Member(a), Member(b) });
        check(!state.Waiting && state.Paused && state.Votes == 0, "closing UI waits for new explicit approval");
        round = state.Revision;
        check(!state.Configure(new[] { Member(b), Member(a) }) && state.Revision == round, "roster order does not invalidate votes");
        state.Request(a, SharedTimeMessage.Resume, 2, epoch, round);
        var packet = state.Packet(9);
        packet.Reporters = new[] { a, b };
        packet.Connections = new[] { new string('c', 32), new string('d', 32) };
        byte[] bytes = SharedTimeWire.Encode(packet);
        var decoded = SharedTimeWire.Decode(bytes);
        check(bytes.Length < 1024 && decoded.Revision == round && decoded.Votes.SequenceEqual(new[] { a }) &&
            decoded.Connections.SequenceEqual(packet.Connections), "time codec preserves round and reconnection acknowledgments within packet budget");
        bool truncationRejected = true;
        for (int i = 0; i < bytes.Length; i++)
        {
            try { SharedTimeWire.Decode(bytes.Take(i).ToArray()); truncationRejected = false; }
            catch (InvalidDataException) { }
        }
        check(truncationRejected, "every truncated time packet refused");
        check(Reject(() => SharedTimeWire.Decode(bytes.Concat(new byte[] { 0 }).ToArray())), "trailing time packet data refused");
        byte[] invalidBoolean = (byte[])bytes.Clone(); invalidBoolean[54] = 2;
        check(Reject(() => SharedTimeWire.Decode(invalidBoolean)), "noncanonical time boolean refused");
        packet.Votes = new[] { new string('f', 32) };
        check(Reject(() => SharedTimeWire.Encode(packet)), "vote from nonvoter cannot appear in canonical state");
        var report = new SharedTimePacket
        {
            Kind = SharedTimeMessage.Report, Epoch = epoch, Generation = new string('c', 32), Revision = 1,
            Sequence = 1, Available = true, CanRun = true
        };
        check(SharedTimeWire.Decode(SharedTimeWire.Encode(report)).CanRun, "availability report round trips");
        report.Available = false;
        check(Reject(() => SharedTimeWire.Encode(report)), "unavailable world cannot claim runnable");
        check(!SharedTimeWire.IsMessage(new byte[] { 79, 78, 73, 87 }), "world packet magic does not enter shared time handler");
        state.Pause();
        round = state.Revision;
        state.Request(a, SharedTimeMessage.Speed, 3, epoch, round);
        state.Pause();
        check(state.Votes == 0 && state.VoteKind == 0 && !state.Request(b, SharedTimeMessage.Speed, 3, epoch, round),
            "explicit keep-paused action cancels speed proposal and rejects its late approval");
        round = state.Revision;
        state.Request(a, SharedTimeMessage.Resume, 2, epoch, round);
        state.Request(b, SharedTimeMessage.Resume, 2, epoch, round);
        check(!state.Paused && state.Speed == 2, "resume works after cancelling the pending proposal");
        int nativeTickets = 2; // One local UI/manual ticket and the controller's one public ticket.
        check(NativePausePolicy.ExternalCount(nativeTickets, true) == 1 &&
            NativePausePolicy.AllowExplicitLocalToggle(nativeTickets, true), "pre-existing local pause keeps player unready but allows an explicit native play action");
        nativeTickets--; // The unchanged native TogglePause/Unpause releases exactly one ticket.
        check(nativeTickets == 1 && NativePausePolicy.ExternalCount(nativeTickets, true) == 0 &&
            !NativePausePolicy.AllowExplicitLocalToggle(nativeTickets, true), "first play action leaves public pause intact; subsequent play must vote");
        check(!NativePausePolicy.AllowExplicitLocalToggle(1, false) && NativePausePolicy.ExternalCount(1, false) == 1,
            "controller cannot release a local ticket before it owns public pause");
        nativeTickets = 3;
        nativeTickets--; // Disposing/resuming the public controller releases only its one ticket.
        check(NativePausePolicy.ExternalCount(nativeTickets, false) == 2,
            "public release preserves both unrelated native UI pause tickets");
    }

    private static SharedTimeMember Member(string id, bool available = true, bool canRun = true)
    { return new SharedTimeMember { Id = id, Available = available, CanRun = canRun }; }
    private static bool Reject(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }
}
