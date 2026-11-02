using System;
using System.Collections.Generic;
using System.Threading;

namespace OniOnline.Multiplayer
{
    // Main-thread bridge to the normal MoveTo chore, never teleportation.
    internal sealed class NativeDupeCommandExecutor : IDupeCommandExecutor, IDupeCommandLifecycle
    {
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly bool probeExecution;
        private readonly Dictionary<string, Lease> leases = new Dictionary<string, Lease>();
        private sealed class Lease
        {
            internal DupeCommandTaskIdentity Identity;
            internal Game Game;
            internal WorldContainer World;
            internal MinionIdentity Dupe;
            internal MoveToLocationMonitor.Instance Monitor;
            internal MoveChore Chore;
            internal bool CancelAttempted;
        }
        internal NativeDupeCommandExecutor(bool enableProbeExecution = false)
        {
#if ONIONLINE_RUNTIME_PROBE
            probeExecution = enableProbeExecution;
#else
            probeExecution = false;
#endif
        }

        public WorkExecution Preflight(WorkPlanet planet, DupeMoveRequest request)
        {
            MoveToLocationMonitor.Instance monitor;
            MinionIdentity dupe;
            WorldContainer world;
            int cell;
            string error = Resolve(planet, request, out monitor, out dupe, out world, out cell);
            return error == null ? WorkExecution.Accept(1) : WorkExecution.RejectBeforeMutation(error);
        }

        public WorkExecution Execute(WorkPlanet planet, DupeMoveRequest request)
        {
            MoveToLocationMonitor.Instance monitor;
            MinionIdentity dupe;
            WorldContainer world;
            int cell;
            // Revalidate navigation immediately before issuing the unique command.
            string error = Resolve(planet, request, out monitor, out dupe, out world, out cell);
            if (error != null) return WorkExecution.RejectBeforeMutation(error);
            if (!DupeCommandAuthority.Id(request.NativeLeaseId) || leases.ContainsKey(request.NativeLeaseId))
                return WorkExecution.RejectBeforeMutation("dupe_native_lease_conflict");
            MoveChore previous;
            if (!TryGetMove(monitor, out previous)) return WorkExecution.RejectBeforeMutation("dupe_native_tracking_unavailable");
            Game game = Game.Instance;
            // Retain a scope tombstone before entering native code. If creation is
            // unobservable, this remains unknown until that actual game/world is gone;
            // it must neither guess a chore nor block unrelated later saves forever.
            var lease = new Lease { Identity = new DupeCommandTaskIdentity(planet, request, game, world, monitor, null),
                Game = game, World = world, Dupe = dupe, Monitor = monitor };
            leases.Add(request.NativeLeaseId, lease);
            bool captured = false;
            try { monitor.MoveToLocation(cell); }
            finally
            {
                // Official 744825 ToggleChore stores the native MoveChore in the
                // monitor's public dataTable. Capture it even if a callback throws.
                MoveChore created;
                if (TryGetMove(monitor, out created) && created != null && !ReferenceEquals(created, previous))
                {
                    lease.Identity = new DupeCommandTaskIdentity(planet, request, game, world, monitor, created);
                    lease.Chore = created;
                    captured = true;
                }
            }
            if (!captured) throw new InvalidOperationException("dupe_native_tracking_unknown");
            return WorkExecution.Accept(1);
        }

        public DupeCommandState GetState(WorkPlanet planet, DupeMoveRequest request)
        {
            Lease lease;
            if (Thread.CurrentThread.ManagedThreadId != thread || !TryLease(planet, request, out lease)) return DupeCommandState.None;
            DupeCommandState state = Inspect(lease);
            if (state >= DupeCommandState.Completed) leases.Remove(request.NativeLeaseId);
            return state;
        }

        public WorkExecution Cancel(WorkPlanet planet, DupeMoveRequest request)
        {
            Lease lease;
            if (Thread.CurrentThread.ManagedThreadId != thread || !TryLease(planet, request, out lease))
                return WorkExecution.RejectBeforeMutation("dupe_cleanup_unknown");
            DupeCommandState before = Inspect(lease);
            if (before >= DupeCommandState.Completed)
            { leases.Remove(request.NativeLeaseId); return WorkExecution.Accept(0); }
            if (before != DupeCommandState.Active) return WorkExecution.RejectBeforeMutation("dupe_cleanup_unknown");
            // Inspect compared the exact game/world/monitor/chore objects. Native
            // ClearChore unhooks its onExit callback before cancelling the chore;
            // stopping the entire monitor would also break later local Move To clicks.
            lease.CancelAttempted = true;
            lease.Monitor.GoTo(lease.Monitor.sm.satisfied);
            DupeCommandState after = Inspect(lease);
            if (after < DupeCommandState.Completed) return WorkExecution.Reject("dupe_cleanup_unknown");
            leases.Remove(request.NativeLeaseId);
            return WorkExecution.Accept(1);
        }

        private bool TryLease(WorkPlanet planet, DupeMoveRequest request, out Lease lease)
        {
            lease = null;
            return request != null && request.NativeLeaseId != null && leases.TryGetValue(request.NativeLeaseId, out lease) &&
                lease.Identity.OwnsRequest(planet, request);
        }

