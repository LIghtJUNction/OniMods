// Compiled only into an explicit test build, never the normal Mod package.
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using OniOnline.Runtime;
using OniOnline.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OniOnline
{
    internal sealed partial class RuntimeProbe : MonoBehaviour
    {
        [Serializable] private sealed class Command { public int sequence = 0; public string action = ""; public string value = ""; }
        [Serializable] private sealed class Snapshot
        {
            public int pid;
            public int command;
            public string scene;
            public string screen;
            public string dependencyError;
            public string error;
            public string commandError;
            public string roomCode;
            public string gameVersion;
            public string cycleTrimVersion;
            public bool mainMenuButton;
            public bool onlineCategory;
            public bool popup;
            public bool joined;
            public bool started;
            public bool loaded;
            public bool paused;
            public int players;
            public int chatLines;
            public string chat;
            public string[] uiLabels;
            public bool defaultSteam;
            public int listedRooms;
            public bool ownPublicRoom;
            public bool ownPublicRoomStarted;
            public int observedWorlds;
            public int localSharedCells;
            public bool sharedPause;
            public bool starMap;
            public bool starSystemPanel;
            public bool printingEngineButton;
            public bool engineStory;
            public double gameTime;
            public bool localStepFinished;
            public double localStepProgress;
            public string planetId;
            public string homeSystemId;
            public string systemId;
            public string homeName;
            public string modSha256;
            public string modAssemblyPath;
            public int admittedPlanets;
            public bool planetAdmitted;
            public bool riftReserved;
            public bool systemMapWindow;
            public bool workPlanWindow;
            public bool teamWindow;
            public string[] asteroidNames;
            public string[] systemPlayers;
            public string testSavePath;
            public bool batchMode;
            public string graphicsDevice;
            public int screenWidth;
            public int screenHeight;
            public int frameCount;
            public NativeWorkFixture nativeWork;
            public NativeDupeFixture nativeDupe;
            public NativeRemoteWorkFixture nativeRemoteWork;
        }

        [Serializable] private sealed class NativeDupeFixture
        {
            public int version = 1;
            public int createdPid;
            public string stage;
            public string planetId;
            public string epoch;
            public string authorityPeerId;
            public string actorPeerId;
            public string requestId;
            public int worldId;
            public int worldWidth;
            public int worldHeight;
            public int dupeId;
            public string dupeName;
            public int sourceCell;
            public int targetCell;
            public int sourceX;
            public int sourceY;
            public int targetX;
            public int targetY;
            public float sourcePositionX;
            public float sourcePositionY;
            public float observedPositionX;
            public float observedPositionY;
            public float targetOxygenMass;
            public float targetTemperature;
            public int observedCell;
            public int observedMonitorTarget;
            public bool observedMovingState;
            public bool pendingBeforePump;
            public bool preflightAccepted;
            public bool orderQueued;
            public string orderReceipt;
            public bool replaySuppressed;
            public int nativeExecuteCalls;
            public bool movedObserved;
            public bool arrivalObserved;
            public bool pausedBefore;
            public bool pausedAfter;
            public double commandGameTime;
            public double observedGameTime;
            public string savePath;
            public string assemblySha256;
        }

        private sealed class FixtureDupeExecutor : IDupeCommandExecutor
        {
            private readonly NativeDupeCommandExecutor native = new NativeDupeCommandExecutor(true);
            internal int Calls;
            internal bool PreflightAccepted;
            public WorkExecution Preflight(WorkPlanet planet, DupeMoveRequest request)
            {
                WorkExecution result = native.Preflight(planet, request);
                PreflightAccepted = result.Accepted;
                return result;
            }
            public WorkExecution Execute(WorkPlanet planet, DupeMoveRequest request)
            { Calls++; return native.Execute(planet, request); }
        }

        [Serializable] private sealed class NativeWorkFixture
        {
            public int version = 1;
            public int createdPid;
            public string stage;
            public string planetId;
            public string epoch;
            public int worldId;
            public int worldWidth;
            public int worldHeight;
            public int managedX;
            public int managedY;
            public int unmanagedX;
            public int unmanagedY;
            public int managedCell;
            public int unmanagedCell;
            public int managedObjectId;
            public int unmanagedObjectId;
            public int managedPriority;
            public int unmanagedPriority;
            public string managedRequestId;
            public string previewRequestId;
            public bool previewAccepted;
            public bool confirmationAccepted;
            public bool replaySuppressed;
            public bool managedPresent;
            public bool unmanagedPresent;
            public bool managedMarked;
            public bool unmanagedMarked;
            public bool managedStillSolid;
            public bool unmanagedStillSolid;
            public double gameTime;
            public string savePath;
            public string assemblySha256;
            public bool revocationStarted;
            public bool revocationFrozen;
            public bool revocationFinished;
            public string revocationPhase;
            public string revocationFailure;
            public double revocationGameTime;
            public bool revocationTimeUnchanged;
            public int revocationStartFrame;
            public int revocationObservedFrames;
            public int originalManagedObjectId;
            public string originalManagedRequestId;
            public bool grantRevoked;
            public bool managedRemovalObserved;
            public bool ordinaryOrderPreserved;
            public bool regrantConfirmed;
            public bool grantTokenChanged;
            public bool oldReplayRejected;
            public string oldReplayReason;
            public bool oldReplayLeftManagedAbsent;
            public long confirmedRevisionBeforeOldReplay;
            public long confirmedRevisionAfterOldReplay;
            public bool freshPreviewAccepted;
            public bool freshConfirmationAccepted;
            public bool freshRequestRestored;
            public long activeGrantRevision;
            public string activeGrantTokenSha256;
            public long originalConfirmedRevision;
            public long freshConfirmedRevision;
        }

        private sealed class FixtureWorkContext : IWorldWorkContext
        {
            internal WorkPlanet Planet;
            public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = Planet; return true; }
        }

        [Serializable] private sealed class NativeRemoteWorkFixture
        {
            public int version = 1;
            public int createdPid;
            public string phase;
            public string error;
            public bool frozen;
            public string planetId;
            public string epoch;
            public string authorityPeerId;
            public string actorPeerId;
            public string assemblySha256;
            public int worldId;
            public int managedX;
            public int managedY;
            public int managedCell;
            public int ordinaryCell;
            public int ordinaryX;
            public int ordinaryY;
            public int ordinaryObjectId;
            public bool managedPresent;
            public bool managedMarked;
            public int managedObjectId;
            public int managedPriority;
            public string managedRequestId;
            public bool ordinaryPreserved;
            public bool bothStillSolid;
            public int nativeCreatedOrders;
            public int nativeRemovalRequests;
            public int nativeReconciliations;
            public long confirmedRevision;
            public int confirmedPlans;
            public int grantPermissions;
            public long grantRevision;
            public string grantTokenSha256;
            public double gameTime;
            public bool gameTimeUnchanged;
            public bool paused;
            public bool complete;
            public bool managedInitiallyReachable;
            public bool ordinaryInitiallyUnreachable;
            public bool ordinaryNaturallyCompleted;
            public bool completionMode;
            public int completionShortSteps;
            public bool completionExcavated;
            public bool completionNoNativeOrder;
            public bool completionNoMarker;
            public bool completionNoCandidate;
            public bool completionNoJournalCell;
            public bool completionFinished;
            public string completionSavePath;
            public bool completionReloadVerified;
        }

        private sealed class NetworkFixtureWorkExecutor : IWorkExecutor, IRemoteWorkPersistence
        {
            private readonly NativeWorkExecutor native = new NativeWorkExecutor();
            internal string PlanetId;
            internal string Epoch;
            internal WorkCell Cell;
            internal int NativeCell;
            internal bool Frozen;
            internal int Creates;
            internal int Removes;
            internal int Reconciliations;
            private bool Matches(WorkPlanet planet) { return !Frozen && planet.PlanetId == PlanetId && planet.AuthorityEpoch == Epoch; }
            public void Attach(RemoteWorkAuthority authority) { native.Attach(authority); }
            public RemoteWorkSavedPlanet Restore(WorkPlanet planet, string owner) { return native.Restore(planet, owner); }
            public void Publish(WorkPlanet planet, string owner) { native.Publish(planet, owner); }
            public WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request)
            {
                if (!Matches(planet) || request.Cells == null || request.Cells.Length != 1 || request.Cells[0].X != Cell.X || request.Cells[0].Y != Cell.Y || request.Priority != 7)
                    return WorkExecution.RejectBeforeMutation("probe_work_outside_inspected_cell");
                return native.Preflight(planet, request);
            }
            public WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] previous, WorkTaskIntent[] desired)
            {
                if (!Matches(planet) || desired.Any(p => p.Cell.X != Cell.X || p.Cell.Y != Cell.Y || p.Priority != 7))
                    return WorkExecution.RejectBeforeMutation("probe_work_outside_inspected_cell");
                bool before = Grid.Objects[NativeCell, (int)ObjectLayer.DigPlacer] != null;
                Reconciliations++;
                WorkExecution result = native.Reconcile(planet, previous, desired);
                if (result.Accepted && !before && Grid.Objects[NativeCell, (int)ObjectLayer.DigPlacer] != null) Creates++;
                if (result.Accepted && before && desired.Length == 0) Removes++;
                return result;
            }
        }

        private string output;
        private int sequence;
        private string commandError;
        private double nextStatus;
        private bool testColony;
        private string modHash;
        private string testSavePath;
        private double runUntil;
        private double stepStartTime;
        private bool stepArmed;
        private NativeWorkFixture nativeWork;
        private NativeDupeFixture nativeDupe;
        private FixtureWorkContext nativeWorkContext;
        private RemoteWorkAuthority nativeWorkAuthority;
        private RemoteWorkRequest nativeWorkConfirmedRequest;
        private Game nativeWorkGame;
        private WorldContainer nativeWorkWorld;
        private bool nativeWorkRevocationWaiting;
        private double nativeWorkRevocationDeadline;
        private NativeRemoteWorkFixture nativeRemoteWork;
        private NetworkFixtureWorkExecutor networkWorkExecutor;
        private Game networkWorkGame;
        private WorldContainer networkWorkWorld;
        private WorldCoordinator networkWorkCoordinator;
        private bool networkWorkActive;

        internal static void Install(GameObject root)
        {
            string argument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--onionline-probe=", StringComparison.Ordinal));
            if (argument == null) return;
            string path = argument.Substring("--onionline-probe=".Length);
            if (!Path.IsPathRooted(path)) throw new ArgumentException("Probe output must be absolute.");
            Directory.CreateDirectory(path);
            var probe = root.AddComponent<RuntimeProbe>(); probe.output = path;
            buildingGenerationObserver = probe;
            using (var sha = SHA256.Create())
                probe.modHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(ModInfo).Assembly.Location))).Replace("-", "").ToLowerInvariant();
        }

        private void Update()
        {
            var runtime = OnlineRuntime.Instance;
            if (runtime == null || output == null) return;
            if (testColony && Time.realtimeSinceStartupAsDouble >= runUntil && Game.Instance != null && Game.Instance.baseAlreadyCreated && SpeedControlScreen.Instance != null && !SpeedControlScreen.Instance.IsPaused)
                SpeedControlScreen.Instance.Pause();
            string commandPath = Path.Combine(output, "command.json");
            try
            {
                if (File.Exists(commandPath) && new FileInfo(commandPath).Length <= 4096)
                {
                    var command = JsonUtility.FromJson<Command>(File.ReadAllText(commandPath));
                    if (command != null && command.sequence > sequence)
                    {
                        sequence = command.sequence;
                        commandError = null;
                        Execute(runtime, command);
                    }
                }
                if (nativeWorkRevocationWaiting) AdvanceNativeWorkRevocation(runtime);
                AdvanceNativeTaskUi(runtime);
                AdvanceNativeChatUi(runtime);
                if (networkWorkActive && !nativeRemoteWork.frozen && ((SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused) || !nativeRemoteWork.completionMode))
                    InspectNativeRemoteWork(runtime);
            }
            catch (Exception error)
            {
                if (nativeWork?.revocationStarted == true && !nativeWork.revocationFinished)
                    FreezeNativeWorkRevocation(error.Message);
                if (networkWorkActive) FreezeNativeRemoteWork(error.Message);
                commandError = error.GetType().Name + ": " + error.Message;
                UnityEngine.Debug.LogWarning("[OniOnline][Probe] " + error);
            }
            if (Time.realtimeSinceStartupAsDouble < nextStatus) return;
            nextStatus = Time.realtimeSinceStartupAsDouble + 0.25;
            try { Publish(runtime); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("[OniOnline][Probe] status: " + error); }
        }

        private void Execute(OnlineRuntime runtime, Command command)
        {
            if (GetComponent<NativeNavigationQuiescenceProbe>() != null && command.action != "quit" && command.action != "screenshot")
                throw new InvalidOperationException("Wait for the single-nav experiment to restore native admission before another command.");
            if (GetComponent<NativeBuildingBridgeProbe>() != null && command.action != "quit" && command.action != "screenshot")
                throw new InvalidOperationException("Wait for the paused building fixture to finish production cleanup before another command.");
            if (nativeWork?.revocationFrozen == true && (command.action.StartsWith("work_native_", StringComparison.Ordinal) ||
                command.action == "native_step" || command.action == "save_test"))
                throw new InvalidOperationException("Native work revocation fixture is frozen after failure; inspect its evidence and use a fresh disposable run.");
            if (nativeWorkRevocationWaiting && command.action != "work_native_inspect" && command.action != "quit")
                throw new InvalidOperationException("Wait for native work cancellation to be observed across frames before another command.");
            if (nativeWork?.revocationStarted == true && !nativeWork.revocationFinished &&
                (command.action == "native_step" || command.action == "save_test"))
                throw new InvalidOperationException("Keep the native revocation scenario paused and unsaved until its fresh-order restoration proof finishes.");
            if (networkWorkActive && (nativeRemoteWork.frozen || (command.action == "native_step" && !nativeRemoteWork.completionMode) ||
                (command.action == "save_test" && !nativeRemoteWork.completionFinished) ||
                command.action.StartsWith("work_native_", StringComparison.Ordinal)) && command.action != "quit")
                throw new InvalidOperationException("The network native-work fixture must remain paused; frozen runs require a fresh disposable colony.");
            switch (command.action)
            {
                case "show": runtime.ShowWindow(); break;
                case "hide": runtime.HideWindow(); break;
                case "join":
                    runtime.Enabled = true; runtime.Nickname = "访客玩家";
                    runtime.Password = ""; runtime.JoinCode = command.value; runtime.Join();
                    runtime.ShowWindow(); break;
                case "host":
                case "host_steam":
                    runtime.Enabled = true; runtime.UseSteam = command.action == "host_steam"; runtime.Nickname = "ONIHost";
                    runtime.RoomName = "OniOnline smoke"; runtime.Port = command.value ?? "27142";
                    runtime.Password = ""; runtime.Host(); break;
                case "leave": runtime.Leave(); break;
                case "search": runtime.Search(); break;
                case "share": runtime.Multiplayer.SetSharing(command.value == "true"); break;
                case "worlds": runtime.ShowWorldOverview(); break;
                case "starmap": runtime.OpenStarMap(); break;
                case "system_map": runtime.ShowSystemMap(); break;
                case "work_plans":
                    if (runtime.SelectedPlanet == null || runtime.Session == null)
                        throw new InvalidOperationException("A joined native colony is required.");
                    runtime.HideWindow();
                    AccessTools.Method(typeof(OnlineRuntime), "CloseSystemMap").Invoke(runtime, null);
                    runtime.ShowWorkPlans(runtime.Session.LocalPlayerId, runtime.SelectedPlanet.PlanetId);
                    break;
                case "team": runtime.ShowTeam(); break;
                case "task_ui_open": OpenNativeTaskUi(runtime); break;
                case "task_ui_close": runtime.Multiplayer.CloseWorldView(); break;
                case "chat_ui_anchor": OpenNativeChatUi(runtime); break;
                case "chat_ui_latest": FollowNativeChatUi(runtime); break;
                case "native_transfer_preflight": InspectNativeTransferPreflight(runtime, command.value); break;
                case "native_world_capacity_preflight": InspectNativeWorldCapacityPreflight(runtime, command.value); break;
                case "native_lifecycle_probe": StartNativeLifecycleProbe(runtime); break;
                case "native_navigation_quiescence": StartNativeNavigationQuiescenceProbe(runtime); break;
                case "native_building_prepare": PrepareNativeBuildingProbe(runtime); break;
                case "native_building_probe": StartNativeBuildingProbe(runtime); break;
                case "native_dupe_round_trip_prepare": PrepareNativeDuplicantRoundTrip(runtime, command.value); break;
                case "archive_selected_starter": CaptureNativeStarterArchive(runtime); break;
                case "native_new": runtime.CreateSystemFromLobby(); break;
                case "map_select":
                    string[] coordinates = command.value.Split(',');
                    UnityEngine.Object.FindFirstObjectByType<OniOnline.UI.StarSystemMapWindow>().Map.Select(int.Parse(coordinates[0]), int.Parse(coordinates[1]));
                    break;
                case "rift_confirm": runtime.ConfirmRift(); break;
                case "work_native_fixture": CreateNativeWorkFixture(runtime); break;
                case "work_native_revoke": RevokeNativeWorkFixture(runtime); break;
                case "work_native_regrant_replay": RegrantAndReplayNativeWorkFixture(runtime); break;
                case "work_native_restore_fresh": RestoreFreshNativeWorkFixture(runtime); break;
                case "network_work_prepare": PrepareNativeRemoteWork(runtime); break;
                case "network_work_grant": GrantNativeRemoteWork(runtime, command.value); break;
                case "network_work_revoke": SetNativeRemoteWorkPermission(runtime, RemoteWorkPermission.None); break;
                case "network_work_regrant": SetNativeRemoteWorkPermission(runtime, RemoteWorkPermission.IssueTasks); break;
                case "network_work_inspect": InspectNativeRemoteWork(runtime); WriteNativeRemoteWork(); break;
                case "network_work_freeze": FreezeNativeRemoteWork(command.value); break;
                case "network_work_completion_begin":
                    InspectNativeRemoteWork(runtime);
                    if (networkWorkExecutor.Creates != 2 || networkWorkExecutor.Removes != 1 || !nativeRemoteWork.managedPresent || nativeRemoteWork.confirmedPlans != 1)
                        throw new InvalidOperationException("Completion requires the successful fresh remote order first.");
                    nativeRemoteWork.completionMode = true; nativeRemoteWork.phase = "completion_waiting_paused"; WriteNativeRemoteWork();
                    break;
                case "network_work_completion_inspect": InspectNativeRemoteWorkCompletion(runtime); break;
                case "network_work_completion_reload_inspect": InspectReloadedNativeRemoteWorkCompletion(runtime); break;
                case "network_work_finish":
                    InspectNativeRemoteWork(runtime);
                    if (networkWorkExecutor.Creates != 2 || networkWorkExecutor.Removes != 1 ||
                        (!nativeRemoteWork.completionFinished && (!nativeRemoteWork.managedPresent || nativeRemoteWork.confirmedPlans != 1)))
                        throw new InvalidOperationException("Network native-work scenario has not completed its exact creation/revocation/fresh-restoration sequence.");
                    nativeRemoteWork.phase = "complete_paused"; nativeRemoteWork.complete = true;
                    WriteNativeRemoteWork(); networkWorkActive = false;
                    break;
                case "dupe_native_fixture": CreateNativeDupeFixture(runtime); break;
                case "dupe_native_inspect":
                    if (nativeDupe == null)
                    {
                        string fixturePath = Path.Combine(output, "dupe-native-fixture.json");
                        if (!File.Exists(fixturePath) || new FileInfo(fixturePath).Length > 16384)
                            throw new InvalidOperationException("A recorded dupe fixture JSON is required for read-only inspection.");
                        nativeDupe = JsonUtility.FromJson<NativeDupeFixture>(File.ReadAllText(fixturePath));
                    }
                    InspectNativeDupeFixture(runtime, true);
                    WriteNativeDupeFixture();
                    break;
                case "work_native_inspect":
                case "work_native_cancel":
                    if (nativeWork == null)
                    {
                        string fixturePath = Path.Combine(output, "work-native-fixture.json");
                        if (!File.Exists(fixturePath) || new FileInfo(fixturePath).Length > 16384)
                            throw new InvalidOperationException("Copy this probe's recorded fixture JSON into its control directory before read-only inspection.");
                        nativeWork = JsonUtility.FromJson<NativeWorkFixture>(File.ReadAllText(fixturePath));
                    }
                    InspectNativeWorkFixture(runtime, true);
                    if (command.action == "work_native_cancel")
                    {
                        GameObject marked = Grid.Objects[nativeWork.managedCell, (int)ObjectLayer.DigPlacer];
                        var provenance = marked != null ? marked.GetComponent<RemoteWorkDigProvenance>() : null;
                        if (provenance == null || !provenance.Managed || !provenance.Matches(
                            new WorkPlanet { PlanetId = nativeWork.planetId, AuthorityEpoch = nativeWork.epoch }, nativeWork.managedRequestId))
                            throw new InvalidOperationException("Refusing to cancel an order without this fixture's exact provenance.");
                        marked.Trigger(2127324410, new Boxed<bool> { value = true });
                    }
                    WriteNativeWorkFixture();
                    break;
                case "save_test":
                    if (!testColony || Game.Instance == null || !Game.Instance.baseAlreadyCreated || !SpeedControlScreen.Instance.IsPaused)
                        throw new InvalidOperationException("Only this probe's own paused generated colony may be saved.");
                    string folder = Path.Combine(SaveLoader.GetSavePrefixAndCreateFolder(), "OniOnlineHarness-" + Process.GetCurrentProcess().Id);
                    Directory.CreateDirectory(folder);
                    string save = Path.Combine(folder, "checkpoint-" + sequence + ".sav");
                    if (File.Exists(save)) throw new InvalidOperationException("Test save must be fresh.");
                    bool activatePreflight = command.value == "activate_preflight";
                    string previousActiveSave = SaveLoader.GetActiveSaveFilePath();
                    string previousActiveHash = activatePreflight && File.Exists(previousActiveSave) ? HashNativePreflightFile(previousActiveSave) : null;
                    testSavePath = SaveLoader.Instance.Save(save, false, activatePreflight);
                    if (testSavePath != save || !File.Exists(save)) throw new IOException("Test save failed.");
                    if (activatePreflight) RecordNativePreflightSource(runtime, save, previousActiveSave, previousActiveHash);
                    if (nativeWork != null) { nativeWork.savePath = save; WriteNativeWorkFixture(); }
                    if (nativeDupe != null) { nativeDupe.savePath = save; WriteNativeDupeFixture(); }
                    if (nativeRemoteWork?.completionFinished == true) { nativeRemoteWork.completionSavePath = save; WriteNativeRemoteWork(); }
                    break;
                case "map_host":
                    runtime.ShowSystemMap();
                    var mapPanel = UnityEngine.Object.FindFirstObjectByType<OniOnline.UI.StarSystemMapWindow>();
                    var nameInput = (TMP_InputField)AccessTools.Field(typeof(OniOnline.UI.StarSystemMapWindow), "nameInput").GetValue(mapPanel);
                    nameInput.text = "行星发动机测试星系";
                    ((UnityEngine.UI.Button)AccessTools.Field(typeof(OniOnline.UI.StarSystemMapWindow), "host").GetValue(mapPanel)).onClick.Invoke();
                    break;
                case "select_pod":
                    if (ClusterMapScreen.Instance.IsScreenActive()) ManagementMenu.Instance.ToggleClusterMap();
                    var pod = Components.Telepads.Items.First();
                    SelectTool.Instance.Select(pod.GetComponent<KSelectable>());
                    break;
                case "engine_entry":
                    MenuButtons().First(b => b.Key.text == OnlineText.Get("engine.title")).Key.onClick();
                    break;
                case "native_step":
                    if (!testColony || Game.Instance == null || !Game.Instance.baseAlreadyCreated || !SpeedControlScreen.Instance.IsPaused)
                        throw new InvalidOperationException("Step requires this probe's paused disposable colony.");
                    if (networkWorkActive && nativeRemoteWork.completionMode)
                    {
                        if (nativeRemoteWork.completionShortSteps >= 30 || nativeRemoteWork.completionFinished)
                            throw new InvalidOperationException("Completion permits at most thirty short native steps and stops after observed cleanup.");
                        nativeRemoteWork.completionShortSteps++; WriteNativeRemoteWork();
                    }
                    stepStartTime = GameClock.Instance.GetTime();
                    stepArmed = true;
                    runUntil = Time.realtimeSinceStartupAsDouble + 1;
                    SpeedControlScreen.Instance.TogglePause(playsound: true);
                    break;
                case "start": runtime.StartOrEnter(); break;
                case "chat":
                    runtime.ShowWindow();
                    var window = UnityEngine.Object.FindFirstObjectByType<OniOnline.UI.OnlineWindow>();
                    var field = (TMP_InputField)AccessTools.Field(typeof(OniOnline.UI.OnlineWindow), "message").GetValue(window);
                    field.text = command.value;
                    AccessTools.Method(typeof(OniOnline.UI.OnlineWindow), "SendChat").Invoke(window, null);
                    break;
                case "survival":
                    var mode = Active<ModeSelectScreen>();
                    ((MultiToggle)AccessTools.Field(typeof(ModeSelectScreen), "survivalButton").GetValue(mode)).onClick();
                    break;
                case "online":
                    Active<ClusterCategorySelectionScreen>().GetComponentsInChildren<UnityEngine.UI.Button>(true)
                        .First(t => t.name == "OniOnlineCategory").onClick.Invoke();
                    break;
                case "generate": Click(Active<ColonyDestinationSelectScreen>(), "launchButton"); testColony = true; break;
                case "embark": Click(Active<MinionSelectScreen>(), "proceedButton"); break;
                case "screenshot":
                    if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                        throw new InvalidOperationException("No screenshot on a Null graphics device.");
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, "screenshot-" + sequence + ".png"));
                    break;
                case "resize":
                    var size = (command.value ?? "").Split('x');
                    int width, height;
                    if (size.Length != 2 || !int.TryParse(size[0], out width) || !int.TryParse(size[1], out height) ||
                        width < 800 || height < 600 || width > 2560 || height > 1600)
                        throw new ArgumentException("Invalid visual-test window size.");
                    Screen.SetResolution(width, height, FullScreenMode.Windowed);
                    break;
                case "quit":
                    if (Game.Instance != null && (SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused))
                        throw new InvalidOperationException("Refusing to quit an unpaused colony.");
                    if (PlayerPrefs.GetString("OniOnline.Nickname", "") == "ONIHost") PlayerPrefs.DeleteKey("OniOnline.Nickname");
                    Application.Quit(); break;
                default: throw new ArgumentException("Unknown probe command.");
            }
        }

        private static T Active<T>() where T : Component
        {
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None).First(x => x.gameObject.activeInHierarchy);
        }
        private static void Click(object screen, string field)
        {
            var button = (KButton)AccessTools.Field(screen.GetType(), field).GetValue(screen);
            if (!button.isInteractable) throw new InvalidOperationException("Native button is disabled.");
            button.SignalClick(KKeyCode.Mouse0);
        }

        private void CreateNativeDupeFixture(OnlineRuntime runtime)
        {
            if (!testColony || Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() ||
                SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused)
                throw new InvalidOperationException("Native movement fixture requires this probe's paused disposable generated colony.");
            if (nativeDupe != null || File.Exists(Path.Combine(output, "dupe-native-fixture.json")))
                throw new InvalidOperationException("Native movement fixture must be fresh and may run only once.");
            if (runtime.Session == null || !runtime.Session.IsHost || !runtime.Session.Joined || runtime.Session.Leaving)
                throw new InvalidOperationException("Native movement fixture requires the existing real host session.");
            var manifest = runtime.SelectedPlanet;
            if (manifest == null) throw new InvalidOperationException("A real selected colony planet is required.");
            WorldContainer world = ClusterManager.Instance.GetWorld(manifest.NativeWorldId);
            if (world == null) throw new InvalidOperationException("Selected native world is unavailable.");
            MinionIdentity selected = null;
            int selectedId = -1, sourceCell = -1;
            WorkCell target = default(WorkCell);
            foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null) continue;
                KPrefabID identity = dupe.GetComponent<KPrefabID>();
                int cell = Grid.PosToCell(dupe);
                Navigator navigator = dupe.GetComponent<Navigator>();
                if (identity == null || identity.InstanceID < 0 || !Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world.id ||
                    navigator == null || navigator.GetSMI<MoveToLocationMonitor.Instance>() == null) continue;
                int x = Grid.CellColumn(cell) - world.WorldOffset.x, y = Grid.CellRow(cell) - world.WorldOffset.y;
                foreach (int distance in new[] { 2, -2, 3, -3 })
                {
                    var point = new WorkCell(x + distance, y);
                    if (!SafeNativeDupeCorridor(world, new WorkCell(x, y), point)) continue;
                    int destination = Grid.XYToCell(point.X + world.WorldOffset.x, point.Y + world.WorldOffset.y);
                    if (!navigator.CanReach(destination)) continue;
                    selected = dupe; selectedId = identity.InstanceID; sourceCell = cell; target = point; break;
                }
                if (selected != null) break;
            }
            if (selected == null)
                throw new InvalidOperationException("No live starter duplicant has a safe, explored oxygen corridor with a reachable target 2-3 tiles away. No movement order was issued.");
            int targetCell = Grid.XYToCell(target.X + world.WorldOffset.x, target.Y + world.WorldOffset.y);
            Vector3 position = selected.transform.GetPosition();
            nativeDupe = new NativeDupeFixture { createdPid = Process.GetCurrentProcess().Id, stage = "inspected_before_order",
                planetId = manifest.PlanetId, epoch = Guid.NewGuid().ToString("N"), authorityPeerId = runtime.Session.LocalPlayerId,
                actorPeerId = runtime.Session.LocalPlayerId, worldId = world.id, worldWidth = world.WorldSize.x, worldHeight = world.WorldSize.y,
                dupeId = selectedId, dupeName = selected.name, sourceCell = sourceCell, targetCell = targetCell,
                sourceX = Grid.CellColumn(sourceCell) - world.WorldOffset.x, sourceY = Grid.CellRow(sourceCell) - world.WorldOffset.y,
                sourcePositionX = position.x - world.WorldOffset.x, sourcePositionY = position.y - world.WorldOffset.y,
                targetX = target.X, targetY = target.Y, targetOxygenMass = Grid.Mass[targetCell], targetTemperature = Grid.Temperature[targetCell],
                pausedBefore = SpeedControlScreen.Instance.IsPaused, commandGameTime = GameClock.Instance.GetTime(), assemblySha256 = modHash };
            WriteNativeDupeFixture(); // Exact source, target and safety readings precede all movement mutations.
            var context = new FixtureWorkContext { Planet = new WorkPlanet { PlanetId = manifest.PlanetId, AuthorityEpoch = nativeDupe.epoch,
                NativeWorldId = world.id, OwnerPeerId = runtime.Session.LocalPlayerId, AuthorityPeerId = runtime.Session.LocalPlayerId } };
            var executor = new FixtureDupeExecutor();
            using (var coordinator = new DupeCommandCoordinator(runtime.Session, context, executor,
                planet => planet == manifest.PlanetId ? runtime.Session.LocalPlayerId : null,
                (actor, dupeId) => actor == runtime.Session.LocalPlayerId && dupeId == selectedId))
            {
                nativeDupe.requestId = coordinator.SubmitMove(manifest.PlanetId, nativeDupe.epoch, selectedId, target);
                if (nativeDupe.requestId == null) throw new InvalidOperationException("Native movement fixture could not create a pending intent.");
                nativeDupe.pendingBeforePump = coordinator.GetSubmission(nativeDupe.requestId)?.Pending == true;
                WriteNativeDupeFixture();
                coordinator.Pump(Time.realtimeSinceStartupAsDouble);
                DupeCommandReceipt receipt = coordinator.GetSubmission(nativeDupe.requestId);
                nativeDupe.preflightAccepted = executor.PreflightAccepted;
                nativeDupe.orderQueued = receipt?.Accepted == true;
                nativeDupe.orderReceipt = receipt?.Reason ?? "missing_receipt";
                if (nativeDupe.orderQueued)
                {
                    var replayRequest = new DupeMoveRequest { RequestId = nativeDupe.requestId, PlanetId = manifest.PlanetId,
                        AuthorityEpoch = nativeDupe.epoch, DupeId = selectedId, Target = target };
                    int beforeReplay = executor.Calls;
                    DupeCommandReceipt replay = coordinator.Authority.Handle(runtime.Session.LocalPlayerId, replayRequest);
                    nativeDupe.replaySuppressed = replay.Accepted && beforeReplay == 1 && executor.Calls == beforeReplay;
                }
                nativeDupe.nativeExecuteCalls = executor.Calls;
                nativeDupe.stage = nativeDupe.orderQueued ? "order_queued_paused" : "order_rejected_paused";
                InspectNativeDupeFixture(runtime, true);
                WriteNativeDupeFixture();
                if (!nativeDupe.orderQueued) throw new InvalidOperationException("Native movement fixture rejected: " + nativeDupe.orderReceipt);
            }
        }

        private bool SafeNativeDupeCorridor(WorldContainer world, WorkCell source, WorkCell target)
        {
            int low = Math.Min(source.X, target.X), high = Math.Max(source.X, target.X);
            if (source.Y < 2 || source.Y >= world.WorldSize.y - 3 || low < 2 || high >= world.WorldSize.x - 2) return false;
            for (int x = low; x <= high; x++)
            {
                // Do not walk beside the two independently recorded digging fixture cells.
                if (nativeWork != null && ((Math.Abs(nativeWork.managedX - x) <= 2 && Math.Abs(nativeWork.managedY - source.Y) <= 2) ||
                    (Math.Abs(nativeWork.unmanagedX - x) <= 2 && Math.Abs(nativeWork.unmanagedY - source.Y) <= 2))) return false;
                int floor = Grid.XYToCell(x + world.WorldOffset.x, source.Y - 1 + world.WorldOffset.y);
                if (!Grid.IsValidCell(floor) || Grid.WorldIdx[floor] != world.id || !Grid.Solid[floor]) return false;
                for (int y = source.Y; y <= source.Y + 1; y++)
                {
                    int body = Grid.XYToCell(x + world.WorldOffset.x, y + world.WorldOffset.y);
                    if (!Grid.IsValidCell(body) || Grid.WorldIdx[body] != world.id || Grid.Visible[body] == 0 || Grid.Solid[body] ||
                        Grid.Element[body].id.ToString() != "Oxygen" || Grid.Mass[body] < .5f ||
                        Grid.Temperature[body] < 270 || Grid.Temperature[body] > 320) return false;
                }
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 2; dy++)
                    {
                        int neighbor = Grid.XYToCell(x + dx + world.WorldOffset.x, source.Y + dy + world.WorldOffset.y);
                        if (!Grid.IsValidCell(neighbor) || Grid.WorldIdx[neighbor] != world.id || Grid.Visible[neighbor] == 0 ||
                            Grid.Element[neighbor].IsLiquid || Grid.Temperature[neighbor] < 270 || Grid.Temperature[neighbor] > 320) return false;
                        string element = Grid.Element[neighbor].id.ToString();
                        if (Grid.Element[neighbor].IsGas && Grid.Mass[neighbor] > .05f && element != "Oxygen" && element != "CarbonDioxide") return false;
                        for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
                        {
                            GameObject obstacle = Grid.Objects[neighbor, layer];
                            if (obstacle != null && (obstacle.GetComponent<Constructable>() != null ||
                                (dy >= 0 && obstacle.GetComponent<BuildingComplete>() != null && obstacle.GetComponent<Telepad>() == null))) return false;
                        }
                    }
            }
            return true;
        }

        private void InspectNativeDupeFixture(OnlineRuntime runtime, bool requirePaused)
        {
            if (nativeDupe == null || nativeDupe.version != 1 || runtime.SelectedPlanet == null || runtime.SelectedPlanet.PlanetId != nativeDupe.planetId ||
                Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading())
                throw new InvalidOperationException("Recorded movement fixture planet does not match the loaded native colony.");
            if (requirePaused && (SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused))
                throw new InvalidOperationException("Native movement inspection requires a paused colony.");
            WorldContainer world = ClusterManager.Instance.GetWorld(nativeDupe.worldId);
            if (world == null || world.WorldSize.x != nativeDupe.worldWidth || world.WorldSize.y != nativeDupe.worldHeight)
                throw new InvalidOperationException("Recorded movement fixture world dimensions differ.");
            MinionIdentity dupe = Components.LiveMinionIdentities.Items.FirstOrDefault(d => NativeDupeHasIdentity(d, nativeDupe.dupeId));
            if (dupe == null) throw new InvalidOperationException("Recorded movement fixture duplicant is unavailable.");
            int currentCell = Grid.PosToCell(dupe);
            if (!Grid.IsValidCell(currentCell) || Grid.WorldIdx[currentCell] != world.id)
                throw new InvalidOperationException("Recorded movement fixture duplicant left its native world.");
            Vector3 position = dupe.transform.GetPosition();
            nativeDupe.observedPositionX = position.x - world.WorldOffset.x;
            nativeDupe.observedPositionY = position.y - world.WorldOffset.y;
            nativeDupe.observedCell = currentCell;
            nativeDupe.movedObserved = nativeDupe.movedObserved || Mathf.Abs(nativeDupe.observedPositionX - nativeDupe.sourcePositionX) > .1f ||
                Mathf.Abs(nativeDupe.observedPositionY - nativeDupe.sourcePositionY) > .1f;
            int targetCell = Grid.XYToCell(nativeDupe.targetX + world.WorldOffset.x, nativeDupe.targetY + world.WorldOffset.y);
            nativeDupe.arrivalObserved = nativeDupe.arrivalObserved || currentCell == targetCell;
            Navigator navigator = dupe.GetComponent<Navigator>();
            MoveToLocationMonitor.Instance monitor = navigator == null ? null : navigator.GetSMI<MoveToLocationMonitor.Instance>();
            nativeDupe.observedMonitorTarget = monitor == null ? -1 : monitor.targetCell;
            nativeDupe.observedMovingState = monitor != null && monitor.GetCurrentState() == monitor.sm.moving;
            nativeDupe.observedGameTime = GameClock.Instance.GetTime();
            nativeDupe.pausedAfter = SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused;
            if (nativeDupe.createdPid != Process.GetCurrentProcess().Id) nativeDupe.stage = "new_process_read_only_inspection";
        }

        private void WriteNativeDupeFixture()
        { File.WriteAllText(Path.Combine(output, "dupe-native-fixture.json"), JsonUtility.ToJson(nativeDupe, true)); }

        private void CreateNativeWorkFixture(OnlineRuntime runtime)
        {
            if (!testColony || Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() ||
                SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused)
                throw new InvalidOperationException("Native work fixture requires this probe's paused disposable generated colony.");
            if (nativeWork != null || File.Exists(Path.Combine(output, "work-native-fixture.json")))
                throw new InvalidOperationException("Native work fixture must be fresh and may run only once.");
            var manifest = runtime.SelectedPlanet;
            if (manifest == null) throw new InvalidOperationException("A real selected colony planet is required.");
            WorldContainer world = ClusterManager.Instance.GetWorld(manifest.NativeWorldId);
            if (world == null) throw new InvalidOperationException("Selected native world is unavailable.");
            var pod = Components.Telepads.Items.FirstOrDefault(p => Grid.WorldIdx[Grid.PosToCell(p)] == world.id);
            if (pod == null) throw new InvalidOperationException("No native Printing Pod in the selected fixture world.");
            int podCell = Grid.PosToCell(pod);
            int startX = Grid.CellColumn(podCell) - world.WorldOffset.x;
            int startY = Grid.CellRow(podCell) - world.WorldOffset.y;
            var candidates = new System.Collections.Generic.List<WorkCell>();
            for (int radius = 2; radius <= 48 && candidates.Count < 2; radius++)
                for (int dx = -radius; dx <= radius && candidates.Count < 2; dx++)
                    for (int dy = -radius; dy <= radius && candidates.Count < 2; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                        var point = new WorkCell(startX + dx, startY + dy);
                        if (candidates.Any(p => Math.Abs(p.X - point.X) + Math.Abs(p.Y - point.Y) < 4)) continue;
                        if (SafeNativeWorkCell(world, point)) candidates.Add(point);
                    }
            if (candidates.Count != 2) throw new InvalidOperationException("No two explored natural solid cells passed the bounded liquid/temperature/building safety inspection. No orders were placed.");
            int managedCell = Grid.XYToCell(candidates[0].X + world.WorldOffset.x, candidates[0].Y + world.WorldOffset.y);
            int unmanagedCell = Grid.XYToCell(candidates[1].X + world.WorldOffset.x, candidates[1].Y + world.WorldOffset.y);
            nativeWork = new NativeWorkFixture { createdPid = Process.GetCurrentProcess().Id, stage = "inspected_before_orders",
                planetId = manifest.PlanetId, epoch = Guid.NewGuid().ToString("N"), worldId = world.id,
                worldWidth = world.WorldSize.x, worldHeight = world.WorldSize.y,
                managedX = candidates[0].X, managedY = candidates[0].Y, unmanagedX = candidates[1].X, unmanagedY = candidates[1].Y,
                managedCell = managedCell, unmanagedCell = unmanagedCell, gameTime = GameClock.Instance.GetTime(), assemblySha256 = modHash };
            WriteNativeWorkFixture(); // Exact preflight coordinates exist on disk before any order mutation.

            var context = new FixtureWorkContext { Planet = new WorkPlanet { PlanetId = manifest.PlanetId, AuthorityEpoch = nativeWork.epoch,
                NativeWorldId = world.id, OwnerPeerId = "fixture-owner", AuthorityPeerId = "fixture-owner", PersistentOwnerId = manifest.OwnerId } };
            var authority = new RemoteWorkAuthority("fixture-owner", context, new NativeWorkExecutor(true));
            nativeWorkContext = context;
            nativeWorkAuthority = authority;
            nativeWorkGame = Game.Instance;
            nativeWorkWorld = world;
            var grant = authority.SetPermissions("fixture-actor", RemoteWorkPermission.IssueTasks);
            nativeWork.activeGrantRevision = grant.Revision;
            nativeWork.activeGrantTokenSha256 = HashNativeWorkToken(grant.Token);
            var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = manifest.PlanetId,
                AuthorityEpoch = nativeWork.epoch, GrantToken = grant.Token, Kind = RemoteWorkKind.Dig, Priority = 7,
                Cells = new[] { candidates[0] }, DryRun = true };
            nativeWork.previewRequestId = request.RequestId;
            var preview = authority.Handle("fixture-actor", request, Time.realtimeSinceStartupAsDouble);
            nativeWork.previewAccepted = preview.Accepted;
            if (!preview.Accepted) { WriteNativeWorkFixture(); throw new InvalidOperationException("Native fixture preflight rejected: " + preview.Reason); }
            request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
            nativeWork.managedRequestId = request.RequestId;
            var receipt = authority.Handle("fixture-actor", request, Time.realtimeSinceStartupAsDouble);
            nativeWork.confirmationAccepted = receipt.Accepted;
            GameObject managed = Grid.Objects[managedCell, (int)ObjectLayer.DigPlacer];
            if (!receipt.Accepted || managed == null) { WriteNativeWorkFixture(); throw new InvalidOperationException("Native fixture confirmation rejected: " + receipt.Reason); }
            nativeWork.managedObjectId = managed.GetInstanceID();
            nativeWorkConfirmedRequest = CopyNativeWorkRequest(request);
            nativeWork.originalConfirmedRevision = authority.ConfirmedRevision;
            long revision = authority.ConfirmedRevision;
            var replay = authority.Handle("fixture-actor", request, Time.realtimeSinceStartupAsDouble);
            nativeWork.replaySuppressed = replay.Accepted && authority.ConfirmedRevision == revision &&
                Grid.Objects[managedCell, (int)ObjectLayer.DigPlacer] == managed;
            GameObject ordinary = DigTool.PlaceDig(unmanagedCell, 0);
            if (ordinary == null) { WriteNativeWorkFixture(); throw new InvalidOperationException("Native unmanaged control order was rejected."); }
            var priority = ordinary.GetComponent<Prioritizable>();
            if (priority != null) priority.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, 3));
            nativeWork.unmanagedObjectId = ordinary.GetInstanceID();
            nativeWork.stage = "orders_created_paused";
            InspectNativeWorkFixture(runtime, true);
            WriteNativeWorkFixture();
        }

        private void PrepareNativeRemoteWork(OnlineRuntime runtime)
        {
            if (!testColony || nativeRemoteWork != null || nativeWork != null || Game.Instance == null || !Game.Instance.baseAlreadyCreated ||
                Game.Instance.IsLoading() || SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused ||
                runtime.Session == null || !runtime.Session.IsHost || !runtime.Session.Joined || runtime.Multiplayer == null)
                throw new InvalidOperationException("Network native-work preparation requires a fresh paused generated colony and its real host session, separate from the local fixture.");
            string path = Path.Combine(output, "network-work-native-fixture.json");
            if (File.Exists(path)) throw new InvalidOperationException("Network native-work fixture must be fresh.");
            PlanetManifest manifest = runtime.SelectedPlanet;
            if (manifest == null || !runtime.Multiplayer.Work.Authority.Synchronize()) throw new InvalidOperationException("Native work planet is unavailable.");
            WorkPlanet planet = runtime.Multiplayer.Work.Authority.Planet;
            WorldContainer world = ClusterManager.Instance.GetWorld(manifest.NativeWorldId);
            var pod = Components.Telepads.Items.FirstOrDefault(p => Grid.WorldIdx[Grid.PosToCell(p)] == world.id);
            if (pod == null) throw new InvalidOperationException("No real Printing Pod in selected test world.");
            int origin = Grid.PosToCell(pod), startX = Grid.CellColumn(origin) - world.WorldOffset.x, startY = Grid.CellRow(origin) - world.WorldOffset.y;
            var points = new System.Collections.Generic.List<WorkCell>();
            for (int radius = 2; radius <= 48 && points.Count < 2; radius++)
                for (int dx = -radius; dx <= radius && points.Count < 2; dx++)
                    for (int dy = -radius; dy <= radius && points.Count < 2; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                        var point = new WorkCell(startX + dx, startY + dy);
                        if (points.Any(p => Math.Abs(p.X - point.X) + Math.Abs(p.Y - point.Y) < 6)) continue;
                        if (!SafeNativeWorkCell(world, point)) continue;
                        int candidate = Grid.XYToCell(point.X + world.WorldOffset.x, point.Y + world.WorldOffset.y);
                        string material = Grid.Element[candidate].id.ToString();
                        if (points.Count == 0 && material != "Dirt" && material != "SandStone") continue;
                        bool reachable = NativeDupeCanApproachDig(world, point, points.Count != 0);
                        if ((points.Count == 0 && reachable) || (points.Count == 1 && !reachable)) points.Add(point);
                    }
            if (points.Count != 2) throw new InvalidOperationException("No two safe explored natural-solid cells; no network native orders were placed.");
            int managed = Grid.XYToCell(points[0].X + world.WorldOffset.x, points[0].Y + world.WorldOffset.y);
            int ordinary = Grid.XYToCell(points[1].X + world.WorldOffset.x, points[1].Y + world.WorldOffset.y);
            nativeRemoteWork = new NativeRemoteWorkFixture { createdPid = Process.GetCurrentProcess().Id, phase = "inspected_before_orders",
                planetId = planet.PlanetId, epoch = planet.AuthorityEpoch, authorityPeerId = runtime.Session.LocalPlayerId, assemblySha256 = modHash,
                worldId = world.id, managedX = points[0].X, managedY = points[0].Y, managedCell = managed, ordinaryCell = ordinary,
                ordinaryX = points[1].X, ordinaryY = points[1].Y, managedInitiallyReachable = true, ordinaryInitiallyUnreachable = true,
                gameTime = GameClock.Instance.GetTime(), paused = true };
            networkWorkGame = Game.Instance; networkWorkWorld = world; networkWorkCoordinator = runtime.Multiplayer;
            networkWorkActive = true;
            WriteNativeRemoteWork();
            networkWorkExecutor = new NetworkFixtureWorkExecutor { PlanetId = planet.PlanetId, Epoch = planet.AuthorityEpoch, Cell = points[0], NativeCell = managed };
            runtime.Multiplayer.SetProbeWorkExecutor(networkWorkExecutor);
            GameObject control = DigTool.PlaceDig(ordinary, 0);
            if (control == null) throw new InvalidOperationException("Native ordinary control order was rejected.");
            var controlPriority = control.GetComponent<Prioritizable>();
            if (controlPriority != null) controlPriority.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, 3));
            nativeRemoteWork.ordinaryObjectId = control.GetInstanceID();
            nativeRemoteWork.phase = "ready_for_protocol_peer";
            InspectNativeRemoteWork(runtime); WriteNativeRemoteWork();
        }

        private void GrantNativeRemoteWork(OnlineRuntime runtime, string actor)
        {
            InspectNativeRemoteWork(runtime);
            if (!string.IsNullOrEmpty(nativeRemoteWork.actorPeerId) || !runtime.Session.Players.Any(p => p.Id == actor && p.Connected &&
                p.Name.StartsWith("Harness work ", StringComparison.Ordinal)))
                throw new InvalidOperationException("Grant requires the authenticated fresh Harness work protocol participant's exact room identity.");
            nativeRemoteWork.actorPeerId = actor;
            SetNativeRemoteWorkPermission(runtime, RemoteWorkPermission.IssueTasks);
        }

        private void SetNativeRemoteWorkPermission(OnlineRuntime runtime, RemoteWorkPermission permission)
        {
            InspectNativeRemoteWork(runtime);
            if (string.IsNullOrEmpty(nativeRemoteWork.actorPeerId)) throw new InvalidOperationException("No authenticated test protocol actor is bound.");
            runtime.Multiplayer.SetWorkPermissions(nativeRemoteWork.actorPeerId, permission);
            nativeRemoteWork.phase = permission == RemoteWorkPermission.None ? "permission_revoked" : "permission_granted";
            InspectNativeRemoteWork(runtime); WriteNativeRemoteWork();
        }

        private void InspectNativeRemoteWork(OnlineRuntime runtime)
        {
            if (nativeRemoteWork == null || nativeRemoteWork.frozen) throw new InvalidOperationException("No active network native-work fixture.");
            if (!testColony || Game.Instance == null || ClusterManager.Instance == null || Game.Instance != networkWorkGame || runtime.Multiplayer != networkWorkCoordinator || Game.Instance.IsLoading() ||
                runtime.SelectedPlanet?.PlanetId != nativeRemoteWork.planetId || ClusterManager.Instance.GetWorld(nativeRemoteWork.worldId) != networkWorkWorld ||
                runtime.Session == null || runtime.Session.LocalPlayerId != nativeRemoteWork.authorityPeerId ||
                SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused)
                throw new InvalidOperationException("Network work fixture lost its exact paused native authority.");
            var authority = runtime.Multiplayer.Work.Authority;
            if (authority.IsBlocked || authority.Planet?.AuthorityEpoch != nativeRemoteWork.epoch)
                throw new InvalidOperationException("Network work authority became uncertain or changed epoch.");
            GameObject managed = Grid.Objects[nativeRemoteWork.managedCell, (int)ObjectLayer.DigPlacer];
            GameObject ordinary = Grid.Objects[nativeRemoteWork.ordinaryCell, (int)ObjectLayer.DigPlacer];
            // Unity keeps managed wrappers after native destruction. Its overloaded
            // == null is required before GetComponent; C# ?. does not perform it.
            nativeRemoteWork.managedPresent = false;
            nativeRemoteWork.managedMarked = false;
            nativeRemoteWork.managedObjectId = 0;
            nativeRemoteWork.managedPriority = -1;
            nativeRemoteWork.managedRequestId = null;
            if (managed != null)
            {
                nativeRemoteWork.managedPresent = true;
                nativeRemoteWork.managedObjectId = managed.GetInstanceID();
                nativeRemoteWork.managedPriority = NativeDigOrderPriority(managed);
                var marker = managed.GetComponent<RemoteWorkDigProvenance>();
                if (marker != null)
                {
                    nativeRemoteWork.managedMarked = marker.Managed;
                    nativeRemoteWork.managedRequestId = marker.CaptureOrder().Winner.RequestId;
                }
            }
            nativeRemoteWork.ordinaryPreserved = ordinary != null && ordinary.GetInstanceID() == nativeRemoteWork.ordinaryObjectId &&
                !NativeDigOrderIsMarked(ordinary) && NativeDigOrderPriority(ordinary) == 3;
            nativeRemoteWork.ordinaryNaturallyCompleted = nativeRemoteWork.completionMode && ordinary == null && !Grid.Solid[nativeRemoteWork.ordinaryCell];
            nativeRemoteWork.bothStillSolid = Grid.Solid[nativeRemoteWork.managedCell] && Grid.Solid[nativeRemoteWork.ordinaryCell];
            nativeRemoteWork.gameTimeUnchanged = Math.Abs(GameClock.Instance.GetTime() - nativeRemoteWork.gameTime) < .000001;
            nativeRemoteWork.paused = SpeedControlScreen.Instance.IsPaused;
            nativeRemoteWork.nativeCreatedOrders = networkWorkExecutor.Creates;
            nativeRemoteWork.nativeRemovalRequests = networkWorkExecutor.Removes;
            nativeRemoteWork.nativeReconciliations = networkWorkExecutor.Reconciliations;
            nativeRemoteWork.confirmedRevision = authority.ConfirmedRevision;
            nativeRemoteWork.confirmedPlans = authority.GetMergedPlans().Length;
            RemoteWorkGrant grant = string.IsNullOrEmpty(nativeRemoteWork.actorPeerId) ? null : authority.GetGrant(nativeRemoteWork.actorPeerId);
            nativeRemoteWork.grantPermissions = (int)(grant?.Permissions ?? RemoteWorkPermission.None);
            nativeRemoteWork.grantRevision = grant?.Revision ?? 0;
            nativeRemoteWork.grantTokenSha256 = grant == null ? "" : HashNativeWorkToken(grant.Token);
            if ((!nativeRemoteWork.ordinaryPreserved && !nativeRemoteWork.ordinaryNaturallyCompleted) ||
                (!nativeRemoteWork.completionMode && (!nativeRemoteWork.bothStillSolid || !nativeRemoteWork.gameTimeUnchanged)) ||
                (managed != null && (!nativeRemoteWork.managedMarked || nativeRemoteWork.managedPriority != 7)) || nativeRemoteWork.confirmedPlans > 1 || networkWorkExecutor.Creates > 2)
                throw new InvalidOperationException("Network work fixture violated physical isolation, unchanged terrain/time, or bounded task counts.");
        }

        private void FreezeNativeRemoteWork(string reason)
        {
            if (networkWorkExecutor != null) networkWorkExecutor.Frozen = true;
            nativeRemoteWork.frozen = true; nativeRemoteWork.phase = "failed_frozen_paused"; nativeRemoteWork.error = reason;
            if (SpeedControlScreen.Instance != null && !SpeedControlScreen.Instance.IsPaused) SpeedControlScreen.Instance.Pause();
            WriteNativeRemoteWork();
        }

        private void WriteNativeRemoteWork()
        { File.WriteAllText(Path.Combine(output, "network-work-native-fixture.json"), JsonConvert.SerializeObject(nativeRemoteWork, Formatting.Indented)); }

        private static bool NativeDupeCanApproachDig(WorldContainer world, WorkCell point, bool allNativeDigOffsets)
        {
            int cell = Grid.XYToCell(point.X + world.WorldOffset.x, point.Y + world.WorldOffset.y);
            foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null) continue;
                int source = Grid.PosToCell(dupe);
                if (!Grid.IsValidCell(source) || Grid.WorldIdx[source] != world.id) continue;
                if (!allNativeDigOffsets && Math.Abs(Grid.CellColumn(source) - Grid.CellColumn(cell)) + Math.Abs(Grid.CellRow(source) - Grid.CellRow(cell)) > 16) continue;
                Navigator navigator = dupe.GetComponent<Navigator>();
                if (navigator == null) continue;
                // A managed target has a simple reachable oxygen approach immediately
                // beside it. A control target conservatively excludes every endpoint
                // in Diggable's actual native offset table, including corner reaches.
                var neighbors = allNativeDigOffsets
                    ? OffsetGroups.InvertedStandardTableWithCorners.Select(group => Grid.OffsetCell(cell, group[0])).Distinct()
                    : (System.Collections.Generic.IEnumerable<int>)new[] { Grid.CellLeft(cell), Grid.CellRight(cell) };
                foreach (int neighbor in neighbors)
                    if (Grid.IsValidCell(neighbor) && Grid.WorldIdx[neighbor] == world.id && Grid.Visible[neighbor] != 0 &&
                        !Grid.Solid[neighbor] && (allNativeDigOffsets || (Grid.Element[neighbor].id.ToString() == "Oxygen" && Grid.Mass[neighbor] >= .5f &&
                        Grid.Temperature[neighbor] >= 270 && Grid.Temperature[neighbor] <= 320)) && navigator.CanReach(neighbor)) return true;
            }
            return false;
        }

        private void InspectNativeRemoteWorkCompletion(OnlineRuntime runtime)
        {
            InspectNativeRemoteWork(runtime);
            if (!nativeRemoteWork.completionMode) throw new InvalidOperationException("Completion inspection requires the explicit bounded short-step phase.");
            int cell = nativeRemoteWork.managedCell;
            string key = ((byte)RemoteTaskLayer.Terrain) + ":" + nativeRemoteWork.managedX + ":" + nativeRemoteWork.managedY;
            var authority = runtime.Multiplayer.Work.Authority;
            Func<WorkTaskIntent, bool> sameCell = plan => plan.Cell.X == nativeRemoteWork.managedX && plan.Cell.Y == nativeRemoteWork.managedY;
            nativeRemoteWork.completionExcavated = !Grid.Solid[cell] && !Grid.Foundation[cell];
            nativeRemoteWork.completionNoNativeOrder = Grid.Objects[cell, (int)ObjectLayer.DigPlacer] == null;
            nativeRemoteWork.completionNoMarker = !NativeWorkJournal.Markers(nativeRemoteWork.planetId).Any(m => m != null && m.TaskKey == key);
            nativeRemoteWork.completionNoCandidate = !authority.GetPlansByActor(nativeRemoteWork.actorPeerId).Any(sameCell) &&
                !authority.GetMergedPlans().Any(sameCell) && !authority.ExportConfirmedPlans().Any(sameCell);
            var saved = NativeWorkJournal.CaptureSavedState(OnlineColonyState.Current, runtime.SelectedPlanet.OwnerId);
            var document = RemoteWorkPersistence.Decode(saved.Payload);
            nativeRemoteWork.completionNoJournalCell = !document.Planets.Where(p => p.PlanetId == nativeRemoteWork.planetId)
                .Any(p => p.Plans.Any(sameCell) || p.Orders.Any(o => sameCell(o.Winner)));
            nativeRemoteWork.completionFinished = nativeRemoteWork.completionExcavated && nativeRemoteWork.completionNoNativeOrder &&
                nativeRemoteWork.completionNoMarker && nativeRemoteWork.completionNoCandidate && nativeRemoteWork.completionNoJournalCell;
            if (nativeRemoteWork.completionFinished) nativeRemoteWork.phase = "completed_natively_cleanup_verified_paused";
            WriteNativeRemoteWork();
        }

        private void InspectReloadedNativeRemoteWorkCompletion(OnlineRuntime runtime)
        {
            if (networkWorkActive || nativeRemoteWork != null || Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() ||
                SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused)
                throw new InvalidOperationException("Reload inspection requires a paused loaded checkpoint in a fresh probe process.");
            string path = Path.Combine(output, "network-work-native-fixture.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) throw new InvalidOperationException("Copy the originating completion fixture into this probe control directory.");
            var fixture = JsonConvert.DeserializeObject<NativeRemoteWorkFixture>(File.ReadAllText(path));
            if (fixture == null || !fixture.completionFinished || string.IsNullOrEmpty(fixture.completionSavePath) ||
                fixture.createdPid == Process.GetCurrentProcess().Id || runtime.SelectedPlanet?.PlanetId != fixture.planetId)
                throw new InvalidOperationException("Reload inspection requires the recorded completed task and matching saved planet from a different process.");
            WorldContainer world = ClusterManager.Instance.GetWorld(fixture.worldId);
            if (world == null || fixture.managedX < 0 || fixture.managedY < 0 || fixture.managedX >= world.WorldSize.x || fixture.managedY >= world.WorldSize.y)
                throw new InvalidOperationException("Recorded completion cell is outside its loaded world.");
            int cell = Grid.XYToCell(fixture.managedX + world.WorldOffset.x, fixture.managedY + world.WorldOffset.y);
            string key = ((byte)RemoteTaskLayer.Terrain) + ":" + fixture.managedX + ":" + fixture.managedY;
            var saved = NativeWorkJournal.CaptureSavedState(OnlineColonyState.Current, runtime.SelectedPlanet.OwnerId);
            var document = RemoteWorkPersistence.Decode(saved.Payload);
            Func<WorkTaskIntent, bool> sameCell = plan => plan.Cell.X == fixture.managedX && plan.Cell.Y == fixture.managedY;
            fixture.completionReloadVerified = Grid.IsValidCell(cell) && Grid.WorldIdx[cell] == world.id && !Grid.Solid[cell] &&
                Grid.Objects[cell, (int)ObjectLayer.DigPlacer] == null && !NativeWorkJournal.Markers(fixture.planetId).Any(m => m != null && m.TaskKey == key) &&
                !document.Planets.Where(p => p.PlanetId == fixture.planetId).Any(p => p.Plans.Any(sameCell) || p.Orders.Any(o => sameCell(o.Winner)));
            fixture.phase = fixture.completionReloadVerified ? "completed_task_absent_after_reload_paused" : "reload_completion_verification_failed";
            nativeRemoteWork = fixture;
            WriteNativeRemoteWork();
            if (!fixture.completionReloadVerified) throw new InvalidOperationException("A naturally completed native task reappeared in terrain, marker, or saved task journal after reload.");
        }

        private void RequireLiveNativeWorkFixture(OnlineRuntime runtime)
        {
            if (!testColony || nativeWork == null || nativeWork.createdPid != Process.GetCurrentProcess().Id ||
                nativeWork.assemblySha256 != modHash || nativeWorkContext == null || nativeWorkAuthority == null || nativeWorkConfirmedRequest == null ||
                Game.Instance != nativeWorkGame || ClusterManager.Instance == null || ClusterManager.Instance.GetWorld(nativeWork.worldId) != nativeWorkWorld)
                throw new InvalidOperationException("Revocation requires this process's exact freshly generated live native work fixture; loaded evidence is read-only.");
            InspectNativeWorkFixture(runtime, true);
            WorkPlanet planet = nativeWorkAuthority.Planet;
            if (planet == null || planet.PlanetId != nativeWork.planetId || planet.AuthorityEpoch != nativeWork.epoch ||
                nativeWorkContext.Planet.PlanetId != nativeWork.planetId || nativeWorkContext.Planet.AuthorityEpoch != nativeWork.epoch ||
                nativeWorkAuthority.IsBlocked)
                throw new InvalidOperationException("Native work authority generation changed or execution became uncertain.");
        }

        private void VerifyNativeRevocationControls(OnlineRuntime runtime)
        {
            RequireLiveNativeWorkFixture(runtime);
            nativeWork.revocationTimeUnchanged = Math.Abs(GameClock.Instance.GetTime() - nativeWork.revocationGameTime) < .000001;
            GameObject ordinary = Grid.Objects[nativeWork.unmanagedCell, (int)ObjectLayer.DigPlacer];
            nativeWork.ordinaryOrderPreserved = ordinary != null && ordinary.GetInstanceID() == nativeWork.unmanagedObjectId &&
                nativeWork.unmanagedPresent && !nativeWork.unmanagedMarked && nativeWork.unmanagedPriority == 3 && nativeWork.unmanagedStillSolid;
            if (!nativeWork.revocationTimeUnchanged || !nativeWork.ordinaryOrderPreserved || !nativeWork.managedStillSolid)
                throw new InvalidOperationException("Revocation changed simulation time, terrain, or the unrelated native control order.");
        }

        private void RevokeNativeWorkFixture(OnlineRuntime runtime)
        {
            RequireLiveNativeWorkFixture(runtime);
            if (nativeWork.revocationStarted || !nativeWork.confirmationAccepted || !nativeWork.replaySuppressed ||
                !nativeWork.managedPresent || !nativeWork.managedMarked || nativeWork.managedPriority != 7 ||
                Grid.Objects[nativeWork.managedCell, (int)ObjectLayer.DigPlacer].GetInstanceID() != nativeWork.managedObjectId)
                throw new InvalidOperationException("Revocation requires the fixture's confirmed, unchanged managed order.");
            nativeWork.revocationStarted = true;
            nativeWork.revocationPhase = "revoke_preflight_paused";
            nativeWork.originalManagedObjectId = nativeWork.managedObjectId;
            nativeWork.originalManagedRequestId = nativeWork.managedRequestId;
            nativeWork.revocationGameTime = GameClock.Instance.GetTime();
            nativeWork.revocationStartFrame = Time.frameCount;
            VerifyNativeRevocationControls(runtime);
            WriteNativeWorkFixture();
            RemoteWorkGrant revoked = nativeWorkAuthority.SetPermissions("fixture-actor", RemoteWorkPermission.None);
            nativeWork.activeGrantRevision = revoked.Revision;
            nativeWork.activeGrantTokenSha256 = HashNativeWorkToken(revoked.Token);
            nativeWork.grantRevoked = revoked.Permissions == RemoteWorkPermission.None &&
                !nativeWorkAuthority.HasPermission("fixture-actor", RemoteWorkPermission.IssueTasks);
            if (!nativeWork.grantRevoked || nativeWorkAuthority.IsBlocked || nativeWorkAuthority.GetOwnPlans("fixture-actor").Length != 0 ||
                nativeWorkAuthority.GetMergedPlans().Length != 0)
                throw new InvalidOperationException("Native authority did not establish an unambiguous revoked task view.");
            nativeWork.revocationPhase = "awaiting_native_order_removal";
            nativeWorkRevocationDeadline = Time.realtimeSinceStartupAsDouble + 10;
            nativeWorkRevocationWaiting = true;
            WriteNativeWorkFixture();
        }

        private void AdvanceNativeWorkRevocation(OnlineRuntime runtime)
        {
            VerifyNativeRevocationControls(runtime);
            nativeWork.revocationObservedFrames = Time.frameCount - nativeWork.revocationStartFrame;
            // Native destruction is deferred. Yield through real Unity frames while paused.
            if (nativeWork.revocationObservedFrames < 1) return;
            GameObject managed = Grid.Objects[nativeWork.managedCell, (int)ObjectLayer.DigPlacer];
            if (managed != null)
            {
                if (managed.GetInstanceID() != nativeWork.originalManagedObjectId || Time.realtimeSinceStartupAsDouble >= nativeWorkRevocationDeadline)
                    throw new InvalidOperationException("Revoked native order was replaced unexpectedly or did not disappear before the inspection deadline.");
                return;
            }
            nativeWork.managedRemovalObserved = true;
            nativeWork.revocationPhase = "revoked_paused";
            nativeWorkRevocationWaiting = false;
            WriteNativeWorkFixture();
        }

        private void RegrantAndReplayNativeWorkFixture(OnlineRuntime runtime)
        {
            VerifyNativeRevocationControls(runtime);
            if (nativeWork.revocationPhase != "revoked_paused" || !nativeWork.managedRemovalObserved || nativeWork.managedPresent)
                throw new InvalidOperationException("Regrant requires an observed native cancellation first.");
            nativeWork.revocationPhase = "regrant_before_old_replay";
            WriteNativeWorkFixture();
            RemoteWorkGrant regrant = nativeWorkAuthority.SetPermissions("fixture-actor", RemoteWorkPermission.IssueTasks);
            nativeWork.activeGrantRevision = regrant.Revision;
            nativeWork.activeGrantTokenSha256 = HashNativeWorkToken(regrant.Token);
            nativeWork.regrantConfirmed = regrant.Permissions == RemoteWorkPermission.IssueTasks;
            nativeWork.grantTokenChanged = regrant.Token != nativeWorkConfirmedRequest.GrantToken;
            if (!nativeWork.regrantConfirmed || !nativeWork.grantTokenChanged || nativeWorkAuthority.IsBlocked)
                throw new InvalidOperationException("Regrant did not create a fresh permission generation.");
            nativeWork.confirmedRevisionBeforeOldReplay = nativeWorkAuthority.ConfirmedRevision;
            // This request is retained only after its original confirmation succeeded.
            // Never turn a failed or unknown confirmation into an accidental first execution.
            RemoteWorkReceipt replay = nativeWorkAuthority.Handle("fixture-actor", CopyNativeWorkRequest(nativeWorkConfirmedRequest), Time.realtimeSinceStartupAsDouble);
            nativeWork.confirmedRevisionAfterOldReplay = nativeWorkAuthority.ConfirmedRevision;
            nativeWork.oldReplayReason = replay.Reason;
            nativeWork.oldReplayRejected = !replay.Accepted && !replay.DryRun && replay.Reason == "work_not_authorized";
            VerifyNativeRevocationControls(runtime);
            nativeWork.oldReplayLeftManagedAbsent = !nativeWork.managedPresent && nativeWorkAuthority.GetOwnPlans("fixture-actor").Length == 0 &&
                nativeWorkAuthority.GetMergedPlans().Length == 0 && nativeWork.confirmedRevisionBeforeOldReplay == nativeWork.confirmedRevisionAfterOldReplay;
            if (!nativeWork.oldReplayRejected || !nativeWork.oldReplayLeftManagedAbsent || nativeWorkAuthority.IsBlocked)
                throw new InvalidOperationException("Old confirmation replay was not unambiguously rejected without native restoration.");
            nativeWork.revocationPhase = "old_replay_rejected_paused";
            WriteNativeWorkFixture();
        }

        private void RestoreFreshNativeWorkFixture(OnlineRuntime runtime)
        {
            VerifyNativeRevocationControls(runtime);
            if (nativeWork.revocationPhase != "old_replay_rejected_paused" || !nativeWork.oldReplayRejected ||
                !nativeWork.oldReplayLeftManagedAbsent || nativeWork.managedPresent)
                throw new InvalidOperationException("Fresh restoration requires the completed stale-request rejection proof.");
            WorkCell point = new WorkCell(nativeWork.managedX, nativeWork.managedY);
            GameObject ordinary = Grid.Objects[nativeWork.unmanagedCell, (int)ObjectLayer.DigPlacer];
            if (!SafeNativeWorkCell(nativeWorkWorld, point, ordinary))
                throw new InvalidOperationException("The original inspected managed cell no longer passes native safety checks.");
            RemoteWorkGrant grant = nativeWorkAuthority.GetGrant("fixture-actor");
            if (grant == null || grant.Permissions != RemoteWorkPermission.IssueTasks || grant.Token == nativeWorkConfirmedRequest.GrantToken)
                throw new InvalidOperationException("Fresh restoration requires a new active grant token.");
            var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = nativeWork.planetId,
                AuthorityEpoch = nativeWork.epoch, GrantToken = grant.Token, Kind = RemoteWorkKind.Dig, Priority = 7,
                Cells = new[] { point }, DryRun = true };
            nativeWork.revocationPhase = "fresh_preview_paused";
            nativeWork.previewRequestId = request.RequestId;
            WriteNativeWorkFixture();
            RemoteWorkReceipt preview = nativeWorkAuthority.Handle("fixture-actor", request, Time.realtimeSinceStartupAsDouble);
            nativeWork.freshPreviewAccepted = preview.Accepted && preview.DryRun;
            if (!nativeWork.freshPreviewAccepted || nativeWorkAuthority.IsBlocked)
                throw new InvalidOperationException("Fresh native preview rejected: " + preview.Reason);
            request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
            nativeWork.revocationPhase = "fresh_confirmation_paused";
            WriteNativeWorkFixture();
            RemoteWorkReceipt confirmed = nativeWorkAuthority.Handle("fixture-actor", request, Time.realtimeSinceStartupAsDouble);
            nativeWork.freshConfirmationAccepted = confirmed.Accepted && !confirmed.DryRun;
            if (!nativeWork.freshConfirmationAccepted || nativeWorkAuthority.IsBlocked)
                throw new InvalidOperationException("Fresh native confirmation rejected: " + confirmed.Reason);
            GameObject restored = Grid.Objects[nativeWork.managedCell, (int)ObjectLayer.DigPlacer];
            if (restored == null || restored.GetInstanceID() == nativeWork.originalManagedObjectId)
                throw new InvalidOperationException("Fresh confirmation did not create a distinct native order.");
            nativeWork.managedObjectId = restored.GetInstanceID();
            nativeWork.managedRequestId = request.RequestId;
            nativeWorkConfirmedRequest = CopyNativeWorkRequest(request);
            nativeWork.freshConfirmedRevision = nativeWorkAuthority.ConfirmedRevision;
            long revision = nativeWorkAuthority.ConfirmedRevision;
            RemoteWorkReceipt replay = nativeWorkAuthority.Handle("fixture-actor", CopyNativeWorkRequest(request), Time.realtimeSinceStartupAsDouble);
            nativeWork.replaySuppressed = replay.Accepted && nativeWorkAuthority.ConfirmedRevision == revision &&
                Grid.Objects[nativeWork.managedCell, (int)ObjectLayer.DigPlacer] == restored;
            VerifyNativeRevocationControls(runtime);
            nativeWork.freshRequestRestored = nativeWork.managedPresent && nativeWork.managedMarked && nativeWork.managedPriority == 7 &&
                nativeWork.replaySuppressed && nativeWorkAuthority.GetOwnPlans("fixture-actor").Length == 1 && nativeWorkAuthority.GetMergedPlans().Length == 1;
            if (!nativeWork.freshRequestRestored || nativeWorkAuthority.IsBlocked)
                throw new InvalidOperationException("Fresh native order did not match the canonical one-cell task view.");
            nativeWork.revocationPhase = "fresh_order_restored_paused";
            nativeWork.revocationFinished = true;
            nativeWork.stage = "revocation_verified_fresh_order_restored_paused";
            WriteNativeWorkFixture();
        }

        private void FreezeNativeWorkRevocation(string reason)
        {
            nativeWorkRevocationWaiting = false;
            nativeWork.revocationFrozen = true;
            nativeWork.revocationPhase = "failed_frozen_paused";
            nativeWork.revocationFailure = reason;
            if (SpeedControlScreen.Instance != null && !SpeedControlScreen.Instance.IsPaused) SpeedControlScreen.Instance.Pause();
            WriteNativeWorkFixture();
        }

        private static RemoteWorkRequest CopyNativeWorkRequest(RemoteWorkRequest request)
        {
            return new RemoteWorkRequest { RequestId = request.RequestId, PlanetId = request.PlanetId, AuthorityEpoch = request.AuthorityEpoch,
                GrantToken = request.GrantToken, Kind = request.Kind, Layer = request.Layer, DryRun = request.DryRun,
                PreviewToken = request.PreviewToken, Priority = request.Priority, Cells = (WorkCell[])request.Cells.Clone() };
        }

        private static string HashNativeWorkToken(string token)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token))).Replace("-", "").ToLowerInvariant();
        }

        private static bool SafeNativeWorkCell(WorldContainer world, WorkCell point, GameObject allowedControlOrder = null)
        {
            if (point.X < 3 || point.Y < 3 || point.X >= world.WorldSize.x - 3 || point.Y >= world.WorldSize.y - 3) return false;
            int cell = Grid.XYToCell(point.X + world.WorldOffset.x, point.Y + world.WorldOffset.y);
            if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell) || Grid.WorldIdx[cell] != world.id || !Grid.Solid[cell] || Grid.Foundation[cell]) return false;
            string element = Grid.Element[cell].id.ToString();
            if (element != "SandStone" && element != "Dirt" && element != "Granite" && element != "IgneousRock" && element != "SedimentaryRock") return false;
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    int neighbor = Grid.XYToCell(point.X + world.WorldOffset.x + dx, point.Y + world.WorldOffset.y + dy);
                    if (!Grid.IsValidCell(neighbor) || Grid.WorldIdx[neighbor] != world.id || !Grid.IsVisible(neighbor) ||
                        Grid.Element[neighbor].IsLiquid || Grid.Temperature[neighbor] < 270 || Grid.Temperature[neighbor] > 320 || Grid.Foundation[neighbor]) return false;
                    string neighborElement = Grid.Element[neighbor].id.ToString();
                    if (Grid.Element[neighbor].IsGas && Grid.Mass[neighbor] > .05f && neighborElement != "Oxygen" && neighborElement != "CarbonDioxide") return false;
                    for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
                        if (Grid.Objects[neighbor, layer] != null && Grid.Objects[neighbor, layer] != allowedControlOrder) return false;
                }
            return true;
        }

        private void InspectNativeWorkFixture(OnlineRuntime runtime, bool requirePaused)
        {
            if (nativeWork == null || nativeWork.version != 1 || runtime.SelectedPlanet == null ||
                runtime.SelectedPlanet.PlanetId != nativeWork.planetId || Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading())
                throw new InvalidOperationException("Recorded fixture planet does not match the loaded native colony.");
            if (requirePaused && (SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused))
                throw new InvalidOperationException("Native fixture inspection requires a paused colony.");
            WorldContainer world = ClusterManager.Instance.GetWorld(nativeWork.worldId);
            if (world == null || world.WorldSize.x != nativeWork.worldWidth || world.WorldSize.y != nativeWork.worldHeight)
                throw new InvalidOperationException("Recorded fixture world dimensions differ.");
            int managedCell = Grid.XYToCell(nativeWork.managedX + world.WorldOffset.x, nativeWork.managedY + world.WorldOffset.y);
            int unmanagedCell = Grid.XYToCell(nativeWork.unmanagedX + world.WorldOffset.x, nativeWork.unmanagedY + world.WorldOffset.y);
            if (!Grid.IsValidCell(managedCell) || !Grid.IsValidCell(unmanagedCell) || Grid.WorldIdx[managedCell] != world.id || Grid.WorldIdx[unmanagedCell] != world.id)
                throw new InvalidOperationException("Recorded fixture cells are outside their world.");
            GameObject managed = Grid.Objects[managedCell, (int)ObjectLayer.DigPlacer];
            GameObject ordinary = Grid.Objects[unmanagedCell, (int)ObjectLayer.DigPlacer];
            nativeWork.managedPresent = managed != null; nativeWork.unmanagedPresent = ordinary != null;
            nativeWork.managedMarked = NativeDigOrderIsMarked(managed);
            nativeWork.unmanagedMarked = NativeDigOrderIsMarked(ordinary);
            nativeWork.managedPriority = NativeDigOrderPriority(managed);
            nativeWork.unmanagedPriority = NativeDigOrderPriority(ordinary);
            nativeWork.managedStillSolid = Grid.Solid[managedCell]; nativeWork.unmanagedStillSolid = Grid.Solid[unmanagedCell];
            if (nativeWork.createdPid != Process.GetCurrentProcess().Id) nativeWork.stage = "new_process_read_only_inspection";
        }

        private void WriteNativeWorkFixture()
        {
            string path = Path.Combine(output, "work-native-fixture.json");
            File.WriteAllText(path, JsonUtility.ToJson(nativeWork, true));
        }

        private static bool NativeDigOrderIsMarked(GameObject order)
        {
            if (order == null) return false;
            var marker = order.GetComponent<RemoteWorkDigProvenance>();
            return marker != null && marker.Managed;
        }

        private static int NativeDigOrderPriority(GameObject order)
        {
            if (order == null) return -1;
            var priority = order.GetComponent<Prioritizable>();
            return priority == null ? -1 : priority.GetMasterPriority().priority_value;
        }

        private static bool NativeDupeHasIdentity(MinionIdentity dupe, int expected)
        {
            if (dupe == null) return false;
            var identity = dupe.GetComponent<KPrefabID>();
            return identity != null && identity.InstanceID == expected;
        }

        private void Publish(OnlineRuntime runtime)
        {
            if (nativeWork != null && Game.Instance != null && Game.Instance.baseAlreadyCreated && !Game.Instance.IsLoading())
                InspectNativeWorkFixture(runtime, false);
            if (nativeDupe != null && Game.Instance != null && Game.Instance.baseAlreadyCreated && !Game.Instance.IsLoading())
                InspectNativeDupeFixture(runtime, false);
            var planet = runtime.SelectedPlanet;
            var colony = OnlineColonyState.Current;
            var system = runtime.Multiplayer?.StarSystem.State;
            var category = UnityEngine.Object.FindObjectsByType<ClusterCategorySelectionScreen>(FindObjectsSortMode.None)
                .FirstOrDefault(s => s.gameObject.activeInHierarchy);
            var flow = UnityEngine.Object.FindFirstObjectByType<NewGameFlow>();
            var current = flow == null ? null : AccessTools.Field(typeof(NewGameFlow), "currentScreen").GetValue(flow) as KScreen;
            var starterSelection = UnityEngine.Object.FindObjectsByType<MinionSelectScreen>(FindObjectsSortMode.None)
                .FirstOrDefault(s => s.gameObject.activeInHierarchy);
            var engineWindow = UnityEngine.Object.FindObjectsByType<OniOnline.UI.StarSystemWindow>(FindObjectsSortMode.None)
                .FirstOrDefault(window => window.gameObject.activeInHierarchy);
            var engineLabels = engineWindow == null ? Array.Empty<TextMeshProUGUI>() :
                engineWindow.GetComponentsInChildren<TextMeshProUGUI>().Where(label => label.gameObject.activeInHierarchy).ToArray();
            var snapshot = new Snapshot
            {
                pid = Process.GetCurrentProcess().Id, command = sequence, scene = SceneManager.GetActiveScene().name,
                screen = starterSelection != null ? "MinionSelectScreen" : current != null && current.gameObject.activeInHierarchy
                    ? current.GetType().Name : Game.Instance != null ? "Game" : "MainMenu",
                dependencyError = runtime.DependencyError, error = runtime.Error ?? runtime.Session?.LastError,
                commandError = commandError, roomCode = runtime.RoomCode,
                gameVersion = runtime.GameVersion, cycleTrimVersion = runtime.CycleTrimVersion,
                mainMenuButton = MainMenu.Instance != null && MainMenu.Instance.GetComponentsInChildren<KButton>(true).Any(b => b.name == "OniOnlineBrowseLobbies"),
                onlineCategory = category != null && category.GetComponentsInChildren<UnityEngine.UI.Button>(true).Any(t => t.name == "OniOnlineCategory"),
                popup = UnityEngine.Object.FindFirstObjectByType<OniOnline.UI.OnlineWindow>() != null,
                joined = runtime.Session != null && runtime.Session.Joined, started = runtime.Session != null && runtime.Session.Started,
                players = runtime.Session?.Players.Length ?? 0, chatLines = runtime.Session?.Chat.Length ?? 0,
                chat = runtime.Session == null ? "" : string.Join("\n", runtime.Session.Chat.Select(l => l.Name + ": " + l.Text)),
                loaded = Game.Instance != null && Game.Instance.baseAlreadyCreated,
                paused = SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused,
                defaultSteam = runtime.UseSteam,
                listedRooms = runtime.Listings.Count,
                ownPublicRoom = runtime.Listings.Any(r => r.Code == runtime.RoomCode),
                ownPublicRoomStarted = runtime.Listings.Any(r => r.Code == runtime.RoomCode && r.Started),
                observedWorlds = runtime.Multiplayer?.Worlds.Count ?? 0,
                localSharedCells = runtime.Multiplayer?.Worlds.FirstOrDefault(w => w.IsLocal)?.Snapshot?.Cells.Length ?? 0,
                sharedPause = OniOnline.Multiplayer.SharedTimeController.Active != null,
                starMap = ClusterMapScreen.Instance != null && ClusterMapScreen.Instance.IsScreenActive(),
                starSystemPanel = UnityEngine.Object.FindFirstObjectByType<OniOnline.UI.StarSystemPanel>() != null,
                printingEngineButton = Game.Instance != null && MenuButtons().Any(b => b.Key.text == OnlineText.Get("engine.title")),
                engineStory = engineLabels.Any(label => label.text == OnlineText.Get("engine.story")) &&
                    engineLabels.Any(label => label.text == OnlineText.Get("engine.preview")),
                gameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0,
                localStepFinished = stepArmed && Time.realtimeSinceStartupAsDouble > runUntil && SpeedControlScreen.Instance.IsPaused,
                localStepProgress = stepArmed ? GameClock.Instance.GetTime() - stepStartTime : 0,
                planetId = planet?.PlanetId, homeSystemId = planet?.HomeSystemId, homeName = colony == null ? null : colony.HomeSystemName,
                systemId = system?.SystemId, admittedPlanets = system?.Members.Count(m => m.Planet != null) ?? 0,
                planetAdmitted = runtime.Multiplayer?.StarSystem.Admitted == true,
                riftReserved = system?.Members.Any(m => m.PeerId == runtime.Session?.LocalPlayerId && m.Planet == null) == true,
                systemMapWindow = UnityEngine.Object.FindFirstObjectByType<OniOnline.UI.StarSystemMapWindow>() != null,
                workPlanWindow = UnityEngine.Object.FindFirstObjectByType<OniOnline.UI.WorkPlanWindow>() != null,
                teamWindow = UnityEngine.Object.FindFirstObjectByType<OniOnline.UI.TeamColonyWindow>() != null,
                asteroidNames = WorldSelector.Instance == null ? new string[0] : WorldSelector.Instance.GetComponentsInChildren<LocText>()
                    .Select(t => t.text).Where(t => !string.IsNullOrEmpty(t)).Take(64).ToArray(),
                systemPlayers = system == null ? new string[0] : system.Members.Select(m => m.PeerId + ":" + m.Q + ":" + m.R + ":" + m.Planet?.PlanetId).ToArray(),
                modSha256 = modHash, modAssemblyPath = typeof(ModInfo).Assembly.Location, testSavePath = testSavePath,
                batchMode = Application.isBatchMode, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                screenWidth = Screen.width, screenHeight = Screen.height, frameCount = Time.frameCount,
                nativeWork = nativeWork,
                nativeDupe = nativeDupe,
                nativeRemoteWork = nativeRemoteWork,
                uiLabels = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None)
                    .Where(t => t.gameObject.activeInHierarchy && t.transform.root.name == "OniOnlineWindow").Select(t => t.text).Take(60).ToArray()
            };
            string path = Path.Combine(output, "online-status.json");
            string temp = path + ".tmp";
            // Unity's field serializer omitted the nested fixture objects in an actual
            // probe run. Use the already shipped JSON serializer for this plain DTO.
            File.WriteAllText(temp, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<KIconButtonMenu.ButtonInfo, float>> MenuButtons()
        {
            if (Game.Instance == null || Game.Instance.userMenu == null)
                return new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<KIconButtonMenu.ButtonInfo, float>>();
            return (System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<KIconButtonMenu.ButtonInfo, float>>)
                AccessTools.Field(typeof(UserMenu), "buttons").GetValue(Game.Instance.userMenu);
        }
    }
}
