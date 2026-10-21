using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // Main-thread native bridge. No resources, dupes or completed buildings are spawned.
    internal sealed class NativeWorkExecutor : IWorkExecutor, IRemoteWorkPersistence, IWorkCapabilities
    {
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<string, TrackedOrder> tracked = new Dictionary<string, TrackedOrder>();
        private string epoch;
        private string planetId;
        private readonly bool executionEnabled;
        private readonly bool buildingExecutionEnabled;
        private RemoteWorkAuthority authority;
        private sealed class TrackedOrder { internal int Cell; internal GameObject Order; internal string RequestId; internal WorkTaskIntent Plan; }

        // The native journal, cancellation and permission-revocation paths have real-game
        // coverage. RemoteWorkAuthority still requires an explicit grant and preview token.
        internal NativeWorkExecutor(bool enableExecution = true, bool enableBuildings = false)
        { executionEnabled = enableExecution; buildingExecutionEnabled = enableBuildings; }

        public bool SupportsBuildingExecution { get { return executionEnabled && buildingExecutionEnabled; } }

        public void Attach(RemoteWorkAuthority value) { authority = value; }
        public RemoteWorkSavedPlanet Restore(WorkPlanet planet, string ownerPeerId)
        {
            RemoteWorkSavedPlanet saved = NativeWorkJournal.Restore(planet, ownerPeerId, authority, this);
            tracked.Clear(); epoch = planet.AuthorityEpoch; planetId = planet.PlanetId;
            if (!SupportsBuildingExecution)
                foreach (var marker in NativeWorkJournal.BuildMarkers(planet.PlanetId)) marker.Quarantine("work_native_build_validation_required");
            saved = RemoteWorkPersistence.FilterBuildRecovery(saved, SupportsBuildingExecution,
                NativeWorkJournal.BuildMarkers(planet.PlanetId).Select(m => m.CaptureOrder()).ToArray());
            if (saved == null || saved.Blocked) return saved;
            WorkTaskIntent[] restoredPlans = RemoteWorkPersistence.RestorePlans(saved, ownerPeerId);
            foreach (var marker in NativeWorkJournal.Markers(planet.PlanetId))
            {
                RemoteWorkSavedOrder order = marker.CaptureOrder();
                if (!saved.Orders.Any(o => o.Token == order.Token && o.OriginEpoch == order.OriginEpoch && RemoteWorkPersistence.SameIntent(o.Winner, order.Winner))) continue;
                WorkTaskIntent plan = restoredPlans.FirstOrDefault(p => p.HistoricalActive && p.RequestId == order.Winner.RequestId && p.Key == order.Winner.Key);
                if (plan == null) continue;
                marker.Bind(planet, plan);
                tracked[plan.Key] = new TrackedOrder { Cell = Grid.PosToCell(marker), Order = marker.gameObject, RequestId = plan.RequestId, Plan = plan.Copy() };
            }
            foreach (var marker in NativeWorkJournal.BuildMarkers(planet.PlanetId))
            {
                RemoteWorkSavedOrder order = marker.CaptureOrder();
                if (!saved.Orders.Any(o => o.Token == order.Token && o.OriginEpoch == order.OriginEpoch && o.NativeObjectId == order.NativeObjectId && RemoteWorkPersistence.SameIntent(o.Winner, order.Winner))) continue;
                WorkTaskIntent plan = restoredPlans.FirstOrDefault(p => p.HistoricalActive && p.RequestId == order.Winner.RequestId && p.Key == order.Winner.Key);
                if (plan == null) continue;
                marker.Bind(planet, plan);
                tracked[plan.Key] = new TrackedOrder { Cell = Grid.PosToCell(marker), Order = marker.gameObject, RequestId = plan.RequestId, Plan = plan.Copy() };
            }
            return saved;
        }
        public void Publish(WorkPlanet planet, string ownerPeerId) { NativeWorkJournal.Publish(planet.PlanetId); }
        internal void ForgetMarker(RemoteWorkDigProvenance marker)
        {
            TrackedOrder order;
            if (tracked.TryGetValue(marker.TaskKey, out order) && order.Order == marker.gameObject) tracked.Remove(marker.TaskKey);
        }

        internal void ForgetBuildMarker(RemoteWorkBuildProvenance marker)
        {
            TrackedOrder order;
            if (tracked.TryGetValue(marker.TaskKey, out order) && order.Order == marker.gameObject) tracked.Remove(marker.TaskKey);
        }
        private bool ManagedPendingBuilding(GameObject order, WorkPlanet planet)
        {
            return tracked.Values.Any(t => t.Order == order && t.Plan != null && t.Plan.Kind == RemoteWorkKind.Build &&
                order.GetComponent<RemoteWorkBuildProvenance>() != null && order.GetComponent<RemoteWorkBuildProvenance>().Managed &&
                RemoteWorkPersistence.SameIntent(t.Plan, order.GetComponent<RemoteWorkBuildProvenance>().CaptureOrder().Winner) &&
                NativeBuildingWork.Matches(order, planet, t.Plan, order.GetComponent<RemoteWorkBuildProvenance>().CaptureOrder().NativeObjectId));
        }

        public WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request)
        {
            WorldContainer world;
            string error = Resolve(planet, out world);
            if (error != null) return WorkExecution.RejectBeforeMutation(error);
            if (request.Kind == RemoteWorkKind.Build && request.Layer == RemoteTaskLayer.Building)
            {
                if (request.Cells == null || request.Cells.Length != 1 || request.Priority < 1 || request.Priority > 9)
                    return WorkExecution.RejectBeforeMutation("work_invalid_cells");
                return NativeBuildingWork.Preflight(world, new WorkTaskIntent { Kind = request.Kind, Layer = request.Layer, Cell = request.Cells[0], Build = request.Build },
                    buildingExecutionEnabled, order => ManagedPendingBuilding(order, planet));
            }
            if (request.Kind != RemoteWorkKind.Dig || request.Layer != RemoteTaskLayer.Terrain || request.Build != null)
                return WorkExecution.Reject("work_unsupported_kind");
            if (request.Cells == null || request.Cells.Length < 1 || request.Cells.Length > RemoteWorkWire.MaxCells || request.Priority < 1 || request.Priority > 9)
                return WorkExecution.Reject("work_invalid_cells");
            foreach (var point in request.Cells)
            {
                int cell;
                error = ResolveCell(world, point, out cell);
                if (error != null) return WorkExecution.Reject(error);
                if (!Grid.Solid[cell] || Grid.Foundation[cell]) return WorkExecution.Reject("work_not_natural_solid");
                GameObject existing = Grid.Objects[cell, (int)ObjectLayer.DigPlacer];
                TrackedOrder own;
                string key = ((byte)RemoteTaskLayer.Terrain) + ":" + point.X + ":" + point.Y;
                if (existing != null && (!tracked.TryGetValue(key, out own) || own.Order != existing || epoch != planet.AuthorityEpoch || planetId != planet.PlanetId))
                    return WorkExecution.Reject("work_untracked_native_order");
                for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
                {
                    GameObject obstruction = Grid.Objects[cell, layer];
                    if (obstruction != null && obstruction.GetComponent<Constructable>() != null)
                        return WorkExecution.Reject("work_native_construction_conflict");
                }
            }
            return WorkExecution.Accept(request.Cells.Length);
        }

        public WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] previous, WorkTaskIntent[] desired)
        {
            WorldContainer world;
            string error = Resolve(planet, out world);
            if (error != null) return WorkExecution.RejectBeforeMutation(error);
            if (epoch != planet.AuthorityEpoch || planetId != planet.PlanetId)
            { tracked.Clear(); epoch = planet.AuthorityEpoch; planetId = planet.PlanetId; }
            // A closed build gate must not cancel existing Dig work and then discover that
            // a new winning blueprint cannot execute. Reject before any physical mutation.
            if (!buildingExecutionEnabled && (desired.Any(p => p.Kind == RemoteWorkKind.Build) || tracked.Values.Any(t => t.Plan != null && t.Plan.Kind == RemoteWorkKind.Build)))
                return WorkExecution.RejectBeforeMutation("work_native_build_validation_required");
            var wanted = desired.ToDictionary(p => p.Key);
            foreach (var old in tracked.Values.Where(t => t.Plan != null && t.Plan.Kind == RemoteWorkKind.Build && t.Order != null))
            {
                WorkTaskIntent replacement;
                if (wanted.TryGetValue(old.Plan.Key, out replacement) && RemoteWorkPersistence.SameIntent(old.Plan, replacement)) continue;
                error = NativeBuildingWork.CanChangePending(world, old.Order, old.Plan);
                if (error != null) return WorkExecution.RejectBeforeMutation(error);
            }
            foreach (string key in tracked.Keys.ToArray())
            {
                TrackedOrder old = tracked[key];
                WorkTaskIntent replacement;
                bool same = wanted.TryGetValue(key, out replacement) && old.Plan != null && WorkPlanGeometry.SameNative(old.Plan, replacement);
                bool live = old.Order != null && Grid.IsValidCell(old.Cell) && Grid.WorldIdx[old.Cell] == world.id &&
                    (old.Plan.Kind == RemoteWorkKind.Build ? old.Order.GetComponent<RemoteWorkBuildProvenance>() != null && old.Order.GetComponent<RemoteWorkBuildProvenance>().Managed &&
                        old.Order.GetComponent<RemoteWorkBuildProvenance>().CaptureOrder().OriginEpoch == planet.AuthorityEpoch &&
                        RemoteWorkPersistence.SameIntent(old.Order.GetComponent<RemoteWorkBuildProvenance>().CaptureOrder().Winner, old.Plan) && NativeBuildingWork.Matches(old.Order, planet, old.Plan, old.Order.GetComponent<RemoteWorkBuildProvenance>().CaptureOrder().NativeObjectId) :
                        Grid.Objects[old.Cell, (int)ObjectLayer.DigPlacer] == old.Order);
                if (same && live)
                {
                    if (RemoteWorkPersistence.SameIntent(old.Plan, replacement)) continue;
                    // Compatible dig plans share one physical designation; replace its attribution/priority.
                    var priority = old.Order.GetComponent<Prioritizable>();
                    if (priority != null) priority.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, replacement.Priority));
                    var provenance = old.Order.GetComponent<RemoteWorkDigProvenance>();
                    if (provenance != null) provenance.Bind(planet, replacement);
                    var buildProvenance = old.Order.GetComponent<RemoteWorkBuildProvenance>();
                    if (buildProvenance != null) buildProvenance.Bind(planet, replacement);
                    old.RequestId = replacement.RequestId; old.Plan = replacement.Copy();
                    continue;
                }
                if (!same && live)
                {
                    // Exact native user-menu cancellation event used by Diggable.OnCancel.
                    var provenance = old.Order.GetComponent<RemoteWorkDigProvenance>();
                    if (provenance != null) provenance.IntentionalRemoval = true;
                    var buildProvenance = old.Order.GetComponent<RemoteWorkBuildProvenance>();
                    if (buildProvenance != null) buildProvenance.IntentionalRemoval = true;
                    old.Order.Trigger(2127324410, new Boxed<bool> { value = true });
                }
                tracked.Remove(key);
            }
            int count = 0;
            foreach (var plan in desired)
            {
                if (tracked.ContainsKey(plan.Key)) continue;
                int cell;
                error = ResolveCell(world, plan.Cell, out cell);
                if (error != null) return WorkExecution.Reject(error);
                if (plan.Kind == RemoteWorkKind.Build)
                {
                    WorkExecution buildCheck = NativeBuildingWork.Preflight(world, plan, buildingExecutionEnabled, null);
                    if (!buildCheck.Accepted) return WorkExecution.Reject(buildCheck.Reason);
                    GameObject blueprint = NativeBuildingWork.Place(world, plan, out error);
                    if (blueprint == null) return WorkExecution.Reject(error ?? "work_native_build_rejected");
                    var marker = blueprint.GetComponent<RemoteWorkBuildProvenance>();
                    if (marker == null)
                    { blueprint.Trigger(2127324410); return WorkExecution.Reject("work_native_build_provenance_missing"); }
                    marker.Bind(planet, plan);
                    var buildPriority = blueprint.GetComponent<Prioritizable>();
                    if (buildPriority != null) buildPriority.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, plan.Priority));
                    tracked[plan.Key] = new TrackedOrder { Cell = cell, Order = blueprint, RequestId = plan.RequestId, Plan = plan.Copy() };
                    count++; continue;
                }
                // A finished dig is physical history. Do not recreate it or restore material.
                if (!Grid.Solid[cell] && !Grid.Foundation[cell]) continue;
                var request = new RemoteWorkRequest { Kind = plan.Kind, Layer = plan.Layer, Priority = plan.Priority, Cells = new[] { plan.Cell }, Build = plan.Build };
                WorkExecution check = Preflight(planet, request);
                // Earlier plans or tracked orders may already have changed. A per-cell
                // preflight cannot promise that the entire reconciliation was untouched.
                if (!check.Accepted) return WorkExecution.Reject(check.Reason);
                GameObject order = DigTool.PlaceDig(cell, 0);
                if (order == null) return WorkExecution.Reject("work_native_rejected");
                var provenanceMarker = order.GetComponent<RemoteWorkDigProvenance>();
                if (provenanceMarker == null)
                {
                    order.Trigger(2127324410, new Boxed<bool> { value = true });
                    return WorkExecution.Reject("work_native_provenance_missing");
                }
                provenanceMarker.Bind(planet, plan);
                var priority = order.GetComponent<Prioritizable>();
                if (priority != null) priority.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, plan.Priority));
                tracked[plan.Key] = new TrackedOrder { Cell = cell, Order = order, RequestId = plan.RequestId, Plan = plan.Copy() };
                count++;
            }
            return WorkExecution.Accept(count);
        }

        private string Resolve(WorkPlanet planet, out WorldContainer world)
        {
            world = null;
            if (!executionEnabled) return "work_native_validation_required";
            if (Thread.CurrentThread.ManagedThreadId != thread) return "work_wrong_thread";
            if (planet == null || Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() || ClusterManager.Instance == null || DigTool.Instance == null)
                return "work_world_unavailable";
            world = ClusterManager.Instance.GetWorld(planet.NativeWorldId);
            if (world == null || world.id < 0 || world.id > byte.MaxValue || Grid.WorldIdx == null || Grid.Visible == null)
                return "work_world_unavailable";
            return null;
        }
        private static string ResolveCell(WorldContainer world, WorkCell point, out int cell)
        {
            cell = -1;
            if (point.X < 0 || point.Y < 0 || point.X >= world.WorldSize.x || point.Y >= world.WorldSize.y)
                return "work_outside_world";
            cell = Grid.XYToCell(point.X + world.WorldOffset.x, point.Y + world.WorldOffset.y);
            if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world.id) return "work_outside_world";
            if (Grid.Visible[cell] == 0) return "work_unexplored";
            return null;
        }
    }
}
