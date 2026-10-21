using System;

namespace OniOnline.Multiplayer
{
    internal sealed class DupeMoveRequest
    {
        public string RequestId;
        public string PlanetId;
        public string AuthorityEpoch;
        public int DupeId;
        public WorkCell Target;
        public string CancelTargetRequestId;
        // Authority-generated, never serialized. Two authenticated actors can choose
        // the same public request ID without sharing ownership of a native chore.
        internal string NativeLeaseId;
        internal bool IsCancellation { get { return CancelTargetRequestId != null; } }
        internal DupeMoveRequest Copy() { return (DupeMoveRequest)MemberwiseClone(); }
        internal bool SameAs(DupeMoveRequest other)
        { return other != null && RequestId == other.RequestId && PlanetId == other.PlanetId && AuthorityEpoch == other.AuthorityEpoch && DupeId == other.DupeId && Target.X == other.Target.X && Target.Y == other.Target.Y && CancelTargetRequestId == other.CancelTargetRequestId; }
    }

    internal enum DupeCommandState : byte
    {
        None = 0, Active = 1, Completed = 2, Cancelled = 3, Replaced = 4,
        Revoked = 5, Disconnected = 6, WorldChanged = 7
    }

    internal sealed class DupeCommandReceipt
    {
        public string RequestId;
        // Pending is local optimistic feedback; Accepted only follows an authority receipt.
        public bool Pending;
        public bool Accepted;
        public bool OutcomeUnknown;
        public string Reason;
        // Stages are monotonic: 1 = initial result, 2 = observed native task,
        // 3 = terminal task. Delayed acknowledgements cannot resurrect a finished move.
        public int Revision = 1;
        public DupeCommandState State;
        internal DupeCommandReceipt Copy() { return (DupeCommandReceipt)MemberwiseClone(); }
    }

    internal sealed class DupeCommandMove
    {
        public string AuthorityId;
        public DupeMoveRequest Request;
        public DupeCommandReceipt Receipt;
        public DupeCommandReceipt Cancellation;
        public bool CanCancel;
    }

    internal interface IDupeCommandExecutor
    {
        WorkExecution Preflight(WorkPlanet planet, DupeMoveRequest request);
        WorkExecution Execute(WorkPlanet planet, DupeMoveRequest request);
    }

    internal interface IDupeCommandLifecycle
    {
        // None means ownership/outcome cannot be established; it is never evidence
        // that the task finished. Terminal states release the native ownership handle.
        DupeCommandState GetState(WorkPlanet planet, DupeMoveRequest request);
        // Must cancel only the exact task captured by Execute. A different task for
        // the same dupe/target is not owned, including a later local Move To click.
        WorkExecution Cancel(WorkPlanet planet, DupeMoveRequest request);
    }

    // Values such as dupe ID and destination are reused across saves and commands.
    // Cancellation needs the actual native instance identities as well as the intent.
    internal sealed class DupeCommandTaskIdentity
    {
        private readonly DupeMoveRequest request;
        private readonly string planetId;
        private readonly string epoch;
        private readonly int nativeWorldId;
        private readonly object game;
        private readonly object world;
        private readonly object monitor;
        private readonly object task;
        internal DupeCommandTaskIdentity(WorkPlanet planet, DupeMoveRequest request, object game, object world, object monitor, object task)
        {
            if (planet == null || !DupeCommandAuthority.Valid(request) || request.IsCancellation || !DupeCommandAuthority.Id(request.NativeLeaseId) ||
                game == null || world == null || monitor == null) throw new ArgumentException("dupe_task_identity");
            this.request = request.Copy(); planetId = planet.PlanetId; epoch = planet.AuthorityEpoch; nativeWorldId = planet.NativeWorldId;
            this.game = game; this.world = world; this.monitor = monitor; this.task = task;
        }
        internal bool OwnsRequest(WorkPlanet planet, DupeMoveRequest candidate)
        {
            return planet != null && candidate != null && planet.PlanetId == planetId && planet.AuthorityEpoch == epoch &&
                planet.NativeWorldId == nativeWorldId && request.NativeLeaseId == candidate.NativeLeaseId && request.SameAs(candidate);
        }
        internal bool MatchesNative(object currentGame, object currentWorld, object currentMonitor, object currentTask)
        {
            // A reservation before native entry may not have captured a chore yet.
            // Null is an unknown handle, never permission to cancel a null/current task.
            return task != null && MatchesScope(currentGame, currentWorld) &&
                ReferenceEquals(monitor, currentMonitor) && ReferenceEquals(task, currentTask);
        }
        internal bool MatchesScope(object currentGame, object currentWorld)
        { return ReferenceEquals(game, currentGame) && ReferenceEquals(world, currentWorld); }
    }

    internal sealed class DupeCommandMessage
    {
        public string TargetId;
        public DupeMoveRequest Request;
        // Read-only status recovery must never retry execution after a lost receipt.
        public DupeMoveRequest Query;
        public DupeCommandReceipt Receipt;
    }
}
