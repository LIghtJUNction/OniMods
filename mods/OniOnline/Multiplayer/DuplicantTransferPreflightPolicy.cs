using System;
using System.Collections.Generic;
using System.Linq;

namespace OniOnline.Multiplayer
{
    [Serializable]
    internal sealed class DuplicantTransferExpectation
    {
        public string OwnerId = "";
        public string PlanetId = "";
        public int NativeWorldId = -1;
        public int MinionId = -1;
        public string ActiveSavePath = "";
    }

    [Serializable]
    internal sealed class DuplicantTransferOwnerEdge
    {
        public string Kind = "";
        public int ObjectId = -1;
        public int SavedOwnerId = -1;
        public string Type = "";
        public string Slot = "";
        public string Group = "";
    }

    [Serializable]
    internal sealed class DuplicantTransferAmbientMembership
    {
        public string RegistryKey = "";
        public string GroupId = "";
        public bool ExactNativeType = false;
        public int RegistryOccurrences = 0;
        public int DirectSelectedProxyCount = 0;
        public int DirectSelectedMinionCount = 0;
        public bool HasNestedMembers = false;
        public bool HasDuplicateMembers = false;
        public bool ReviewedProxyMembers = false;
    }

    [Serializable]
    internal sealed class DuplicantTransferObservation
    {
        public int Build = 0;
        public bool Loaded = false;
        public bool Paused = false;
        public bool Stable = false;
        public bool SavedSource = false;
        public string ActiveSavePath = "";
        public double SourceTime = 0;
        public string OwnerId = "";
        public string PlanetId = "";
        public int NativeWorldId = -1;
        public int MinionId = -1;
        public int ProxyId = -1;
        public bool ExistingSelectedMinion = false;
        public bool ExactTwoRegisteredRoots = false;
        public bool BiologicalModel = false;
        public bool Live = false;
        public bool HealthyState = false;
        public float HitPoints = 0;
        public float MaxHitPoints = 0;
        public int Sicknesses = -1;
        public int StorageItems = -1;
        public int EquipmentAssignments = -1;
        public int OwnableAssignments = -1;
        public bool WorkerIdle = false;
        public bool ChoreStateInitialized = false;
        public string CurrentChore = "";
        public string NextChore = "";
        public bool ExactNativeIdleChore = false;
        public bool IdleChoreStateInitialized = false;
        public bool IdleChoreOnFloor = false;
        public bool IdleChoreBoundToSelected = false;
        public bool NavigationInitialized = false;
        public bool NavigationStopped = false;
        public bool FloorNavigation = false;
        public bool TransitionClear = false;
        public bool NavigationTargetClear = false;
        public bool NavigationPathClear = false;
        public bool Standing = false;
        public bool RocketMoveClear = false;
        public bool AssignmentAuditComplete = false;
        public int SceneAssignables = 0;
        public int AssignmentGroups = 0;
        public DuplicantTransferAmbientMembership[] AmbientMembership = Array.Empty<DuplicantTransferAmbientMembership>();
        public int AmbientPublicFacilities = 0;
        public DuplicantTransferOwnerEdge[] IncomingOwners = Array.Empty<DuplicantTransferOwnerEdge>();
        public string[] ReadFailures = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class DuplicantTransferPreflightResult
    {
        public bool SourceEligible = false;
        public bool ImportReady = false;
        public DuplicantTransferObservation Observation = null;
        public string[] Rejections = Array.Empty<string>();
        public string[] RequiredContracts = Array.Empty<string>();
    }

