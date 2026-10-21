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
        internal DupeMoveRequest Copy() { return (DupeMoveRequest)MemberwiseClone(); }
        internal bool SameAs(DupeMoveRequest other)
        { return other != null && RequestId == other.RequestId && PlanetId == other.PlanetId && AuthorityEpoch == other.AuthorityEpoch && DupeId == other.DupeId && Target.X == other.Target.X && Target.Y == other.Target.Y; }
    }

    internal sealed class DupeCommandReceipt
    {
        public string RequestId;
        // Pending is local optimistic feedback; Accepted only follows an authority receipt.
        public bool Pending;
        public bool Accepted;
        public bool OutcomeUnknown;
        public string Reason;
        internal DupeCommandReceipt Copy() { return (DupeCommandReceipt)MemberwiseClone(); }
    }

    internal interface IDupeCommandExecutor
    {
        WorkExecution Preflight(WorkPlanet planet, DupeMoveRequest request);
        WorkExecution Execute(WorkPlanet planet, DupeMoveRequest request);
    }

    internal sealed class DupeCommandMessage
    {
        public string TargetId;
        public DupeMoveRequest Request;
        public DupeCommandReceipt Receipt;
    }
}
