using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using HarmonyLib;
using OniOnline.Network;
using OniOnline.UI;
using OniOnline.Multiplayer;
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
        internal bool Enabled;
        internal bool UseSteam;
        internal string Nickname;
        internal string RoomName = "OniOnline";
        internal string Port = "27140";
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
        internal readonly List<RoomListing> Listings = new List<RoomListing>();
        private SteamRoomDirectory directory;
        private LanDiscovery advertiser;
        private LanDiscovery scanner;
        private OnlineWindow window;
        private ClusterCategorySelectionScreen category;
        private double nextAdvert;
        private double nextSearch;
        private Sprite background;
        private string modPath;
        private SessionProfile pendingProfile;
        private string pendingPassword;
        private int hostedPort;
        private bool hostedPassword;
        private bool openMapAfterStart;
        private OnlineColonyState lastColony;
        private OnlineColonyState moderationColony;
        private string lastColonyHomeId;
        private string lastColonyPlanetId;
        private StarSystemMapWindow systemMapWindow;
        private WorkPlanWindow workPlanWindow;
        private TeamColonyWindow teamWindow;
        private bool awaitingSystemPreview;
        private bool returningHome;
        private bool destroying;
        private string pendingPreviewCode;
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
        { get { return OnlineColonyState.Current?.CapturePlanet(); } }

        internal void SelectNextPlanet()
        {
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
            pendingPreviewCode = null;
            if (Session != null && !Session.IsHost && Session.RequestLeave())
            { returningHome = true; Revision++; return; }
            Leave(); OpenStarMap();
        }

        internal void ShowSystemMap()
        {
            if (ClusterMapScreen.Instance != null) ClusterMapScreen.Instance.GetComponent<StarSystemPanel>()?.CloseWindow();
            CloseWorkPlans();
            CloseTeam();
            HideWindow();
            if (systemMapWindow == null) systemMapWindow = StarSystemMapWindow.Create(this);
        }
        private void CloseSystemMap()
        { var old = systemMapWindow; systemMapWindow = null; if (old != null) old.Close(); }

        internal void BrowseSystems()
        {
            CloseSystemMap(); BrowsingSystems = true;
            if (window == null) window = OnlineWindow.Create(this);
            else window.Activate();
            if (!Enabled) SetEnabled(true);
            if (!Busy) Search();
            Revision++;
        }

        internal void PreviewSystem(string code)
        {
            if (Session != null && !Session.IsHost)
            {
                pendingPreviewCode = code;
                if (Session.RequestLeave()) { Revision++; return; }
            }
            if (Session != null) Leave();
            JoinCode = code; Join();
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
            CloseSystemMap(); openMapAfterStart = true; BeginNativeGame();
        }
        internal void LoadRiftColony()
        {
            if (Game.Instance != null || MainMenu.Instance == null) { Error = "planet_not_ready"; Revision++; return; }
            CloseSystemMap(); openMapAfterStart = true;
            AccessTools.Method(typeof(MainMenu), "LoadGame").Invoke(MainMenu.Instance, null);
        }

        internal string DependencyError
        {
            get
            {
                if (!CycleTrimPresent) return "dependency.cycletrim_required";
                if (!DlcManager.IsExpansion1Active()) return "dependency.spaced_out_required";
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
            var options = POptions.ReadSettings<OnlineOptions>() ?? new OnlineOptions();
            UseSteam = options.PreferSteamLobbies && SteamManager.Initialized;
            Nickname = PlayerPrefs.GetString("OniOnline.Nickname", SteamManager.Initialized ? SteamFriends.GetPersonaName() : Environment.UserName);
            if (Nickname.Length > 32) Nickname = Nickname.Substring(0, char.IsHighSurrogate(Nickname[31]) ? 31 : 32);
            RoomName = DefaultHomeName;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Debug.Log("[OniOnline] Preview loaded: native star map, Planetary Engine entry, public rooms and independent local time; observations only, no colony migration.");
        }

        internal void ShowWindow()
        {
            CloseSystemMap(); BrowsingSystems = false;
            if (window != null) { window.Activate(); return; }
            window = OnlineWindow.Create(this);
            if (Enabled && Session == null && !Busy) Search();
        }

        internal void HideWindow()
        {
            if (window == null) return;
            window.Deactivate();
            Destroy(window.transform.root.gameObject);
            window = null;
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
                CycleTrimVersion = CycleTrimVersion, SpacedOut = true };
        }

        internal void Host()
        {
            Attempt(() =>
            {
                if (Session != null || Busy) throw new InvalidOperationException("already_connected");
                var profile = Profile();
                if (!Protocol.ValidText(RoomName, 64)) throw new ArgumentException("invalid_room_name");
                if (Password.Length > 128) throw new ArgumentException("password_too_long");
                var colony = OnlineColonyState.Current;
                if (colony != null) RoomModerationPersistence.Decode(colony.HomeSystemId, colony.SavedAccountBans);
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
            });
        }

        internal void Join()
        {
            Attempt(() =>
            {
                if (Session != null || Busy) throw new InvalidOperationException("already_connected");
                var profile = Profile();
                if (Password.Length > 128) throw new ArgumentException("password_too_long");
                var address = RoomAddress.Parse(JoinCode);
                UseSteam = address.Steam;
                if (address.Steam)
                {
                    pendingProfile = profile; pendingPassword = Password;
                    SteamDirectory().Join(address.LobbyId);
                }
                else Attach(TcpRoomTransport.Join(address.Host, address.Port), profile, Password,
                    "oni://" + (address.Host.Contains(":") ? "[" + address.Host + "]" : address.Host) + ":" + address.Port);
                awaitingSystemPreview = true;
            });
        }

        private void Attach(IRoomTransport transport, SessionProfile profile, string password, string code)
        {
            RoomSession next = null;
            WorldCoordinator worlds;
            OnlineColonyState boundColony = null;
            try
            {
                next = new RoomSession(transport, profile, password, RoomName);
                boundColony = next.IsHost ? OnlineColonyState.Current : null;
                if (boundColony != null) BindModeration(next, boundColony, false);
                worlds = new WorldCoordinator(next);
            }
            catch
            {
                var cleanup = new RoomCleanup();
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
            Multiplayer.Changed += () => Revision++;
            Session.Changed += () => { Revision++; Advertise(); };
            Session.GameStartRequested += () => { if (Session?.IsHost == true) BeginNativeGame(); };
            Revision++;
            Advertise();
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
            directory.Ready += (transport, code) =>
            {
                Attempt(() => Attach(transport, pendingProfile, pendingPassword, code));
                pendingPassword = null; pendingProfile = null;
            };
            directory.Failed += reason => { Error = reason; pendingPassword = null; pendingProfile = null; Revision++; };
            directory.Found += rooms =>
            {
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
            // Detach all owners first. A failed UI/native close cannot strand the rest,
            // and a reentrant Leave must never dispose the same connection twice.
            var oldMultiplayer = Multiplayer; Multiplayer = null;
            var oldSession = Session; Session = null;
            moderationColony = null;
            var oldAdvertiser = advertiser; advertiser = null;
            var oldScanner = scanner; scanner = null;
            var oldDirectory = directory; directory = null;
            pendingProfile = null; pendingPassword = null;
            RoomCode = ""; awaitingSystemPreview = false;
            var cleanup = new RoomCleanup();
            cleanup.Run("work_window", CloseWorkPlans);
            cleanup.Run("team_window", CloseTeam);
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
            category = screen;
            BrowseSystems();
        }

        internal void OpenStarMap(bool showEngine = false)
        {
            if (Game.Instance == null) { ShowSystemMap(); return; }
            if (!Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() || ClusterMapScreen.Instance == null) return;
            HideWindow();
            if (!ClusterMapScreen.Instance.IsScreenActive()) ManagementMenu.Instance.ToggleClusterMap();
            var panel = StarSystemPanel.Attach(ClusterMapScreen.Instance);
            if (showEngine) { CloseSystemMap(); panel.ShowEngine(); }
            else ShowSystemMap();
        }

        internal void HostFromStarMap()
        {
            if (Game.Instance == null || !Game.Instance.baseAlreadyCreated) return;
            if (SelectedPlanet == null) { Error = "planet_not_ready"; Revision++; return; }
            SaveHomeName();
            if (!Enabled) SetEnabled(true);
            Host();
            if (Session != null && Session.IsHost && Session.Joined) Session.StartGame();
        }

        internal void CreateSystemFromLobby()
        {
            if (Game.Instance != null && Game.Instance.baseAlreadyCreated) { OpenStarMap(); return; }
            openMapAfterStart = true;
            BeginNativeGame();
        }

        private void BeginNativeGame()
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
                    .Invoke(chosen, new object[] { ProcGen.ClusterLayout.ClusterCategory.SpacedOutStyle });
                return;
            }
            var menu = MainMenu.Instance;
            if (menu != null && menu.IsActive())
            {
                var button = AccessTools.Field(typeof(MainMenu), "Button_NewGame").GetValue(menu) as KButton;
                if (button != null && button.isInteractable) button.SignalClick(KKeyCode.Mouse0);
            }
        }

        private void Update()
        {
            try
            {
                var colony = OnlineColonyState.Current;
                if (colony != null && !ReferenceEquals(lastColony, colony))
                {
                    var selected = colony.CapturePlanet();
                    if (lastColonyHomeId != null && Session != null && (lastColonyHomeId != colony.HomeSystemId ||
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
                    Session.Pump();
                    if (Multiplayer != null) Multiplayer.Pump(Time.realtimeSinceStartupAsDouble);
                    if (Session.Closed)
                    {
                        string reason = Session.LastError;
                        Error = reason == "left_room" ? null : reason ?? "connection_closed";
                        ErrorDetail = Session.LastErrorDetail;
                        string next = pendingPreviewCode; pendingPreviewCode = null;
                        bool home = returningHome; returningHome = false;
                        Leave();
                        if (next != null && reason == "left_room") { JoinCode = next; Join(); }
                        else if (home) OpenStarMap();
                    }
                }
                if (awaitingSystemPreview && Session?.Joined == true && Multiplayer?.StarSystem.State != null)
                { awaitingSystemPreview = false; ShowSystemMap(); }
                if (openMapAfterStart && Game.Instance != null && Game.Instance.baseAlreadyCreated && !Game.Instance.IsLoading() && ClusterMapScreen.Instance != null)
                { openMapAfterStart = false; OpenStarMap(); }
                if (advertiser != null && Time.realtimeSinceStartupAsDouble >= nextAdvert)
                { Advertise(); nextAdvert = Time.realtimeSinceStartupAsDouble + 2; }
                if (window != null && Enabled && UseSteam && Session == null && !Busy && Time.realtimeSinceStartupAsDouble >= nextSearch)
                    Search(false);
                RoomListing room;
                int budget = 64;
                while (scanner != null && budget-- > 0 && scanner.TryReceive(out room))
                {
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
        { HideWindow(); CloseSystemMap(); CloseWorkPlans(); CloseTeam(); Multiplayer?.CloseWorldView(); category = null; }
        private void OnDestroy()
        {
            destroying = true;
            var cleanup = new RoomCleanup();
            cleanup.Run("scene_unsubscribe", () => SceneManager.sceneLoaded -= OnSceneLoaded);
            cleanup.Run("leave", Leave);
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
