using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using HarmonyLib;
using OniOnline.Network;
using OniOnline.UI;
using OniOnline.Multiplayer;
using OniOnline.Core;
using OniOnline.Sdk;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using PeterHan.PLib.Options;

namespace OniOnline.Runtime
{
    public sealed class OnlineRuntime : MonoBehaviour
    {
        internal static OnlineRuntime Instance { get; private set; }
        internal RoomSession Session { get; private set; }
        internal WorldCoordinator Multiplayer { get; private set; }
        internal ModSyncSession ModSynchronization { get; private set; }
        internal bool Enabled;
        internal bool UseSteam;
        internal string Nickname;
        internal string RoomName = "OniOnline";
        internal string Port = "27140";
        internal int ChatFontSize { get; private set; } = 18;
        internal bool VoiceEnabled = true;
        internal int VoiceVolume = 100;
        internal OnlineVoice Voice;
        internal string Password = "";
        internal string JoinCode = "";
        internal string RoomCode = "";
        internal string Error;
        internal string ErrorDetail;
        internal int Revision;
        internal string CycleTrimVersion;
        internal string GameVersion;
        internal bool CycleTrimPresent;
        internal bool Busy { get { return directory != null && directory.Busy; } }
        internal bool Searching { get { return UseSteam && directory?.Searching == true; } }
        internal readonly List<RoomListing> Listings = new List<RoomListing>();
        private SteamRoomDirectory directory;
        private LanDiscovery advertiser;
        private LanDiscovery scanner;
        private OnlineWindow window;
        private NativeSystemBrowser browser;
        private OnlineChatWindow chatWindow;
        private NativePlanetEntryDialog planetEntryDialog;
        private readonly ColonyEntryFlow colonyEntry = new ColonyEntryFlow();
        private double entryDeadline, nextEntryReservation;
        private bool nativeEntryNavigation;
        private ClusterCategorySelectionScreen category;
        private double nextAdvert;
        private double nextSearch;
        private Sprite background;
        private string modPath;
        private SessionProfile pendingProfile;
        private string pendingPassword;
        private NativeModCatalog modCatalog;
        private NativeModCatalogSnapshot loadedModSnapshot;
        private double nextCatalogCapture;
        private NativeModSyncDialog modSyncDialog;
        private SteamModContentSync modContentSync;
        private string shownModSyncPlan;
        private string reviewedModManifest;
        private ModSyncPlan reviewedModSyncPlan;
        private long reviewedConnectionGeneration;
        private bool catalogCaptureStopped;
        private double catalogCaptureStarted = -1;
        private readonly LoadedColonyRoomStart hostStart = new LoadedColonyRoomStart();
        private int hostedPort;
        private bool hostedPassword;
        internal bool CreatingSystem { get { return colonyEntry.Purpose == ColonyEntryPurpose.CreateSystem && colonyEntry.AwaitingNative; } }
        internal void ClearCreationIntent()
        { if (colonyEntry.Purpose == ColonyEntryPurpose.CreateSystem) colonyEntry.CancelFrontend(); }

        internal void BeginNativeFlowFromMenu()
        { if (!nativeEntryNavigation) CancelNativeNavigation(); else ClearCreationIntent(); }

        internal void BeginNativeLoadFromMenu()
        { if (!nativeEntryNavigation) CancelNativeNavigation(); }

        internal void CancelNativeModal(KModalScreen screen)
        {
            if (colonyEntry.AwaitingNative && (screen is NewGameFlowScreen &&
                    (colonyEntry.Purpose == ColonyEntryPurpose.CreateSystem || colonyEntry.Purpose == ColonyEntryPurpose.JoinNew) ||
                screen is LoadScreen && colonyEntry.Purpose == ColonyEntryPurpose.JoinLoad)) CancelNativeNavigation();
        }

        private void CancelNativeNavigation()
        {
            bool release = colonyEntry.Purpose == ColonyEntryPurpose.JoinNew || colonyEntry.Purpose == ColonyEntryPurpose.JoinLoad ||
                colonyEntry.Purpose == ColonyEntryPurpose.CreateSystem && (Session != null || Busy);
            colonyEntry.CancelFrontend();
            planetEntryDialog?.Dispose(); planetEntryDialog = null;
            if (release) Leave();
        }
        private OnlineColonyState lastColony;
        private OnlineColonyState moderationColony;
        private string lastColonyHomeId;
        private string lastColonyPlanetId;
        internal StarMapDirectorySession StarMapDirectory { get; private set; }
        private WorkPlanWindow workPlanWindow;
        private TeamColonyWindow teamWindow;
        private bool awaitingSystemPreview;
        private bool returningHome;
        private bool destroying;
        private sealed class JoinIntent
        { internal string Code, Password; internal SessionProfile Profile; }
        private JoinIntent pendingPreviewJoin;
        internal bool BrowsingSystems;
        internal string DefaultHomeName
        {
            get
            {
                string accountName = SteamManager.Initialized ? SteamFriends.GetPersonaName() : Environment.UserName;
                if (accountName.Length > 32) accountName = accountName.Substring(0, char.IsHighSurrogate(accountName[31]) ? 31 : 32);
                return OnlineText.DefaultSystemName(accountName);
            }
        }

        internal PlanetManifest SelectedPlanet
        { get { return NativeAdapterAvailability.Supports(SessionCapabilities.NativePlanetV1)
                    ? OnlineColonyState.Current?.CapturePlanet() : null; } }

        internal void SelectNextPlanet()
        {
            if (!NativeAdapterAvailability.Supports(SessionCapabilities.NativePlanetV1))
            { Error = "native_adapter_unavailable"; Revision++; return; }
            if (Busy || Multiplayer?.StarSystem.Admitted == true || Multiplayer?.StarSystem.TravelConfirmed == true) return;
            var colony = OnlineColonyState.Current;
            if (colony == null) return;
            var planets = colony.AvailablePlanets();
            if (planets.Length == 0) return;
            int index = Array.FindIndex(planets, p => p.id == colony.SelectedNativeWorldId);
            colony.Select(planets[(index + 1) % planets.Length].id);
            Revision++;
        }

        internal void SaveHomeName()
        {
            var colony = OnlineColonyState.Current;
            if (colony != null && Protocol.ValidText(RoomName, 64)) colony.HomeSystemName = RoomName;
        }