    // This policy only judges an observed source. It cannot authorize any physical migration.
    internal static class DuplicantTransferPreflightPolicy
    {
        internal static DuplicantTransferPreflightResult Evaluate(DuplicantTransferExpectation expected, DuplicantTransferObservation observed)
        {
            var reasons = new List<string>();
            if (expected == null || !Id(expected.OwnerId) || !Id(expected.PlanetId) || expected.NativeWorldId < 0 || expected.MinionId <= 0 ||
                string.IsNullOrEmpty(expected.ActiveSavePath)) reasons.Add("transfer_expected_source_identity_required");
            if (observed == null)
            {
                reasons.Add("transfer_native_observation_missing");
                return Result(observed, reasons);
            }
            if (observed.Build != 744825) reasons.Add("transfer_native_build_not_reviewed");
            if (!observed.Loaded) reasons.Add("transfer_loaded_nonloading_colony_required");
            if (!observed.Paused) reasons.Add("transfer_pause_required");
            if (!observed.Stable || double.IsNaN(observed.SourceTime) || double.IsInfinity(observed.SourceTime)) reasons.Add("transfer_observation_changed");
            if (!observed.SavedSource) reasons.Add("transfer_saved_source_required");
            if (expected != null && (observed.OwnerId != expected.OwnerId || observed.PlanetId != expected.PlanetId ||
                observed.NativeWorldId != expected.NativeWorldId || observed.MinionId != expected.MinionId ||
                observed.ActiveSavePath != expected.ActiveSavePath)) reasons.Add("transfer_source_identity_mismatch");
            if (!observed.ExistingSelectedMinion) reasons.Add("transfer_exact_existing_selection_required");
            if (!observed.ExactTwoRegisteredRoots || observed.MinionId <= 0 || observed.ProxyId <= 0 || observed.MinionId == observed.ProxyId)
                reasons.Add("transfer_exact_registered_minion_proxy_required");
            if (!observed.BiologicalModel) reasons.Add("transfer_biological_model_only");
            if (!observed.Live || !observed.HealthyState || !Finite(observed.HitPoints) || !Finite(observed.MaxHitPoints) ||
                observed.MaxHitPoints <= 0 || observed.HitPoints != observed.MaxHitPoints || observed.Sicknesses != 0)
                reasons.Add("transfer_healthy_living_unsick_minion_required");
            if (observed.StorageItems != 0) reasons.Add("transfer_empty_storage_required");
            if (observed.EquipmentAssignments != 0) reasons.Add("transfer_empty_equipment_required");
            if (observed.OwnableAssignments != 0) reasons.Add("transfer_source_ownable_assignment_present");
            if (!observed.WorkerIdle) reasons.Add("transfer_worker_not_idle");
            if (!observed.ChoreStateInitialized) reasons.Add("transfer_chore_state_uninitialized");
            bool reconstructibleIdle = observed.CurrentChore == "IdleChore" && observed.ExactNativeIdleChore &&
                observed.IdleChoreStateInitialized && observed.IdleChoreOnFloor && observed.IdleChoreBoundToSelected;
            if (!string.IsNullOrEmpty(observed.CurrentChore) && !reconstructibleIdle) reasons.Add("transfer_current_chore_present");
            if (!string.IsNullOrEmpty(observed.NextChore)) reasons.Add("transfer_queued_chore_present");
            if (!observed.NavigationInitialized || !observed.NavigationStopped || !observed.FloorNavigation || !observed.Standing)
                reasons.Add("transfer_stopped_floor_navigation_required");
            if (!observed.TransitionClear || !observed.NavigationTargetClear || !observed.NavigationPathClear || !observed.RocketMoveClear)
                reasons.Add("transfer_navigation_or_rocket_transition_present");
            if (observed.AmbientMembership == null || observed.AmbientMembership.Length != 1 ||
                !IsStandardPublicMembership(observed.AmbientMembership[0]) || observed.AmbientPublicFacilities < 0)
                reasons.Add("transfer_default_public_membership_not_exact");
            if (!observed.AssignmentAuditComplete) reasons.Add("transfer_incoming_assignment_audit_incomplete");
            if (observed.IncomingOwners == null || observed.IncomingOwners.Length != 0) reasons.Add("transfer_incoming_assignment_present");
            if (observed.ReadFailures == null) reasons.Add("transfer_native_observation_incomplete");
            else reasons.AddRange(observed.ReadFailures);
            return Result(observed, reasons);
        }

        private static DuplicantTransferPreflightResult Result(DuplicantTransferObservation observed, List<string> reasons)
        {
            string[] rejection = reasons.Distinct(StringComparer.Ordinal).ToArray();
            return new DuplicantTransferPreflightResult { Observation = observed, Rejections = rejection, SourceEligible = rejection.Length == 0,
                ImportReady = false, RequiredContracts = new[] {
                    "complete_all_component_ref_cell_world_time_and_callback_closure",
                    "destination_public_group_rebind",
                    "rebuild_transient_native_idle_after_standard_load_never_copy_source_chore",
                    "source_checkpoint_hash_and_durable_transfer_origin_identity",
                    "two_root_destination_id_reservation_without_tracker_collisions",
                    "inactive_prefab_initialization_and_deserialization_callback_barrier",
                    "persist_source_release_before_destination_activation",
                    "durable_transaction_commit_rollback_and_reload_deduplication",
                    "source_3_to_2_destination_3_to_4_native_saved_population_conservation" } };
        }

        // Native AssignmentManager creates exactly public/public, and a proxy's OnSpawn adds
        // that proxy directly. Shared access is not exclusive ownership of every public facility.
        internal static bool IsStandardPublicMembership(DuplicantTransferAmbientMembership membership)
        {
            return membership != null && membership.RegistryKey == "public" && membership.GroupId == "public" &&
                membership.ExactNativeType && membership.RegistryOccurrences == 1 && membership.DirectSelectedProxyCount == 1 &&
                membership.DirectSelectedMinionCount == 0 && !membership.HasNestedMembers && !membership.HasDuplicateMembers &&
                membership.ReviewedProxyMembers;
        }

        private static bool Id(string value) { return value != null && Guid.TryParseExact(value, "N", out _); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
