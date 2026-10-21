using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    internal sealed class DupeCommandCoordinator : IDisposable
    {
        private readonly RoomSession session;
        private readonly IWorldWorkContext context;
        private readonly IDupeCommandExecutor executor;
        private readonly Func<string, string> authorityOfPlanet;
        private readonly Func<string, int, bool> canControl;
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        private readonly Dictionary<string, Known> requests = new Dictionary<string, Known>();
        private readonly Dictionary<string, DupeCommandReceipt> submissions = new Dictionary<string, DupeCommandReceipt>();
        private readonly Queue<System.Tuple<string, DupeCommandMessage>> outgoing = new Queue<System.Tuple<string, DupeCommandMessage>>();
        private bool disposed;
        private double now;
        private bool clockStarted;
        private string authorityId;
        internal DupeCommandAuthority Authority { get; private set; }
        internal event Action<DupeCommandReceipt> SubmissionChanged;
        private sealed class Known
        {
            internal string Authority;
            internal DupeMoveRequest Request;
            internal double QueryAt;
            internal string CancellationId;
            internal int Sequence;
        }
        private sealed class Pending { internal Known Known; internal double RetryAt; internal double Deadline; }
        private bool Ready { get { return !disposed && session.Joined && !session.Leaving && !session.Reconnecting; } }

        internal DupeCommandCoordinator(RoomSession session, IWorldWorkContext context, IDupeCommandExecutor executor,
            Func<string, string> authorityOfPlanet, Func<string, int, bool> canControl)
        {
            this.session = session ?? throw new ArgumentNullException("session");
            this.context = context ?? throw new ArgumentNullException("context");
            this.executor = executor ?? throw new ArgumentNullException("executor");
            this.authorityOfPlanet = authorityOfPlanet ?? throw new ArgumentNullException("authorityOfPlanet");
            this.canControl = canControl ?? throw new ArgumentNullException("canControl");
            session.ApplicationMessageReceived += Receive;
        }

        // Immediate local feedback is separate from the authority's native result.
        internal string SubmitMove(string planetId, string authorityEpoch, int dupeId, WorkCell target)
        {
            var request = new DupeMoveRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = planetId,
                AuthorityEpoch = authorityEpoch, DupeId = dupeId, Target = target };
            return Begin(request, authorityOfPlanet(planetId));
        }

        internal string CancelMove(string originalRequestId)
        {
            Known original;
            if (originalRequestId == null || !requests.TryGetValue(originalRequestId, out original) || original.Request.IsCancellation ||
                !Move(original).CanCancel) return null;
            DupeMoveRequest cancellation = original.Request.Copy();
            cancellation.RequestId = Guid.NewGuid().ToString("N");
            cancellation.CancelTargetRequestId = original.Request.RequestId;
            string previousCancellation = original.CancellationId;
            // Publish's synchronous listeners must already see cancellation pending.
            original.CancellationId = cancellation.RequestId;
            string id = Begin(cancellation, original.Authority);
            if (id == null) original.CancellationId = previousCancellation;
            return id;
        }

        internal DupeCommandMove[] GetMoves(string planetId)
        { return GetMoves(null, planetId); }

        internal DupeCommandMove[] GetMoves(string authorityPeerId, string planetId)
        {
            return requests.Values.Where(r => !r.Request.IsCancellation && (planetId == null || r.Request.PlanetId == planetId) &&
                    (authorityPeerId == null || r.Authority == authorityPeerId))
                .OrderByDescending(r => r.Sequence).Select(Move).ToArray();
        }

        private DupeCommandMove Move(Known known)
        {
            DupeCommandReceipt receipt = GetSubmission(known.Request.RequestId);
            DupeCommandReceipt cancellation = GetSubmission(known.CancellationId);
            bool cancellationResolved = cancellation == null || !cancellation.Pending && !cancellation.Accepted &&
                (!cancellation.OutcomeUnknown || cancellation.Reason == "dupe_response_unconfirmed");
            return new DupeCommandMove { AuthorityId = known.Authority, Request = known.Request.Copy(), Receipt = receipt, Cancellation = cancellation,
                CanCancel = Ready && Connected(known.Authority) && authorityOfPlanet(known.Request.PlanetId) == known.Authority &&
                    receipt != null && receipt.State == DupeCommandState.Active && cancellationResolved };
        }

        private string Begin(DupeMoveRequest request, string peer)
        {
            if (!Ready || string.IsNullOrEmpty(peer) || !DupeCommandAuthority.Valid(request) ||
                requests.Count >= DupeCommandAuthority.MaxLedger || pending.Count >= 128) return null;
            var known = new Known { Authority = peer, Request = request.Copy(), QueryAt = now + 3, Sequence = requests.Count };
            requests.Add(request.RequestId, known);
            pending.Add(request.RequestId, new Pending { Known = known, RetryAt = now, Deadline = now + 90 });
            Publish(new DupeCommandReceipt { RequestId = request.RequestId, Pending = true, Revision = 0, Reason = "dupe_pending" });
            return request.RequestId;
        }

        internal DupeCommandReceipt GetSubmission(string requestId)
        { DupeCommandReceipt receipt; return requestId != null && submissions.TryGetValue(requestId, out receipt) ? receipt.Copy() : null; }

        internal void Pump(double seconds)
        {
            if (disposed) return;
            if (!clockStarted)
            {
                double offset = seconds - now;
                foreach (var item in pending.Values) { item.RetryAt += offset; item.Deadline += offset; }
                foreach (var item in requests.Values) item.QueryAt += offset;
                clockStarted = true;
            }
            now = seconds;
            if (!string.IsNullOrEmpty(session.LocalPlayerId)) EnsureAuthority();
            if (Authority != null) Authority.Reconcile(Ready, Connected);
            // A reconnect temporarily clears Joined. Keep the same pending IDs and
            // deadline so replay can recover their authority result after resuming.
            if (!session.Joined && !session.Reconnecting || session.Leaving)
            { UnconfirmPending(); outgoing.Clear(); return; }
            foreach (var item in pending.Values.ToArray())
            {
                Known known = item.Known;
                if (now >= item.Deadline || authorityOfPlanet(known.Request.PlanetId) != known.Authority)
                {
                    pending.Remove(known.Request.RequestId);
                    Publish(Unconfirmed(known.Request.RequestId));
                    continue;
                }
                if (!Ready || now < item.RetryAt || !Connected(known.Authority)) continue;
                item.RetryAt = now + 3;
                if (known.Authority == session.LocalPlayerId)
                    Deliver(Authority.Handle(session.LocalPlayerId, known.Request));
                else Send(known.Authority, new DupeCommandMessage { Request = known.Request.Copy() });
            }
            if (!Ready) { outgoing.Clear(); return; }
            // Read-only queries recover dropped lifecycle receipts, including an
            // uncertain original outcome. They can never issue a late native move.
            int queryBudget = 4;
            foreach (Known known in requests.Values.OrderBy(r => r.QueryAt))
            {
                DupeCommandReceipt receipt = GetSubmission(known.Request.RequestId);
                if (pending.ContainsKey(known.Request.RequestId) || now < known.QueryAt || receipt == null ||
                    receipt.State != DupeCommandState.Active && !receipt.OutcomeUnknown ||
                    authorityOfPlanet(known.Request.PlanetId) != known.Authority || !Connected(known.Authority)) continue;
                known.QueryAt = now + 3;
                if (known.Authority == session.LocalPlayerId) Deliver(Authority.Query(session.LocalPlayerId, known.Request));
                else Send(known.Authority, new DupeCommandMessage { Query = known.Request.Copy() });
                if (--queryBudget == 0) break;
            }
            int budget = 4;
            while (outgoing.Count > 0 && budget-- > 0)
            {
                var item = outgoing.Dequeue();
                if (!Connected(item.Item1)) continue;
                // A queued request that expired/was acknowledged must not escape
                // later from a congested queue and mutate the native world.
                if (item.Item2.Request != null && !pending.ContainsKey(item.Item2.Request.RequestId)) continue;
                byte[] bytes = DupeCommandWire.Encode(item.Item2);
                if (session.IsHost) session.SendApplicationTo(item.Item1, bytes);
                else session.SendApplication(bytes);
            }
        }

        private void EnsureAuthority()
        {
            if (Authority != null && authorityId == session.LocalPlayerId) return;
            if (Authority != null) { Authority.StopAll(); Authority.Changed -= AuthorityChanged; }
            authorityId = session.LocalPlayerId;
            Authority = new DupeCommandAuthority(authorityId, context, executor,
                (actor, dupe) => Ready && Connected(actor) && canControl(actor, dupe));
            Authority.Changed += AuthorityChanged;
        }

        private void AuthorityChanged(string actor, DupeCommandReceipt receipt)
        {
            if (actor == session.LocalPlayerId)
            { if (requests.ContainsKey(receipt.RequestId)) Deliver(receipt); }
            else Send(actor, new DupeCommandMessage { Receipt = receipt });
        }

        private void Receive(string sender, byte[] data)
        {
            if (!Ready || !DupeCommandWire.IsMessage(data) || !Connected(sender)) return;
            DupeCommandMessage message;
            try { message = DupeCommandWire.Decode(data); }
            catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException || error is ArgumentException) { return; }
            if (message.TargetId != session.LocalPlayerId)
            {
                if (session.IsHost && Connected(message.TargetId)) session.ForwardApplication(sender, message.TargetId, data);
                return;
            }
            EnsureAuthority();
            DupeMoveRequest request = message.Request ?? message.Query;
            if (request != null)
            {
                if (authorityOfPlanet(request.PlanetId) != session.LocalPlayerId) return;
                DupeCommandReceipt receipt = message.Request != null ? Authority.Handle(sender, request) : Authority.Query(sender, request);
                Send(sender, new DupeCommandMessage { Receipt = receipt });
            }
            else
            {
                Known known;
                if (!requests.TryGetValue(message.Receipt.RequestId, out known) || known.Authority != sender ||
                    authorityOfPlanet(known.Request.PlanetId) != sender) return;
                Deliver(message.Receipt);
            }
        }

        private void Deliver(DupeCommandReceipt receipt)
        {
            pending.Remove(receipt.RequestId);
            Known known;
            if (requests.TryGetValue(receipt.RequestId, out known)) known.QueryAt = now + 3;
            Publish(receipt);
        }
        private bool Connected(string peer) { return session.Players.Any(p => p.Id == peer && p.Connected); }
        private void Send(string target, DupeCommandMessage message)
        {
            if (outgoing.Count >= 256 || !Connected(target)) return;
            message.TargetId = target; outgoing.Enqueue(System.Tuple.Create(target, message));
        }
        private void Publish(DupeCommandReceipt receipt)
        {
            DupeCommandReceipt old;
            if (submissions.TryGetValue(receipt.RequestId, out old) && !old.Pending)
            {
                if (receipt.Pending || receipt.Revision < old.Revision) return;
                if (receipt.Revision == old.Revision && !old.OutcomeUnknown) return;
                if (receipt.Revision == old.Revision && old.OutcomeUnknown == receipt.OutcomeUnknown &&
                    old.Reason == receipt.Reason && old.State == receipt.State) return;
            }
            submissions[receipt.RequestId] = receipt.Copy();
            if (SubmissionChanged != null) SubmissionChanged(receipt.Copy());
        }
        private static DupeCommandReceipt Unconfirmed(string id)
        { return new DupeCommandReceipt { RequestId = id, Revision = 0, OutcomeUnknown = true, Reason = "dupe_response_unconfirmed" }; }
        private void UnconfirmPending()
        {
            foreach (string id in pending.Keys.ToArray()) { pending.Remove(id); Publish(Unconfirmed(id)); }
        }
        public void Dispose()
        {
            if (disposed) return;
            if (Authority != null) { Authority.StopAll(); Authority.Changed -= AuthorityChanged; }
            UnconfirmPending();
            disposed = true; session.ApplicationMessageReceived -= Receive; outgoing.Clear();
        }
    }
}