        internal void ReturnHome()
        {
            pendingPreviewJoin = null;
            if (Session != null && !Session.IsHost && Session.RequestLeave())
            { returningHome = true; Revision++; return; }
            Leave(); OpenStarMap();
        }

        internal void ShowSystemMap()
        {
            CloseWorkPlans(); CloseTeam(); HideWindow();
            if (!NativeAdapterAvailability.Supports(SessionCapabilities.NativeStarMapV1))
            { ShowWindow(); return; }
            if (Game.Instance == null || ClusterMapScreen.Instance == null)
            {
                // A native chart only exists after a real colony has loaded.
                // Keep the existing room controls until native initialization completes.
                ShowWindow();
                return;
            }
            if (!ClusterMapScreen.Instance.IsScreenActive()) ManagementMenu.Instance.ToggleClusterMap();
            StarSystemPanel.Attach(ClusterMapScreen.Instance).Open();
        }
        private void CloseSystemMap()
        {
            ClusterMapScreen.Instance?.GetComponent<StarSystemPanel>()?.CloseOnlineView();
            StarMapDirectory?.Stop();
        }

        internal void BrowseSystems()
        {
            CloseSystemMap(); BrowsingSystems = true;
            HideWindow();
            browser?.Close();
            browser = NativeSystemBrowser.Open(this);
            if (!Enabled) SetEnabled(true);
            if (!Busy) Search();
            Revision++;
        }

        internal void PreviewSystem(string code)
        {
            var request = new JoinIntent { Code = code, Password = Password, Profile = Profile() };
            if (Session != null && !Session.IsHost)
            {
                pendingPreviewJoin = request;
                if (Session.RequestLeave()) { Revision++; return; }
            }
            pendingPreviewJoin = null;
            if (Session != null) Leave();
            Join(request);
        }

        internal void ConfirmRift()
        {
            if (Multiplayer?.StarSystem.ConfirmTravel() != true) { Error = "planet_choose_position"; Revision++; return; }
            if (SelectedPlanet == null) CreateRiftColony();
            Revision++;
        }
        internal void CreateRiftColony()
        {
            if (Session != null && !Session.IsHost && Multiplayer?.StarSystem.ConfirmTravel() != true)
            { Error = "planet_choose_position"; Revision++; return; }
            CloseSystemMap(); BeginNativeGame();
        }
        internal void LoadRiftColony()
        {
            if (Game.Instance != null || MainMenu.Instance == null) { Error = "planet_not_ready"; Revision++; return; }
            CloseSystemMap();
            nativeEntryNavigation = true;
            try { AccessTools.Method(typeof(MainMenu), "LoadGame").Invoke(MainMenu.Instance, null); }
            finally { nativeEntryNavigation = false; }
        }

        internal string DependencyError
        {
            get
            {
                // Native feature adapters advertise their own capabilities. Neither
                // an optimization mod nor DLC ownership gates basic networking.
                return null;
            }
        }

        internal void Initialize(string directoryPath, IReadOnlyList<KMod.Mod> mods)
        {
            Instance = this;
            modPath = directoryPath;
            OnlineText.Load(modPath);
            // Read the loaded game's constant rather than inlining the reference build's number.
            GameVersion = AccessTools.Field(typeof(KleiVersion), "ChangeList").GetRawConstantValue().ToString();
            var cycleTrim = mods.FirstOrDefault(m => m.staticID == "LIghtJUNction.CycleTrim" && m.IsEnabledForActiveDlc());
            CycleTrimPresent = cycleTrim != null && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "CycleTrim");
            CycleTrimVersion = cycleTrim?.packagedModInfo?.version ?? "";
            if (string.IsNullOrEmpty(CycleTrimVersion)) CycleTrimPresent = false;
            Enabled = PlayerPrefs.GetInt("OniOnline.Enabled", 0) == 1;
            var options = (POptions.ReadSettings<OnlineOptions>() ?? new OnlineOptions()).Normalize();
            UseSteam = options.PreferSteamLobbies && SteamManager.Initialized;
            Port = options.DefaultLanPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ChatFontSize = options.ChatFontSize;
            VoiceEnabled = options.VoiceEnabled;
            VoiceVolume = options.VoiceVolume;
            Nickname = PlayerPrefs.GetString("OniOnline.Nickname", SteamManager.Initialized ? SteamFriends.GetPersonaName() : Environment.UserName);
            if (Nickname.Length > 32) Nickname = Nickname.Substring(0, char.IsHighSurrogate(Nickname[31]) ? 31 : 32);
            RoomName = DefaultHomeName;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Voice = OnlineVoice.Attach(this);
            OnlineInputControls.Attach(this);

            LogLoadedAssembly();
            Debug.Log("[OniOnline] Preview loaded: native star map, Planetary Engine entry, public rooms and independent local time; observations only, no colony migration.");
        }

        private static void LogLoadedAssembly()
        {
            try
            {
                var assembly = typeof(OnlineRuntime).Assembly;
                using (var input = File.OpenRead(assembly.Location))
                using (var sha = new SHA256Managed())
                    Debug.Log("[OniOnline] Loaded assembly: version=" + assembly.GetName().Version +
                        "; location=" + assembly.Location + "; sha256=" +
                        BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant());
            }
            catch (Exception error)
            { Debug.LogWarning("[OniOnline] Loaded assembly identity unavailable: " + error.GetType().Name); }
        }

        internal void ShowWindow()
        {
            browser?.Close(); browser = null;
            CloseSystemMap(); BrowsingSystems = false;
            if (window != null) { window.Activate(); return; }
            window = OnlineWindow.Create(this);
            if (Enabled && Session == null && !Busy) Search();
        }

        internal void HideWindow()
        {
            browser?.Close(); browser = null;
            if (window == null) return;
            window.Deactivate();
            Destroy(window.transform.root.gameObject);
            window = null;
        }

        internal bool ChatOpen { get { return chatWindow != null && chatWindow.IsScreenActive(); } }

        internal void ShowChat()
        {
            if (!NativeGameplayUi.Ready) return;
            HideWindow();
            if (chatWindow == null) chatWindow = OnlineChatWindow.Create(this);
            chatWindow.FocusComposer();
        }

        internal void HideChat()
        {
            var old = chatWindow;
            chatWindow = null;
            if (old != null) old.Close();
        }

