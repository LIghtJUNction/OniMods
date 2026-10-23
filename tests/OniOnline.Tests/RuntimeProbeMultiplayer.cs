// Only an explicit probe build includes this file. It never starts a game or peer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using OniOnline.Multiplayer;
using OniOnline.Network;
using OniOnline.Runtime;
using Steamworks;
using UnityEngine;

namespace OniOnline
{
    internal sealed partial class RuntimeProbe
    {
        [Serializable] private sealed class MultiplayerPermit
        {
            public string runNonce { get; set; }
            public string nonce { get; set; }
            public string slot { get; set; }
            public string modSha256 { get; set; }
            public string gameVersion { get; set; }
            public string gameAssemblySha256 { get; set; }
            public string cycleTrimSha256 { get; set; }
            public string saveRelativePath { get; set; }
            public string saveSha256 { get; set; }
            public long saveBytes { get; set; }
            public long saveWriteUnixMilliseconds { get; set; }
            public bool sharing { get; set; }
            public string ownerPeerId { get; set; }
            public string planetId { get; set; }
            public int x { get; set; }
            public int y { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public int index { get; set; }
        }

        [Serializable] private sealed class MultiplayerCellProof
        {
            public int index, elementHash, fields;
            public uint color;
            public float temperature, mass;
            public bool explored;
        }

        [Serializable] private sealed class MultiplayerEntityProof
        {
            public int id, cellIndex, kind;
            public float x, y;
        }

        [Serializable] private sealed class MultiplayerRegionProof
        { public int x, y, width, height; }

        [Serializable] private sealed class MultiplayerSourceProof
        {
            public string planetId, lineageId, ownerPeerId, epoch;
            public int nativeWorldId, width, height;
            public MultiplayerRegionProof region;
            public MultiplayerCellProof[] cells;
            public MultiplayerEntityProof[] entities;
            public int[] hiddenIndices;
            public MultiplayerCellProof detail;
        }

        [Serializable] private sealed class MultiplayerDetailProof
        {
            public string epoch;
            public int width, height;
            public MultiplayerCellProof cell;
        }

        [Serializable] private sealed class MultiplayerObservationProof
        {
            public int version = 1;
            public bool originalSharing, sharing, cleanupComplete;
            public string cleanupError;
            public MultiplayerSourceProof source;
            public string ownerPeerId, planetId, boundEpoch, interestError, detailsError;
            public long visualSequence, motionSequence;
            public bool stale, detailsStale;
            public int activeObservers, activeDetailObservers;
            public MultiplayerCellProof[] cells;
            public MultiplayerEntityProof[] entities;
            public MultiplayerDetailProof detail;
        }

        [Serializable] private sealed class MultiplayerChatProof
        {
            public string playerId, text;
            public long sequence, messageId;
        }

        [Serializable] private sealed class MultiplayerProbeSnapshot
        {
            public int version = 1;
            public string publishedUtc, runNonce, processStartedUtc, executable, nonce, deviceHash;
            public string gameAssemblySha256, cycleTrimSha256, steamAccountHash, roomId, localPlayerId, transport;
            public string saveRelativePath, saveSha256, failure;
            public long saveBytes, saveWriteUnixMilliseconds;
            public bool steamInitialized, steamLoggedOn, steamSubscribed, armed, finished, saveUnchanged;
            public int worldId, worldCells, pendingChats;
            public string[] connectedPlayers;
            public MultiplayerChatProof[] messages;
            public MultiplayerObservationProof observation;
        }

        private readonly string multiplayerRunNonce = Guid.NewGuid().ToString("N");
        private readonly string multiplayerProcessStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O");
        private MultiplayerPermit multiplayerPermit;
        private RoomSession multiplayerSession;
        private Game multiplayerGame;
        private WorldContainer multiplayerWorld;
        private string multiplayerSavePath, multiplayerAccountHash, multiplayerDeviceHash, multiplayerFailure;
        private string multiplayerGameHash, multiplayerCycleTrimHash;
        private bool multiplayerArmed, multiplayerFinished, multiplayerChatSent;
        private double multiplayerLeaseEnd;
        private WorldCoordinator multiplayerCoordinator;
        private MultiplayerSourceProof multiplayerSource;
        private bool multiplayerOriginalSharing, multiplayerCleanupComplete;
        private string multiplayerCleanupError;
        private long multiplayerObservationRevision;

        private static MultiplayerCellProof MultiplayerCell(WorldInterestCell cell)
        {
            return new MultiplayerCellProof { index = cell.Index, elementHash = cell.ElementHash, color = cell.Color,
                fields = (int)cell.Fields, temperature = cell.Temperature, mass = cell.Mass, explored = cell.Explored };
        }

        private static MultiplayerEntityProof MultiplayerEntity(WorldInterestEntity entity)
        {
            return new MultiplayerEntityProof { id = entity.Id, cellIndex = entity.CellIndex, kind = entity.Kind,
                x = entity.X, y = entity.Y };
        }

        private int MultiplayerNativeCell(int x, int y)
        { return Grid.XYToCell(multiplayerWorld.WorldOffset.x + x, multiplayerWorld.WorldOffset.y + y); }

        private bool MultiplayerExplored(int x, int y)
        {
            int cell = MultiplayerNativeCell(x, y);
            return Grid.IsValidCell(cell) && Grid.WorldIdx[cell] == multiplayerWorld.id && Grid.Visible[cell] != 0;
        }

        private void PrepareMultiplayerObservation(OnlineRuntime runtime)
        {
            var coordinator = runtime.Multiplayer;
            MultiplayerNeed(coordinator != null && coordinator.StarSystem.Admitted, "registered_planet_required");
            MultiplayerNeed(coordinator.Work.Authority.Synchronize(), "native_authority_required");
            var planet = coordinator.Work.Authority.Planet;
            var manifest = coordinator.StarSystem.State?.Members.SingleOrDefault(m => m.PeerId == multiplayerSession.LocalPlayerId)?.Planet;
            MultiplayerNeed(planet != null && manifest != null && planet.PlanetId == manifest.PlanetId &&
                planet.NativeWorldId == multiplayerWorld.id && manifest.NativeWorldId == multiplayerWorld.id &&
                planet.OwnerPeerId == multiplayerSession.LocalPlayerId, "selected_native_planet_required");
            MultiplayerNeed(multiplayerSession.Players.Where(p => p.Id != multiplayerSession.LocalPlayerId).All(p =>
                !coordinator.Work.Authority.HasPermission(p.Id, RemoteWorkPermission.ViewMap)), "existing_view_grant_refused");
            MultiplayerNeed(coordinator.Interest.PlanetId == null && coordinator.CellDetails.Current == null &&
                coordinator.Interest.ActiveObservers == 0 && coordinator.CellDetails.ActiveObservers == 0,
                "idle_observation_required");

            int width = multiplayerWorld.WorldSize.x, height = multiplayerWorld.WorldSize.y;
            MultiplayerNeed(width == manifest.Width && height == manifest.Height && width >= 2 && height >= 2,
                "native_dimensions_changed");
            WorldInterestRegion region = new WorldInterestRegion();
            bool found = false;
            // Read the owner's persistent exploration state. No terrain or exploration is changed.
            for (int y = 0; y < height && !found; y++)
                for (int x = 0; x < width && !found; x++)
                {
                    if (!MultiplayerExplored(x, y) || !((x > 0 && !MultiplayerExplored(x - 1, y)) ||
                        (x + 1 < width && !MultiplayerExplored(x + 1, y)) || (y > 0 && !MultiplayerExplored(x, y - 1)) ||
                        (y + 1 < height && !MultiplayerExplored(x, y + 1)))) continue;
                    region = new WorldInterestRegion { Width = Math.Min(16, width), Height = Math.Min(12, height) };
                    region.X = Math.Max(0, Math.Min(width - region.Width, x - region.Width / 2));
                    region.Y = Math.Max(0, Math.Min(height - region.Height, y - region.Height / 2));
                    found = true;
                }
            MultiplayerNeed(found, "mixed_exploration_region_required");
            var hidden = new List<int>();
            for (int y = region.Y; y < region.Y + region.Height; y++)
                for (int x = region.X; x < region.X + region.Width; x++)
                {
                    int cell = MultiplayerNativeCell(x, y);
                    if (Grid.IsValidCell(cell) && Grid.WorldIdx[cell] == multiplayerWorld.id && Grid.Visible[cell] == 0)
                        hidden.Add(y * width + x);
                }
            var frame = new NativeWorldInterestSource().Capture(planet, new WorldInterestSubscription
            { Revision = 1, Region = region }, (long)(GameClock.Instance.GetTime() * 5), 1, index => true);
            MultiplayerNeed(frame.Cells.Count > 0 && hidden.Count > 0, "mixed_exploration_region_required");
            var detailCell = frame.Cells[0];
            int detailNative = MultiplayerNativeCell(detailCell.Index % width, detailCell.Index / width);
            detailCell.Fields = WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass;
            detailCell.Temperature = Grid.Temperature[detailNative]; detailCell.Mass = Grid.Mass[detailNative];
            MultiplayerNeed(WorldInterestPublisher.Finite(detailCell.Temperature) && detailCell.Temperature >= 0 &&
                WorldInterestPublisher.Finite(detailCell.Mass) && detailCell.Mass >= 0, "native_details_unavailable");
            multiplayerCoordinator = coordinator; multiplayerOriginalSharing = coordinator.ShareOwnWorld;
            multiplayerCleanupComplete = false; multiplayerCleanupError = null; multiplayerObservationRevision = 0;
            multiplayerSource = new MultiplayerSourceProof { planetId = planet.PlanetId, lineageId = manifest.LineageId,
                ownerPeerId = multiplayerSession.LocalPlayerId, epoch = planet.AuthorityEpoch, nativeWorldId = planet.NativeWorldId,
                width = width, height = height, region = new MultiplayerRegionProof
                { x = region.X, y = region.Y, width = region.Width, height = region.Height },
                cells = frame.Cells.OrderBy(c => c.Index).Select(MultiplayerCell).ToArray(),
                entities = frame.Entities.OrderBy(e => e.Id).Select(MultiplayerEntity).ToArray(),
                hiddenIndices = hidden.ToArray(), detail = MultiplayerCell(detailCell) };
        }

        private void CleanupMultiplayerObservation(OnlineRuntime runtime)
        {
            multiplayerCleanupComplete = false; multiplayerCleanupError = null;
            try
            {
                MultiplayerNeed(multiplayerCoordinator != null && runtime.Multiplayer == multiplayerCoordinator &&
                    runtime.Session == multiplayerSession, "cleanup_coordinator_changed");
                multiplayerCoordinator.Interest.Stop(); multiplayerCoordinator.CellDetails.Stop();
                var planet = multiplayerCoordinator.Work.Authority.Planet;
                bool originalSource = Game.Instance == multiplayerGame && ClusterManager.Instance != null &&
                    ClusterManager.Instance.activeWorldId == multiplayerWorld.id &&
                    ClusterManager.Instance.GetWorld(multiplayerWorld.id) == multiplayerWorld &&
                    multiplayerSource != null && planet != null && planet.PlanetId == multiplayerSource.planetId &&
                    planet.AuthorityEpoch == multiplayerSource.epoch && MultiplayerSaveUnchanged(true);
                if (!originalSource)
                {
                    // Never reopen a different world/save using the old test's grant.
                    multiplayerCoordinator.SetSharing(false);
                    throw new InvalidOperationException("multiplayer_cleanup_source_changed");
                }
                multiplayerCoordinator.SetSharing(multiplayerOriginalSharing);
                multiplayerCleanupComplete = true;
            }
            catch (Exception error) { multiplayerCleanupError = error.GetType().Name + ": " + error.Message; }
        }

        private MultiplayerObservationProof CaptureMultiplayerObservation(OnlineRuntime runtime)
        {
            var coordinator = runtime.Multiplayer;
            var interest = coordinator?.Interest;
            var details = coordinator?.CellDetails;
            var detail = details?.Current;
            return new MultiplayerObservationProof { originalSharing = multiplayerOriginalSharing,
                sharing = coordinator != null && coordinator.ShareOwnWorld,
                cleanupComplete = multiplayerCleanupComplete, cleanupError = multiplayerCleanupError, source = multiplayerSource,
                ownerPeerId = interest?.OwnerId, planetId = interest?.PlanetId, boundEpoch = interest?.BoundEpoch,
                visualSequence = interest?.VisualReplica?.Sequence ?? 0, motionSequence = interest?.MotionReplica?.Sequence ?? 0,
                stale = interest == null || interest.Stale, interestError = interest?.LastError,
                cells = interest?.VisualReplica == null ? new MultiplayerCellProof[0] :
                    interest.VisualReplica.Cells.Values.OrderBy(c => c.Index).Select(MultiplayerCell).ToArray(),
                entities = interest?.MotionReplica == null ? new MultiplayerEntityProof[0] :
                    interest.MotionReplica.Entities.Values.OrderBy(e => e.Id).Select(MultiplayerEntity).ToArray(),
                detail = detail == null ? null : new MultiplayerDetailProof
                { epoch = detail.Epoch, width = detail.Width, height = detail.Height, cell = MultiplayerCell(detail.Cell) },
                detailsStale = details == null || details.Stale,
                detailsError = details?.LastError, activeObservers = interest?.ActiveObservers ?? 0,
                activeDetailObservers = details?.ActiveObservers ?? 0 };
        }

        private static string MultiplayerHash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        private static string MultiplayerFileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static bool MultiplayerHex(string value, int length)
        { return value != null && value.Length == length && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f'); }

        private static void MultiplayerNeed(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException("multiplayer_" + reason); }

        private void CaptureMultiplayerBuilds()
        {
            if (multiplayerGameHash != null) return;
            multiplayerGameHash = MultiplayerFileHash(typeof(Game).Assembly.Location);
            var cycleTrim = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "CycleTrim");
            multiplayerCycleTrimHash = cycleTrim == null ? "" : MultiplayerFileHash(cycleTrim.Location);
        }

        private static string MultiplayerTransport(RoomSession session)
        {
            var transport = session == null ? null : AccessTools.Field(typeof(RoomSession), "transport").GetValue(session);
            return transport is SteamRoomTransport ? "steam" : transport is TcpRoomTransport ? "tcp" : "unavailable";
        }

        private string ResolveMultiplayerSave(string relative)
        {
            // Harness already approves its isolated profile's OniHarness/test.sav.
            // Accept that same disposable location without changing its native allowlist.
            MultiplayerNeed(relative != null && (relative.StartsWith("multiplayer-disposable/", StringComparison.Ordinal) ||
                relative.StartsWith("OniHarness/", StringComparison.Ordinal)) &&
                relative.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) && !relative.Contains("\\") &&
                !relative.Split('/').Any(p => p == "." || p == ".." || p.Length == 0), "disposable_save_path_required");
            string root = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative));
            MultiplayerNeed(path.StartsWith(root, StringComparison.Ordinal) && File.Exists(path), "disposable_save_missing");
            // Refuse symlinked copies/directories rather than following them to personal saves.
            for (string part = path; !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
                MultiplayerNeed((File.GetAttributes(part) & FileAttributes.ReparsePoint) == 0, "save_symlink_refused");
            return path;
        }

