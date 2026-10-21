using System;
using System.Linq;

namespace OniOnline.Multiplayer
{
    [Flags]
    internal enum RemoteWorkPermission : byte { None = 0, ViewMap = 1, IssueTasks = 2, CommandDupes = 4, ViewPlans = 8 }
    internal enum RemoteWorkKind : byte { Dig = 1, Build = 2 }
    internal enum RemoteTaskLayer : byte { Terrain = 1, Building = 2 }

    internal struct WorkCell
    {
        public int X;
        public int Y;
        public WorkCell(int x, int y) { X = x; Y = y; }
    }

    internal sealed class WorkPlanet
    {
        public string PlanetId;
        // A new, unpredictable generation on every native save load/world replacement.
        public string AuthorityEpoch;
        public int NativeWorldId;
        public string OwnerPeerId;
        public string AuthorityPeerId;
        public string PersistentOwnerId;
    }

    internal interface IWorldWorkContext
    {
        bool TryGetLocalPlanet(out WorkPlanet planet);
    }

    internal interface IWorkCapabilities
    {
        bool SupportsBuildingExecution { get; }
    }

    internal interface IWorkExecutor
    {
        WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request);
        // Only this mod's still-pending native orders may be replaced/cancelled.
        WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] previous, WorkTaskIntent[] desired);
    }

    internal sealed class WorkBuildSpec
    {
        public string PrefabId = "";
        public string[] MaterialTags = Array.Empty<string>();
        public int Orientation;
        public string FacadeId = "DEFAULT_FACADE";
        internal WorkBuildSpec Copy() { return new WorkBuildSpec { PrefabId = PrefabId, MaterialTags = MaterialTags == null ? null : (string[])MaterialTags.Clone(), Orientation = Orientation, FacadeId = FacadeId }; }
    }

    // The initial native allowlist has no ports, replacement or tile claims. Authority owns
    // this geometry; neither network clients nor stored claims can supply arbitrary extents.
    internal static class WorkPlanGeometry
    {
        internal static bool ValidBuild(WorkBuildSpec build)
        { return build != null && (build.PrefabId == "StorageLocker" || build.PrefabId == "Outhouse") && build.Orientation == 0 &&
            build.FacadeId == "DEFAULT_FACADE" && build.MaterialTags != null && build.MaterialTags.Length == 1 &&
            !string.IsNullOrEmpty(build.MaterialTags[0]) && build.MaterialTags[0].Length <= 64 && build.MaterialTags[0].All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_'); }
        internal static bool ValidShape(RemoteWorkKind kind, RemoteTaskLayer layer, WorkBuildSpec build)
        { return kind == RemoteWorkKind.Dig && layer == RemoteTaskLayer.Terrain && build == null ||
            kind == RemoteWorkKind.Build && layer == RemoteTaskLayer.Building && ValidBuild(build); }
        internal static WorkCell[] Footprint(WorkTaskIntent plan)
        {
            if (plan.Kind == RemoteWorkKind.Dig) return new[] { plan.Cell };
            if (!ValidBuild(plan.Build)) throw new InvalidOperationException("work_invalid_build_spec");
            int width = plan.Build.PrefabId == "Outhouse" ? 2 : 1, height = plan.Build.PrefabId == "Outhouse" ? 3 : 2;
            return Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width).Select(x => new WorkCell(checked(plan.Cell.X + x), checked(plan.Cell.Y + y)))).ToArray();
        }
        internal static bool Overlaps(WorkTaskIntent a, WorkTaskIntent b)
        {
            WorkCell[] first = Footprint(a), second = Footprint(b);
            return first.Any(x => second.Any(y => x.X == y.X && x.Y == y.Y));
        }
        internal static bool SameBuild(WorkBuildSpec a, WorkBuildSpec b)
        { return a == null || b == null ? a == b : a.PrefabId == b.PrefabId && a.Orientation == b.Orientation && a.FacadeId == b.FacadeId &&
            a.MaterialTags != null && b.MaterialTags != null && a.MaterialTags.SequenceEqual(b.MaterialTags); }
        internal static bool SameNative(WorkTaskIntent a, WorkTaskIntent b)
        { return a.Kind == b.Kind && a.Layer == b.Layer && a.Cell.X == b.Cell.X && a.Cell.Y == b.Cell.Y && SameBuild(a.Build, b.Build); }
        internal static WorkTaskIntent[] Merge(WorkTaskIntent[] candidates)
        {
            var result = new System.Collections.Generic.List<WorkTaskIntent>();
            foreach (var plan in candidates.OrderByDescending(p => p.Priority).ThenByDescending(p => p.ConfirmedRevision)
                .ThenBy(p => p.ActorId, StringComparer.Ordinal).ThenBy(p => p.RequestId, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal))
                if (!result.Any(p => Overlaps(p, plan))) result.Add(plan.Copy());
            return result.ToArray();
        }
    }

    internal sealed class WorkTaskIntent
    {
        public string PlanetId;
        public string ActorId;
        public string RequestId;
        public WorkCell Cell;
        public WorkBuildSpec Build;
        public RemoteTaskLayer Layer;
        public RemoteWorkKind Kind;
        public int Priority;
        public long ConfirmedRevision;
        public bool Historical;
        public bool HistoricalActive;
        internal string Key { get { return ((byte)Layer) + ":" + Cell.X + ":" + Cell.Y; } }
        internal WorkTaskIntent Copy() { var copy = (WorkTaskIntent)MemberwiseClone(); copy.Build = Build == null ? null : Build.Copy(); return copy; }
    }

    internal sealed class WorkExecution
    {
        public bool Accepted;
        public string Reason;
        public int Count;
        // Executor contract: true only when no order, priority or cancellation was mutated.
        public bool NoMutationGuaranteed;
        internal static WorkExecution Reject(string reason) { return new WorkExecution { Reason = reason }; }
        internal static WorkExecution RejectBeforeMutation(string reason)
        { return new WorkExecution { Reason = reason, NoMutationGuaranteed = true }; }
        internal static WorkExecution Accept(int count) { return new WorkExecution { Accepted = true, Count = count, Reason = "ok" }; }
    }

    internal sealed class RemoteWorkGrant
    {
        public string PlanetId;
        public string AuthorityEpoch;
        public string ActorId;
        public string Token;
        public long Revision;
        public long Generation;
        public RemoteWorkPermission Permissions;
    }

    internal sealed class RemoteWorkRequest
    {
        public string RequestId;
        public string PlanetId;
        public string AuthorityEpoch;
        public string GrantToken;
        public RemoteWorkKind Kind;
        public RemoteTaskLayer Layer = RemoteTaskLayer.Terrain;
        public bool DryRun;
        public string PreviewToken;
        public int Priority = 5;
        public WorkCell[] Cells = Array.Empty<WorkCell>();
        public WorkBuildSpec Build;
    }

    internal sealed class RemoteWorkReceipt
    {
        public string RequestId;
        public bool Accepted;
        public bool DryRun;
        public string Reason;
        public int Count;
        public string PreviewToken;
    }

    internal enum RemoteWorkMessageKind : byte { Grant = 1, Request = 2, Receipt = 3, QueryPlans = 4, Plans = 5 }
    internal sealed class RemoteWorkMessage
    {
        public RemoteWorkMessageKind Kind;
        public string TargetId;
        public RemoteWorkGrant Grant;
        public RemoteWorkRequest Request;
        public RemoteWorkReceipt Receipt;
        public string PlanetId;
        public bool Merged;
        public WorkTaskIntent[] Plans;
        public long Revision;
        public long Generation;
        public long GrantRevision;
        public int Offset;
        public int Total;
        public string AuthorityEpoch;
        public string PlanActorId;
        public bool AuthorityBlocked;
        public string AuthorityError;
    }
}