        private static DupeCommandState Inspect(Lease lease)
        {
            Game game = Game.Instance;
            if (lease.Game == null || lease.World == null) return DupeCommandState.WorldChanged;
            // A temporarily missing singleton/lookup during scene loading is not
            // proof that an unobserved task is gone. Require destruction or a real
            // replacement object before dropping an uncertain scope reservation.
            if (game == null) return DupeCommandState.None;
            if (!ReferenceEquals(game, lease.Game)) return DupeCommandState.WorldChanged;
            if (ClusterManager.Instance == null) return DupeCommandState.None;
            WorldContainer currentWorld = ClusterManager.Instance.GetWorld(lease.World.id);
            if (currentWorld == null) return DupeCommandState.None;
            if (!lease.Identity.MatchesScope(game, currentWorld)) return DupeCommandState.WorldChanged;
            if (!game.baseAlreadyCreated || game.IsLoading() || KMonoBehaviour.isLoadingScene) return DupeCommandState.None;
            if (lease.Chore == null) return DupeCommandState.None;
            if (lease.Dupe == null || lease.Monitor.isMasterNull) return DupeCommandState.WorldChanged;
            Navigator navigator = lease.Dupe.GetComponent<Navigator>();
            MoveToLocationMonitor.Instance currentMonitor = navigator == null ? null : navigator.GetSMI<MoveToLocationMonitor.Instance>();
            if (!ReferenceEquals(currentMonitor, lease.Monitor)) return DupeCommandState.Replaced;
            MoveChore current;
            if (!TryGetMove(currentMonitor, out current)) return DupeCommandState.None;
            if (lease.Chore.isComplete) return DupeCommandState.Completed;
            if (!lease.Identity.MatchesNative(game, lease.World, currentMonitor, current) ||
                !currentMonitor.IsInsideState(currentMonitor.sm.moving) || lease.Chore.provider == null)
                return lease.CancelAttempted ? DupeCommandState.Cancelled : DupeCommandState.Replaced;
            return DupeCommandState.Active;
        }

        private static bool TryGetMove(MoveToLocationMonitor.Instance monitor, out MoveChore chore)
        {
            chore = null;
            if (monitor == null || monitor.dataTable == null) return false;
            foreach (object entry in monitor.dataTable)
            {
                MoveChore candidate = entry as MoveChore;
                if (candidate == null) continue;
                // An incompatible mod changing the monitor to own several chores is
                // ambiguous. Never guess an index or cancel whichever chore is first.
                if (chore != null && !ReferenceEquals(chore, candidate)) return false;
                chore = candidate;
            }
            return true;
        }

        private string Resolve(WorkPlanet planet, DupeMoveRequest request, out MoveToLocationMonitor.Instance monitor,
            out MinionIdentity selected, out WorldContainer world, out int cell)
        {
            monitor = null; selected = null; world = null; cell = -1;
            if (!probeExecution) return "dupe_native_validation_required";
            if (Thread.CurrentThread.ManagedThreadId != thread) return "dupe_wrong_thread";
            if (StateMachine.Instance.error || App.IsExiting) return "dupe_cannot_move";
            if (planet == null || !DupeCommandAuthority.Valid(request) || request.IsCancellation || Game.Instance == null ||
                !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() || KMonoBehaviour.isLoadingScene || ClusterManager.Instance == null)
                return "dupe_world_unavailable";
            world = ClusterManager.Instance.GetWorld(planet.NativeWorldId);
            if (world == null || world.id < 0 || world.id > byte.MaxValue || Grid.WorldIdx == null || Grid.Visible == null) return "dupe_world_unavailable";
            if (request.Target.X >= world.WorldSize.x || request.Target.Y >= world.WorldSize.y) return "dupe_outside_world";
            cell = Grid.XYToCell(request.Target.X + world.WorldOffset.x, request.Target.Y + world.WorldOffset.y);
            if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world.id) return "dupe_outside_world";
            if (Grid.Visible[cell] == 0) return "dupe_unexplored";
            foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null) continue;
                KPrefabID identity = dupe.GetComponent<KPrefabID>();
                if (identity == null || identity.InstanceID != request.DupeId) continue;
                int dupeCell = Grid.PosToCell(dupe);
                if (!Grid.IsValidCell(dupeCell) || Grid.WorldIdx[dupeCell] != world.id) return "dupe_not_in_world";
                Navigator navigator = dupe.GetComponent<Navigator>();
                monitor = navigator == null ? null : navigator.GetSMI<MoveToLocationMonitor.Instance>();
                if (monitor == null || !monitor.IsRunning() || monitor.isCrashed || monitor.dataTable == null) return "dupe_cannot_move";
                MoveChore current;
                if (!TryGetMove(monitor, out current)) return "dupe_native_tracking_unavailable";
                if (!navigator.CanReach(cell)) return "dupe_unreachable";
                selected = dupe;
                return null;
            }
            return "dupe_not_found";
        }
    }
}
