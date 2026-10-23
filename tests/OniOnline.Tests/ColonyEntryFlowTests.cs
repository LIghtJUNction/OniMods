using System;
using OniOnline.Core;

internal static class ColonyEntryFlowTests
{
    private const string Room = "11111111111111111111111111111111";
    private const string OtherRoom = "22222222222222222222222222222222";

    internal static void Run(Action<bool, string> check)
    {
        var flow = new ColonyEntryFlow();
        check(!flow.TryBeginHost(true) && !flow.NativeReady(), "a loaded ordinary colony cannot invent online creation intent");
        flow.BeginCreation();
        check(flow.AwaitingNative && !flow.TryBeginHost(false), "online creation waits for native UI readiness");
        flow.CancelFrontend();
        check(flow.Purpose == ColonyEntryPurpose.None && !flow.TryBeginHost(true),
            "cancelling creation then loading an old colony cannot automatically host it");

        flow.BeginCreation();
        check(flow.TryBeginHost(true) && !flow.TryBeginHost(true), "one ready transition permits exactly one host attempt");
        check(flow.CreationQueued() && !flow.TryBeginHost(true), "an asynchronous host request cannot be submitted again each frame");
        check(flow.CreationConnected() && flow.Purpose == ColonyEntryPurpose.None && !flow.TryBeginHost(true),
            "successful connection consumes the creation intent");

        flow.BeginCreation(); flow.TryBeginHost(true); flow.CreationFailed();
        check(flow.Stage == ColonyEntryStage.HostFailed && !flow.TryBeginHost(true) && !flow.TryBeginHost(true),
            "a failed host does not retry on subsequent ready frames");
        check(flow.RetryCreation() && flow.TryBeginHost(true) && !flow.RetryCreation(),
            "only an explicit failure retry arms one new host attempt");
        flow.CreationQueued(); flow.ConnectionEnded();
        check(flow.Stage == ColonyEntryStage.HostFailed && !flow.TryBeginHost(true),
            "a failed queued connection still requires an explicit retry");

        flow.BeginJoin(ColonyEntryPurpose.JoinNew, Room, 7);
        flow.BeginCreation(hasConnection: true);
        check(flow.RequiresLeaveConnection && !flow.TryBeginHost(true),
            "switching from a guest to creation requires the runtime to leave the old connection first");
        flow.ConnectionEnded();
        check(flow.AwaitingNative && flow.TryBeginHost(true), "completed connection cleanup releases creation to wait for native readiness");

        flow.BeginJoin(ColonyEntryPurpose.JoinExisting, Room, 8);
        check(flow.Stage == ColonyEntryStage.Reserving && !flow.NativeReady() && !flow.TryBeginHost(true),
            "joining an existing colony must reserve and cannot become a host attempt");
        check(!flow.AdmissionConfirmed(true) && flow.Matches(Room, 8) && flow.ReservationConfirmed() &&
            flow.Purpose == ColonyEntryPurpose.JoinExisting && flow.Stage == ColonyEntryStage.AwaitingAdmission,
            "an existing-colony reservation waits for registration acknowledgement without another native load");
        check(!flow.AdmissionConfirmed(false) && flow.Purpose == ColonyEntryPurpose.JoinExisting &&
            flow.Stage == ColonyEntryStage.AwaitingAdmission && !flow.TryBeginHost(true),
            "a rejected existing colony retains its join intent instead of silently completing or hosting");
        check(flow.Matches(Room, 8) && flow.AdmissionConfirmed(true) && flow.Purpose == ColonyEntryPurpose.None &&
            !flow.AdmissionConfirmed(true), "only a current admitted existing planet completes the join once");

        foreach (var purpose in new[] { ColonyEntryPurpose.JoinNew, ColonyEntryPurpose.JoinLoad })
        {
            flow.BeginJoin(purpose, Room, 9);
            check(flow.Matches(Room, 9) && flow.ReservationConfirmed() && flow.AwaitingNative && !flow.ReservationConfirmed(),
                "new/load join waits for native readiness after exactly one reservation confirmation");
            check(!flow.AdmissionConfirmed(true) && flow.Matches(Room, 9) && flow.NativeReady() &&
                flow.Stage == ColonyEntryStage.AwaitingAdmission && flow.Purpose == purpose &&
                !flow.NativeReady() && !flow.TryBeginHost(true),
                "native readiness preserves new/load join intent until the real planet registration is admitted");
            check(!flow.AdmissionConfirmed(false) && flow.Matches(Room, 9) && flow.Stage == ColonyEntryStage.AwaitingAdmission &&
                flow.Purpose == purpose, "a wrong save or duplicate branch cannot silently complete a native join");
            check(flow.AdmissionConfirmed(true) && flow.Purpose == ColonyEntryPurpose.None && !flow.AdmissionConfirmed(true),
                "new/load join completes exactly once after a real registration acknowledgement");
            flow.BeginJoin(purpose, Room, 9); flow.ReservationConfirmed(); flow.CancelFrontend();
            check(!flow.NativeReady() && !flow.TryBeginHost(true), "cancelling native new/load navigation clears its pending join intent");
        }

        flow.BeginJoin(ColonyEntryPurpose.JoinNew, Room, 10); flow.ReservationConfirmed();
        check(!flow.Matches(Room, 11) && !flow.NativeReady() && flow.Purpose == ColonyEntryPurpose.None,
            "a new admission generation cancels a pending native join in the same room");
        flow.BeginJoin(ColonyEntryPurpose.JoinLoad, Room, 10);
        check(!flow.Matches(OtherRoom, 10) && !flow.ReservationConfirmed(), "another room cannot consume an old reservation intent");
        flow.BeginJoin(ColonyEntryPurpose.JoinExisting, Room, 10); flow.ConnectionEnded();
        check(!flow.Matches(Room, 10) && !flow.ReservationConfirmed(), "connection cleanup invalidates even an unacknowledged join");

        foreach (var purpose in new[] { ColonyEntryPurpose.JoinExisting, ColonyEntryPurpose.JoinNew, ColonyEntryPurpose.JoinLoad })
        {
            flow.BeginJoin(purpose, Room, 12); flow.ReservationConfirmed();
            if (flow.AwaitingNative) flow.NativeReady();
            check(!flow.Matches(Room, 13) && !flow.AdmissionConfirmed(true) && flow.Purpose == ColonyEntryPurpose.None,
                "a changed admission stamp invalidates a registration acknowledgement for every join purpose");
            flow.BeginJoin(purpose, Room, 12); flow.ReservationConfirmed();
            if (flow.AwaitingNative) flow.NativeReady();
            flow.ConnectionEnded();
            check(!flow.AdmissionConfirmed(true) && flow.Purpose == ColonyEntryPurpose.None,
                "connection end cancels a join that is waiting for planet registration");
            flow.BeginJoin(purpose, Room, 12); flow.ReservationConfirmed();
            if (flow.AwaitingNative) flow.NativeReady();
            flow.CancelFrontend();
            check(!flow.AdmissionConfirmed(true) && !flow.NativeReady() && !flow.TryBeginHost(true),
                "frontend cancellation clears the final admission wait without arming creation");
        }

        bool invalid = false;
        try { flow.BeginJoin(ColonyEntryPurpose.CreateSystem, Room, 1); } catch (ArgumentException) { invalid = true; }
        check(invalid, "the join API cannot carry creation intent");
        invalid = false;
        try { flow.BeginJoin(ColonyEntryPurpose.JoinNew, Room, 0); } catch (ArgumentOutOfRangeException) { invalid = true; }
        check(invalid && flow.Purpose == ColonyEntryPurpose.None, "an unadmitted connection cannot arm native navigation");
        Console.WriteLine("Colony entry flow: production intent transitions, cancellation, explicit host retries and session/admission binding. No native navigation or network simulation.");
    }
}
