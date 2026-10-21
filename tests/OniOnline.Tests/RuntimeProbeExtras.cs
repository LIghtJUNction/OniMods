// Explicit test-build extensions. These never compile into the ordinary Mod.
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using OniOnline.Multiplayer;
using OniOnline.Runtime;
using OniOnline.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OniOnline
{
    internal sealed partial class RuntimeProbe
    {
        private bool taskUiWaiting;
        private float taskUiDeadline;
        private int[] archiveStarterIds;
        private static Game preflightCheckpointGame;
        private static string preflightCheckpointPath, preflightCheckpointHash;

        private void PrepareNativeDuplicantRoundTrip(OnlineRuntime runtime, string artifactDirectory)
        {
            var report = new NativeRoundTripPreparationReport();
            object before = null, after = null;
            string checkpoint = null, checkpointHash = null;
            try
            {
                var source = ReadNativePreflightSource(runtime);
                checkpoint = source.savePath; checkpointHash = source.saveSha256;
                before = new { native = CaptureNativePreflightFingerprint(), identities = NativeDuplicantRoundTripPreparation.CaptureIdentityFingerprint(),
                    checkpointSha256 = HashNativePreflightFile(checkpoint) };
                if (string.IsNullOrEmpty(artifactDirectory))
                {
                    var directories = Directory.GetDirectories(output, "native-dupe-archive-*", SearchOption.TopDirectoryOnly);
                    if (directories.Length != 1) throw new InvalidOperationException("Specify the one existing exact-checkpoint Archive v2 directory; no archive is captured during preparation.");
                    artifactDirectory = directories[0];
                }
                var selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
                var identity = selected == null ? null : selected.GetComponent<KPrefabID>();
                bool reloaded = source.createdPid != System.Diagnostics.Process.GetCurrentProcess().Id ||
                    (!ReferenceEquals(preflightCheckpointGame, null) && !ReferenceEquals(preflightCheckpointGame, Game.Instance) &&
                        preflightCheckpointPath == checkpoint && preflightCheckpointHash == checkpointHash);
                report = NativeDuplicantRoundTripPreparation.Inspect(new NativeRoundTripPreparationPermit {
                    Source = new DuplicantTransferExpectation { OwnerId = source.ownerId, PlanetId = source.planetId,
                        NativeWorldId = source.nativeWorldId, MinionId = identity == null ? -1 : identity.InstanceID, ActiveSavePath = checkpoint },
                    OriginalStarterIds = source.originalStarterIds, ArtifactDirectory = artifactDirectory,
                    NormalCheckpointReloadProven = reloaded,
                    LifecycleProven = NativeLifecycleProbe.HasSuccessfulProof(Game.Instance, checkpoint, checkpointHash) });
            }
            catch (Exception error) { report.Block("preparation_read_contract:" + error.GetType().Name + ":" + error.Message); }
            finally
            {
                if (before != null)
                {
                    try {
                        after = new { native = CaptureNativePreflightFingerprint(), identities = NativeDuplicantRoundTripPreparation.CaptureIdentityFingerprint(),
                            checkpointSha256 = HashNativePreflightFile(checkpoint) };
                        report.NativeFingerprintUnchanged = JsonConvert.SerializeObject(before) == JsonConvert.SerializeObject(after);
                    }
                    catch (Exception error) { report.Block("after_fingerprint_read_contract:" + error.GetType().Name + ":" + error.Message); }
                }
                if (!report.NativeFingerprintUnchanged) report.Block("native_source_identity_counter_grid_or_checkpoint_fingerprint_changed_or_unproven");
                report.PreparationReady &= report.NativeFingerprintUnchanged;
                string directory = Path.Combine(output, "native-round-trip-prepare-" + sequence);
                if (Directory.Exists(directory)) throw new IOException("Round-trip observation evidence directory must be fresh.");
                Directory.CreateDirectory(directory);
                if (report.Payload != null)
                    for (int i = 0; i < report.Payload.Roots.Length; i++) File.WriteAllBytes(Path.Combine(directory, "candidate-root-" + i + ".bin"), report.Payload.Roots[i]);
                File.WriteAllText(Path.Combine(directory, "receipt.json"), JsonConvert.SerializeObject(new { report, before, after,
                    sourceCheckpoint = checkpoint, sourceCheckpointSha256 = checkpointHash, artifactDirectory }, Formatting.Indented));
            }
        }

        private void StartNativeLifecycleProbe(OnlineRuntime runtime)
        {
            // Reuse only the exact existing private checkpoint/original-starter read guard.
            // No testColony mutation gates are opened for an inspected reload.
            var source = ReadNativePreflightSource(runtime);
            string name = "native-lifecycle-probe-" + sequence + ".json";
            NativeLifecycleProbe.Begin(gameObject,
                () => new { native = CaptureNativePreflightFingerprint(), sourceSaveSha256 = HashNativePreflightFile(source.savePath) },
                () => {
                    RequireNativePreflightColony();
                    if (SaveLoader.GetActiveSaveFilePath() != source.savePath)
                        throw new InvalidOperationException("Lifecycle experiment's existing private active checkpoint changed.");
                }, receipt => WriteNativePreflightJson(name, receipt), source.savePath, source.saveSha256);
        }

        private void StartNativeNavigationQuiescenceProbe(OnlineRuntime runtime)
        {
            var source = ReadNativePreflightSource(runtime);
            if (GetComponent<NativeLifecycleProbe>() != null || networkWorkActive || nativeWork != null || nativeDupe != null ||
                !NativeLifecycleProbe.HasSuccessfulProof(Game.Instance, source.savePath, source.saveSha256))
                throw new InvalidOperationException("A successful empty lifecycle on this exact checkpoint, without another native fixture, is required.");
            var selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
            var identity = selected == null ? null : selected.GetComponent<KPrefabID>();
            string name = "native-navigation-quiescence-" + sequence + ".json";
            NativeNavigationQuiescenceProbe.Begin(gameObject, new NativeNavigationQuiescencePermit {
                Source = new DuplicantTransferExpectation { OwnerId = source.ownerId, PlanetId = source.planetId,
                    NativeWorldId = source.nativeWorldId, MinionId = identity == null ? -1 : identity.InstanceID,
                    ActiveSavePath = source.savePath },
                OriginalStarterIds = source.originalStarterIds, CheckpointSha256 = source.saveSha256
            }, () => CaptureNativePreflightFingerprint(),
                () => { ReadNativePreflightSource(runtime); }, receipt => WriteNativePreflightJson(name, receipt));
        }

        private sealed class NativePreflightSource
        {
            public string savePath, saveSha256, ownedSaveDirectory, ownerId, planetId, previousActiveSave, previousActiveSha256;
            public bool previousActiveBytesUnchanged;
            public int nativeWorldId, createdPid;
            public int[] originalStarterIds;
        }

        private static string HashNativePreflightFile(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private void RecordNativePreflightSource(OnlineRuntime runtime, string save, string previousActiveSave, string previousActiveHash)
        {
            RequireNativePreflightColony();
            var starters = Components.LiveMinionIdentities.Items.Where(dupe => dupe != null)
                .Select(dupe => dupe.GetComponent<KPrefabID>().InstanceID).OrderBy(id => id).ToArray();
            if (archiveStarterIds == null || !archiveStarterIds.OrderBy(id => id).SequenceEqual(starters) || starters.Length != 3 ||
                runtime.SelectedPlanet == null || SaveLoader.GetActiveSaveFilePath() != save)
                throw new InvalidOperationException("Preflight checkpoint requires the same three original starters and the fresh active private save.");
            bool unchanged = previousActiveHash == null || (File.Exists(previousActiveSave) && HashNativePreflightFile(previousActiveSave) == previousActiveHash);
            var source = new NativePreflightSource { savePath = save, saveSha256 = HashNativePreflightFile(save),
                ownedSaveDirectory = Path.GetDirectoryName(Path.GetFullPath(save)),
                ownerId = runtime.SelectedPlanet.OwnerId, planetId = runtime.SelectedPlanet.PlanetId,
                nativeWorldId = runtime.SelectedPlanet.NativeWorldId, createdPid = System.Diagnostics.Process.GetCurrentProcess().Id,
                originalStarterIds = starters, previousActiveSave = previousActiveSave, previousActiveSha256 = previousActiveHash,
                previousActiveBytesUnchanged = unchanged };
            WriteNativePreflightJson("native-preflight-source.json", source);
            preflightCheckpointGame = Game.Instance; preflightCheckpointPath = save; preflightCheckpointHash = source.saveSha256;
            if (!unchanged) throw new InvalidOperationException("The previous active save bytes changed during the explicit private checkpoint save.");
        }

        private static void RequireNativePreflightColony()
        {
            if (Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() ||
                SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused)
                throw new InvalidOperationException("Native preflight requires an already loaded, paused disposable colony.");
        }

        private NativePreflightSource ReadNativePreflightSource(OnlineRuntime runtime)
        {
            RequireNativePreflightColony();
            string path = Path.Combine(output, "native-preflight-source.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 16384)
                throw new InvalidOperationException("An existing private fresh checkpoint and its original-starter receipt are required; use explicit save_test activate_preflight first, or copy its receipt for read-only reload inspection.");
            var source = JsonConvert.DeserializeObject<NativePreflightSource>(File.ReadAllText(path));
            string active = SaveLoader.GetActiveSaveFilePath();
            var starters = Components.LiveMinionIdentities.Items.Where(dupe => dupe != null)
                .Select(dupe => dupe.GetComponent<KPrefabID>().InstanceID).OrderBy(id => id).ToArray();
            if (source == null || source.originalStarterIds == null || source.originalStarterIds.Length != 3 || starters.Length != 3 ||
                !source.originalStarterIds.OrderBy(id => id).SequenceEqual(starters) || !source.previousActiveBytesUnchanged ||
                active != source.savePath || !Path.IsPathRooted(active) ||
                !Path.IsPathRooted(source.ownedSaveDirectory) || Path.GetDirectoryName(Path.GetFullPath(active)) != source.ownedSaveDirectory ||
                Path.GetFileName(source.ownedSaveDirectory) != "OniOnlineHarness-" + source.createdPid || !File.Exists(active) ||
                HashNativePreflightFile(active) != source.saveSha256 || (!testColony && source.createdPid == System.Diagnostics.Process.GetCurrentProcess().Id))
                throw new InvalidOperationException("Preflight source does not match this exact private saved three-starter colony; no load or repair was attempted.");
            // A copied receipt permits only this read-only command after reload. It never enables
            // testColony or any of the probe's mutation/step fixture gates.
            return source;
        }

        private void InspectNativeTransferPreflight(OnlineRuntime runtime, string archiveManifestPath)
        {
            var source = ReadNativePreflightSource(runtime);
            if (!string.IsNullOrEmpty(archiveManifestPath))
            {
                string runRoot = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.IsPathRooted(archiveManifestPath) ||
                    !Path.GetFullPath(archiveManifestPath).StartsWith(runRoot, StringComparison.Ordinal) ||
                    Path.GetFileName(archiveManifestPath) != "manifest.json")
                    throw new InvalidOperationException("Diagnostic archive manifest must belong to this current private probe run.");
            }
            int chosenId = source.originalStarterIds.Min();
            var chosen = Components.LiveMinionIdentities.Items.Single(dupe => dupe != null && dupe.GetComponent<KPrefabID>().InstanceID == chosenId);
            SelectTool.Instance.Select(chosen.GetComponent<KSelectable>());
            var expected = new DuplicantTransferExpectation { OwnerId = source.ownerId, PlanetId = source.planetId,
                NativeWorldId = source.nativeWorldId, MinionId = chosenId, ActiveSavePath = source.savePath };
            var before = CaptureNativePreflightFingerprint();
            var result = NativeDuplicantTransferPreflight.Inspect(expected);
            var readOnlyDiagnostics = NativeDuplicantReadOnlyDiagnostics.Capture(source.originalStarterIds, chosenId, archiveManifestPath);
            var after = CaptureNativePreflightFingerprint();
            bool unchanged = JsonConvert.SerializeObject(before) == JsonConvert.SerializeObject(after) && HashNativePreflightFile(source.savePath) == source.saveSha256;
            WriteNativePreflightJson("native-transfer-preflight.json", new {
                preflightExecuted = true, preflightCheckPassed = unchanged, sourceEligible = result.SourceEligible,
                importReady = result.ImportReady, migrationAcceptancePassed = false, nativeFingerprintUnchanged = unchanged,
                expected, result, readOnlyDiagnostics, diagnosticsChangeEligibility = false, before, after, source,
                scope = "Read-only source inspection of an original existing starter; eligibility refusal is evidence, not a failed probe or permission to repair. No migration, spawn, load, destroy, chore cancellation or assignment change." });
            if (!unchanged) throw new InvalidOperationException("Native transfer preflight changed its native fingerprint or source save bytes; inspect the preserved receipt.");
        }

        private void InspectNativeWorldCapacityPreflight(OnlineRuntime runtime, string coordinates)
        {
            var source = ReadNativePreflightSource(runtime);
            var sourceWorld = ClusterManager.Instance == null ? null : ClusterManager.Instance.GetWorld(source.nativeWorldId);
            if (sourceWorld == null || sourceWorld.IsModuleInterior || sourceWorld.WorldSize.x < 1 || sourceWorld.WorldSize.y < 1)
                throw new InvalidOperationException("Capacity inspection requires the exact saved source's existing native asteroid dimensions.");
            int candidateWidth = sourceWorld.WorldSize.x, candidateHeight = sourceWorld.WorldSize.y;
            var grid = ClusterGrid.Instance;
            if (grid == null || grid.cellContents == null) throw new InvalidOperationException("The existing native cluster grid is required for capacity inspection.");
            AxialI hex;
            string selection;
            if (!string.IsNullOrEmpty(coordinates))
            {
                var parts = coordinates.Split(',');
                int q, r;
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out q) ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out r))
                    throw new InvalidOperationException("Native capacity coordinates must be native axial q,r integers, not a room-map slot.");
                hex = new AxialI(q, r); selection = "explicit_native_axial_coordinates";
            }
            else
            {
                var empty = grid.cellContents.Where(pair => pair.Value != null && pair.Value.Count == 0 && grid.IsValidCell(pair.Key))
                    .Select(pair => pair.Key).OrderBy(cell => cell.q).ThenBy(cell => cell.r).ToArray();
                if (empty.Length == 0) throw new InvalidOperationException("No proven empty valid native hex is available; no logical-room slot or hidden-entity lookup was substituted.");
                hex = empty[0]; selection = "existing_valid_empty_native_cellContents_bucket";
            }
            var before = CaptureNativePreflightFingerprint();
            var report = new NativeWorldAdmissionPreflight().Inspect(candidateWidth, candidateHeight, hex.q, hex.r);
            var after = CaptureNativePreflightFingerprint();
            bool unchanged = JsonConvert.SerializeObject(before) == JsonConvert.SerializeObject(after) && HashNativePreflightFile(source.savePath) == source.saveSha256;
            WriteNativePreflightJson("native-world-capacity-preflight.json", new {
                preflightExecuted = true, preflightCheckPassed = unchanged, nativeFingerprintUnchanged = unchanged,
                capacityAvailable = report.CapacityAvailable, importReady = report.ImportReady, migrationAcceptancePassed = false,
                nativeHex = new { q = hex.q, r = hex.r, selection }, candidateWidth, candidateHeight,
                candidateDimensionsSource = "exact_saved_source_native_world", sourceNativeWorldId = source.nativeWorldId,
                report, before, after, source,
                scope = "Read-only native host capacity and existing native hex inspection. No GetFreeGridSpace, grid allocation, world loading, entity spawning or transfer activation." });
            if (!unchanged) throw new InvalidOperationException("Native world capacity preflight changed its native fingerprint or source save bytes; inspect the preserved receipt.");
        }

        private static object CaptureNativePreflightFingerprint()
        {
            var dupes = Components.LiveMinionIdentities.Items.Where(dupe => dupe != null).OrderBy(dupe => dupe.GetComponent<KPrefabID>().InstanceID)
                .Select(dupe => {
                    var modifiers = dupe.GetComponent<MinionModifiers>();
                    var amounts = modifiers == null ? null : modifiers.amounts;
                    if (amounts == null) throw new InvalidOperationException("Native fingerprint requires existing starter amounts.");
                    return new { id = dupe.GetComponent<KPrefabID>().InstanceID, cell = Grid.PosToCell(dupe),
                        amounts = amounts.ModifierList.Where(amount => amount != null && amount.amount != null)
                            .Select(amount => amount.amount.Id + "=" + amount.value.ToString("R", CultureInfo.InvariantCulture)).OrderBy(value => value, StringComparer.Ordinal).ToArray() };
                }).ToArray();
            var savedRef = AccessTools.Field(typeof(Assignable), "assignee_identityRef");
            var savedId = AccessTools.Field(typeof(Ref<KMonoBehaviour>), "id");
            var cached = AccessTools.Field(typeof(Ref<KMonoBehaviour>), "obj");
            var group = AccessTools.Field(typeof(Assignable), "assignee_groupID");
            if (savedRef == null || savedId == null || cached == null || group == null)
                throw new InvalidOperationException("Native fingerprint assignment field contract is missing.");
            var assignments = UnityEngine.Object.FindObjectsByType<Assignable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item != null).OrderBy(item => item.GetInstanceID()).Select(item => {
                    object reference = savedRef.GetValue(item);
                    var cachedOwner = reference == null ? null : cached.GetValue(reference) as KMonoBehaviour;
                    return new { instance = item.GetInstanceID(), type = item.GetType().FullName, slot = item.slotID,
                        savedOwnerId = reference == null ? -1 : (int)savedId.GetValue(reference),
                        cachedOwnerInstance = cachedOwner == null ? 0 : cachedOwner.GetInstanceID(), group = group.GetValue(item) as string };
                }).ToArray();
            var assignmentGroups = Game.Instance.assignmentManager.assignment_groups.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { key = pair.Key, members = pair.Value.GetMembers().Select(member => {
                    var component = member as KMonoBehaviour;
                    return member == null ? "null" : member.GetType().FullName + ":" +
                        (component == null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(member) : component.GetInstanceID());
                }).OrderBy(member => member, StringComparer.Ordinal).ToArray() }).ToArray();
            string gridHash;
            using (var sha = SHA256.Create())
            {
                using (var crypto = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
                {
                    using (var writer = new BinaryWriter(crypto, Encoding.UTF8, true))
                    {
                        writer.Write(Grid.WidthInCells); writer.Write(Grid.HeightInCells);
                        for (int cell = 0; cell < Grid.CellCount; cell++)
                        {
                            writer.Write(Grid.WorldIdx[cell]); writer.Write(Grid.Visible[cell]); writer.Write(Grid.Solid[cell]);
                            writer.Write(Grid.Mass[cell]); writer.Write(Grid.Temperature[cell]);
                            writer.Write(Grid.Element[cell] == null ? 0 : (int)Grid.Element[cell].id);
                        }
                    }
                    crypto.FlushFinalBlock();
                    gridHash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                }
            }
            var selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
            return new { time = GameClock.Instance.GetTime(), paused = SpeedControlScreen.Instance.IsPaused,
                activeSave = SaveLoader.GetActiveSaveFilePath(), selection = selected == null ? 0 : selected.GetInstanceID(), dupes, assignments, assignmentGroups,
                grid = new { width = Grid.WidthInCells, height = Grid.HeightInCells, cells = Grid.CellCount, sha256 = gridHash } };
        }

        private void WriteNativePreflightJson(string name, object value)
        {
            string path = Path.Combine(output, name);
            File.WriteAllText(path + ".tmp", JsonConvert.SerializeObject(value, Formatting.Indented));
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
        }

        private sealed class NativeChatUiReceipt
        {
            public string phase, failure;
            public bool passed, anchorPreserved, latestVisible, notForcedToBottom, controlsInsidePanel, richTextDisabled, paused, gameTimeUnchanged;
            public int initialMessages, finalMessages, connectedPlayers, anchorInstanceId, framesSettled, renderedBodies;
            public long anchorSequence, initialLastSequence, appendedSequence;
            public string anchorText;
            public float anchorViewportY, observedViewportY, anchorError, anchorWorldX, anchorWorldY,
                observedWorldX, observedWorldY, initialBottomDistance, readingBottomDistance, latestBottomDistance;
            public double gameTime;
            public string scope = "Real Steam host RoomSession messages, Unity ScrollRect scroll handler, retained native layout and Button pointer-click handler. No physical mouse or complete native guest acceptance.";
        }

        private NativeChatUiReceipt nativeChatUi;
        private int chatUiAfterFrame;
        private float chatUiDeadline;
        private RectTransform chatUiAnchor;

        private static void RequireNativeChatUi(OnlineRuntime runtime)
        {
            if (Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() ||
                SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused ||
                Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
                runtime.Session == null || !runtime.Session.Joined || runtime.Session.Players.Length != 4 ||
                runtime.Session.Players.Any(player => !player.Connected))
                throw new InvalidOperationException("Chat UI acceptance requires a paused graphical test colony and four connected real RoomSession players.");
            if (UnityEngine.Object.FindObjectsByType<ConfirmDialogScreen>(FindObjectsSortMode.None)
                .Any(dialog => dialog != null && dialog.gameObject.activeInHierarchy))
                throw new InvalidOperationException("A native confirmation dialog blocks chat UI acceptance; no modal was dismissed.");
        }

        private void OpenNativeChatUi(OnlineRuntime runtime)
        {
            if (!testColony) throw new InvalidOperationException("Chat UI acceptance requires this probe's generated disposable colony.");
            RequireNativeChatUi(runtime);
            if (nativeChatUi != null) throw new InvalidOperationException("Chat UI acceptance already started; a failed run cannot be reused as a pass.");
            runtime.ShowWindow();
            nativeChatUi = new NativeChatUiReceipt { phase = "waiting_initial_layout", gameTime = GameClock.Instance.GetTime(),
                initialMessages = runtime.Session.Chat.Length, initialLastSequence = runtime.Session.Chat.Last().Sequence };
            chatUiAfterFrame = Time.frameCount + 4;
            chatUiDeadline = Time.realtimeSinceStartup + 40;
            WriteNativeChatUi();
        }

        private static OnlineWindow NativeChatWindow()
        {
            var window = UnityEngine.Object.FindFirstObjectByType<OnlineWindow>();
            if (window == null || !window.gameObject.activeInHierarchy)
                throw new InvalidOperationException("The real chat window is not visible.");
            return window;
        }

        private static ScrollRect NativeChatScroll(OnlineWindow window)
        {
            return window.GetComponentsInChildren<ScrollRect>(true).Single(scroll => scroll.name == "ChatTranscript");
        }

        private static float NativeChatBottomDistance(ScrollRect scroll)
        {
            return Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height) * Mathf.Clamp01(scroll.verticalNormalizedPosition);
        }

        private static float NativeChatViewportTop(ScrollRect scroll, RectTransform row)
        {
            return scroll.viewport.InverseTransformPoint(row.TransformPoint(new Vector3(0, row.rect.yMax, 0))).y;
        }

        private static PointerEventData NativeChatPointer(RectTransform rect)
        {
            return new PointerEventData(UnityEngine.EventSystems.EventSystem.current) {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
        }

        private void FollowNativeChatUi(OnlineRuntime runtime)
        {
            RequireNativeChatUi(runtime);
            if (nativeChatUi == null || nativeChatUi.phase != "anchor_preserved")
                throw new InvalidOperationException("Verify the retained reading anchor before clicking latest messages.");
            var latest = NativeChatWindow().GetComponentsInChildren<Button>(true).Single(button => button.name == "ChatLatestMessages");
            if (!latest.gameObject.activeInHierarchy || !latest.IsInteractable())
                throw new InvalidOperationException("The real latest-messages button is not visible and interactable.");
            ExecuteEvents.Execute(latest.gameObject, NativeChatPointer((RectTransform)latest.transform), ExecuteEvents.pointerClickHandler);
            nativeChatUi.phase = "waiting_latest_layout";
            chatUiAfterFrame = Time.frameCount + 4;
            chatUiDeadline = Time.realtimeSinceStartup + 20;
            WriteNativeChatUi();
        }

        private void AdvanceNativeChatUi(OnlineRuntime runtime)
        {
            if (nativeChatUi == null || nativeChatUi.phase == "complete" || nativeChatUi.phase == "failed" ||
                nativeChatUi.phase == "anchor_preserved") return;
            try
            {
                RequireNativeChatUi(runtime);
                if (Time.realtimeSinceStartup > chatUiDeadline) throw new InvalidOperationException("Chat UI layout/message phase timed out: " + nativeChatUi.phase);
                if (Time.frameCount < chatUiAfterFrame) return;
                var window = NativeChatWindow();
                var scroll = NativeChatScroll(window);
                Canvas.ForceUpdateCanvases();
                if (nativeChatUi.phase == "waiting_initial_layout")
                {
                    nativeChatUi.initialBottomDistance = NativeChatBottomDistance(scroll);
                    if (scroll.content.rect.height - scroll.viewport.rect.height < 300 || nativeChatUi.initialBottomDistance > 12)
                        throw new InvalidOperationException("Real long-message log must overflow by at least 300 logical pixels and initially follow latest.");
                    var pointer = NativeChatPointer(scroll.viewport); pointer.scrollDelta = new Vector2(0, 8);
                    ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler);
                    scroll.StopMovement();
                    nativeChatUi.phase = "waiting_scroll_layout"; chatUiAfterFrame = Time.frameCount + 4;
                }
                else if (nativeChatUi.phase == "waiting_scroll_layout")
                {
                    var rows = scroll.content.Cast<Transform>().Where(row => row.name == "ChatMessage" && row.gameObject.activeInHierarchy)
                        .Select(row => (RectTransform)row).ToArray();
                    var lines = runtime.Session.Chat;
                    if (rows.Length != lines.Length) throw new InvalidOperationException("Rendered chat rows do not match actual RoomSession history.");
                    int index = Array.FindIndex(rows, row => {
                        float top = NativeChatViewportTop(scroll, row);
                        float bottom = scroll.viewport.InverseTransformPoint(row.TransformPoint(new Vector3(0, row.rect.yMin, 0))).y;
                        return bottom <= scroll.viewport.rect.yMax && top >= scroll.viewport.rect.yMin;
                    });
                    if (index < 0 || NativeChatBottomDistance(scroll) < 100)
                        throw new InvalidOperationException("The actual scroll handler did not establish an older visible reading position.");
                    chatUiAnchor = rows[index];
                    var body = chatUiAnchor.GetComponentsInChildren<TextMeshProUGUI>(true).Single(text => text.name == "ChatMessageBody");
                    if (body.text != lines[index].Text) throw new InvalidOperationException("Reading anchor is not the corresponding real chat message.");
                    Vector3 world = chatUiAnchor.TransformPoint(new Vector3(0, chatUiAnchor.rect.yMax, 0));
                    nativeChatUi.anchorSequence = lines[index].Sequence; nativeChatUi.anchorText = lines[index].Text;
                    nativeChatUi.anchorInstanceId = chatUiAnchor.GetInstanceID();
                    nativeChatUi.anchorWorldX = world.x; nativeChatUi.anchorWorldY = world.y;
                    nativeChatUi.anchorViewportY = NativeChatViewportTop(scroll, chatUiAnchor);
                    nativeChatUi.readingBottomDistance = NativeChatBottomDistance(scroll);
                    nativeChatUi.phase = "anchored_waiting_message";
                }
                else if (nativeChatUi.phase == "anchored_waiting_message")
                {
                    var last = runtime.Session.Chat.Last();
                    if (last.Sequence <= nativeChatUi.initialLastSequence) return;
                    nativeChatUi.appendedSequence = last.Sequence;
                    nativeChatUi.phase = "waiting_appended_layout"; chatUiAfterFrame = Time.frameCount + 4;
                }
                else if (nativeChatUi.phase == "waiting_appended_layout")
                {
                    if (chatUiAnchor == null || chatUiAnchor.GetInstanceID() != nativeChatUi.anchorInstanceId)
                        throw new InvalidOperationException("The actual reading-anchor row was destroyed or replaced.");
                    Vector3 world = chatUiAnchor.TransformPoint(new Vector3(0, chatUiAnchor.rect.yMax, 0));
                    nativeChatUi.observedWorldX = world.x; nativeChatUi.observedWorldY = world.y;
                    nativeChatUi.observedViewportY = NativeChatViewportTop(scroll, chatUiAnchor);
                    nativeChatUi.anchorError = Mathf.Abs(nativeChatUi.anchorViewportY - nativeChatUi.observedViewportY);
                    nativeChatUi.anchorPreserved = nativeChatUi.anchorError <= 2;
                    nativeChatUi.notForcedToBottom = NativeChatBottomDistance(scroll) > 12;
                    var latest = window.GetComponentsInChildren<Button>(true).Single(button => button.name == "ChatLatestMessages");
                    nativeChatUi.latestVisible = latest.gameObject.activeInHierarchy && latest.IsInteractable();
                    if (!nativeChatUi.anchorPreserved || !nativeChatUi.notForcedToBottom || !nativeChatUi.latestVisible)
                        throw new InvalidOperationException("Actual appended chat layout lost the reading anchor or unread/latest indicator.");
                    nativeChatUi.phase = "anchor_preserved";
                }
                else if (nativeChatUi.phase == "waiting_latest_layout")
                {
                    nativeChatUi.latestBottomDistance = NativeChatBottomDistance(scroll);
                    var input = window.GetComponentsInChildren<TMP_InputField>(true).Single(field => field.name == "ChatMessageInput");
                    var send = window.GetComponentsInChildren<Button>(true).Single(button => button.name == "SendChatMessage");
                    var leave = window.GetComponentsInChildren<Button>(true).Single(button => button.name == "LeaveSystem");
                    var panel = (RectTransform)window.transform;
                    nativeChatUi.controlsInsidePanel = NativeChatRectVisible(panel, (RectTransform)input.transform) &&
                        NativeChatRectVisible(panel, (RectTransform)send.transform) && NativeChatRectVisible(panel, (RectTransform)leave.transform) &&
                        input.IsInteractable() && send.IsInteractable() && leave.IsInteractable();
                    var bodies = scroll.content.GetComponentsInChildren<TextMeshProUGUI>(false).Where(text => text.name == "ChatMessageBody").ToArray();
                    nativeChatUi.renderedBodies = bodies.Length;
                    nativeChatUi.richTextDisabled = bodies.Length == runtime.Session.Chat.Length && bodies.All(text => !text.richText) &&
                        scroll.content.GetComponentsInChildren<TextMeshProUGUI>(false).All(text => !text.richText) &&
                        input.textComponent != null && !input.textComponent.richText;
                    nativeChatUi.paused = SpeedControlScreen.Instance.IsPaused;
                    nativeChatUi.gameTimeUnchanged = Math.Abs(GameClock.Instance.GetTime() - nativeChatUi.gameTime) < .000001;
                    nativeChatUi.finalMessages = runtime.Session.Chat.Length;
                    nativeChatUi.connectedPlayers = runtime.Session.Players.Count(player => player.Connected);
                    nativeChatUi.framesSettled = 4;
                    nativeChatUi.passed = nativeChatUi.latestBottomDistance <= 12 && nativeChatUi.controlsInsidePanel &&
                        nativeChatUi.richTextDisabled && nativeChatUi.anchorPreserved && nativeChatUi.latestVisible &&
                        nativeChatUi.notForcedToBottom && nativeChatUi.gameTimeUnchanged && nativeChatUi.paused;
                    if (!nativeChatUi.passed) throw new InvalidOperationException("Actual latest-chat layout/control containment or literal-text check failed.");
                    nativeChatUi.phase = "complete";
                }
                WriteNativeChatUi();
            }
            catch (Exception error)
            {
                nativeChatUi.phase = "failed"; nativeChatUi.failure = error.Message; nativeChatUi.passed = false;
                WriteNativeChatUi(); throw;
            }
        }

        private static bool NativeChatRectVisible(RectTransform panel, RectTransform control)
        {
            if (!control.gameObject.activeInHierarchy || control.rect.width <= 0 || control.rect.height <= 0) return false;
            var corners = new Vector3[4]; control.GetWorldCorners(corners);
            return corners.All(corner => {
                Vector3 local = panel.InverseTransformPoint(corner);
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corner);
                return local.x >= panel.rect.xMin - 1 && local.x <= panel.rect.xMax + 1 &&
                    local.y >= panel.rect.yMin - 1 && local.y <= panel.rect.yMax + 1 &&
                    screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
            });
        }

        private void WriteNativeChatUi()
        {
            string path = Path.Combine(output, "chat-ui-receipt.json");
            File.WriteAllText(path + ".tmp", JsonConvert.SerializeObject(nativeChatUi, Formatting.Indented));
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
        }

        private void OpenNativeTaskUi(OnlineRuntime runtime)
        {
            if (!testColony || nativeRemoteWork == null || Game.Instance == null || !Game.Instance.baseAlreadyCreated ||
                !SpeedControlScreen.Instance.IsPaused || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Task UI acceptance requires this paused, graphical native-work test colony.");
            runtime.HideWindow();
            runtime.Multiplayer.SetSharing(true);
            runtime.Multiplayer.Show(runtime.Session.LocalPlayerId);
            taskUiDeadline = Time.realtimeSinceStartup + 30;
            taskUiWaiting = true;
        }

        private void AdvanceNativeTaskUi(OnlineRuntime runtime)
        {
            if (archiveStarterIds == null && testColony && Game.Instance != null && Game.Instance.baseAlreadyCreated &&
                !Game.Instance.IsLoading() && Components.LiveMinionIdentities.Items.Count == 3)
                archiveStarterIds = Components.LiveMinionIdentities.Items.Select(d => d.GetComponent<KPrefabID>().InstanceID).ToArray();
            if (!taskUiWaiting) return;
            var window = UnityEngine.Object.FindFirstObjectByType<WorldOverviewWindow>();
            if (window == null || window.MapInputObject == null || !window.MapInputObject.GetComponent<RawImage>().enabled)
            {
                if (Time.realtimeSinceStartup >= taskUiDeadline)
                    throw new InvalidOperationException("Native world map did not become ready for task UI acceptance.");
                return;
            }
            taskUiWaiting = false;
            if (UnityEngine.Object.FindObjectsByType<ConfirmDialogScreen>(FindObjectsSortMode.None)
                .Any(dialog => dialog != null && dialog.gameObject.activeInHierarchy))
                throw new InvalidOperationException("A native confirmation dialog blocks real task-map input; dismiss it through the normal Steam restart flow before UI acceptance.");
            Canvas.ForceUpdateCanvases();
            var input = window.MapInputObject;
            var point = new WorkCell(nativeRemoteWork.managedX, nativeRemoteWork.managedY);
            var ordinary = new WorkCell(nativeRemoteWork.ordinaryX, nativeRemoteWork.ordinaryY);
            var defaultRegion = window.MapRegion;
            Vector2 defaultCellPixels = NativeTaskCellPixels(window);
            bool defaultReadable = defaultRegion.Width <= 64 && defaultRegion.Height <= 64 &&
                defaultCellPixels.x >= 2 && defaultCellPixels.y >= 2;
            int navigationEvents = 0;
            FitNativeTaskCells(window, new[] { point, ordinary }, ref navigationEvents);
            Func<WorkCell, Vector2> screen = cell => NativeTaskCellScreen(window, cell);
            Func<WorkCell, PointerEventData> pointer = cell => new PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = PointerEventData.InputButton.Left, position = screen(cell) };
            Action<string> click = name => window.GetComponentsInChildren<Button>(true).Single(b => b.name == name).onClick.Invoke();
            System.Action refresh = () => AccessTools.Method(typeof(WorldOverviewWindow), "RefreshTaskOverlay").Invoke(window, null);
            long authorityRevisionBefore = runtime.Multiplayer.Work.Authority.ConfirmedRevision;
            var selected = pointer(point);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerClickHandler);
            refresh();
            bool singleSelected = NativeTaskSelectsCell(window, point);
            var beforeZoom = window.MapRegion;
            ClickNativeTaskNavigation(window, "+"); navigationEvents++;
            Canvas.ForceUpdateCanvases();
            FitNativeTaskCells(window, new[] { point }, ref navigationEvents);
            CenterNativeTaskCell(window, point, ref navigationEvents);
            var afterZoom = window.MapRegion;
            selected = pointer(point);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerClickHandler);
            refresh();
            bool zoomSelectionConsistent = afterZoom.Width < beforeZoom.Width && NativeTaskSelectsCell(window, point);
            var beforePan = window.MapRegion;
            string panButton = beforePan.X + beforePan.Width < window.MapWorldWidth ? "→" : "←";
            ClickNativeTaskNavigation(window, panButton); navigationEvents++;
            Canvas.ForceUpdateCanvases();
            var afterPan = window.MapRegion;
            selected = pointer(point);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerClickHandler);
            refresh();
            bool panSelectionConsistent = (afterPan.X != beforePan.X || afterPan.Y != beforePan.Y) && NativeTaskSelectsCell(window, point);
            FitNativeTaskCells(window, new[] { point, ordinary }, ref navigationEvents);
            selected = pointer(point);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerClickHandler);
            refresh();
            ExecuteEvents.Execute(input, pointer(ordinary), ExecuteEvents.pointerMoveHandler);
            bool hoverPreserved = window.TaskSelectionStart?.X == point.X && window.TaskSelectionStart?.Y == point.Y;
            int priorPriority = window.TaskPriority;
            click(priorPriority < 9 ? "TaskPriorityIncrease" : "TaskPriorityDecrease"); refresh();
            bool priorityChanged = window.TaskPriority == priorPriority + (priorPriority < 9 ? 1 : -1);
            WorkCell farCell = FindNativeTaskLargeDragEnd(window, point);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.beginDragHandler);
            var far = pointer(farCell); far.dragging = true;
            ExecuteEvents.Execute(input, far, ExecuteEvents.dragHandler);
            // Unity suppresses the click after a drag; this handler also defends that sequence.
            ExecuteEvents.Execute(input, far, ExecuteEvents.pointerClickHandler);
            refresh();
            bool largeRejected = window.TaskSelectionCount == 0 &&
                !window.GetComponentsInChildren<Button>(true).Single(b => b.name == "TaskSubmitDig").interactable;
            bool dragPreserved = window.TaskSelectionStart?.X == point.X && window.TaskSelectionStart?.Y == point.Y &&
                window.TaskSelectionEnd?.X == farCell.X && window.TaskSelectionEnd?.Y == farCell.Y;
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(input, selected, ExecuteEvents.pointerClickHandler);
            click("TaskLayerMine"); refresh(); bool mine = window.TaskLayer == 0;
            click("TaskLayerMember"); refresh(); bool member = window.TaskLayer == 1;
            click("TaskLayerMerged"); refresh(); bool merged = window.TaskLayer == 2;
            int marks = window.VisibleTaskMarkCount;
            bool stayedOpen = window.gameObject.activeInHierarchy && window.MapInputObject.GetComponent<RawImage>().enabled;
            long authorityRevisionAfter = runtime.Multiplayer.Work.Authority.ConfirmedRevision;
            bool noTaskMutation = authorityRevisionBefore == authorityRevisionAfter;
            var result = new { passed = defaultReadable && zoomSelectionConsistent && panSelectionConsistent && noTaskMutation &&
                    singleSelected && hoverPreserved && priorityChanged && largeRejected && dragPreserved && mine && member && merged && marks > 0 && stayedOpen,
                singleSelected, hoverPreserved, priorityChanged, largeRejected, dragPreserved, mine, member, merged,
                defaultReadable, minimumCellPixels = 2, defaultCellPixelsX = defaultCellPixels.x, defaultCellPixelsY = defaultCellPixels.y,
                defaultRegion, beforeZoom, afterZoom, beforePan, afterPan, finalRegion = window.MapRegion,
                zoomSelectionConsistent, panSelectionConsistent, navigationEvents,
                selectedCell = new { x = point.X, y = point.Y }, dragEndCell = new { x = farCell.X, y = farCell.Y },
                authorityRevisionBefore, authorityRevisionAfter,
                canonicalMarks = marks, stayedOpen, nativeTaskMutationIssued = !noTaskMutation,
                paused = SpeedControlScreen.Instance.IsPaused, screenWidth = Screen.width, screenHeight = Screen.height,
                scope = "Real Unity map input handlers and native colony texture; no remote full-game client or fabricated pending frame." };
            string receiptPath = Path.Combine(output, "task-ui-receipt.json");
            File.WriteAllText(receiptPath + ".tmp", JsonConvert.SerializeObject(result, Formatting.Indented));
            if (File.Exists(receiptPath)) File.Replace(receiptPath + ".tmp", receiptPath, null);
            else File.Move(receiptPath + ".tmp", receiptPath);
            if (!result.passed) throw new InvalidOperationException("Task UI event acceptance failed; inspect task-ui-receipt.json.");
        }

        private static Vector2 NativeTaskCellScreen(WorldOverviewWindow window, WorkCell cell)
        {
            Vector2 position;
            if (window.TryMapCellScreenPosition(cell, out position)) return position;
            var region = window.MapRegion;
            throw new InvalidOperationException("Task UI cannot target explored cell " + cell.X + "," + cell.Y + " in current ROI " +
                region.X + "," + region.Y + " " + region.Width + "x" + region.Height + "; no screen position was fabricated.");
        }

        private static Vector2 NativeTaskMapPixels(WorldOverviewWindow window)
        {
            var rect = (RectTransform)window.MapInputObject.transform;
            Vector2 minimum = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.min));
            Vector2 maximum = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.max));
            return new Vector2(Mathf.Abs(maximum.x - minimum.x), Mathf.Abs(maximum.y - minimum.y));
        }

        private static Vector2 NativeTaskCellPixels(WorldOverviewWindow window)
        {
            var region = window.MapRegion;
            if (region.Width < 1 || region.Height < 1) throw new InvalidOperationException("Task UI viewport has no valid ROI.");
            Vector2 pixels = NativeTaskMapPixels(window);
            return new Vector2(pixels.x / region.Width, pixels.y / region.Height);
        }

        private static bool NativeTaskSelectsCell(WorldOverviewWindow window, WorkCell cell)
        {
            return window.TaskSelectionCount == 1 && window.TaskSelectionStart?.X == cell.X && window.TaskSelectionStart?.Y == cell.Y &&
                window.TaskSelectionEnd?.X == cell.X && window.TaskSelectionEnd?.Y == cell.Y;
        }

        private static void ClickNativeTaskNavigation(WorldOverviewWindow window, string label)
        {
            var candidates = window.GetComponentsInChildren<Button>(true).Where(button => button != null && button.name == "Button" &&
                button.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Any(text => text != null && text.text == label)).ToArray();
            if (candidates.Length != 1 || !candidates[0].interactable)
                throw new InvalidOperationException("Expected one interactable native map navigation button: " + label);
            var rect = (RectTransform)candidates[0].transform;
            var pointer = new PointerEventData(UnityEngine.EventSystems.EventSystem.current) { button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            ExecuteEvents.Execute(candidates[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        private static void PanNativeTaskViewport(WorldOverviewWindow window, int x, int y)
        {
            if (x == 0 && y == 0) return;
            Vector2 pixels = NativeTaskCellPixels(window);
            // Drive the public right-drag handler in screen pixels. Fractional padding
            // avoids losing a requested cell to float truncation in the real pan code.
            Vector2 delta = new Vector2(-(x + Math.Sign(x) * .25f) * pixels.x, -(y + Math.Sign(y) * .25f) * pixels.y);
            var rect = (RectTransform)window.MapInputObject.transform;
            Vector2 start = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = PointerEventData.InputButton.Right, pressPosition = start, position = start + delta, delta = delta, dragging = true };
            ExecuteEvents.Execute(window.MapInputObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(window.MapInputObject, pointer, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(window.MapInputObject, pointer, ExecuteEvents.dragHandler);
            Canvas.ForceUpdateCanvases();
        }

        private static void CenterNativeTaskCell(WorldOverviewWindow window, WorkCell cell, ref int navigationEvents)
        {
            var region = window.MapRegion;
            int x = Mathf.Clamp(cell.X - region.Width / 2, 0, window.MapWorldWidth - region.Width);
            int y = Mathf.Clamp(cell.Y - region.Height / 2, 0, window.MapWorldHeight - region.Height);
            if (x != region.X || y != region.Y)
            { PanNativeTaskViewport(window, x - region.X, y - region.Y); navigationEvents++; }
            NativeTaskCellScreen(window, cell);
        }

        private static void FitNativeTaskCells(WorldOverviewWindow window, WorkCell[] cells, ref int navigationEvents)
        {
            int minimumX = cells.Min(cell => cell.X), maximumX = cells.Max(cell => cell.X);
            int minimumY = cells.Min(cell => cell.Y), maximumY = cells.Max(cell => cell.Y);
            if (minimumX < 0 || minimumY < 0 || maximumX >= window.MapWorldWidth || maximumY >= window.MapWorldHeight)
                throw new InvalidOperationException("Task UI fixture cells exceed the displayed planet bounds.");
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var region = window.MapRegion;
                if (maximumX - minimumX + 3 > region.Width || maximumY - minimumY + 3 > region.Height)
                {
                    ClickNativeTaskNavigation(window, "−"); navigationEvents++; Canvas.ForceUpdateCanvases();
                    var changed = window.MapRegion;
                    if (changed.Width == region.Width && changed.Height == region.Height)
                        throw new InvalidOperationException("Map zoom limit prevents both native fixture cells fitting in one visible ROI.");
                    continue;
                }
                int x = Mathf.Clamp((minimumX + maximumX + 1 - region.Width) / 2, 0, window.MapWorldWidth - region.Width);
                int y = Mathf.Clamp((minimumY + maximumY + 1 - region.Height) / 2, 0, window.MapWorldHeight - region.Height);
                if (x != region.X || y != region.Y)
                { PanNativeTaskViewport(window, x - region.X, y - region.Y); navigationEvents++; }
                foreach (WorkCell cell in cells) NativeTaskCellScreen(window, cell);
                return;
            }
            throw new InvalidOperationException("Native map navigation could not expose the fixture cells within twelve bounded UI actions.");
        }

        private static WorkCell FindNativeTaskLargeDragEnd(WorldOverviewWindow window, WorkCell start)
        {
            foreach (int x in new[] { 8, -8, 7, -7, 9, -9 })
                foreach (int y in new[] { 8, -8, 7, -7 })
                {
                    if ((Math.Abs(x) + 1) * (Math.Abs(y) + 1) <= RemoteWorkWire.MaxCells) continue;
                    var end = new WorkCell(start.X + x, start.Y + y);
                    Vector2 screen;
                    if (window.TryMapCellScreenPosition(end, out screen)) return end;
                }
            throw new InvalidOperationException("No explored >64-cell drag endpoint is visible in the actual ROI; the UI limit test cannot be claimed.");
        }

        private void CaptureNativeStarterArchive(OnlineRuntime runtime)
        {
            if (!testColony || Game.Instance == null || !Game.Instance.baseAlreadyCreated || !SpeedControlScreen.Instance.IsPaused ||
                (networkWorkActive && !nativeRemoteWork.completionFinished))
                throw new InvalidOperationException("Only this paused, completed disposable test colony may be exported.");
            var starters = Components.LiveMinionIdentities.Items.Where(d => d != null).ToArray();
            if (starters.Length != 3) throw new InvalidOperationException("Archive fixture requires the three original native starters.");
            int[] identities = starters.Select(d => d.GetComponent<KPrefabID>().InstanceID).ToArray();
            if (archiveStarterIds == null || !archiveStarterIds.OrderBy(i => i).SequenceEqual(identities.OrderBy(i => i)))
                throw new InvalidOperationException("Archive starter identities changed.");
            string directory = Path.Combine(SaveLoader.GetSavePrefixAndCreateFolder(), "OniOnlineHarness-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(directory);
            string checkpoint = Path.Combine(directory, "archive-source-" + sequence + ".sav");
            if (File.Exists(checkpoint)) throw new IOException("Archive source checkpoint must be new.");
            string previousActiveSave = SaveLoader.GetActiveSaveFilePath();
            string previousActiveHash = !string.IsNullOrEmpty(previousActiveSave) && File.Exists(previousActiveSave)
                ? HashNativePreflightFile(previousActiveSave) : null;
            string saved = SaveLoader.Instance.Save(checkpoint, false, true);
            if (saved != checkpoint || !File.Exists(saved)) throw new IOException("Archive source save failed.");
            // The archive uses this new checkpoint. Keep the subsequent reload/lifecycle
            // guard tied to it rather than to an older preflight save.
            RecordNativePreflightSource(runtime, saved, previousActiveSave, previousActiveHash);
            var chosen = starters.OrderBy(d => d.GetComponent<KPrefabID>().InstanceID).First();
            SelectTool.Instance.Select(chosen.GetComponent<KSelectable>());
            var archive = NativeDuplicantArchive.CaptureSelected(new NativeDuplicantArchivePermit {
                IsDisposableProbeColony = testColony, OwnedSaveDirectory = directory, ExpectedActiveSave = saved,
                ExpectedOwnerId = runtime.SelectedPlanet.OwnerId, ExpectedPlanetId = runtime.SelectedPlanet.PlanetId,
                ExpectedSelectedInstanceId = chosen.GetComponent<KPrefabID>().InstanceID, OriginalStarterIds = archiveStarterIds });
            string export = Path.Combine(output, "native-dupe-archive-" + sequence);
            if (Directory.Exists(export)) throw new IOException("Archive evidence directory must be fresh.");
            Directory.CreateDirectory(export);
            for (int i = 0; i < archive.NativeRoots.Length; i++) File.WriteAllBytes(Path.Combine(export, "root-" + i + ".bin"), archive.NativeRoots[i]);
            File.WriteAllBytes(Path.Combine(export, "schema.bin"), archive.NativeSchemaDirectory);
            File.WriteAllText(Path.Combine(export, "manifest.json"), JsonConvert.SerializeObject(archive.Manifest, Formatting.Indented));
        }
    }
}
