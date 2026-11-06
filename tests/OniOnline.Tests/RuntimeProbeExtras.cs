// Explicit test-build extensions. These never compile into the ordinary Mod.
using System;
using System.Collections.Generic;
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

        private sealed class NativeBrowserUiReceipt
        {
            public string phase, failure, windowType;
            public bool passed, actualMainMenuHandler, nativeModal, originalControllerRemoved, nativeList,
                noSessionCreated, noLegacyWindow, closedByNativeButton;
            public int openedFrame, settledFrame, remainingWindows;
            public string scope = "One real graphical main menu; programmatic native KButton handlers after topmost-raycast checks. Browser opening/closing only, no remote discovery/join or visual-style acceptance.";
        }

        private NativeBrowserUiReceipt nativeBrowserUi;
        private int nativeBrowserCloseFrame;

        private void OpenNativeBrowserUi(OnlineRuntime runtime)
        {
            if (nativeBrowserUi != null || MainMenu.Instance == null || !MainMenu.Instance.IsActive() ||
                Game.Instance != null || runtime.Session != null || Application.isBatchMode ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || !KInputManager.isFocused)
                throw new InvalidOperationException("Native browser acceptance requires a fresh focused graphical main menu without a room or colony.");
            var button = MainMenu.Instance.GetComponentsInChildren<KButton>(true)
                .Single(candidate => candidate.name == "OniOnlineBrowseLobbies" && candidate.gameObject.activeInHierarchy);
            ClickNativeChatControl(button);
            var browser = UnityEngine.Object.FindObjectsByType<NativeSystemBrowser>(FindObjectsSortMode.None)
                .Single(candidate => candidate.IsOpen && candidate.gameObject.activeInHierarchy);
            nativeBrowserUi = new NativeBrowserUiReceipt {
                phase = "opened", windowType = browser.GetType().FullName, openedFrame = Time.frameCount,
                actualMainMenuHandler = true, nativeModal = browser.GetType().BaseType == typeof(KModalScreen) && browser.IsModal() && browser.ConsumeMouseScroll,
                originalControllerRemoved = browser.GetComponent<ModsScreen>() == null,
                nativeList = browser.GetComponentsInChildren<ScrollRect>(true).Any(control => control.verticalScrollbar != null),
                noSessionCreated = runtime.Session == null, noLegacyWindow = !NativeChatLobbyVisible()
            };
            WriteNativePreflightJson("browser-ui-receipt.json", nativeBrowserUi);
            if (!nativeBrowserUi.nativeModal || !nativeBrowserUi.originalControllerRemoved || !nativeBrowserUi.nativeList ||
                !nativeBrowserUi.noSessionCreated || !nativeBrowserUi.noLegacyWindow)
                throw new InvalidOperationException("The actual main-menu entry did not open the native browse-only system list.");
        }

        private void CloseNativeBrowserUi(OnlineRuntime runtime)
        {
            if (nativeBrowserUi == null || nativeBrowserUi.phase != "opened" || runtime.Session != null)
                throw new InvalidOperationException("Open the native browse-only page first.");
            var browser = UnityEngine.Object.FindObjectsByType<NativeSystemBrowser>(FindObjectsSortMode.None)
                .Single(candidate => candidate.IsOpen && candidate.gameObject.activeInHierarchy);
            var close = browser.transform.Find("Panel/BrowserActions/CloseButton").GetComponent<KButton>();
            ClickNativeChatControl(close);
            nativeBrowserUi.closedByNativeButton = !browser.IsOpen;
            nativeBrowserUi.phase = "waiting_close";
            nativeBrowserCloseFrame = Time.frameCount + 4;
            WriteNativePreflightJson("browser-ui-receipt.json", nativeBrowserUi);
        }

        private void AdvanceNativeBrowserUi(OnlineRuntime runtime)
        {
            if (nativeBrowserUi == null || nativeBrowserUi.phase != "waiting_close" || Time.frameCount < nativeBrowserCloseFrame) return;
            nativeBrowserUi.remainingWindows = UnityEngine.Object.FindObjectsByType<NativeSystemBrowser>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            nativeBrowserUi.settledFrame = Time.frameCount;
            nativeBrowserUi.passed = nativeBrowserUi.closedByNativeButton && nativeBrowserUi.remainingWindows == 0 &&
                runtime.Session == null && MainMenu.Instance != null && MainMenu.Instance.IsActive();
            nativeBrowserUi.phase = nativeBrowserUi.passed ? "complete" : "failed";
            if (!nativeBrowserUi.passed) nativeBrowserUi.failure = "Browser close leaked a window, created a session or lost the native main menu.";
            WriteNativePreflightJson("browser-ui-receipt.json", nativeBrowserUi);
            if (!nativeBrowserUi.passed) throw new InvalidOperationException(nativeBrowserUi.failure);
        }

        private sealed class NativeChatEntryObservation
        {
            public string stage;
            public int frame, entries, activeEntries, visibleEntries, chatWindows, visibleChatWindows;
            public bool gameplayReady, nativeParent, nativeSize, nativeIcon, interactable, passed;
        }

        private sealed class NativeChatUiReceipt
        {
            public string phase, failure;
            public bool passed, anchorPreserved, latestVisible, latestDismissed, notForcedToBottom,
                controlsInsidePanel, richTextDisabled, rawTextPreserved, nativeLabelsCorrect, paused, gameTimeUnchanged,
                entryOpenedChat, entryClosedChat, draftPreserved, hudSurvivedClose, nativeFrameControllerRemoved,
                attachedToNativeCanvas, nonModal, outsidePanelRaycastClear, noLobbyWindow, endEditDidNotSend,
                appendThroughHostSession;
            public int initialMessages, finalMessages, connectedPlayers, anchorInstanceId, framesSettled, renderedBodies,
                buttonSends, submitSends, nativeFrameSpritesRetained, reopenedFramesSettled, remainingWindowsAfterClose;
            public long anchorSequence, initialLastSequence, appendedSequence;
            public string anchorText;
            public float anchorViewportY, observedViewportY, anchorError, anchorWorldX, anchorWorldY,
                observedWorldX, observedWorldY, initialBottomDistance, readingBottomDistance, latestBottomDistance;
            public double gameTime;
            public NativeChatEntryObservation[] entryObservations;
            public float[] outsideRaycastScreenPosition;
            public string scope = "One graphical Steam host with actual RoomSession history. Programmatic native KToggle/KButton pointer-click handlers after topmost-raycast checks, plus TMP onSubmit/onEndEdit events. Anchor append uses host RoomSession.SendChat without the composer. No physical pointer, IME, visual-style, remote-sender or native guest acceptance.";
        }

        private readonly List<NativeChatEntryObservation> nativeChatEntryObservations = new List<NativeChatEntryObservation>();
        private NativeChatUiReceipt nativeChatUi;
        private int chatUiAfterFrame, nativeChatButtonSends, nativeChatSubmitSends;
        private float chatUiDeadline;
        private RectTransform chatUiAnchor;
        private Canvas chatUiNativeCanvas;
        private ManagementMenu chatUiNativeMenu;
        private double nativeChatOpenGameTime;
        private bool nativeChatEntryOpened, nativeChatEndEditDidNotSend = true;
        private const string NativeChatDraft = "[native-chat-draft] <b>中文</b> /:*?";

        private static void RequireNativeChatUi(OnlineRuntime runtime)
        {
            if (!NativeGameplayUi.Ready || SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused ||
                Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
                runtime.Session == null || !runtime.Session.Joined || !runtime.Session.IsHost || runtime.Session.Players.Length != 1 ||
                runtime.Session.Players.Any(player => !player.Connected) || !KInputManager.isFocused ||
                UnityEngine.EventSystems.EventSystem.current == null || !UnityEngine.EventSystems.EventSystem.current.enabled)
                throw new InvalidOperationException("Chat UI acceptance requires this paused, focused graphical host and one real local host player, without protocol peers.");
            if (UnityEngine.Object.FindObjectsByType<ConfirmDialogScreen>(FindObjectsSortMode.None)
                    .Any(dialog => dialog.gameObject.activeInHierarchy) ||
                UnityEngine.Object.FindObjectsByType<PauseScreen>(FindObjectsSortMode.None)
                    .Any(screen => screen.gameObject.activeInHierarchy))
                throw new InvalidOperationException("A native confirmation or pause menu blocks chat UI acceptance; no modal was dismissed.");
        }

        private static KToggle[] NativeChatEntries()
        {
            return UnityEngine.Object.FindObjectsByType<KToggle>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(toggle => toggle.name == "OniOnlineCommunications").ToArray();
        }

        private static int VisibleNativeChatWindows()
        {
            return UnityEngine.Object.FindObjectsByType<OnlineChatWindow>(FindObjectsSortMode.None)
                .Count(window => window.gameObject.activeInHierarchy && window.IsScreenActive());
        }

        private static int NativeChatWindowCount()
        {
            return UnityEngine.Object.FindObjectsByType<OnlineChatWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        }

        private static bool NativeChatLobbyVisible()
        {
            return UnityEngine.Object.FindObjectsByType<OnlineWindow>(FindObjectsSortMode.None)
                .Any(window => window.gameObject.activeInHierarchy && window.IsScreenActive());
        }

        private void ObserveNativeChatEntry(OnlineRuntime runtime, string expectedStage)
        {
            bool characters = UnityEngine.Object.FindObjectsByType<MinionSelectScreen>(FindObjectsSortMode.None)
                .Any(screen => screen.IsScreenActive());
            bool destination = UnityEngine.Object.FindObjectsByType<ColonyDestinationSelectScreen>(FindObjectsSortMode.None)
                .Any(screen => screen.IsScreenActive());
            bool ready = NativeGameplayUi.Ready;
            string stage = characters ? "characters" : destination ? "destination" : ready ? "gameplay" :
                MainMenu.Instance != null && MainMenu.Instance.IsActive() ? "main_menu" : "loading_or_frontend";
            var entries = NativeChatEntries();
            var source = ManagementMenu.Instance == null ? null : ManagementMenu.Instance.PauseMenuButton;
            var entry = entries.Length == 1 ? entries[0] : null;
            var sourceRect = source == null ? null : source.transform as RectTransform;
            var entryRect = entry == null ? null : entry.transform as RectTransform;
            var observation = new NativeChatEntryObservation {
                stage = stage, frame = Time.frameCount, gameplayReady = ready, entries = entries.Length,
                activeEntries = entries.Count(toggle => toggle.gameObject.activeSelf),
                visibleEntries = entries.Count(toggle => toggle.gameObject.activeInHierarchy),
                chatWindows = NativeChatWindowCount(), visibleChatWindows = VisibleNativeChatWindows(),
                nativeParent = entry != null && source != null && entry.transform.parent == source.transform.parent,
                nativeSize = sourceRect != null && entryRect != null && entryRect.rect.width > 0 && entryRect.rect.height > 0 &&
                    Mathf.Abs(sourceRect.rect.width - entryRect.rect.width) <= 1 && Mathf.Abs(sourceRect.rect.height - entryRect.rect.height) <= 1,
                nativeIcon = entry != null && entry.fgImage != null && entry.fgImage.sprite == Assets.GetSprite("bubble_conversation"),
                interactable = entry != null && entry.IsInteractable()
            };
            observation.passed = stage == expectedStage && (stage == "gameplay" ? ready && entries.Length == 1 &&
                observation.activeEntries == 1 && observation.visibleEntries == 1 && observation.nativeParent &&
                observation.nativeSize && observation.nativeIcon && observation.interactable : !ready &&
                observation.activeEntries == 0 && observation.visibleEntries == 0 && observation.chatWindows == 0 && !runtime.ChatOpen);
            nativeChatEntryObservations.Add(observation);
            WriteNativePreflightJson("chat-entry-receipt.json", new { passed = observation.passed, observation,
                observations = nativeChatEntryObservations.ToArray(),
                scope = "Actual scene KToggle and OnlineChatWindow state, including inactive scene objects. No synthetic visibility or frontend chat opening." });
            if (!observation.passed) throw new InvalidOperationException("Native chat entry observation failed for " + expectedStage + "; actual stage is " + stage + ".");
        }

        private static KToggle NativeChatEntryToggle()
        {
            var entries = NativeChatEntries();
            if (entries.Length != 1 || !entries[0].gameObject.activeInHierarchy || !entries[0].IsInteractable())
                throw new InvalidOperationException("Exactly one visible, interactable native management-toolbar chat entry is required.");
            return entries[0];
        }

        private void OpenNativeChatUiWindow(OnlineRuntime runtime)
        {
            if (!testColony) throw new InvalidOperationException("Chat UI acceptance requires this probe's generated disposable colony.");
            RequireNativeChatUi(runtime);
            if (runtime.ChatOpen || NativeChatWindowCount() != 0 || NativeChatLobbyVisible())
                throw new InvalidOperationException("Close existing chat/lobby windows before testing the one-click native toolbar entry.");
            ObserveNativeChatEntry(runtime, "gameplay");
            chatUiNativeMenu = ManagementMenu.Instance;
            chatUiNativeCanvas = chatUiNativeMenu.GetComponentInParent<Canvas>().rootCanvas;
            nativeChatOpenGameTime = GameClock.Instance.GetTime();
            var entry = NativeChatEntryToggle();
            ClickNativeChatControl(entry);
            nativeChatEntryOpened = runtime.ChatOpen && VisibleNativeChatWindows() == 1 && !NativeChatLobbyVisible() &&
                SpeedControlScreen.Instance.IsPaused && Math.Abs(GameClock.Instance.GetTime() - nativeChatOpenGameTime) < .000001;
            WriteNativePreflightJson("chat-open-receipt.json", new { passed = nativeChatEntryOpened,
                entry = entry.name, route = "native KToggle pointerClickHandler", windowType = NativeChatWindow().GetType().FullName,
                oldLobbyVisible = NativeChatLobbyVisible(), frame = Time.frameCount });
            if (!nativeChatEntryOpened) throw new InvalidOperationException("One native toolbar click did not open exactly the native chat window.");
        }

        private void SendNativeChatUiMessage(OnlineRuntime runtime, string text, bool submit)
        {
            RequireNativeChatUi(runtime);
            if (!nativeChatEntryOpened || NativeChatLobbyVisible()) throw new InvalidOperationException("Open chat through its native toolbar entry first.");
            var window = NativeChatWindow();
            var input = NativeChatInput(window);
            var send = NativeChatSendButton(window);
            int before = runtime.Session.Chat.Length;
            input.text = text;
            if (input.text != text) throw new InvalidOperationException("Native chat input filtered literal message text.");
            input.onEndEdit.Invoke(input.text);
            nativeChatEndEditDidNotSend &= runtime.Session.Chat.Length == before;
            if (!nativeChatEndEditDidNotSend) throw new InvalidOperationException("Losing input focus unexpectedly sent a chat message.");
            if (submit) input.onSubmit.Invoke(input.text);
            else ClickNativeChatControl(send);
            bool passed = runtime.Session.Chat.Length == before + 1 && runtime.Session.Chat.Last().Text == text && input.text == "";
            if (passed) { if (submit) nativeChatSubmitSends++; else nativeChatButtonSends++; }
            WriteNativePreflightJson("chat-send-receipt.json", new { passed, text, before, after = runtime.Session.Chat.Length,
                route = submit ? "native TMP_InputField.onSubmit" : "native KButton pointerClickHandler",
                endEditDidNotSend = nativeChatEndEditDidNotSend, inputCleared = input.text == "", frame = Time.frameCount });
            if (!passed) throw new InvalidOperationException("Native chat send handler did not publish exactly one literal message and clear the input.");
        }

        private void AppendNativeChatUiMessage(OnlineRuntime runtime, string text)
        {
            RequireNativeChatUi(runtime);
            if (nativeChatUi == null || nativeChatUi.phase != "anchored_waiting_message")
                throw new InvalidOperationException("Establish the actual reading anchor before appending a non-composer message.");
            if (!runtime.Session.SendChat(text)) throw new InvalidOperationException("The real host RoomSession rejected the non-composer append.");
            nativeChatUi.appendThroughHostSession = true;
            WriteNativeChatUi();
        }

        private void OpenNativeChatUi(OnlineRuntime runtime)
        {
            RequireNativeChatUi(runtime);
            if (!testColony || !nativeChatEntryOpened || nativeChatButtonSends == 0 || nativeChatSubmitSends == 0)
                throw new InvalidOperationException("Use this disposable colony's native toolbar, send button and submit event before the reading-anchor fixture.");
            foreach (string stage in new[] { "main_menu", "destination", "characters", "gameplay" })
                if (!nativeChatEntryObservations.Any(observation => observation.stage == stage && observation.passed))
                    throw new InvalidOperationException("Missing native chat entry observation: " + stage);
            if (nativeChatUi != null) throw new InvalidOperationException("Chat UI acceptance already started; a failed run cannot be reused as a pass.");
            NativeChatWindow();
            nativeChatUi = new NativeChatUiReceipt { phase = "waiting_initial_layout", gameTime = nativeChatOpenGameTime,
                initialMessages = runtime.Session.Chat.Length, initialLastSequence = runtime.Session.Chat.Last().Sequence,
                entryOpenedChat = nativeChatEntryOpened, buttonSends = nativeChatButtonSends, submitSends = nativeChatSubmitSends,
                endEditDidNotSend = nativeChatEndEditDidNotSend, entryObservations = nativeChatEntryObservations.ToArray() };
            chatUiAfterFrame = Time.frameCount + 4;
            chatUiDeadline = Time.realtimeSinceStartup + 40;
            WriteNativeChatUi();
        }

        private static OnlineChatWindow NativeChatWindow()
        {
            var windows = UnityEngine.Object.FindObjectsByType<OnlineChatWindow>(FindObjectsSortMode.None)
                .Where(window => window.gameObject.activeInHierarchy && window.IsScreenActive()).ToArray();
            if (windows.Length != 1 || NativeChatLobbyVisible())
                throw new InvalidOperationException("Exactly one native OnlineChatWindow must be visible, with no legacy lobby window overlay.");
            return windows[0];
        }

        private static KInputTextField NativeChatInput(OnlineChatWindow window)
        {
            return (KInputTextField)AccessTools.Field(typeof(OnlineChatWindow), "message").GetValue(window);
        }

        private static KButton NativeChatSendButton(OnlineChatWindow window)
        {
            var button = (KButton)AccessTools.Field(typeof(OnlineChatWindow), "send").GetValue(window);
            if (button == null || !button.gameObject.activeInHierarchy || !button.isInteractable)
                throw new InvalidOperationException("The actual native chat send button is unavailable.");
            return button;
        }

        private static KButton NativeChatLatestButton(OnlineChatWindow window)
        {
            return window.GetComponentsInChildren<KButton>(true).Single(button => button.name == "ChatLatestMessages");
        }

        private static Transform NativeChatClonedElement(OnlineChatWindow window, Transform source)
        {
            var path = new List<string>();
            var prefab = ScreenPrefabs.Instance.MessageDialogFrame.transform;
            for (var item = source; item != prefab; item = item.parent)
            {
                if (item == null) throw new InvalidOperationException("The native frame element is outside its template.");
                path.Insert(0, item.name);
            }
            return path.Count == 0 ? window.transform : window.transform.Find(string.Join("/", path));
        }

        private static KButton NativeChatCloseButton(OnlineChatWindow window)
        {
            var source = (KButton)AccessTools.Field(typeof(MessageDialogFrame), "closeButton")
                .GetValue(ScreenPrefabs.Instance.MessageDialogFrame);
            var clone = NativeChatClonedElement(window, source.transform);
            return clone == null ? null : clone.GetComponent<KButton>();
        }

        private static ScrollRect NativeChatScroll(OnlineChatWindow window)
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

        private static Camera NativeChatCamera(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        private static PointerEventData NativeChatPointer(RectTransform rect)
        {
            return new PointerEventData(UnityEngine.EventSystems.EventSystem.current) {
                button = PointerEventData.InputButton.Left, clickCount = 1,
                position = RectTransformUtility.WorldToScreenPoint(NativeChatCamera(rect), rect.TransformPoint(rect.rect.center)) };
        }

        private static void RequireNativeChatRaycast<T>(GameObject expected, PointerEventData pointer) where T : IEventSystemHandler
        {
            var hits = new List<RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer, hits);
            var hit = hits.FirstOrDefault();
            var target = hit.gameObject == null ? null : ExecuteEvents.GetEventHandler<T>(hit.gameObject);
            if (target != expected)
                throw new InvalidOperationException("The native chat control is not the top raycast target: " + expected.name);
            pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hit;
        }

        private static void ClickNativeChatControl(Component control)
        {
            var pointer = NativeChatPointer((RectTransform)control.transform);
            RequireNativeChatRaycast<IPointerClickHandler>(control.gameObject, pointer);
            ExecuteEvents.Execute(control.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        private void FollowNativeChatUi(OnlineRuntime runtime)
        {
            RequireNativeChatUi(runtime);
            if (nativeChatUi == null || nativeChatUi.phase != "anchor_preserved")
                throw new InvalidOperationException("Verify the retained reading anchor before clicking latest messages.");
            var latest = NativeChatLatestButton(NativeChatWindow());
            if (!latest.gameObject.activeInHierarchy || !latest.isInteractable)
                throw new InvalidOperationException("The real latest-messages button is not visible and interactable.");
            ClickNativeChatControl(latest);
            nativeChatUi.phase = "waiting_latest_layout";
            chatUiAfterFrame = Time.frameCount + 4;
            chatUiDeadline = Time.realtimeSinceStartup + 20;
            WriteNativeChatUi();
        }

        private void CloseNativeChatUi(OnlineRuntime runtime)
        {
            RequireNativeChatUi(runtime);
            if (nativeChatUi == null || nativeChatUi.phase != "latest_verified")
                throw new InvalidOperationException("Verify latest-message layout before the native toolbar close/reopen fixture.");
            NativeChatInput(NativeChatWindow()).text = NativeChatDraft;
            var entry = NativeChatEntryToggle();
            ClickNativeChatControl(entry);
            nativeChatUi.entryClosedChat = !runtime.ChatOpen && VisibleNativeChatWindows() == 0;
            nativeChatUi.phase = "waiting_toolbar_close";
            chatUiAfterFrame = Time.frameCount + 4;
            chatUiDeadline = Time.realtimeSinceStartup + 20;
            WriteNativeChatUi();
        }

        private void AdvanceNativeChatUi(OnlineRuntime runtime)
        {
            AdvanceNativeBrowserUi(runtime);
            if (nativeChatUi == null || nativeChatUi.phase == "complete" || nativeChatUi.phase == "failed" ||
                nativeChatUi.phase == "anchor_preserved" || nativeChatUi.phase == "latest_verified") return;
            try
            {
                RequireNativeChatUi(runtime);
                if (Time.realtimeSinceStartup > chatUiDeadline) throw new InvalidOperationException("Chat UI layout/message phase timed out: " + nativeChatUi.phase);
                if (Time.frameCount < chatUiAfterFrame) return;
                if (nativeChatUi.phase == "waiting_toolbar_close")
                {
                    nativeChatUi.hudSurvivedClose = chatUiNativeMenu != null && ManagementMenu.Instance == chatUiNativeMenu &&
                        chatUiNativeCanvas != null && chatUiNativeCanvas.gameObject.activeInHierarchy;
                    if (!nativeChatUi.entryClosedChat || runtime.ChatOpen || NativeChatWindowCount() != 0 || !nativeChatUi.hudSurvivedClose)
                        throw new InvalidOperationException("Native toolbar close left a chat window or destroyed the game HUD.");
                    var entry = NativeChatEntryToggle();
                    ClickNativeChatControl(entry);
                    nativeChatUi.phase = "waiting_reopened_layout"; chatUiAfterFrame = Time.frameCount + 4;
                    WriteNativeChatUi(); return;
                }
                if (nativeChatUi.phase == "waiting_reopened_layout")
                {
                    var input = NativeChatInput(NativeChatWindow());
                    nativeChatUi.draftPreserved = runtime.ChatOpen && NativeChatWindowCount() == 1 && input.text == NativeChatDraft &&
                        !input.richText && input.textComponent != null && !input.textComponent.richText &&
                        runtime.Session.Chat.Length == nativeChatUi.finalMessages;
                    nativeChatUi.reopenedFramesSettled = 4;
                    if (!nativeChatUi.draftPreserved)
                        throw new InvalidOperationException("Native chat reopen lost or submitted the literal same-room draft after layout and focus settled.");
                    ClickNativeChatControl(NativeChatEntryToggle());
                    if (runtime.ChatOpen) throw new InvalidOperationException("A single native toolbar click did not close the reopened chat.");
                    nativeChatUi.phase = "waiting_final_close"; chatUiAfterFrame = Time.frameCount + 4;
                    WriteNativeChatUi(); return;
                }
                if (nativeChatUi.phase == "waiting_final_close")
                {
                    nativeChatUi.gameTimeUnchanged &= Math.Abs(GameClock.Instance.GetTime() - nativeChatUi.gameTime) < .000001;
                    nativeChatUi.remainingWindowsAfterClose = NativeChatWindowCount();
                    nativeChatUi.passed = !runtime.ChatOpen && nativeChatUi.remainingWindowsAfterClose == 0 && !NativeChatLobbyVisible() &&
                        nativeChatUi.entryOpenedChat && nativeChatUi.entryClosedChat && nativeChatUi.draftPreserved &&
                        nativeChatUi.hudSurvivedClose && nativeChatUi.gameTimeUnchanged;
                    if (!nativeChatUi.passed) throw new InvalidOperationException("Final native chat close left an orphan window or changed paused game time.");
                    nativeChatUi.phase = "complete"; WriteNativeChatUi(); return;
                }
                var window = NativeChatWindow();
                var scroll = NativeChatScroll(window);
                Canvas.ForceUpdateCanvases();
                if (nativeChatUi.phase == "waiting_initial_layout")
                {
                    nativeChatUi.initialBottomDistance = NativeChatBottomDistance(scroll);
                    if (scroll.content.rect.height - scroll.viewport.rect.height < 300 || nativeChatUi.initialBottomDistance > 12)
                        throw new InvalidOperationException("Real long-message log must overflow by at least 300 logical pixels and initially follow latest.");
                    var pointer = NativeChatPointer(scroll.viewport); pointer.scrollDelta = new Vector2(0, 8);
                    RequireNativeChatRaycast<IScrollHandler>(scroll.gameObject, pointer);
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
                    var latest = NativeChatLatestButton(window);
                    nativeChatUi.latestVisible = latest.gameObject.activeInHierarchy && latest.isInteractable;
                    if (!nativeChatUi.anchorPreserved || !nativeChatUi.notForcedToBottom || !nativeChatUi.latestVisible || !nativeChatUi.appendThroughHostSession)
                        throw new InvalidOperationException("Actual non-composer appended chat layout lost the reading anchor or latest indicator.");
                    nativeChatUi.phase = "anchor_preserved";
                }
                else if (nativeChatUi.phase == "waiting_latest_layout")
                {
                    InspectNativeChatLayout(runtime, window, scroll);
                    nativeChatUi.phase = "latest_verified";
                }
                WriteNativeChatUi();
            }
            catch (Exception error)
            {
                nativeChatUi.phase = "failed"; nativeChatUi.failure = error.Message; nativeChatUi.passed = false;
                WriteNativeChatUi(); throw;
            }
        }

        private void InspectNativeChatLayout(OnlineRuntime runtime, OnlineChatWindow window, ScrollRect scroll)
        {
            nativeChatUi.latestBottomDistance = NativeChatBottomDistance(scroll);
            var input = NativeChatInput(window);
            var send = NativeChatSendButton(window);
            var latest = NativeChatLatestButton(window);
            var close = NativeChatCloseButton(window);
            if (close == null) throw new InvalidOperationException("The native frame close button was not retained.");
            var panel = (RectTransform)window.transform;
            nativeChatUi.controlsInsidePanel = NativeChatRectVisible(panel, (RectTransform)input.transform) &&
                NativeChatRectVisible(panel, (RectTransform)send.transform) && NativeChatRectVisible(panel, (RectTransform)close.transform) &&
                input.IsInteractable() && send.isInteractable && close.isInteractable;
            var bodies = scroll.content.GetComponentsInChildren<TextMeshProUGUI>(false).Where(text => text.name == "ChatMessageBody").ToArray();
            nativeChatUi.renderedBodies = bodies.Length;
            nativeChatUi.rawTextPreserved = bodies.Select(text => text.text).SequenceEqual(runtime.Session.Chat.Select(line => line.Text));
            nativeChatUi.richTextDisabled = bodies.Length == runtime.Session.Chat.Length &&
                scroll.content.GetComponentsInChildren<TextMeshProUGUI>(false).All(text => !text.richText) &&
                !input.richText && input.textComponent != null && !input.textComponent.richText;
            var heading = (LocText)AccessTools.Field(typeof(OnlineChatWindow), "heading").GetValue(window);
            var sendLabel = send.GetComponentInChildren<LocText>(true);
            var latestLabel = latest.GetComponentInChildren<LocText>(true);
            foreach (var label in new[] { heading, sendLabel, latestLabel, input.placeholder as LocText })
                if (label != null) label.ApplySettings();
            nativeChatUi.nativeLabelsCorrect = heading.text == OnlineText.Get("chat.button") &&
                sendLabel.text == OnlineText.Get("ui.send") && latestLabel.text == OnlineText.Get("chat.latest") &&
                input.placeholder is TMP_Text && ((TMP_Text)input.placeholder).text == OnlineText.Get("ui.chat_placeholder");
            nativeChatUi.latestDismissed = !latest.gameObject.activeSelf;
            nativeChatUi.nativeFrameControllerRemoved = window.GetComponent<MessageDialogFrame>() == null;
            nativeChatUi.attachedToNativeCanvas = chatUiNativeCanvas != null && window.transform.parent == chatUiNativeCanvas.transform;
            nativeChatUi.nonModal = !window.IsModal();
            nativeChatUi.noLobbyWindow = !NativeChatLobbyVisible();
            foreach (var image in ScreenPrefabs.Instance.MessageDialogFrame.GetComponentsInChildren<Image>(true).Where(image => image.sprite != null))
            {
                var clone = NativeChatClonedElement(window, image.transform);
                var retained = clone == null ? null : clone.GetComponent<Image>();
                if (retained != null && retained.sprite == image.sprite) nativeChatUi.nativeFrameSpritesRetained++;
            }
            var outside = NativeChatPointer((RectTransform)chatUiNativeCanvas.transform);
            var canvasRect = (RectTransform)chatUiNativeCanvas.transform;
            outside.position = RectTransformUtility.WorldToScreenPoint(NativeChatCamera(panel),
                canvasRect.TransformPoint(new Vector3(canvasRect.rect.xMax - 4, canvasRect.rect.yMax - 4, 0)));
            if (RectTransformUtility.RectangleContainsScreenPoint(panel, outside.position, NativeChatCamera(panel)))
                throw new InvalidOperationException("No outside-panel raycast point is available on the native canvas.");
            nativeChatUi.outsideRaycastScreenPosition = new[] { outside.position.x, outside.position.y };
            var hits = new List<RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(outside, hits);
            nativeChatUi.outsidePanelRaycastClear = hits.All(hit => !hit.gameObject.transform.IsChildOf(window.transform));
            nativeChatUi.paused = SpeedControlScreen.Instance.IsPaused;
            nativeChatUi.gameTimeUnchanged = Math.Abs(GameClock.Instance.GetTime() - nativeChatUi.gameTime) < .000001;
            nativeChatUi.finalMessages = runtime.Session.Chat.Length;
            nativeChatUi.connectedPlayers = runtime.Session.Players.Count(player => player.Connected);
            nativeChatUi.framesSettled = 4;
            if (nativeChatUi.latestBottomDistance > 12 || !nativeChatUi.controlsInsidePanel || !nativeChatUi.richTextDisabled ||
                !nativeChatUi.rawTextPreserved || !nativeChatUi.nativeLabelsCorrect || !nativeChatUi.latestDismissed || !nativeChatUi.gameTimeUnchanged || !nativeChatUi.paused ||
                !nativeChatUi.nativeFrameControllerRemoved || !nativeChatUi.attachedToNativeCanvas || !nativeChatUi.nonModal ||
                !nativeChatUi.noLobbyWindow || !nativeChatUi.outsidePanelRaycastClear || nativeChatUi.nativeFrameSpritesRetained == 0)
                throw new InvalidOperationException("Native chat layout, retained frame art, literal text, latest state or outside-panel raycast check failed.");
        }

        private static bool NativeChatRectVisible(RectTransform panel, RectTransform control)
        {
            if (!control.gameObject.activeInHierarchy || control.rect.width <= 0 || control.rect.height <= 0) return false;
            var corners = new Vector3[4]; control.GetWorldCorners(corners);
            return corners.All(corner => {
                Vector3 local = panel.InverseTransformPoint(corner);
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(NativeChatCamera(panel), corner);
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