        internal void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            PlayerPrefs.SetInt("OniOnline.Enabled", enabled ? 1 : 0);
            if (!enabled) Leave();
            else if (window != null && Session == null && !Busy) Search();
            Revision++;
        }

        internal void SelectTransport(bool steam)
        {
            if (Session != null || Busy) return;
            UseSteam = steam;
            if (steam) { scanner?.Dispose(); scanner = null; }
            else if (directory != null) { var old = directory; directory = null; old.Dispose(); }
            Listings.Clear();
            Revision++;
            if (Enabled) Search();
        }

        private SessionProfile Profile()
        {
            if (!Enabled) throw new InvalidOperationException("online_disabled");
            if (DependencyError != null) throw new InvalidOperationException(DependencyError);
            if (!Protocol.ValidText(Nickname, 32)) throw new ArgumentException("invalid_name");
            return new SessionProfile { Name = Nickname, GameVersion = GameVersion,
                CycleTrimVersion = CycleTrimVersion, SpacedOut = DlcManager.IsExpansion1Active(),
                Capabilities = SessionCapabilities.ForNativeAdapter(GameVersion, DlcManager.IsExpansion1Active())
                    .Concat(new[] { SessionCapabilities.ModSynchronizationV1 }).ToArray(),
                RequiredCapabilities = SessionCapabilities.Core.Concat(new[] { SessionCapabilities.ModSynchronizationV1 }).ToArray(),
                GameplayId = GameplayFramework.SelectedId, GameplayVersion = GameplayFramework.SelectedVersion };
        }

        internal void Host()
        { Host(null); }

        private void Host(OnlineColonyState startColony)
        {
            Attempt(() =>
            {
                if (Session != null || Busy) throw new InvalidOperationException("already_connected");
                PrepareModCatalog(true);
                CaptureConnectionManifest();
                var profile = Profile();
                if (!Protocol.ValidText(RoomName, 64)) throw new ArgumentException("invalid_room_name");
                if (Password.Length > 128) throw new ArgumentException("password_too_long");
                var colony = OnlineColonyState.Current;
                if (colony != null) RoomModerationPersistence.Decode(colony.HomeSystemId, colony.SavedAccountBans);
                hostStart.Begin(startColony);
                try
                {
                    if (UseSteam)
                    {
                        pendingProfile = profile; pendingPassword = Password;
                        SteamDirectory().Host(RoomName, Password.Length > 0);
                    }
                    else
                    {
                        int port;
                        if (!int.TryParse(Port, out port) || port < 1 || port > 65535) throw new ArgumentException("invalid_address");
                        var transport = TcpRoomTransport.Host(IPAddress.Any, port);
                        hostedPort = transport.Port;
                        hostedPassword = Password.Length > 0;
                        Attach(transport, profile, Password, "oni://" + LocalAddress() + ":" + transport.Port);
                        try { advertiser = new LanDiscovery(true); Advertise(); }
                        catch (SocketException) { Error = "lan_discovery_unavailable"; }
                    }
                }
                catch
                {
                    hostStart.Cancel();
                    throw;
                }
            });
        }

        internal void Join()
        { Join(null); }

        private void Join(JoinIntent intent)
        {
            Attempt(() =>
            {
                if (Session != null || Busy) throw new InvalidOperationException("already_connected");
                PrepareModCatalog(true);
                CaptureConnectionManifest();
                hostStart.Cancel();
                var profile = intent?.Profile ?? Profile();
                string password = intent?.Password ?? Password;
                string code = intent?.Code ?? JoinCode;
                if (password.Length > 128) throw new ArgumentException("password_too_long");
                var address = RoomAddress.Parse(code);
                JoinCode = code;
                UseSteam = address.Steam;
                if (address.Steam)
                {
                    pendingProfile = profile; pendingPassword = password;
                    SteamDirectory().Join(address.LobbyId);
                }
                else Attach(TcpRoomTransport.Join(address.Host, address.Port), profile, password,
                    "oni://" + (address.Host.Contains(":") ? "[" + address.Host + "]" : address.Host) + ":" + address.Port);
                awaitingSystemPreview = true;
            });
        }

        private void Attach(IRoomTransport transport, SessionProfile profile, string password, string code)
        {
            RoomSession next = null;
            WorldCoordinator worlds = null;
            ModSyncSession modSync = null;
            OnlineColonyState boundColony = null;
            try
            {
                var manifest = CaptureConnectionManifest();
                var options = POptions.ReadSettings<OnlineOptions>() ?? new OnlineOptions();
                next = new RoomSession(transport, profile, password, RoomName, options: options.CreateSessionOptions());
                modSync = new ModSyncSession(next, manifest);
                boundColony = next.IsHost ? OnlineColonyState.Current : null;
                if (boundColony != null) BindModeration(next, boundColony, false);
                var candidateSync = modSync;
                if (NativeAdapterAvailability.Supports(SessionCapabilities.NativeObservationV1))
                    worlds = new WorldCoordinator(next, peer => candidateSync.IsReady(peer) &&
                        candidateSync.SupportsNativeCapability(peer, SessionCapabilities.NativePlanetV1),
                        candidateSync.AdmissionGenerationFor);
            }
            catch
            {
                hostStart.Cancel();
                var cleanup = new RoomCleanup();
                cleanup.Dispose("attach_worlds", worlds);
                cleanup.Dispose("attach_mod_sync", modSync);
                if (next == null) cleanup.Dispose("attach_transport", transport);
                else cleanup.Dispose("attach_session", next);
                ReportCleanup(cleanup);
                throw;
            }
            Session = next;
            moderationColony = boundColony;
            RoomCode = code;
            Error = null;
            ErrorDetail = null;
            Multiplayer = worlds;
            ModSynchronization = modSync;
            GameplayFramework.Bind(new GameplaySession(next, () => Multiplayer?.StarSystem.State,
                () => OnlineColonyState.Current?.GameplayState, modSync.IsReady, modSync.AdmissionGenerationFor));
            ModSynchronization.Changed += () => { Revision++; GameplayFramework.Current?.Notify(); };
            if (NativeAdapterAvailability.Supports(SessionCapabilities.NativeStarMapV1))
            {
                StarMapDirectory = new StarMapDirectorySession(next, () => {
                    if (ModSynchronization?.LocalReady != true) return new StarMapEntry[0];
                    var chart = LocalNativeStarMap.Capture(Multiplayer?.StarSystem.State, Session?.LocalPlayerId);
                    return chart.Entries.Where(e => e.Discovered).Select(e => new StarMapEntry { Q = e.Q, R = e.R,
                        Name = e.Name, Discovered = e.Discovered }).ToArray();
                });
                StarMapDirectory.Changed += () => Revision++;
            }
            if (Multiplayer != null) Multiplayer.Changed += () => { Revision++; GameplayFramework.Current?.Notify(); };
            Session.Changed += () => { Revision++; Advertise(); };
            Session.GameStartRequested += () => { if (Session?.IsHost == true) BeginNativeGame(); };
            hostStart.Complete(Session, OnlineColonyState.Current);
            if (colonyEntry.Purpose == ColonyEntryPurpose.CreateSystem) colonyEntry.CreationConnected();
            Revision++;
            Advertise();
        }

        private ModManifest CaptureConnectionManifest()
        {
            if (loadedModSnapshot?.IsComplete != true)
            {
                ErrorDetail = NativeModCatalogDiagnostics.Summary(loadedModSnapshot);
                throw new InvalidOperationException("mod_sync_catalog_unavailable");
            }
            var snapshot = modCatalog.CaptureInstalled();
            if (!snapshot.IsComplete)
            { ErrorDetail = NativeModCatalogDiagnostics.Summary(snapshot); throw new InvalidOperationException("mod_sync_restart_required"); }
            return snapshot.InstalledManifest;
        }

        private void PrepareModCatalog(bool explicitRetry = false)
        {
            // This runs after all OnAllModsLoaded callbacks, so plugins can declare
            // their local synchronization policy before the catalog freezes it.
            if (modCatalog == null)
                modCatalog = NativeKModCatalogSource.Create(Global.Instance.modManager, ModSyncDeclarations.Snapshot());
            if (loadedModSnapshot?.IsComplete == true) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (explicitRetry && catalogCaptureStopped)
            { catalogCaptureStopped = false; catalogCaptureStarted = -1; nextCatalogCapture = 0; }
            if (catalogCaptureStopped || now < nextCatalogCapture) return;
            if (catalogCaptureStarted < 0) catalogCaptureStarted = now;
            nextCatalogCapture = now + 2;
            loadedModSnapshot = modCatalog.CaptureLoadedSnapshot();
            if (!loadedModSnapshot.IsComplete &&
                !NativeModCatalogDiagnostics.ShouldRetryInitial(loadedModSnapshot, now - catalogCaptureStarted))
                catalogCaptureStopped = true;
        }

        private void UpdateModSynchronization(double now)
        {
            var connection = Session;
            var barrier = ModSynchronization;
            var pending = barrier?.PendingPlan;
            if (connection == null || barrier == null) return;
            long generation = connection.ConnectionGenerationFor(connection.LocalPlayerId);
            if (reviewedConnectionGeneration != generation || (reviewedModSyncPlan != null &&
                pending?.RequiredFingerprint != reviewedModSyncPlan.RequiredFingerprint))
            {
                modSyncDialog?.Dispose(); modSyncDialog = null;
                modContentSync?.Dispose(); modContentSync = null;
                shownModSyncPlan = reviewedModManifest = null; reviewedModSyncPlan = null;
                reviewedConnectionGeneration = generation;
            }
            modSyncDialog?.Refresh();
            if (pending == null || pending.CanJoin)
            { modSyncDialog?.Dispose(); modSyncDialog = null; return; }
            if (modContentSync != null)
            {
                modContentSync.Tick(now);
                var lines = modContentSync.Status.Take(8).Select(s => s.Id + ": " + OnlineText.Get("mod_sync.stage_" + s.Stage));
                modSyncDialog?.SetStatus(string.Join("\n", lines) + "\n\n" + OnlineText.Get("mod_sync.restart"));
                return;
            }
            if (reviewedModManifest != pending.Fingerprint)
            {
                var installed = modCatalog.CaptureInstalled(pending.RequiredManifest);
                if (!installed.IsComplete) { Error = "mod_sync_restart_required"; return; }
                reviewedModSyncPlan = ModSyncPlanner.Compare(pending.RequiredManifest, installed.InstalledManifest);
                reviewedModManifest = pending.Fingerprint;
            }
            var plan = reviewedModSyncPlan;
            if (plan == null || plan.CanJoin) return;
            if (shownModSyncPlan == plan.Fingerprint) return;
            shownModSyncPlan = plan.Fingerprint;
            HideWindow();
            modSyncDialog?.Dispose();
            long connectionGeneration = connection.ConnectionGenerationFor(connection.LocalPlayerId);
            Func<string, bool> current = fingerprint => ReferenceEquals(Session, connection) &&
                ReferenceEquals(ModSynchronization, barrier) && ReferenceEquals(reviewedModSyncPlan, plan) &&
                plan.Fingerprint == fingerprint && barrier.PendingPlan?.RequiredFingerprint == plan.RequiredFingerprint &&
                connection.ConnectionGenerationFor(connection.LocalPlayerId) == connectionGeneration &&
                connection.Joined && !connection.Closed && !connection.Reconnecting && !connection.Leaving;
            System.Action manage = () => {
                Leave(); HideWindow();
                Util.KInstantiateUI<ModsScreen>(ScreenPrefabs.Instance.modsMenu.gameObject, Global.Instance.globalCanvas);
            };
            System.Action<string> download = SteamManager.Initialized ? fingerprint => {
                if (!current(fingerprint)) return;
                SteamModContentSync content = null;
                try
                {
                    content = new SteamModContentSync(new SteamWorkshopModContentApi(),
                        () => modCatalog.CaptureInstalled(barrier.PendingPlan?.RequiredManifest).InstalledManifest,
                        () => barrier.PendingPlan?.RequiredManifest);
                    if (!content.Begin(plan, fingerprint, Time.realtimeSinceStartupAsDouble))
                        throw new InvalidOperationException(content.Error);
                    modContentSync = content;
                    modSyncDialog = NativeModSyncDialog.OpenStatus(fingerprint, OnlineText.Get("mod_sync.downloading"),
                        Leave, current, manage);
                }
                catch (Exception)
                {
                    content?.Dispose(); modContentSync = null;
                    modSyncDialog = NativeModSyncDialog.OpenStatus(fingerprint, OnlineText.Get("mod_sync.download_failed"),
                        Leave, current, manage);
                }
            } : (System.Action<string>)null;
            modSyncDialog = NativeModSyncDialog.Open(plan, download, manage, Leave, current);
        }

        private static void BindModeration(RoomSession session, OnlineColonyState colony, bool reload)
        {
            System.Action<string> save = payload => {
                if (colony == null) throw new InvalidOperationException("moderation_save_failed");
                colony.StoreAccountBans(payload);
            };
            if (reload) session.LoadModeration(colony.HomeSystemId, colony.SavedAccountBans, save);
            else session.BindModeration(colony.HomeSystemId, colony.SavedAccountBans, save);
        }

        private SteamRoomDirectory SteamDirectory()
        {
            if (!SteamManager.Initialized) throw new InvalidOperationException("steam_unavailable");
            if (directory != null) return directory;
            directory = new SteamRoomDirectory();
            var owner = directory;
            directory.Ready += (transport, code) =>
            {
                if (!ReferenceEquals(directory, owner)) { transport.Dispose(); return; }
                try { Attach(transport, pendingProfile, pendingPassword, code); }
                catch (Exception error)
                {
                    Error = error is InvalidOperationException || error is ArgumentException ? error.Message : "network_failed";
                    if (ReferenceEquals(directory, owner)) directory = null;
                    hostStart.Cancel();
                    var cleanup = new RoomCleanup();
                    cleanup.Dispose("failed_steam_directory", owner);
                    ReportCleanup(cleanup);
                    Debug.LogWarning("[OniOnline] Connection setup failed: " + error.GetType().Name);
                    if (colonyEntry.CreationFailed()) ShowCreationFailure();
                }
                finally { pendingPassword = null; pendingProfile = null; Revision++; }
            };
            directory.Failed += reason => {
                if (!ReferenceEquals(directory, owner)) return;
                Error = reason; hostStart.Cancel(); pendingPassword = null; pendingProfile = null; Revision++;
                if (colonyEntry.CreationFailed()) ShowCreationFailure();
            };
            directory.SearchFailed += reason => { if (ReferenceEquals(directory, owner)) { Error = reason; Revision++; } };
            directory.Found += rooms =>
            {
                if (!ReferenceEquals(directory, owner) || !UseSteam || !Enabled || Session != null) return;
                Listings.RemoveAll(r => r.Code.StartsWith("oni://steam/", StringComparison.OrdinalIgnoreCase) ||
                    r.Code.StartsWith("oni-steam://", StringComparison.OrdinalIgnoreCase));
                Listings.AddRange(rooms);
                Revision++;
            };
            return directory;
        }

        internal void Search(bool clear = true)
        {
            Attempt(() =>
            {
                Profile();
                if (UseSteam && directory != null && directory.Searching) return;
                if (clear) Listings.Clear();
                if (UseSteam) SteamDirectory().Search();
                else
                {
                    if (scanner == null) scanner = new LanDiscovery(false);
                    scanner.Search();
                }
                Revision++;
                nextSearch = Time.realtimeSinceStartupAsDouble + 15;
            }, clear);
        }

        internal void Leave()
        {
            pendingPreviewJoin = null;
            planetEntryDialog?.Dispose(); planetEntryDialog = null;
            colonyEntry.ConnectionEnded();
            var gameplay = GameplayFramework.Current;
            GameplayFramework.Bind(null);
            gameplay?.Dispose();
            // Detach all owners first. A failed UI/native close cannot strand the rest,
            // and a reentrant Leave must never dispose the same connection twice.
            var oldMultiplayer = Multiplayer; Multiplayer = null;
            var oldSession = Session; Session = null;
            var oldModSync = ModSynchronization; ModSynchronization = null;
            var oldModDialog = modSyncDialog; modSyncDialog = null;
            var oldModContentSync = modContentSync; modContentSync = null;
            shownModSyncPlan = null; reviewedModManifest = null; reviewedModSyncPlan = null;
            var oldStarMapDirectory = StarMapDirectory; StarMapDirectory = null;
            moderationColony = null;
            var oldAdvertiser = advertiser; advertiser = null;
            var oldScanner = scanner; scanner = null;
            var oldDirectory = directory; directory = null;
            pendingProfile = null; pendingPassword = null;
            hostStart.Cancel();
            RoomCode = ""; awaitingSystemPreview = false;
            var cleanup = new RoomCleanup();
            cleanup.Run("work_window", CloseWorkPlans);
            cleanup.Run("team_window", CloseTeam);
            cleanup.Dispose("mod_sync_dialog", oldModDialog);
            cleanup.Dispose("mod_content_sync", oldModContentSync);
            cleanup.Dispose("mod_sync", oldModSync);
            cleanup.Dispose("star_map_directory", oldStarMapDirectory);
            cleanup.Dispose("world_coordinator", oldMultiplayer);
            cleanup.Dispose("room_session", oldSession);
            cleanup.Dispose("lan_advertiser", oldAdvertiser);
            cleanup.Dispose("lan_scanner", oldScanner);
            cleanup.Dispose("steam_directory", oldDirectory);
            // HomeSystemName may call Ensure -> DefaultHomeName -> the lazy SteamManager
            // getter. A shutdown path must not create a new manager/SDK instance.
            if (!destroying)
                cleanup.Run("home_name", () => {
                    if (!SteamRoomCleanup.ClientAvailable) return;
                    var colony = OnlineColonyState.Current;
                    if (colony != null) RoomName = colony.HomeSystemName;
                });
            Revision++;
            ReportCleanup(cleanup);
        }

        private static void ReportCleanup(RoomCleanup cleanup)
        {
            if (string.IsNullOrEmpty(cleanup.Status)) return;
            if (cleanup.HasFailures) Debug.LogWarning("[OniOnline] Connection cleanup incomplete: " + cleanup.Status);
            else Debug.Log("[OniOnline] Connection cleanup: " + cleanup.Status);
        }

        internal void StartOrEnter()
        {
            if (Session == null || !Session.Joined) return;
            if (Session.Started) BeginNativeGame();
            else Session.StartGame();
        }

        internal void SelectOnline(ClusterCategorySelectionScreen screen)
        {
            if (!NativeAdapterAvailability.Supports(SessionCapabilities.NativePlanetV1))
            { Error = "native_adapter_unavailable"; Revision++; return; }
            if (!Enabled) SetEnabled(true);
            colonyEntry.BeginCreation(Session != null || Busy);
            if (colonyEntry.RequiresLeaveConnection) Leave();
            category = screen;
            BeginNativeGame(creatingSystem: true);
        }

        internal void OpenStarMap(bool showEngine = false)
        {
            if (Game.Instance == null) { ShowSystemMap(); return; }
            if (!Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading()) return;
            HideWindow();
            if (showEngine)
            {
                CloseSystemMap();
                PlanetEngineCodex.Open();
                return;
            }
            if (!NativeAdapterAvailability.Supports(SessionCapabilities.NativeStarMapV1))
            { ShowWindow(); return; }
            if (ClusterMapScreen.Instance == null) return;
            if (!ClusterMapScreen.Instance.IsScreenActive()) ManagementMenu.Instance.ToggleClusterMap();
            StarSystemPanel.Attach(ClusterMapScreen.Instance);
            ShowSystemMap();
        }

        internal void HostFromStarMap()
        {
            if (Game.Instance == null || !Game.Instance.baseAlreadyCreated) return;
            if (SelectedPlanet == null) { Error = "planet_not_ready"; Revision++; return; }
            SaveHomeName();
            if (!Enabled) SetEnabled(true);
            Host(OnlineColonyState.Current);
        }

        private void BeginNativeGame(bool creatingSystem = false)
        {
            if (DependencyError != null) { Error = DependencyError; Revision++; return; }
            // Never discard a loaded colony in response to a network packet.
            if (Game.Instance != null) return;
            HideWindow();
            if (category != null && category.gameObject.activeInHierarchy && category.IsActive())
            {
                var chosen = category;
                category = null;
                AccessTools.Method(typeof(ClusterCategorySelectionScreen), "OnClickOption")
                    .Invoke(chosen, new object[] { NativeClusterCategory() });
                if (creatingSystem) colonyEntry.BeginCreation();
                return;
            }
            var menu = MainMenu.Instance;
            if (menu != null && menu.IsActive())
            {
                var button = AccessTools.Field(typeof(MainMenu), "Button_NewGame").GetValue(menu) as KButton;
                if (button != null && button.isInteractable)
                {
                    nativeEntryNavigation = true;
                    try { button.SignalClick(KKeyCode.Mouse0); }
                    finally { nativeEntryNavigation = false; }
                    if (creatingSystem) SelectNativeOnlineDefaults(menu.GetComponent<NewGameFlow>());
                }
            }
        }

        private static void SelectNativeOnlineDefaults(NewGameFlow flow)
        {
            // Let native prefab initialization load cluster data before choosing
            // Survival. Advance after BeginFlow returns, not reentrantly in OnSpawn.
            var current = AccessTools.Field(typeof(NewGameFlow), "currentScreen");
            var mode = flow == null ? null : current.GetValue(flow) as ModeSelectScreen;
            if (mode == null || !mode.gameObject.activeInHierarchy) return;
            // OnSpawn runs on Unity Start, so its button callbacks need not be
            // attached yet. The native method applies the defaults and advances.
            AccessTools.Method(typeof(ModeSelectScreen), "OnClickSurvival").Invoke(mode, null);
            var online = current.GetValue(flow) as ClusterCategorySelectionScreen;
            if (online != null && online.gameObject.activeInHierarchy)
                AccessTools.Method(typeof(ClusterCategorySelectionScreen), "OnClickOption")
                    .Invoke(online, new object[] { NativeClusterCategory() });
            if (flow != null) Instance.colonyEntry.BeginCreation();
        }

        private static ProcGen.ClusterLayout.ClusterCategory NativeClusterCategory()
        { return DlcManager.IsExpansion1Active() ? ProcGen.ClusterLayout.ClusterCategory.SpacedOutStyle
                : ProcGen.ClusterLayout.ClusterCategory.Vanilla; }

        private void ShowCreationFailure()
        {
            planetEntryDialog?.Dispose();
            planetEntryDialog = NativePlanetEntryDialog.OpenFailure(OnlineText.Get("error." + (Error ?? "network_failed")),
                () => colonyEntry.RetryCreation(), () => colonyEntry.CancelFrontend(),
                () => colonyEntry.Purpose == ColonyEntryPurpose.CreateSystem && colonyEntry.Stage == ColonyEntryStage.HostFailed);
        }

        private bool EntryConnectionCurrent(RoomSession connection, long admission)
        { return ReferenceEquals(Session, connection) && connection.Joined && !connection.Closed && !connection.Reconnecting &&
            !connection.Leaving && admission > 0 && ModSynchronization?.AdmissionGenerationFor(connection.LocalPlayerId) == admission; }

        private void ShowPlanetEntry()
        {
            var connection = Session;
            var system = Multiplayer?.StarSystem.State;
            if (connection == null || system == null || ModSynchronization?.LocalReady != true) return;
            long admission = ModSynchronization.AdmissionGenerationFor(connection.LocalPlayerId);
            var planet = SelectedPlanet;
            HideWindow();
            planetEntryDialog?.Dispose();
            System.Action alternate = planet == null ? (System.Action)(() => QueuePlanetEntry(ColonyEntryPurpose.JoinLoad)) :
                OnlineColonyState.Current?.AvailablePlanets().Length > 1 ? () => {
                    SelectNextPlanet(); awaitingSystemPreview = true;
                } : (System.Action)null;
            planetEntryDialog = NativePlanetEntryDialog.Open(system.Name, planet?.Name,
                () => QueuePlanetEntry(planet == null ? ColonyEntryPurpose.JoinNew : ColonyEntryPurpose.JoinExisting),
                alternate, Leave, () => EntryConnectionCurrent(connection, admission));
        }

        private void QueuePlanetEntry(ColonyEntryPurpose purpose)
        {
            var connection = Session;
            var starSystem = Multiplayer?.StarSystem;
            if (connection == null || starSystem?.State == null || ModSynchronization?.LocalReady != true) return;
            long admission = ModSynchronization.AdmissionGenerationFor(connection.LocalPlayerId);
            colonyEntry.BeginJoin(purpose, connection.RoomId, admission);
            entryDeadline = Time.realtimeSinceStartupAsDouble + 30;
            nextEntryReservation = 0;
            planetEntryDialog?.Dispose();
            planetEntryDialog = NativePlanetEntryDialog.OpenWaiting(connection.RoomName, Leave,
                () => EntryConnectionCurrent(connection, admission) &&
                    (colonyEntry.Stage == ColonyEntryStage.Reserving || colonyEntry.Stage == ColonyEntryStage.AwaitingAdmission));
        }

        private void ShowJoinFailure(ColonyEntryPurpose purpose, string error)
        {
            var connection = Session;
            long admission = ModSynchronization?.AdmissionGenerationFor(connection?.LocalPlayerId) ?? 0;
            colonyEntry.CancelFrontend();
            Error = error; Revision++;
            planetEntryDialog?.Dispose();
            planetEntryDialog = NativePlanetEntryDialog.OpenFailure(OnlineText.Get("error." + error),
                () => QueuePlanetEntry(purpose), Leave,
                () => connection != null && EntryConnectionCurrent(connection, admission), "system.join_failed");
        }

        private static bool ReserveAvailablePosition(StarSystemCoordinator system, string localPlayer)
        {
            var state = system.State;
            if (state == null) return false;
            for (int radius = 0; radius <= state.Radius; radius++)
                for (int q = -radius; q <= radius; q++)
                    for (int r = -radius; r <= radius; r++)
                        if (Math.Max(Math.Max(Math.Abs(q), Math.Abs(r)), Math.Abs(q + r)) == radius &&
                            !state.Members.Any(m => m.PeerId != localPlayer && m.Q == q && m.R == r))
                            return system.ChooseLanding(q, r);
            return false;
        }

        private void UpdatePlanetEntry(double now)
        {
            planetEntryDialog?.Refresh();
            var connection = Session;
            var system = Multiplayer?.StarSystem;
            if (connection == null || connection.IsHost || ModSynchronization?.LocalReady != true) return;
            if (system == null)
            {
                if (awaitingSystemPreview)
                { awaitingSystemPreview = false; ShowJoinFailure(ColonyEntryPurpose.JoinExisting, "native_adapter_unavailable"); }
                return;
            }
            long admission = ModSynchronization.AdmissionGenerationFor(connection.LocalPlayerId);
            if (awaitingSystemPreview && system.State != null && (Game.Instance == null || NativeGameplayUi.Ready))
            { awaitingSystemPreview = false; ShowPlanetEntry(); }
            var purpose = colonyEntry.Purpose;
            if (purpose == ColonyEntryPurpose.JoinExisting || purpose == ColonyEntryPurpose.JoinNew || purpose == ColonyEntryPurpose.JoinLoad)
            {
                if (!colonyEntry.Matches(connection.RoomId, admission))
                {
                    planetEntryDialog?.Dispose(); planetEntryDialog = null;
                    awaitingSystemPreview = true;
                    return;
                }
                if (colonyEntry.Stage == ColonyEntryStage.Reserving)
                {
                    var own = system.State?.Members.FirstOrDefault(m => m.PeerId == connection.LocalPlayerId);
                    bool confirmed = system.TravelConfirmed || system.ConfirmTravel();
                    if (!confirmed && own?.Planet != null && SelectedPlanet != null)
                    {
                        confirmed = system.ReconfirmPlanet();
                        if (!confirmed) { ShowJoinFailure(purpose, system.LastError ?? "planet_not_ready"); return; }
                    }
                    if (!confirmed && own?.Planet != null && SelectedPlanet == null)
                    {
                        if (purpose == ColonyEntryPurpose.JoinLoad) confirmed = true;
                        else { ShowJoinFailure(purpose, "planet_selection_locked"); return; }
                    }
                    if (confirmed)
                    {
                        colonyEntry.ReservationConfirmed();
                        entryDeadline = now + 30;
                        if (purpose != ColonyEntryPurpose.JoinExisting)
                        { planetEntryDialog?.Dispose(); planetEntryDialog = null; }
                        if (purpose == ColonyEntryPurpose.JoinNew) BeginNativeGame();
                        else if (purpose == ColonyEntryPurpose.JoinLoad) LoadRiftColony();
                    }
                    else if (now >= entryDeadline) ShowJoinFailure(purpose, system.LastError ?? "planet_system_full");
                    else if (now >= nextEntryReservation)
                    { nextEntryReservation = now + 3; if (!ReserveAvailablePosition(system, connection.LocalPlayerId)) ShowJoinFailure(purpose, "planet_system_full"); }
                }
                else if (colonyEntry.AwaitingNative && NativeGameplayUi.Ready && SelectedPlanet != null)
                {
                    var assigned = system.State?.Members.FirstOrDefault(m => m.PeerId == connection.LocalPlayerId)?.Planet;
                    if (assigned != null && !system.ReconfirmPlanet())
                    { ShowJoinFailure(purpose, system.LastError ?? "planet_selection_locked"); return; }
                    colonyEntry.NativeReady(); entryDeadline = now + 30;
                }
                if (colonyEntry.Stage == ColonyEntryStage.AwaitingAdmission)
                {
                    if (colonyEntry.AdmissionConfirmed(system.Admitted))
                    { planetEntryDialog?.Dispose(); planetEntryDialog = null; }
                    else if (!string.IsNullOrEmpty(system.LastError) || now >= entryDeadline)
                        ShowJoinFailure(purpose, system.LastError ?? "planet_not_ready");
                }
            }
            // An authenticated reconnect retains the already chosen planet, but
            // requires a fresh native claim before any world authorization returns.
            if (!system.TravelConfirmed && SelectedPlanet != null &&
                system.State?.Members.Any(m => m.PeerId == connection.LocalPlayerId && m.Planet != null) == true)
                system.ReconfirmPlanet();
        }


        private void Update()
        {
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                PrepareModCatalog();
                var colony = OnlineColonyState.Current;
                if (colony != null && !ReferenceEquals(lastColony, colony))
                {
                    var selected = SelectedPlanet;
                    if (!colonyEntry.AwaitingNative && lastColonyHomeId != null && Session != null && (lastColonyHomeId != colony.HomeSystemId ||
                        (selected != null && lastColonyPlanetId != null && lastColonyPlanetId != selected.PlanetId)))
                    { Leave(); Error = "planet_colony_changed"; }
                    lastColony = colony;
                    lastColonyHomeId = colony.HomeSystemId;
                    lastColonyPlanetId = selected?.PlanetId;
                    if (Session == null) RoomName = colony.HomeSystemName;
                    Revision++;
                }
                if (Session != null)
                {
                    if (Session.IsHost && colony != null && !ReferenceEquals(moderationColony, colony))
                    {
                        BindModeration(Session, colony, true);
                        moderationColony = colony;
                    }
                    ModSynchronization?.Pump(now);
                    GameplayFramework.Current?.Pump(now);
                    Session.Pump();
                    ModSynchronization?.Pump(now);
                    UpdateModSynchronization(now);
                    if (Multiplayer != null) Multiplayer.Pump(Time.realtimeSinceStartupAsDouble);
                    StarMapDirectory?.Pump(Time.realtimeSinceStartupAsDouble);
                    if (Session.Closed)
                    {
                        string reason = Session.LastError;
                        Error = reason == "left_room" ? null : reason ?? "connection_closed";
                        ErrorDetail = Session.LastErrorDetail;
                        var next = pendingPreviewJoin; pendingPreviewJoin = null;
                        bool home = returningHome; returningHome = false;
                        Leave();
                        if (next != null && reason == "left_room") Join(next);
                        else if (home) OpenStarMap();
                    }
                }
                UpdatePlanetEntry(now);
                if (colonyEntry.TryBeginHost(NativeGameplayUi.Ready && colony != null))
                {
                    if (Session == null) Host(colony);
                    if (Session != null) colonyEntry.CreationConnected();
                    else if (Busy) colonyEntry.CreationQueued();
                    else { colonyEntry.CreationFailed(); ShowCreationFailure(); }
                }
                if (advertiser != null && Time.realtimeSinceStartupAsDouble >= nextAdvert)
                { Advertise(); nextAdvert = Time.realtimeSinceStartupAsDouble + 2; }
                if ((window != null || browser?.IsOpen == true) && Enabled && UseSteam && Session == null && !Busy && Time.realtimeSinceStartupAsDouble >= nextSearch)
                    Search(false);
                RoomListing room;
                int budget = 64;
                while (scanner != null && budget-- > 0 && scanner.TryReceive(out room))
                {
                    if (UseSteam || !Enabled || Session != null) continue;
                    Listings.RemoveAll(r => r.Code == room.Code);
                    if (Listings.Count < 64) Listings.Add(room);
                    Revision++;
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[OniOnline] Network service stopped: " + error);
                Error = error.Message == "moderation_state_invalid" ? error.Message : "network_failed";
                Leave();
            }
        }

        private void Advertise()
        {
            if (advertiser != null && Session != null)
                advertiser.Advertise(Session.RoomName, hostedPort, Session.Players.Length, hostedPassword, Session.Started);
            if (directory != null) directory.Advertise(Session);
        }

        internal Sprite Background()
        {
            if (background != null) return background;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // Unity 6 also exposes Span overloads. Select its byte[] ABI explicitly
            // so the Mod retains the repository's net48 compile profile.
            var conversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true);
            var loadImage = conversion.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
            if (loadImage == null || !(bool)loadImage.Invoke(null, new object[] { texture,
                File.ReadAllBytes(Path.Combine(modPath, "assets", "online-background-concept.png")), true }))
            { Destroy(texture); return null; }
            background = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            return background;
        }

        private static string LocalAddress()
        {
            return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback).SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString())
                .FirstOrDefault() ?? "127.0.0.1";
        }

        private void Attempt(System.Action action, bool clearError = true)
        {
            try
            {
                if (clearError) { Error = null; ErrorDetail = null; }
                action();
            }
            catch (Exception error)
            {
                Error = error is ArgumentException || error is InvalidOperationException ||
                    (error is InvalidDataException && error.Message == "moderation_state_invalid") ? error.Message : "network_failed";
                Debug.LogWarning("[OniOnline] Operation failed: " + error.GetType().Name);
            }
            Revision++;
        }

        internal void ShowWorldOverview()
        {
            HideWindow();
            Multiplayer?.Show();
        }

        internal void ObserveWorld(string ownerId)
        { HideWindow(); Multiplayer?.Show(ownerId); }

        internal void ShowWorkPlans(string ownerId, string planetId, WorkCell? draftCell = null)
        {
            if (Multiplayer == null || Session == null) return;
            CloseTeam();
            CloseWorkPlans();
            workPlanWindow = WorkPlanWindow.Create(this, ownerId, planetId);
            if (draftCell.HasValue)
            {
                var draft = Multiplayer.Work.Draft(planetId, RemoteWorkKind.Dig, new[] { draftCell.Value });
                Multiplayer.Work.SubmitDraft(ownerId, draft, true);
            }
        }
        private void CloseWorkPlans()
        { var old = workPlanWindow; workPlanWindow = null; if (old != null) old.Close(); }
        internal void ShowTeam()
        {
            if (Multiplayer == null || Session == null) return;
            HideWindow(); CloseSystemMap(); CloseWorkPlans(); Multiplayer.CloseWorldView();
            CloseTeam(); teamWindow = TeamColonyWindow.Create(this);
        }
        private void CloseTeam()
        { var old = teamWindow; teamWindow = null; if (old != null) old.Close(); }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "frontend")
            {
                CancelNativeNavigation();
                if (Session?.IsHost == true) Leave();
                awaitingSystemPreview = Session?.Joined == true && !Session.IsHost;
            }
            planetEntryDialog?.Dispose(); planetEntryDialog = null;
            HideWindow(); HideChat(); CloseSystemMap(); CloseWorkPlans(); CloseTeam(); Multiplayer?.CloseWorldView(); category = null;
        }
        private void OnDestroy()
        {
            destroying = true;
            var cleanup = new RoomCleanup();
            cleanup.Run("scene_unsubscribe", () => SceneManager.sceneLoaded -= OnSceneLoaded);
            cleanup.Run("leave", Leave);
            cleanup.Run("chat_window", HideChat);
            cleanup.Run("system_map", CloseSystemMap);
            cleanup.Run("work_window", CloseWorkPlans);
            cleanup.Run("team_window", CloseTeam);
            var oldBackground = background; background = null;
            cleanup.Run("background_texture", () => { if (oldBackground != null) Destroy(oldBackground.texture); });
            cleanup.Run("background_sprite", () => { if (oldBackground != null) Destroy(oldBackground); });
            if (Instance == this) Instance = null;
            ReportCleanup(cleanup);
        }
    }
}
