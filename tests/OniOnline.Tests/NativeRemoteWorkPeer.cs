using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using OniOnline.Multiplayer;
using OniOnline.Network;

// A TCP protocol test participant. It is never a complete ONI game client.
internal static class NativeRemoteWorkPeer
{
    private sealed class PreviewSource : IPlanetSource { public PlanetManifest CapturePlanet() { return null; } }
    private sealed class NoWorld : IWorldWorkContext
    { public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = null; return false; } }
    private sealed class NoNative : IWorkExecutor
    {
        public WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request) { return WorkExecution.RejectBeforeMutation("protocol_peer_has_no_native_world"); }
        public WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] before, WorkTaskIntent[] after) { return WorkExecution.RejectBeforeMutation("protocol_peer_has_no_native_world"); }
    }
    private sealed class Transport : IRoomTransport
    {
        private readonly IRoomTransport inner;
        private readonly RunState run;
        internal Transport(IRoomTransport inner, RunState run) { this.inner = inner; this.run = run; }
        public bool IsHost { get { return false; } }
        public string HostPeer { get { return inner.HostPeer; } }
        public bool TryReceive(out TransportEvent item)
        {
            while (inner.TryReceive(out item))
            {
                if (run.DropConfirmedReceipts && item.Data != null)
                {
                    Packet packet = Protocol.Decode(item.Data);
                    if (packet.Kind == PacketKind.Application && RemoteWorkWire.IsMessage(packet.Payload))
                    {
                        var message = RemoteWorkWire.Decode(packet.Payload);
                        if (message.Kind == RemoteWorkMessageKind.Receipt && !message.Receipt.DryRun)
                        { run.WithheldReceipts++; continue; }
                    }
                }
                return true;
            }
            item = null; return false;
        }
        public bool Send(string peer, byte[] data) { return inner.Send(peer, data); }
        public void Disconnect(string peer) { inner.Disconnect(peer); }
        public void Poll() { inner.Poll(); }
        public void Dispose() { inner.Dispose(); }
    }
    private sealed class RunState
    {
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal readonly Dictionary<string, RemoteWorkReceipt> Receipts = new Dictionary<string, RemoteWorkReceipt>();
        internal readonly Dictionary<string, object> Evidence = new Dictionary<string, object>();
        internal RoomSession Session;
        internal StarSystemCoordinator Stars;
        internal RemoteWorkCoordinator Work;
        internal Transport CurrentTransport;
        internal string DirectoryPath, Host, Build, CtVersion, Stage = "connecting", Error, AssemblyHash;
        internal string Owner, Planet, IdentityBeforeReconnect, ConfirmationId, CurrentDraftId;
        internal string ExpectedEpoch, UnauthorizedId;
        internal int Port, X, Y, Sequence, WithheldReceipts;
        internal double Timeout, NextWrite;
        internal bool DropConfirmedReceipts, SawReconnect, Passed, Finished;
        internal RemoteWorkRequest Draft, OldConfirmation;
        internal RemoteWorkGrant DraftGrant;

        internal void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        internal void SetStage(string stage) { Stage = stage; Write(); Console.WriteLine("STAGE " + stage); }
        internal void StartDraft(bool first)
        {
            Require(Stage == (first ? "unauthorized_rejected" : "old_request_rejected"), "unexpected_draft_phase:" + Stage);
            DraftGrant = Work.GetReceivedGrant(Owner, Planet);
            Require(DraftGrant != null && (DraftGrant.Permissions & RemoteWorkPermission.IssueTasks) != 0, "no_received_authenticated_task_grant");
            Draft = Work.Draft(Planet, RemoteWorkKind.Dig, new[] { new WorkCell(X, Y) }, 7);
            CurrentDraftId = Draft.RequestId;
            Require(Work.GetSubmission(CurrentDraftId).Reason == "work_draft", "missing_immediate_personal_draft");
            Require(Work.Preview(Owner, Draft), "preview_submit_failed");
            SetStage(first ? "previewing" : "fresh_previewing");
        }
        internal void ReadCommand()
        {
            string path = Path.Combine(DirectoryPath, "command.json");
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 4096) throw new InvalidDataException("peer_command_too_large");
            using (var document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                int sequence = document.RootElement.GetProperty("sequence").GetInt32();
                if (sequence <= Sequence) return;
                Sequence = sequence;
                string action = document.RootElement.GetProperty("action").GetString();
                if (action == "unauthorized")
                {
                    Require(Stage == "connected", "unauthorized_probe_requires_connected_ungranted_peer");
                    UnauthorizedId = Guid.NewGuid().ToString("N");
                    Require(Work.Submit(Owner, new RemoteWorkRequest { RequestId = UnauthorizedId, PlanetId = Planet,
                        AuthorityEpoch = ExpectedEpoch, GrantToken = "", Kind = RemoteWorkKind.Dig, DryRun = true,
                        Cells = new[] { new WorkCell(X, Y) }, Priority = 7 }), "unauthorized_network_probe_submit_failed");
                    SetStage("unauthorized_probe");
                }
                else if (action == "dig") StartDraft(true);
                else if (action == "fresh") StartDraft(false);
                else if (action == "disconnect_replay")
                {
                    Require(Stage == "confirming_withheld_ack" && OldConfirmation != null && WithheldReceipts > 0,
                        "disconnect_requires_original_confirmation_withheld_after_native_ack");
                    IdentityBeforeReconnect = Session.LocalPlayerId;
                    DropConfirmedReceipts = false; SawReconnect = false;
                    CurrentTransport.Disconnect(CurrentTransport.HostPeer);
                    SetStage("reconnecting");
                }
                else if (action == "replay_old")
                {
                    Require(Stage == "replay_confirmed", "old_replay_requires_confirmed_reconnection");
                    RemoteWorkGrant grant = Work.GetReceivedGrant(Owner, Planet);
                    Require(grant != null && (grant.Permissions & RemoteWorkPermission.IssueTasks) != 0 && grant.Token != OldConfirmation.GrantToken,
                        "old_replay_requires_received_fresh_grant");
                    Receipts.Remove(OldConfirmation.RequestId);
                    Require(Work.Submit(Owner, Copy(OldConfirmation)), "old_confirmation_submit_failed");
                    SetStage("replaying_old_grant");
                }
                else if (action == "leave")
                {
                    Require(Stage == "fresh_complete", "leave_before_network_scenario_completed");
                    Require(Session.RequestLeave(), "acknowledged_leave_request_failed"); SetStage("leaving");
                }
                else throw new InvalidDataException("unknown_peer_command:" + action);
            }
        }
        internal void Advance()
        {
            if (Session.Reconnecting || !Session.Joined) SawReconnect = true;
            if (Stage == "connecting" && Session.Joined && Stars.State?.Members.Any(m => m.Planet != null && m.PeerId == Session.Players.First().Id) == true)
            {
                var target = Stars.State.Members.First(m => m.Planet != null && m.PeerId == Session.Players.First().Id);
                Owner = target.PeerId; Planet = target.Planet.PlanetId;
                Require(!Stars.Admitted && !Stars.LandingRequested && !Stars.TravelConfirmed, "test_peer_must_not_import_a_fake_planet");
                SetStage("connected");
            }
            if (Stage == "previewing" || Stage == "fresh_previewing")
            {
                var preview = Work.GetSubmission(CurrentDraftId);
                if (preview?.Accepted == true && preview.DryRun)
                {
                    bool first = Stage == "previewing";
                    if (first) DropConfirmedReceipts = true;
                    Require(Work.Confirm(Owner, Draft, preview), "confirmation_submit_failed");
                    ConfirmationId = Work.LastSubmittedRequestId;
                    var confirmation = new RemoteWorkRequest { RequestId = ConfirmationId, PlanetId = Planet,
                        AuthorityEpoch = DraftGrant.AuthorityEpoch, GrantToken = DraftGrant.Token, Kind = RemoteWorkKind.Dig,
                        Layer = RemoteTaskLayer.Terrain, Priority = 7, Cells = new[] { new WorkCell(X, Y) }, DryRun = false, PreviewToken = preview.PreviewToken };
                    if (first) { OldConfirmation = confirmation; Evidence["initialConfirmationId"] = ConfirmationId; }
                    else Evidence["freshConfirmationId"] = ConfirmationId;
                    SetStage(first ? "confirming_withheld_ack" : "fresh_confirming");
                }
                else if (preview != null && preview.Reason != "work_queued" && preview.Reason != "work_draft")
                    throw new InvalidOperationException("preview_rejected:" + preview.Reason);
            }
            if (Stage == "reconnecting" && SawReconnect && Session.Joined && !Session.Reconnecting)
            {
                Require(Session.LocalPlayerId == IdentityBeforeReconnect, "reconnection_changed_authenticated_identity");
                Receipts.Remove(OldConfirmation.RequestId);
                Require(Work.Submit(Owner, Copy(OldConfirmation)), "reconnection_confirmation_replay_submit_failed");
                SetStage("replay_after_reconnect");
            }
            RemoteWorkReceipt receipt;
            if (Stage == "unauthorized_probe" && Receipts.TryGetValue(UnauthorizedId, out receipt))
            {
                Require(!receipt.Accepted && receipt.Reason == "work_not_authorized", "server_did_not_reject_unauthorized_request:" + receipt.Reason);
                Evidence["unauthorizedNetworkReceipt"] = Receipt(receipt); SetStage("unauthorized_rejected");
            }
            if (Stage == "replay_after_reconnect" && Receipts.TryGetValue(OldConfirmation.RequestId, out receipt))
            {
                Require(receipt.Accepted && !receipt.DryRun, "replayed_confirmation_rejected:" + receipt.Reason);
                Evidence["reconnectIdentityUnchanged"] = true; Evidence["reconnectReceipt"] = Receipt(receipt);
                SetStage("replay_confirmed");
            }
            if (Stage == "replaying_old_grant" && Receipts.TryGetValue(OldConfirmation.RequestId, out receipt))
            {
                Require(!receipt.Accepted && receipt.Reason == "work_not_authorized", "old_grant_replay_not_rejected:" + receipt.Reason);
                Evidence["oldGrantReplayReceipt"] = Receipt(receipt); SetStage("old_request_rejected");
            }
            if (Stage == "fresh_confirming" && Receipts.TryGetValue(ConfirmationId, out receipt))
            {
                Require(receipt.Accepted && !receipt.DryRun && ConfirmationId != OldConfirmation.RequestId, "fresh_confirmation_not_accepted:" + receipt.Reason);
                Evidence["freshReceipt"] = Receipt(receipt); SetStage("fresh_complete");
            }
            if (Stage == "leaving" && Session.Closed)
            { Require(Session.LastError == "left_room", "departure_not_acknowledged:" + Session.LastError); Passed = Finished = true; SetStage("complete"); }
            else if (Session.Closed) throw new InvalidOperationException("session_closed:" + Session.LastError);
        }
        internal void Write()
        {
            RemoteWorkGrant grant = Owner == null ? null : Work?.GetReceivedGrant(Owner, Planet);
            var value = new { schemaVersion = 1, participantKind = "test_protocol_client", completeGameClient = false,
                nativeGameInstancesLaunched = 0, stage = Stage, passed = Passed, error = Error, command = Sequence,
                elapsedSeconds = Clock.Elapsed.TotalSeconds, protocolClientAssemblySha256 = AssemblyHash,
                localProtocolIdentity = Session?.LocalPlayerId, ownerPeerId = Owner, planetId = Planet,
                target = new { x = X, y = Y }, receivedGrantPermissions = (int)(grant?.Permissions ?? RemoteWorkPermission.None),
                receivedGrantRevision = grant?.Revision ?? 0, withheldConfirmationReceipts = WithheldReceipts,
                confirmationRequestId = ConfirmationId, evidence = Evidence,
                boundary = "TCP protocol participant commanding a separately Steam-launched ONI host; not a complete native guest game." };
            string path = Path.Combine(DirectoryPath, "receipt.json"), temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
    }
    private static object Receipt(RemoteWorkReceipt receipt)
    { return new { requestId = receipt.RequestId, accepted = receipt.Accepted, dryRun = receipt.DryRun, reason = receipt.Reason, count = receipt.Count }; }
    private static RemoteWorkRequest Copy(RemoteWorkRequest request)
    { return new RemoteWorkRequest { RequestId = request.RequestId, PlanetId = request.PlanetId, AuthorityEpoch = request.AuthorityEpoch,
        GrantToken = request.GrantToken, Kind = request.Kind, Layer = request.Layer, DryRun = request.DryRun, PreviewToken = request.PreviewToken,
        Priority = request.Priority, Cells = (WorkCell[])request.Cells.Clone() }; }

    internal static int Run(string[] args)
    {
        var run = new RunState();
        try
        {
            if (args.Length != 10) throw new ArgumentException("usage: --native-work-peer host port gameBuild ctVersion targetX targetY authorityEpoch freshControlDirectory timeoutSeconds");
            run.Host = args[1]; run.Port = int.Parse(args[2], CultureInfo.InvariantCulture); run.Build = args[3]; run.CtVersion = args[4];
            run.X = int.Parse(args[5], CultureInfo.InvariantCulture); run.Y = int.Parse(args[6], CultureInfo.InvariantCulture);
            run.ExpectedEpoch = args[7];
            run.Timeout = double.Parse(args[9], CultureInfo.InvariantCulture);
            if (!Guid.TryParseExact(run.ExpectedEpoch, "N", out _)) throw new ArgumentException("invalid_expected_authority_epoch");
            if (run.Port < 1 || run.Port > 65535 || run.X < 0 || run.Y < 0 || run.Timeout < 40 || run.Timeout > 600) throw new ArgumentException("invalid_peer_parameters");
            run.DirectoryPath = Path.GetFullPath(args[8]);
            if (Directory.Exists(run.DirectoryPath) && Directory.EnumerateFileSystemEntries(run.DirectoryPath).Any()) throw new IOException("peer_control_directory_must_be_fresh");
            Directory.CreateDirectory(run.DirectoryPath);
            using (var sha = SHA256.Create()) run.AssemblyHash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(typeof(NativeRemoteWorkPeer).Assembly.Location))).ToLowerInvariant();
            Func<IRoomTransport> connect = () => run.CurrentTransport = new Transport(TcpRoomTransport.Join(run.Host, run.Port), run);
            run.Session = new RoomSession(connect(), new SessionProfile { Name = "Harness work " + Guid.NewGuid().ToString("N").Substring(0, 6),
                GameVersion = run.Build, CycleTrimVersion = run.CtVersion, SpacedOut = true }, "", reconnect: connect);
            run.Stars = new StarSystemCoordinator(run.Session, new PreviewSource());
            run.Work = new RemoteWorkCoordinator(run.Session, new NoWorld(), new NoNative(),
                planet => run.Stars.State?.Members.FirstOrDefault(m => m.Planet?.PlanetId == planet)?.PeerId);
            run.Work.ReceiptReceived += (owner, receipt) => run.Receipts[receipt.RequestId] = receipt;
            while (!run.Finished)
            {
                if (run.Clock.Elapsed.TotalSeconds >= run.Timeout) throw new TimeoutException("native_work_peer_stage:" + run.Stage);
                double now = run.Clock.Elapsed.TotalSeconds;
                run.Session.Pump(now); run.Stars.Pump(now); run.Work.Pump(now);
                run.ReadCommand(); run.Advance();
                if (now >= run.NextWrite) { run.Write(); run.NextWrite = now + .2; }
                Thread.Sleep(20);
            }
            return 0;
        }
        catch (Exception error)
        {
            run.Error = error.GetType().Name + ": " + error.Message;
            Console.Error.WriteLine("FAIL native-work-peer stage=" + run.Stage + " " + run.Error);
            if (run.DirectoryPath != null && run.Work != null) run.Write();
            return 1;
        }
        finally { run.Work?.Dispose(); run.Stars?.Dispose(); run.Session?.Dispose(); }
    }
}