        private bool MultiplayerSaveUnchanged(bool hash)
        {
            if (multiplayerPermit == null || string.IsNullOrEmpty(multiplayerSavePath) || !File.Exists(multiplayerSavePath)) return false;
            var file = new FileInfo(multiplayerSavePath);
            return SaveLoader.GetActiveSaveFilePath() != null &&
                Path.GetFullPath(SaveLoader.GetActiveSaveFilePath()) == multiplayerSavePath &&
                file.Length == multiplayerPermit.saveBytes &&
                (file.LastWriteTimeUtc.Ticks - 621355968000000000L) / 10000 == multiplayerPermit.saveWriteUnixMilliseconds &&
                (!hash || MultiplayerFileHash(multiplayerSavePath) == multiplayerPermit.saveSha256);
        }

        private void RequireMultiplayerIsolation()
        {
            // A loaded disposable copy in a fresh probe process must not share an
            // Update loop with an older mutation, UI, generation or stepping fixture.
            MultiplayerNeed(!testColony && !stepArmed && runUntil == 0 && stepStartTime == 0 && testSavePath == null &&
                nativeWork == null && nativeDupe == null && nativeRemoteWork == null &&
                nativeWorkContext == null && nativeWorkAuthority == null && nativeWorkConfirmedRequest == null &&
                nativeWorkGame == null && nativeWorkWorld == null && !nativeWorkRevocationWaiting &&
                !networkWorkActive && networkWorkExecutor == null && networkWorkCoordinator == null &&
                networkWorkGame == null && networkWorkWorld == null && !taskUiWaiting && nativeChatUi == null &&
                nativeBuildingPermit == null && nativeBuildingScout == null &&
                GetComponent<NativeLifecycleProbe>() == null && GetComponent<NativeNavigationQuiescenceProbe>() == null &&
                GetComponent<NativeBuildingBridgeProbe>() == null, "fresh_probe_required");
        }

