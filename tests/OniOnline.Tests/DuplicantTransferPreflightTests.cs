using System;
using System.Linq;
using OniOnline.Multiplayer;

internal static class DuplicantTransferPreflightTests
{
    private const string Owner = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Planet = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static DuplicantTransferExpectation Expected() => new DuplicantTransferExpectation {
        OwnerId = Owner, PlanetId = Planet, NativeWorldId = 0, MinionId = 102, ActiveSavePath = "/fixture/source.sav" };
    private static DuplicantTransferAmbientMembership Public() => new DuplicantTransferAmbientMembership {
        RegistryKey = "public", GroupId = "public", ExactNativeType = true, RegistryOccurrences = 1,
        DirectSelectedProxyCount = 1, ReviewedProxyMembers = true };
    private static DuplicantTransferObservation Idle() => new DuplicantTransferObservation {
        Build = 744825, Loaded = true, Paused = true, Stable = true, SavedSource = true, ActiveSavePath = "/fixture/source.sav", SourceTime = 59.3831901550293,
        OwnerId = Owner, PlanetId = Planet, NativeWorldId = 0, MinionId = 102, ProxyId = 108, ExistingSelectedMinion = true,
        ExactTwoRegisteredRoots = true, BiologicalModel = true, Live = true, HealthyState = true, HitPoints = 100, MaxHitPoints = 100,
        Sicknesses = 0, StorageItems = 0, EquipmentAssignments = 0, OwnableAssignments = 0, WorkerIdle = true, ChoreStateInitialized = true,
        NavigationInitialized = true, NavigationStopped = true, FloorNavigation = true, TransitionClear = true, NavigationTargetClear = true,
        NavigationPathClear = true, Standing = true, RocketMoveClear = true, AssignmentAuditComplete = true,
        AmbientMembership = new[] { Public() }, AmbientPublicFacilities = 3 };

    private static DuplicantTransferObservation NativeIdle()
    {
        var source = Idle(); source.CurrentChore = "IdleChore";
        source.ExactNativeIdleChore = source.IdleChoreStateInitialized = source.IdleChoreOnFloor = source.IdleChoreBoundToSelected = true;
        return source;
    }

