using System;
using HarmonyLib;
using KSerialization;
using System.Runtime.Serialization;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // Ordinary blueprints have managed=false. Only an exact saved native ID, random token
    // and full journal intent can retain a remote blueprint after a native checkpoint load.
    [SerializationConfig(MemberSerialization.OptIn)]
    internal sealed class RemoteWorkBuildProvenance : KMonoBehaviour
    {
        [Serialize] private bool managed = false;
        [Serialize] private string planetId = "";
        [Serialize] private string actorId = "";
        [Serialize] private string requestId = "";
        [Serialize] private string authorityEpoch = "";
        [Serialize] private string orderToken = "";
        [Serialize] private int localX = 0;
        [Serialize] private int localY = 0;
        [Serialize] private int priority = 5;
        [Serialize] private long confirmedRevision = 0;
        [Serialize] private string prefabId = "";
        [Serialize] private string materialTag = "";
        [Serialize] private string facadeId = "DEFAULT_FACADE";
        [Serialize] private int orientation = 0;
        [Serialize] private int nativeObjectId = 0;
        [Serialize] private bool quarantined = false;
        private bool restored;
        private bool claimed;
        internal bool IntentionalRemoval;
        internal bool Managed { get { return managed && !quarantined && claimed; } }
        internal bool HasRestoredProvenance { get { return managed && !quarantined; } }
        internal string PlanetId { get { return planetId; } }
        internal string OrderToken { get { return orderToken; } }
        internal string TaskKey { get { return ((byte)RemoteTaskLayer.Building) + ":" + localX + ":" + localY; } }
        [OnDeserialized] private void OnDeserialized() { restored = true; }

        internal void Bind(WorkPlanet planet, WorkTaskIntent plan)
        {
            if (quarantined || !WorkPlanGeometry.ValidBuild(plan.Build)) throw new InvalidOperationException("work_build_marker_spec");
            var id = GetComponent<KPrefabID>();
            if (id == null || id.InstanceID < 1) throw new InvalidOperationException("work_build_native_identity_missing");
            managed = true; claimed = true; planetId = planet.PlanetId; actorId = plan.ActorId; requestId = plan.RequestId;
            authorityEpoch = planet.AuthorityEpoch; localX = plan.Cell.X; localY = plan.Cell.Y; priority = plan.Priority;
            confirmedRevision = plan.ConfirmedRevision; prefabId = plan.Build.PrefabId; materialTag = plan.Build.MaterialTags[0];
            facadeId = plan.Build.FacadeId; orientation = plan.Build.Orientation; nativeObjectId = id.InstanceID;
            if (string.IsNullOrEmpty(orderToken)) orderToken = Guid.NewGuid().ToString("N");
            NativeWorkJournal.Register(this);
        }
        internal RemoteWorkSavedOrder CaptureOrder()
        {
            return new RemoteWorkSavedOrder { Token = orderToken, OriginEpoch = authorityEpoch, NativeObjectId = nativeObjectId,
                Winner = new WorkTaskIntent { PlanetId = planetId, ActorId = actorId, RequestId = requestId, Cell = new WorkCell(localX, localY),
                    Priority = priority, ConfirmedRevision = confirmedRevision, Kind = RemoteWorkKind.Build, Layer = RemoteTaskLayer.Building,
                    Build = new WorkBuildSpec { PrefabId = prefabId, MaterialTags = new[] { materialTag }, FacadeId = facadeId, Orientation = orientation } } };
        }
        internal bool MatchesNativeCell(int worldId)
        { return managed && NativeBuildingWork.Matches(gameObject, new WorkPlanet { NativeWorldId = worldId }, CaptureOrder().Winner, nativeObjectId); }
        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (!managed || !restored) return;
            if (!quarantined && NativeWorkJournal.TryRetain(this))
            { Debug.Log("[OniOnline] Restored build awaits explicit authority capability; native blueprint preserved: " + orderToken); return; }
            Quarantine("work_build_restore_unverified");
        }
        internal void ClaimRestored()
        {
            if (!HasRestoredProvenance) throw new InvalidOperationException("work_build_restore_quarantined");
            claimed = true;
        }
        internal void Quarantine(string reason)
        {
            quarantined = true; claimed = false;
            NativeWorkJournal.ReleaseBuildMarker(this);
            // A cancellation can erase unrelated native dig/uproot orders. Failed recovery
            // therefore relinquishes remote ownership and preserves the physical blueprint.
            Debug.LogWarning("[OniOnline] Build provenance quarantined; native blueprint preserved: " + reason + " / " + planetId + "/" + requestId);
        }
        protected override void OnCleanUp()
        {
            if (Managed) NativeWorkJournal.Removed(this, IntentionalRemoval);
            else NativeWorkJournal.ReleaseBuildMarker(this);
            base.OnCleanUp();
        }
    }
    [HarmonyPatch(typeof(BuildingLoader), nameof(BuildingLoader.CreateBuildingUnderConstruction))]
    internal static class RemoteWorkBuildPrefabPatch
    {
        private static void Postfix(BuildingDef def, GameObject __result)
        {
            if (__result != null && (def.PrefabID == "StorageLocker" || def.PrefabID == "Outhouse")) __result.AddOrGet<RemoteWorkBuildProvenance>();
        }
    }
}
