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

// A protocol observer for an independently launched native host. This executable is not an ONI guest.
internal static class NativeInterestPeer
{
    private sealed class PreviewSource : IPlanetSource
    { public PlanetManifest CapturePlanet() { return null; } }

    private sealed class Meter
    {
        internal long SentBytes, ReceivedBytes, SentPackets, ReceivedPackets, ApplicationBytes;
    }

    private sealed class MeteredTransport : IRoomTransport
    {
        private readonly IRoomTransport transport;
        private readonly Meter meter;
        internal MeteredTransport(IRoomTransport transport, Meter meter) { this.transport = transport; this.meter = meter; }
        public bool IsHost { get { return transport.IsHost; } }
        public string HostPeer { get { return transport.HostPeer; } }
        public bool TryReceive(out TransportEvent item)
        {
            bool received = transport.TryReceive(out item);
            if (received && item.Data != null) { meter.ReceivedBytes += item.Data.Length; meter.ReceivedPackets++; }
            return received;
        }
        public bool Send(string peer, byte[] data)
        {
            bool sent = transport.Send(peer, data);
            if (sent) { meter.SentBytes += data.Length; meter.SentPackets++; }
            return sent;
        }
        public void Disconnect(string peer) { transport.Disconnect(peer); }
        public void Poll() { transport.Poll(); }
        public void Dispose() { transport.Dispose(); }
    }

    private sealed class RunState
    {
        internal readonly Stopwatch Watch = Stopwatch.StartNew();
        internal readonly Meter Meter = new Meter();
        internal readonly Dictionary<string, object> Stages = new Dictionary<string, object>();
        internal readonly Queue<object> MotionSamples = new Queue<object>();
        internal readonly Dictionary<int, WorldInterestEntity> PreviousPositions = new Dictionary<int, WorldInterestEntity>();
        internal string Output, Stage = "starting", Error, ProtocolHash, Host, Build, CtVersion, LastMotionEpoch;
        internal int Port, CenterX, CenterY, DetailIndex = -1;
        internal double Deadline, NextReceipt;
        internal bool CreatedReceipt, Passed, MotionChanged, RevocationObserved, DetailsRevoked;
        internal RoomSession Session;
        internal StarSystemCoordinator Stars;
        internal WorldInterestSession Interest;
        internal WorldCellDetailsSession Details;
        internal SystemMember Target;
        internal long LastMotionSequence;
        internal long Assertions;

        internal void Assert(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException(message); }

        internal void Pump()
        {
            Session.Pump(Watch.Elapsed.TotalSeconds);
            Stars.Pump(Watch.Elapsed.TotalSeconds);
            Interest.Pump(Watch.Elapsed.TotalSeconds);
            Details.Pump(Watch.Elapsed.TotalSeconds);
            if (Session.Closed && Stage != "leaving") throw new InvalidOperationException("room_closed:" + Session.LastError);
            SampleMotion();
            if (Stage == "holding" && Interest.LastError == "world_interest_denied") RevocationObserved = true;
            if (Stage == "holding" && Details.LastError == "world_cell_details_denied") DetailsRevoked = true;
            if (Watch.Elapsed.TotalSeconds >= NextReceipt) { WriteReceipt(); NextReceipt = Watch.Elapsed.TotalSeconds + 1; }
            Thread.Sleep(20);
        }

        internal void Until(Func<bool> condition, string description)
        {
            while (Watch.Elapsed.TotalSeconds < Deadline)
            {
                Pump();
                if (condition()) return;
            }
            throw new TimeoutException("stage=" + Stage + "; waiting=" + description + "; session=" + Session.LastError +
                "; interest=" + Interest.LastError + "; details=" + Details.LastError);
        }

        internal void SetStage(string stage)
        { Stage = stage; WriteReceipt(); Console.WriteLine("STAGE " + stage); }

        internal object FieldsSnapshot()
        {
            var cells = Interest?.VisualReplica?.Cells.Values.ToArray() ?? Array.Empty<WorldInterestCell>();
            return new { count = cells.Length, visual = cells.Count(c => (c.Fields & WorldInterestChannels.Visual) != 0),
                temperature = cells.Count(c => (c.Fields & WorldInterestChannels.Temperature) != 0),
                mass = cells.Count(c => (c.Fields & WorldInterestChannels.Mass) != 0),
                unrequestedNumericalNonzero = cells.Count(c => ((c.Fields & WorldInterestChannels.Temperature) == 0 && c.Temperature != 0) ||
                    ((c.Fields & WorldInterestChannels.Mass) == 0 && c.Mass != 0)),
                sequence = Interest?.VisualReplica?.Sequence ?? 0, epoch = Interest?.BoundEpoch };
        }