        private void RequireMultiplayerState(OnlineRuntime runtime)
        {
            RequireMultiplayerIsolation();
            MultiplayerNeed(multiplayerArmed && multiplayerFailure == null && runtime.Session == multiplayerSession &&
                multiplayerSession.Joined && !multiplayerSession.Leaving && !multiplayerSession.Reconnecting &&
                !multiplayerSession.Closed, "session_changed");
            MultiplayerNeed(Game.Instance == multiplayerGame && Game.Instance != null && Game.Instance.baseAlreadyCreated &&
                !Game.Instance.IsLoading() && ClusterManager.Instance != null &&
                ClusterManager.Instance.activeWorldId == multiplayerWorld.id &&
                ClusterManager.Instance.GetWorld(multiplayerWorld.id) == multiplayerWorld &&
                GameClock.Instance != null && SpeedControlScreen.Instance != null, "world_changed");
            MultiplayerNeed(SharedTimeController.Active == null, "shared_time_enabled");
            MultiplayerNeed(SpeedControlScreen.Instance.IsPaused, "local_pause_required");
            MultiplayerNeed(MultiplayerSaveUnchanged(false), "active_save_changed");
            if (multiplayerSource != null)
            {
                var planet = runtime.Multiplayer?.Work.Authority.Planet;
                MultiplayerNeed(runtime.Multiplayer == multiplayerCoordinator && planet != null &&
                    planet.PlanetId == multiplayerSource.planetId && planet.AuthorityEpoch == multiplayerSource.epoch &&
                    planet.NativeWorldId == multiplayerSource.nativeWorldId && planet.OwnerPeerId == multiplayerSource.ownerPeerId,
                    "observation_source_changed");
            }
        }

