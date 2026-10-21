using System.Threading;

namespace OniOnline.Multiplayer
{
    // Main-thread bridge to the normal MoveTo chore, never teleportation.
    internal sealed class NativeDupeCommandExecutor : IDupeCommandExecutor
    {
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly bool probeExecution;
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
            int cell;
            string error = Resolve(planet, request, out monitor, out cell);
            return error == null ? WorkExecution.Accept(1) : WorkExecution.Reject(error);
        }
        public WorkExecution Execute(WorkPlanet planet, DupeMoveRequest request)
        {
            MoveToLocationMonitor.Instance monitor;
            int cell;
            // Revalidate navigation immediately before issuing the unique command.
            string error = Resolve(planet, request, out monitor, out cell);
            if (error != null) return WorkExecution.Reject(error);
            monitor.MoveToLocation(cell);
            return WorkExecution.Accept(1);
        }
        private string Resolve(WorkPlanet planet, DupeMoveRequest request, out MoveToLocationMonitor.Instance monitor, out int cell)
        {
            monitor = null; cell = -1;
            if (!probeExecution) return "dupe_native_validation_required";
            if (Thread.CurrentThread.ManagedThreadId != thread) return "dupe_wrong_thread";
            if (planet == null || !DupeCommandAuthority.Valid(request) || Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() || ClusterManager.Instance == null)
                return "dupe_world_unavailable";
            WorldContainer world = ClusterManager.Instance.GetWorld(planet.NativeWorldId);
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
                if (monitor == null) return "dupe_cannot_move";
                if (!navigator.CanReach(cell)) return "dupe_unreachable";
                return null;
            }
            return "dupe_not_found";
        }
    }
}