        internal void SampleMotion()
        {
            var replica = Interest?.MotionReplica;
            if (replica == null || replica.Sequence == 0 || replica.Sequence == LastMotionSequence) return;
            if (LastMotionEpoch != Interest.BoundEpoch)
            { PreviousPositions.Clear(); LastMotionEpoch = Interest.BoundEpoch; }
            LastMotionSequence = replica.Sequence;
            var values = replica.Entities.Values.OrderBy(e => e.Id).Take(16).ToArray();
            foreach (var current in values)
            {
                WorldInterestEntity previous;
                if (PreviousPositions.TryGetValue(current.Id, out previous) &&
                    (previous.X != current.X || previous.Y != current.Y)) MotionChanged = true;
                PreviousPositions[current.Id] = current;
            }
            foreach (int id in PreviousPositions.Keys.Where(id => !replica.Entities.ContainsKey(id)).ToArray()) PreviousPositions.Remove(id);
            MotionSamples.Enqueue(new { elapsedSeconds = Watch.Elapsed.TotalSeconds, epoch = Interest.BoundEpoch,
                sequence = replica.Sequence, entities = values.Select(e => new { id = e.Id, kind = e.Kind, cell = e.CellIndex, x = e.X, y = e.Y }).ToArray() });
            while (MotionSamples.Count > 2) MotionSamples.Dequeue();
        }