        private bool TryExecuteMultiplayerProbe(OnlineRuntime runtime, Command command)
        {
            bool own = command.action != null && command.action.StartsWith("multiplayer_", StringComparison.Ordinal);
            if (!own)
            {
                if (command.action != "quit") MultiplayerNeed(!multiplayerArmed, "finish_before_other_probe_commands");
                return false;
            }
            var request = JsonConvert.DeserializeObject<MultiplayerPermit>(command.value ?? "");
            MultiplayerNeed(request != null && request.runNonce == multiplayerRunNonce && MultiplayerHex(request.nonce, 32), "stale_command");
            if (command.action == "multiplayer_arm")
            {
                MultiplayerNeed(!multiplayerArmed, "already_armed");
                RequireMultiplayerIsolation();
                CaptureMultiplayerBuilds();
                MultiplayerNeed(request.modSha256 == modHash && request.gameVersion == runtime.GameVersion &&
                    request.gameAssemblySha256 == multiplayerGameHash && request.cycleTrimSha256 == multiplayerCycleTrimHash,
                    "build_mismatch");
                MultiplayerNeed(request.slot == "a" || request.slot == "b", "invalid_slot");
                MultiplayerNeed(MultiplayerHex(request.saveSha256, 64) && request.saveBytes > 0, "save_fingerprint_required");
                MultiplayerNeed(SteamManager.Initialized && SteamUser.GetSteamID().IsValid() &&
                    SteamApps.BIsSubscribedApp(new AppId_t(457140)), "authorized_steam_session_required");
                MultiplayerNeed(runtime.Session != null && runtime.Session.Joined && Game.Instance != null &&
                    Game.Instance.baseAlreadyCreated && !Game.Instance.IsLoading() && ClusterManager.Instance != null,
                    "loaded_native_room_required");
                WorldContainer world = ClusterManager.Instance.GetWorld(ClusterManager.Instance.activeWorldId);
                MultiplayerNeed(world != null && world.WorldSize.x > 0 && world.WorldSize.y > 0 && Grid.WidthInCells > 0,
                    "native_world_required");
                string device = SystemInfo.deviceUniqueIdentifier;
                MultiplayerNeed(!string.IsNullOrEmpty(device) && !string.Equals(device, "unsupported", StringComparison.OrdinalIgnoreCase),
                    "device_identity_unavailable");
                multiplayerPermit = request; multiplayerSavePath = ResolveMultiplayerSave(request.saveRelativePath);
                multiplayerSession = runtime.Session; multiplayerGame = Game.Instance; multiplayerWorld = world;
                multiplayerAccountHash = MultiplayerHash(request.nonce + "\nsteam\n" + SteamUser.GetSteamID().m_SteamID.ToString(CultureInfo.InvariantCulture));
                multiplayerDeviceHash = MultiplayerHash(request.nonce + "\ndevice\n" + device);
                multiplayerFailure = null; multiplayerFinished = false; multiplayerChatSent = false;
                multiplayerArmed = true;
                multiplayerLeaseEnd = Time.realtimeSinceStartupAsDouble + 120;
                try
                {
                    RequireMultiplayerState(runtime); MultiplayerNeed(MultiplayerSaveUnchanged(true), "save_hash_mismatch");
                    PrepareMultiplayerObservation(runtime);
                }
                catch { multiplayerArmed = false; throw; }
                return true;
            }
            MultiplayerNeed(multiplayerPermit != null && request.nonce == multiplayerPermit.nonce, "nonce_mismatch");
            if (command.action == "multiplayer_finish")
            {
                try { RequireMultiplayerState(runtime); MultiplayerNeed(MultiplayerSaveUnchanged(true), "save_hash_changed"); multiplayerFinished = true; }
                finally { CleanupMultiplayerObservation(runtime); multiplayerArmed = false; }
                return true;
            }
            try
            {
                RequireMultiplayerState(runtime);
                if (command.action == "multiplayer_chat")
                {
                    MultiplayerNeed(!multiplayerChatSent, "chat_already_sent");
                    string text = "[oni-multiplayer:" + request.nonce + ":" + multiplayerPermit.slot + "]:" + multiplayerAccountHash;
                    MultiplayerNeed(runtime.Session.SendChat(text), "chat_not_accepted"); multiplayerChatSent = true;
                }
                else if (command.action == "multiplayer_share") multiplayerCoordinator.SetSharing(request.sharing);
                else if (command.action == "multiplayer_observe")
                {
                    multiplayerCoordinator.CellDetails.Stop();
                    MultiplayerNeed(multiplayerCoordinator.Interest.Subscribe(request.ownerPeerId, request.planetId,
                        new WorldInterestSubscription { Revision = ++multiplayerObservationRevision,
                            Region = new WorldInterestRegion { X = request.x, Y = request.y, Width = request.width, Height = request.height } }),
                        "viewport_not_accepted");
                }
                else if (command.action == "multiplayer_details")
                {
                    var interest = multiplayerCoordinator.Interest;
                    MultiplayerNeed(!interest.Stale && interest.OwnerId == request.ownerPeerId && interest.PlanetId == request.planetId &&
                        multiplayerCoordinator.CellDetails.Watch(request.ownerPeerId, request.planetId, interest.BoundEpoch, request.index),
                        "detail_watch_not_accepted");
                }
                else if (command.action == "multiplayer_stop")
                { multiplayerCoordinator.Interest.Stop(); multiplayerCoordinator.CellDetails.Stop(); }
                else throw new InvalidOperationException("multiplayer_unknown_command");
                // Every command is challenge-bound and validated while paused. An idle
                // or lost collector still expires after this bounded recovery window.
                multiplayerLeaseEnd = Time.realtimeSinceStartupAsDouble + 120;
            }
            catch
            {
                CleanupMultiplayerObservation(runtime);
                multiplayerFailure = "multiplayer_command_failed"; multiplayerArmed = false;
                throw;
            }
            return true;
        }

