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
        private readonly Dictionary<string, DupeCommandReceipt> submissions = new Dictionary<string, DupeCommandReceipt>();
        private readonly Queue<System.Tuple<string, byte[]>> outgoing = new Queue<System.Tuple<string, byte[]>>();
        private bool disposed;
        private double now;
        private bool clockStarted;
        private string authorityId;
        internal DupeCommandAuthority Authority { get; private set; }
        internal event Action<DupeCommandReceipt> SubmissionChanged;
        private sealed class Pending { internal string Authority; internal DupeMoveRequest Request; internal double RetryAt; internal double Deadline; }

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

        // A pending cursor can be drawn immediately from this result. No round trip is
        // required before displaying it, and acceptance never precedes native execution.
        internal string SubmitMove(string planetId, string authorityEpoch, int dupeId, WorkCell target)
        {
            var request = new DupeMoveRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = planetId,
                AuthorityEpoch = authorityEpoch, DupeId = dupeId, Target = target };
            if (!DupeCommandAuthority.Valid(request) || submissions.Count >= DupeCommandAuthority.MaxLedger || pending.Count >= 128) return null;
            string peer = authorityOfPlanet(planetId);
            if (disposed || !session.Joined || session.Leaving || string.IsNullOrEmpty(peer)) return null;
            pending[request.RequestId] = new Pending { Authority = peer, Request = request.Copy(), RetryAt = now, Deadline = now + 90 };
            Publish(new DupeCommandReceipt { RequestId = request.RequestId, Pending = true, Reason = "dupe_pending" });
            return request.RequestId;
        }

        internal DupeCommandReceipt GetSubmission(string requestId)
        { DupeCommandReceipt receipt; return requestId != null && submissions.TryGetValue(requestId, out receipt) ? receipt.Copy() : null; }

        internal void Pump(double seconds)
        {
            if (!clockStarted)
            {
                // UI may submit immediately after constructing this coordinator, long
                // after the Unity clock began. Anchor existing relative deadlines to
                // the first real sample instead of treating game uptime as elapsed wait.
                double offset = seconds - now;
                foreach (var item in pending.Values) { item.RetryAt += offset; item.Deadline += offset; }
                clockStarted = true;
            }
            now = seconds;
            if (disposed || !session.Joined || session.Leaving) return;
            EnsureAuthority();
            foreach (var item in pending.Values.ToArray())
            {
                if (now >= item.Deadline || authorityOfPlanet(item.Request.PlanetId) != item.Authority)
                {
                    pending.Remove(item.Request.RequestId);
                    Publish(new DupeCommandReceipt { RequestId = item.Request.RequestId, OutcomeUnknown = true, Reason = "dupe_response_unconfirmed" });
                    continue;
                }
                if (session.Reconnecting || now < item.RetryAt || !Connected(item.Authority)) continue;
                item.RetryAt = now + 3;
                if (item.Authority == session.LocalPlayerId)
                {
                    pending.Remove(item.Request.RequestId);
                    Publish(Authority.Handle(session.LocalPlayerId, item.Request));
                }
                else Send(item.Authority, new DupeCommandMessage { Request = item.Request.Copy() });
            }
            if (session.Reconnecting) return;
            int budget = 4;
            while (outgoing.Count > 0 && budget-- > 0)
            {
                var item = outgoing.Dequeue();
                if (session.IsHost) session.SendApplicationTo(item.Item1, item.Item2);
                else session.SendApplication(item.Item2);
            }
        }

        private void EnsureAuthority()
        {
            if (Authority != null && authorityId == session.LocalPlayerId) return;
            authorityId = session.LocalPlayerId;
            Authority = new DupeCommandAuthority(authorityId, context, executor, canControl);
        }

        private void Receive(string sender, byte[] data)
        {
            if (disposed || session.Leaving || !DupeCommandWire.IsMessage(data) || !Connected(sender)) return;
            DupeCommandMessage message;
            try { message = DupeCommandWire.Decode(data); }
            catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException || error is ArgumentException) { return; }
            if (message.TargetId != session.LocalPlayerId)
            {
                if (session.IsHost && Connected(message.TargetId)) session.ForwardApplication(sender, message.TargetId, data);
                return;
            }
            EnsureAuthority();
            if (message.Request != null)
            {
                if (authorityOfPlanet(message.Request.PlanetId) != session.LocalPlayerId) return;
                Send(sender, new DupeCommandMessage { Receipt = Authority.Handle(sender, message.Request) });
            }
            else
            {
                Pending item;
                if (!pending.TryGetValue(message.Receipt.RequestId, out item) || item.Authority != sender || authorityOfPlanet(item.Request.PlanetId) != sender) return;
                pending.Remove(item.Request.RequestId); Publish(message.Receipt);
            }
        }
        private bool Connected(string peer) { return session.Players.Any(p => p.Id == peer && p.Connected); }
        private void Send(string target, DupeCommandMessage message)
        {
            if (outgoing.Count >= 256 || !Connected(target)) return;
            message.TargetId = target; outgoing.Enqueue(System.Tuple.Create(target, DupeCommandWire.Encode(message)));
        }
        private void Publish(DupeCommandReceipt receipt)
        {
            submissions[receipt.RequestId] = receipt.Copy();
            if (SubmissionChanged != null) SubmissionChanged(receipt.Copy());
        }
        public void Dispose() { disposed = true; session.ApplicationMessageReceived -= Receive; pending.Clear(); outgoing.Clear(); }
    }
}