    internal static void Run(Action<bool, string> check)
    {
        var idle = Idle(); var result = DuplicantTransferPreflightPolicy.Evaluate(Expected(), idle);
        check(result.SourceEligible && result.Rejections.Length == 0, "healthy empty unassigned stopped existing source can pass source preflight");
        check(!result.ImportReady && result.RequiredContracts.Contains("destination_public_group_rebind") && result.RequiredContracts.Any(c => c.Contains("callback_barrier")) && result.RequiredContracts.Any(c => c.Contains("reload_deduplication")),
            "source eligibility never authorizes native import or skips durable transfer contract");
        check(ReferenceEquals(result.Observation, idle) && idle.HitPoints == 100 && idle.IncomingOwners.Length == 0 && idle.CurrentChore == "",
            "policy evaluates observation without repairing or mutating source facts");

        check(idle.AmbientMembership.Length == 1 && idle.AmbientPublicFacilities == 3 && idle.IncomingOwners.Length == 0,
            "native standard public membership and shared facilities remain visible without claiming source-specific ownership");
        Reject(check, o => o.AmbientMembership = Array.Empty<DuplicantTransferAmbientMembership>(), "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership = null, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership = new[] { Public(), Public() }, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].RegistryKey = "private", "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].GroupId = "private", "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].ExactNativeType = false, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].RegistryOccurrences = 2, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].DirectSelectedProxyCount = 0, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].DirectSelectedProxyCount = 2, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].DirectSelectedMinionCount = 1, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].HasNestedMembers = true, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].HasDuplicateMembers = true, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientMembership[0].ReviewedProxyMembers = false, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.AmbientPublicFacilities = -1, "transfer_default_public_membership_not_exact");
        Reject(check, o => o.IncomingOwners = new[] { new DuplicantTransferOwnerEdge { Kind = "saved-ref", ObjectId = 801,
            SavedOwnerId = 108, Type = "Bed", Group = "public" } }, "transfer_incoming_assignment_present");
        Reject(check, o => o.IncomingOwners = new[] { new DuplicantTransferOwnerEdge { Kind = "cached-ref", ObjectId = 801,
            SavedOwnerId = -1, Type = "Bed", Group = "public" } }, "transfer_incoming_assignment_present");
        Reject(check, o => o.IncomingOwners = new[] { new DuplicantTransferOwnerEdge { Kind = "live-owner", ObjectId = 801,
            Type = "Bed", Group = "public" } }, "transfer_incoming_assignment_present");
        var nativeIdle = NativeIdle();
        var idleResult = DuplicantTransferPreflightPolicy.Evaluate(Expected(), nativeIdle);
        check(idleResult.SourceEligible && !idleResult.ImportReady && idleResult.RequiredContracts.Contains("rebuild_transient_native_idle_after_standard_load_never_copy_source_chore"),
            "exact native stopped floor idle is a source candidate for standard-load transient reconstruction, never an import permit");
        check(nativeIdle.CurrentChore == "IdleChore" && nativeIdle.WorkerIdle && nativeIdle.NavigationStopped,
            "eligible native idle remains observed as an active idle chore without stopping, clearing or mutating it");
        RejectNativeIdle(check, o => o.CurrentChore = "Fake.IdleChore", "transfer_current_chore_present");
        RejectNativeIdle(check, o => o.ExactNativeIdleChore = false, "transfer_current_chore_present");
        RejectNativeIdle(check, o => o.IdleChoreStateInitialized = false, "transfer_current_chore_present");
        RejectNativeIdle(check, o => o.IdleChoreOnFloor = false, "transfer_current_chore_present");
        RejectNativeIdle(check, o => o.IdleChoreBoundToSelected = false, "transfer_current_chore_present");
        RejectNativeIdle(check, o => o.NextChore = "FetchChore", "transfer_queued_chore_present");
        RejectNativeIdle(check, o => o.WorkerIdle = false, "transfer_worker_not_idle");
        RejectNativeIdle(check, o => o.ChoreStateInitialized = false, "transfer_chore_state_uninitialized");
        RejectNativeIdle(check, o => o.NavigationInitialized = false, "transfer_stopped_floor_navigation_required");
        RejectNativeIdle(check, o => o.NavigationStopped = false, "transfer_stopped_floor_navigation_required");
        RejectNativeIdle(check, o => o.FloorNavigation = false, "transfer_stopped_floor_navigation_required");
        RejectNativeIdle(check, o => o.Standing = false, "transfer_stopped_floor_navigation_required");
        RejectNativeIdle(check, o => o.TransitionClear = false, "transfer_navigation_or_rocket_transition_present");
        RejectNativeIdle(check, o => o.NavigationTargetClear = false, "transfer_navigation_or_rocket_transition_present");
        RejectNativeIdle(check, o => o.NavigationPathClear = false, "transfer_navigation_or_rocket_transition_present");
        RejectNativeIdle(check, o => o.ReadFailures = new[] { "transfer_native_read_failed:InvalidOperationException" }, "transfer_native_read_failed:InvalidOperationException");
        Reject(check, o => o.Build = 744826, "transfer_native_build_not_reviewed");
        Reject(check, o => o.Loaded = false, "transfer_loaded_nonloading_colony_required");
        Reject(check, o => o.Paused = false, "transfer_pause_required");
        Reject(check, o => o.Stable = false, "transfer_observation_changed");
        Reject(check, o => o.SourceTime = double.NaN, "transfer_observation_changed");
        Reject(check, o => o.SavedSource = false, "transfer_saved_source_required");
        Reject(check, o => o.ActiveSavePath = "/different/source.sav", "transfer_source_identity_mismatch");
        Reject(check, o => o.OwnerId = "cccccccccccccccccccccccccccccccc", "transfer_source_identity_mismatch");
        Reject(check, o => o.PlanetId = "cccccccccccccccccccccccccccccccc", "transfer_source_identity_mismatch");
        Reject(check, o => o.NativeWorldId = 1, "transfer_source_identity_mismatch");
        Reject(check, o => o.MinionId = 103, "transfer_source_identity_mismatch");
        Reject(check, o => o.ExistingSelectedMinion = false, "transfer_exact_existing_selection_required");
        Reject(check, o => o.ExactTwoRegisteredRoots = false, "transfer_exact_registered_minion_proxy_required");
        Reject(check, o => o.ProxyId = 102, "transfer_exact_registered_minion_proxy_required");
        Reject(check, o => o.BiologicalModel = false, "transfer_biological_model_only");
        Reject(check, o => o.Live = false, "transfer_healthy_living_unsick_minion_required");
        Reject(check, o => o.HealthyState = false, "transfer_healthy_living_unsick_minion_required");
        Reject(check, o => o.HitPoints = 99, "transfer_healthy_living_unsick_minion_required");
        Reject(check, o => o.HitPoints = float.NaN, "transfer_healthy_living_unsick_minion_required");
        Reject(check, o => o.MaxHitPoints = float.PositiveInfinity, "transfer_healthy_living_unsick_minion_required");
        Reject(check, o => o.Sicknesses = 1, "transfer_healthy_living_unsick_minion_required");
        Reject(check, o => o.StorageItems = 1, "transfer_empty_storage_required");
        Reject(check, o => o.EquipmentAssignments = 1, "transfer_empty_equipment_required");
        Reject(check, o => o.OwnableAssignments = 1, "transfer_source_ownable_assignment_present");
        Reject(check, o => o.WorkerIdle = false, "transfer_worker_not_idle");
        Reject(check, o => o.ChoreStateInitialized = false, "transfer_chore_state_uninitialized");
        Reject(check, o => o.CurrentChore = "IdleChore", "transfer_current_chore_present");
        Reject(check, o => o.NextChore = "FetchChore", "transfer_queued_chore_present");
        Reject(check, o => o.NavigationInitialized = false, "transfer_stopped_floor_navigation_required");
        Reject(check, o => o.NavigationStopped = false, "transfer_stopped_floor_navigation_required");
        Reject(check, o => o.FloorNavigation = false, "transfer_stopped_floor_navigation_required");
        Reject(check, o => o.Standing = false, "transfer_stopped_floor_navigation_required");
        Reject(check, o => o.TransitionClear = false, "transfer_navigation_or_rocket_transition_present");
        Reject(check, o => o.NavigationTargetClear = false, "transfer_navigation_or_rocket_transition_present");
        Reject(check, o => o.NavigationPathClear = false, "transfer_navigation_or_rocket_transition_present");
        Reject(check, o => o.RocketMoveClear = false, "transfer_navigation_or_rocket_transition_present");
        Reject(check, o => o.AssignmentAuditComplete = false, "transfer_incoming_assignment_audit_incomplete");
        Reject(check, o => o.IncomingOwners = new[] { new DuplicantTransferOwnerEdge { Kind = "saved-ref", ObjectId = 801, SavedOwnerId = 108, Type = "Bed" } },
            "transfer_incoming_assignment_present");
        Reject(check, o => o.IncomingOwners = new[] { new DuplicantTransferOwnerEdge { Kind = "cached-ref", ObjectId = 801, SavedOwnerId = -1, Type = "Bed" } },
            "transfer_incoming_assignment_present");
        Reject(check, o => o.IncomingOwners = new[] { new DuplicantTransferOwnerEdge { Kind = "group-member", Group = "private-team" } },
            "transfer_incoming_assignment_present");
        Reject(check, o => o.ReadFailures = new[] { "transfer_assignment_registry_incomplete" }, "transfer_assignment_registry_incomplete");

        var damaged = Idle(); damaged.CurrentChore = "DigChore"; damaged.StorageItems = 2;
        var rejection = DuplicantTransferPreflightPolicy.Evaluate(Expected(), damaged);
        check(!rejection.SourceEligible && rejection.Rejections.Contains("transfer_current_chore_present") && rejection.Rejections.Contains("transfer_empty_storage_required"),
            "all relevant rejection causes reported together without stopping work or dropping cargo");
        check(damaged.CurrentChore == "DigChore" && damaged.StorageItems == 2, "rejected source work and carried objects remain untouched");
        var wrongExpected = Expected(); wrongExpected.MinionId = -1;
        check(!DuplicantTransferPreflightPolicy.Evaluate(wrongExpected, Idle()).SourceEligible, "selection identity cannot be inferred from an invalid permit");
        check(!DuplicantTransferPreflightPolicy.Evaluate(null, Idle()).SourceEligible, "missing exact expectation fails closed");
        check(!DuplicantTransferPreflightPolicy.Evaluate(Expected(), null).SourceEligible, "missing observation fails closed");
        check(!DuplicantTransferPreflightPolicy.Evaluate(Expected(), new DuplicantTransferObservation()).SourceEligible, "unobserved default facts fail closed");
    }

    private static void RejectNativeIdle(Action<bool, string> check, Action<DuplicantTransferObservation> change, string reason)
    {
        var source = NativeIdle(); change(source);
        var result = DuplicantTransferPreflightPolicy.Evaluate(Expected(), source);
        check(!result.SourceEligible && !result.ImportReady && result.Rejections.Contains(reason), "native idle still rejects " + reason);
    }

    private static void Reject(Action<bool, string> check, Action<DuplicantTransferObservation> change, string reason)
    {
        var source = Idle(); change(source);
        var result = DuplicantTransferPreflightPolicy.Evaluate(Expected(), source);
        check(!result.SourceEligible && !result.ImportReady && result.Rejections.Contains(reason), reason);
    }
}