        private void AdvanceMultiplayerProbe(OnlineRuntime runtime)
        {
            if (!multiplayerArmed) return;
            try
            {
                RequireMultiplayerState(runtime);
                MultiplayerNeed(Time.realtimeSinceStartupAsDouble <= multiplayerLeaseEnd, "lease_expired");
            }
            catch (Exception)
            {
                multiplayerFailure = "multiplayer_live_guard_failed";
                CleanupMultiplayerObservation(runtime); multiplayerArmed = false;
            }
        }

        private MultiplayerProbeSnapshot CaptureMultiplayerProbe(OnlineRuntime runtime)
        {
            CaptureMultiplayerBuilds();
            var session = runtime.Session;
            var world = ClusterManager.Instance == null ? null : ClusterManager.Instance.GetWorld(ClusterManager.Instance.activeWorldId);
            string prefix = multiplayerPermit == null ? null : "[oni-multiplayer:" + multiplayerPermit.nonce + ":";
            return new MultiplayerProbeSnapshot
            {
                publishedUtc = System.DateTime.UtcNow.ToString("O"), runNonce = multiplayerRunNonce, processStartedUtc = multiplayerProcessStart,
                executable = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName),
                nonce = multiplayerPermit?.nonce, deviceHash = multiplayerDeviceHash,
                gameAssemblySha256 = multiplayerGameHash, cycleTrimSha256 = multiplayerCycleTrimHash,
                steamInitialized = SteamManager.Initialized,
                steamLoggedOn = SteamManager.Initialized && SteamUser.BLoggedOn(),
                steamSubscribed = SteamManager.Initialized && SteamApps.BIsSubscribedApp(new AppId_t(457140)),
                steamAccountHash = multiplayerAccountHash, roomId = session?.RoomId, localPlayerId = session?.LocalPlayerId,
                transport = MultiplayerTransport(session), armed = multiplayerArmed, finished = multiplayerFinished,
                saveRelativePath = multiplayerPermit?.saveRelativePath, saveSha256 = multiplayerPermit?.saveSha256,
                saveBytes = multiplayerPermit?.saveBytes ?? 0, saveWriteUnixMilliseconds = multiplayerPermit?.saveWriteUnixMilliseconds ?? 0,
                saveUnchanged = MultiplayerSaveUnchanged(false), failure = multiplayerFailure,
                worldId = world == null ? -1 : world.id, worldCells = world == null ? 0 : checked(world.WorldSize.x * world.WorldSize.y),
                connectedPlayers = session == null ? new string[0] : session.Players.Where(p => p.Connected).Select(p => p.Id).ToArray(),
                pendingChats = session?.PendingChatCount ?? 0,
                messages = session == null || prefix == null ? new MultiplayerChatProof[0] : session.Chat
                    .Where(c => c.Text.StartsWith(prefix, StringComparison.Ordinal)).Select(c => new MultiplayerChatProof
                    { playerId = c.PlayerId, text = c.Text, sequence = c.Sequence, messageId = c.MessageId }).ToArray(),
                observation = CaptureMultiplayerObservation(runtime)
            };
        }
    }
}
