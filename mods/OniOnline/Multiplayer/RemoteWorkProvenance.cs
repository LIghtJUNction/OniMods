using HarmonyLib;
using KSerialization;
using System.Runtime.Serialization;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // Added to the prefab, so ONI can deserialize this exact component on restored DigPlacers.
    // Restored managed orders survive only when the authority journal matches their exact token.
    // Ordinary native dig orders have Managed=false and are never touched.
    [SerializationConfig(MemberSerialization.OptIn)]
    internal sealed class RemoteWorkDigProvenance : KMonoBehaviour
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
        private bool restored;
        internal bool IntentionalRemoval;
        internal bool Managed { get { return managed; } }
        internal string PlanetId { get { return planetId; } }
        internal string OrderToken { get { return orderToken; } }
        internal string TaskKey { get { return ((byte)RemoteTaskLayer.Terrain) + ":" + localX + ":" + localY; } }

        [OnDeserialized]
        private void OnDeserialized() { restored = true; }

        internal void Bind(WorkPlanet planet, WorkTaskIntent plan)
        {
            managed = true; planetId = planet.PlanetId; actorId = plan.ActorId;
            requestId = plan.RequestId; authorityEpoch = planet.AuthorityEpoch;
            if (string.IsNullOrEmpty(orderToken)) orderToken = System.Guid.NewGuid().ToString("N");
            localX = plan.Cell.X; localY = plan.Cell.Y; priority = plan.Priority; confirmedRevision = plan.ConfirmedRevision;
            NativeWorkJournal.Register(this);
        }
        internal RemoteWorkSavedOrder CaptureOrder()
        { return new RemoteWorkSavedOrder { Token = orderToken, OriginEpoch = authorityEpoch, Winner = new WorkTaskIntent { PlanetId = planetId,
            ActorId = actorId, RequestId = requestId, Cell = new WorkCell(localX, localY), Priority = priority, ConfirmedRevision = confirmedRevision,
            Kind = RemoteWorkKind.Dig, Layer = RemoteTaskLayer.Terrain } }; }
        internal bool MatchesNativeCell(int nativeWorldId)
        {
            var world = ClusterManager.Instance?.GetWorld(nativeWorldId);
            int cell = Grid.PosToCell(this);
            return world != null && Grid.IsValidCell(cell) && Grid.WorldIdx[cell] == nativeWorldId && Grid.CellColumn(cell) - world.WorldOffset.x == localX &&
                Grid.CellRow(cell) - world.WorldOffset.y == localY && GetComponent<Diggable>() != null;
        }

        internal bool Matches(WorkPlanet planet, string id)
        { return managed && planetId == planet.PlanetId && authorityEpoch == planet.AuthorityEpoch && requestId == id; }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (!managed || !restored) return;
            if (NativeWorkJournal.TryRetain(this))
            { Debug.Log("[OniOnline] Restored managed dig retained with exact journal token: " + orderToken); return; }
            IntentionalRemoval = true;
            gameObject.Trigger(2127324410, new Boxed<bool> { value = true });
            Debug.Log("[OniOnline] Restored managed dig cancelled pending authority-journal restoration: " + planetId + "/" + actorId + "/" + requestId);
        }
        protected override void OnCleanUp()
        {
            if (managed) NativeWorkJournal.Removed(this, IntentionalRemoval);
            base.OnCleanUp();
        }
    }

    [HarmonyPatch(typeof(DigPlacerConfig), nameof(DigPlacerConfig.CreatePrefab))]
    internal static class RemoteWorkDigPrefabPatch
    {
        private static void Postfix(GameObject __result)
        { if (__result != null) __result.AddOrGet<RemoteWorkDigProvenance>(); }
    }
}
