// Only an explicit probe build includes this file. It never starts a game or peer.
using System;
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
            MultiplayerNeed(relative != null && relative.StartsWith("multiplayer-disposable/", StringComparison.Ordinal) &&
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
                try { RequireMultiplayerState(runtime); MultiplayerNeed(MultiplayerSaveUnchanged(true), "save_hash_mismatch"); }
                catch { multiplayerArmed = false; throw; }
                return true;
            }
            MultiplayerNeed(multiplayerPermit != null && request.nonce == multiplayerPermit.nonce, "nonce_mismatch");
            if (command.action == "multiplayer_finish")
            {
                try { RequireMultiplayerState(runtime); MultiplayerNeed(MultiplayerSaveUnchanged(true), "save_hash_changed"); multiplayerFinished = true; }
                finally { multiplayerArmed = false; }
                return true;
            }
            RequireMultiplayerState(runtime);
            if (command.action == "multiplayer_chat")
            {
                MultiplayerNeed(!multiplayerChatSent, "chat_already_sent");
                string text = "[oni-multiplayer:" + request.nonce + ":" + multiplayerPermit.slot + "]:" + multiplayerAccountHash;
                MultiplayerNeed(runtime.Session.SendChat(text), "chat_not_accepted"); multiplayerChatSent = true;
            }
            else throw new InvalidOperationException("multiplayer_unknown_command");
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
                multiplayerFailure = "multiplayer_live_guard_failed"; multiplayerArmed = false;
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
                    { playerId = c.PlayerId, text = c.Text, sequence = c.Sequence, messageId = c.MessageId }).ToArray()
            };
        }
    }
}