        internal void WriteReceipt()
        {
            if (Output == null) return;
            var current = Details?.Current;
            var receipt = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1, ["participantKind"] = "test_protocol_client", ["completeGameClient"] = false,
                ["nativeGameInstancesLaunched"] = 0,
                ["boundary"] = "This process observes a separately launched LAN host; it does not establish full native guest simulation or four-game concurrency.",
                ["protocolClientAssemblySha256"] = ProtocolHash,
                ["nativeHostDllHashAvailable"] = false,
                ["nativeHostHashBoundary"] = "The game host does not transmit its loaded DLL hash on this protocol; correlate with its independent native probe receipt.",
                ["target"] = Host + ":" + Port.ToString(CultureInfo.InvariantCulture), ["gameBuild"] = Build, ["cycleTrimVersion"] = CtVersion,
                ["stage"] = Stage, ["passed"] = Passed, ["error"] = Error,
                ["elapsedSeconds"] = Watch.Elapsed.TotalSeconds, ["assertions"] = Assertions,
                ["roomId"] = Session?.RoomId, ["localProtocolIdentity"] = Session?.LocalPlayerId,
                ["ownerPeerId"] = Target?.PeerId, ["planetId"] = Target?.Planet?.PlanetId,
                ["admittedAsPlanet"] = Stars?.Admitted ?? false,
                ["previewOnly"] = Stars == null || (!Stars.LandingRequested && !Stars.TravelConfirmed),
                ["center"] = new { x = CenterX, y = CenterY }, ["fields"] = FieldsSnapshot(),
                ["motionSequence"] = Interest?.MotionReplica?.Sequence ?? 0,
                ["motionPositionsChanged"] = MotionChanged, ["lastTwoMotionSamples"] = MotionSamples.ToArray(),
                ["stale"] = Interest?.Stale ?? true, ["interestError"] = Interest?.LastError,
                ["detailError"] = Details?.LastError, ["detailStale"] = Details?.Stale ?? true,
                ["detail"] = current == null ? null : new { epoch = current.Epoch, sequence = current.Sequence,
                    index = current.Cell.Index, fields = (byte)current.Cell.Fields, temperature = current.Cell.Temperature,
                    mass = current.Cell.Mass, elementHash = current.Cell.ElementHash },
                ["revocationObserved"] = RevocationObserved, ["detailsRevoked"] = DetailsRevoked,
                ["bytes"] = new { sentProtocolBytes = Meter.SentBytes, receivedProtocolBytes = Meter.ReceivedBytes,
                    sentPackets = Meter.SentPackets, receivedPackets = Meter.ReceivedPackets, receivedApplicationPayloadBytes = Meter.ApplicationBytes,
                    excludesTcpIpHeaders = true },
                ["stages"] = Stages
            };
            string json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
            string temporary = Output + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream)) writer.Write(json);
                File.Move(temporary, Output, CreatedReceipt);
                CreatedReceipt = true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static int Run(string[] args)
    {
        var run = new RunState();
        try
        {
            if (args == null || args.Length != 9 || args[0] != "--native-interest-peer")
                throw new ArgumentException("usage: --native-interest-peer host port gameBuild ctVersion centerX centerY outputPath timeoutSeconds");
            run.Host = args[1]; run.Port = int.Parse(args[2], CultureInfo.InvariantCulture); run.Build = args[3]; run.CtVersion = args[4];
            run.CenterX = int.Parse(args[5], CultureInfo.InvariantCulture); run.CenterY = int.Parse(args[6], CultureInfo.InvariantCulture);
            string output = Path.GetFullPath(args[7]);
            if (File.Exists(output)) throw new IOException("receipt_exists_refusing_overwrite:" + output);
            run.Output = output;
            double timeout = double.Parse(args[8], CultureInfo.InvariantCulture);
            if (run.Port < 1 || run.Port > 65535 || timeout < 40 || timeout > 600) throw new ArgumentException("invalid_port_or_timeout_40_to_600_seconds");
            Directory.CreateDirectory(Path.GetDirectoryName(run.Output)); run.Deadline = timeout;
            using (var sha = SHA256.Create())
                run.ProtocolHash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(typeof(NativeInterestPeer).Assembly.Location))).ToLowerInvariant();
            Func<IRoomTransport> connect = () => new MeteredTransport(TcpRoomTransport.Join(run.Host, run.Port), run.Meter);
            run.Session = new RoomSession(connect(), new SessionProfile { Name = "Harness observer " + Guid.NewGuid().ToString("N").Substring(0, 6),
                GameVersion = run.Build, CycleTrimVersion = run.CtVersion, SpacedOut = true }, "", "Native interest protocol test", reconnect: connect);
            run.Stars = new StarSystemCoordinator(run.Session, new PreviewSource());
            Func<string, SystemMember> member = planet => run.Stars.State?.Members.FirstOrDefault(m => m.Connected && m.Planet?.PlanetId == planet);
            Func<string, string> authority = planet => member(planet)?.PeerId;
            run.Interest = new WorldInterestSession(run.Session, authority, planet => member(planet)?.Planet,
                (observer, planet, sub) => null, (observer, planet) => false);
            run.Details = new WorldCellDetailsSession(run.Session, authority, (observer, planet, index) => null, (observer, planet) => false);
            run.Session.ApplicationMessageReceived += (sender, data) => run.Meter.ApplicationBytes += data.Length;
            run.SetStage("connecting");
            run.Until(() => run.Session.Joined && run.Stars.State?.Members.Any(m => m.Connected && m.Planet != null &&
                m.PeerId == run.Session.Players.FirstOrDefault()?.Id) == true, "host admitted native planet manifest");
            run.Target = run.Stars.State.Members.First(m => m.Connected && m.Planet != null && m.PeerId == run.Session.Players[0].Id);
            var manifest = run.Target.Planet;
            run.Assert(!run.Stars.Admitted && !run.Stars.LandingRequested && !run.Stars.TravelConfirmed, "protocol observer must remain preview-only");
            int roiWidth = Math.Min(64, manifest.Width), roiHeight = Math.Min(64, manifest.Height);
            var region = new WorldInterestRegion { X = Math.Max(0, Math.Min(manifest.Width - roiWidth, run.CenterX - roiWidth / 2)),
                Y = Math.Max(0, Math.Min(manifest.Height - roiHeight, run.CenterY - roiHeight / 2)), Width = roiWidth, Height = roiHeight };
            var sub = new WorldInterestSubscription { Revision = 1, Region = region,
                Channels = WorldInterestChannels.Visual | WorldInterestChannels.Entities };
            run.SetStage("default_visual");
            run.Assert(run.Interest.Subscribe(run.Target.PeerId, manifest.PlanetId, sub), "default visual subscription rejected locally");
            run.Until(() => !run.Interest.Stale && run.Interest.VisualReplica?.Cells.Count > 0 &&
                run.Interest.MotionReplica?.Entities.Values.Any(e => e.Kind == 1) == true, "visible cells plus native duplicant movement lane");
            run.Assert(run.Interest.VisualReplica.Cells.Values.All(c => c.Fields == WorldInterestChannels.Visual && c.Mass == 0 && c.Temperature == 0),
                "default native visual payload included unwanted numerical fields");
            run.Assert(run.Interest.VisualReplica.Cells.Count <= roiWidth * roiHeight, "native host exceeded requested viewport");
            run.Stages["defaultVisual"] = run.FieldsSnapshot();
            run.DetailIndex = run.Interest.VisualReplica.Cells.Keys.OrderBy(index => Math.Abs(index % manifest.Width - run.CenterX) +
                Math.Abs(index / manifest.Width - run.CenterY)).First();
            string trustedEpoch = run.Interest.BoundEpoch;
            run.SetStage("single_cell_details");
            run.Assert(run.Details.Watch(run.Target.PeerId, manifest.PlanetId, trustedEpoch, run.DetailIndex), "trusted primary Bind did not permit detail Watch");
            run.Until(() => !run.Details.Stale && run.Details.Current != null, "native exact temperature and mass response");
            var detail = run.Details.Current;
            run.Assert(detail.Epoch == trustedEpoch && detail.Cell.Index == run.DetailIndex && detail.Cell.Temperature > 0 && detail.Cell.Mass >= 0 &&
                (detail.Cell.Fields & (WorldInterestChannels.Mass | WorldInterestChannels.Temperature)) ==
                    (WorldInterestChannels.Mass | WorldInterestChannels.Temperature), "native detail response invalid or unrelated to trusted epoch");
            run.Stages["singleCellDetail"] = new { index = detail.Cell.Index, epoch = detail.Epoch, sequence = detail.Sequence,
                temperature = detail.Cell.Temperature, mass = detail.Cell.Mass, fields = (byte)detail.Cell.Fields };
            run.SetStage("temperature_overlay");
            sub = new WorldInterestSubscription { Revision = 2, Region = region,
                Channels = WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Entities };
            run.Assert(run.Interest.Subscribe(run.Target.PeerId, manifest.PlanetId, sub), "thermal overlay subscription rejected locally");
            run.Until(() => !run.Interest.Stale && run.Interest.VisualReplica?.Cells.Count > 0 &&
                run.Interest.VisualReplica.Cells.Values.All(c => (c.Fields & WorldInterestChannels.Temperature) != 0 && c.Temperature > 0),
                "native thermal overlay field push");
            run.Assert(run.Interest.VisualReplica.Cells.Values.All(c => c.Fields == (WorldInterestChannels.Visual | WorldInterestChannels.Temperature) && c.Mass == 0),
                "temperature overlay exposed unrequested mass fields");
            run.Stages["temperatureOverlay"] = run.FieldsSnapshot();
            run.SetStage("holding");
            Console.WriteLine("READY " + run.Output);
            double holdUntil = run.Watch.Elapsed.TotalSeconds + 30;
            if (holdUntil + 2 > run.Deadline) throw new TimeoutException("insufficient overall timeout for required 30-second concurrent observation hold");
            while (run.Watch.Elapsed.TotalSeconds < holdUntil) run.Pump();
            run.Stages["hold"] = new { seconds = 30, motionPositionsChanged = run.MotionChanged,
                revocationObserved = run.RevocationObserved, detailsRevoked = run.DetailsRevoked,
                motionSequence = run.Interest.MotionReplica?.Sequence ?? 0, payloadBytes = run.Meter.ApplicationBytes };
            run.SetStage("stopping"); run.Details.Stop(); run.Interest.Stop();
            double drainUntil = run.Watch.Elapsed.TotalSeconds + 1;
            while (run.Watch.Elapsed.TotalSeconds < drainUntil) run.Pump();
            run.Assert(run.Interest.VisualReplica == null && run.Interest.MotionReplica == null && run.Details.Current == null,
                "Stop failed to release protocol observation caches");
            run.SetStage("leaving"); run.Session.RequestLeave();
            run.Until(() => run.Session.Closed, "acknowledged room departure");
            run.Assert(run.Session.LastError == "left_room", "native host did not acknowledge protocol observer departure:" + run.Session.LastError);
            run.Passed = true; run.SetStage("complete");
            Console.WriteLine("PASS native-host protocol observation; not a full native game guest; " + run.Output);
            return 0;
        }
        catch (Exception error)
        {
            run.Error = error.GetType().Name + ": " + error.Message;
            Console.Error.WriteLine("FAIL native-interest-peer stage=" + run.Stage + " " + run.Error);
            if (run.Output != null)
            {
                try { run.WriteReceipt(); }
                catch (Exception receiptError) { Console.Error.WriteLine("receipt_write_failed:" + receiptError.Message); }
            }
            return 1;
        }
        finally
        {
            run.Details?.Dispose(); run.Interest?.Dispose(); run.Stars?.Dispose(); run.Session?.Dispose();
        }
    }
}
