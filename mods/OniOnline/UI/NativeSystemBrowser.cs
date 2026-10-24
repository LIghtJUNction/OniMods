using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Network;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal sealed class NativeSystemBrowser : KModalScreen
    {
        private OnlineRuntime runtime;
        private RectTransform panel, codeRegion;
        private Transform entries;
        private GameObject entryTemplate;
        private ScrollRect scroll;
        private KButton refresh, joinByCode;
        private MultiToggle steam, lan;
        private KInputTextField code, nickname, password, pendingFocus;
        private LocText status;
        private GameObject advanced;
        private readonly List<GameObject> rows = new List<GameObject>();
        private int revision = -1, focusFrame;
        private string listSignature, passwordTargetCode;
        private bool codeOpen, advancedOpen, joining, closed, lastBusy, lastSearching;

        internal bool IsOpen { get { return !closed && IsScreenActive(); } }
        private bool Connecting { get { return runtime.Busy || joining && runtime.Session != null &&
            (!runtime.Session.Joined || runtime.Session.Leaving || runtime.Session.Reconnecting) && !runtime.Session.Closed; } }

        internal static NativeSystemBrowser Open(OnlineRuntime runtime)
        {
            if (runtime == null || ScreenPrefabs.Instance == null || ScreenPrefabs.Instance.modsMenu == null)
                throw new InvalidOperationException("native_browser_unavailable");
            var canvas = NativeUiTemplates.RootCanvas();
            var staging = new GameObject("OniOnlineBrowserStaging");
            staging.SetActive(false);
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(ScreenPrefabs.Instance.modsMenu.gameObject, staging.transform, false);
                instance.name = "OniOnlineSystemBrowser";
                var native = instance.GetComponent<ModsScreen>();
                var title = NativeUiTemplates.Field<LocText>(native, "windowTitle");
                var closeTitle = NativeUiTemplates.Field<KButton>(native, "closeButtonTitle");
                var close = NativeUiTemplates.Field<KButton>(native, "closeButton");
                var refresh = NativeUiTemplates.Field<KButton>(native, "workshopButton");
                var codeToggle = NativeUiTemplates.Field<KButton>(native, "toggleAllButton");
                var entryTemplate = NativeUiTemplates.Field<GameObject>(native, "entryPrefab");
                var entries = NativeUiTemplates.Field<Transform>(native, "entryParent");
                // ModsScreen activation edits the real mod list. Retain its art,
                // layout and controls, and remove that controller while inactive.
                UnityEngine.Object.DestroyImmediate(native);
                var window = instance.AddComponent<NativeSystemBrowser>();
                window.pause = false;
                window.runtime = runtime;
                window.entries = entries;
                window.entryTemplate = entryTemplate;
                window.refresh = refresh;
                NativeUiTemplates.Text(title, T("menu.browse_lobbies"));
                closeTitle.ClearOnClick();
                closeTitle.ClearOnPointerEvents();
                closeTitle.onClick += window.Close;
                NativeUiTemplates.Bind(close, T("ui.close"), window.Close);
                NativeUiTemplates.Bind(refresh, T("ui.refresh"), () => runtime.Search());
                NativeUiTemplates.Bind(codeToggle, T("menu.join_by_code"), () => window.SetCodeOpen(!window.codeOpen));
                window.Build();
                instance.transform.SetParent(canvas.transform, false);
                window.Activate();
                window.Refresh();
                return window;
            }
            catch
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
                throw;
            }
            finally { UnityEngine.Object.Destroy(staging); }
        }

        private static string T(string key) { return OnlineText.Get(key); }
        public override bool IsModal() { return true; }
        public override float GetSortKey() { return 35; }

        public override void OnKeyDown(KButtonEvent e)
        {
            if (e.TryConsume(global::Action.Escape))
            {
                if (codeOpen) SetCodeOpen(false); else Close();
                return;
            }
            base.OnKeyDown(e);
            e.Consumed = true;
        }

        public override void OnKeyUp(KButtonEvent e)
        {
            base.OnKeyUp(e);
            e.Consumed = true;
        }

        private void Build()
        {
            panel = (RectTransform)transform.Find("Panel");
            var list = (RectTransform)panel.Find("ListView");
            var footer = panel.Find("DetailsView");
            scroll = list.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null || entryTemplate.GetComponent<HierarchyReferences>() == null)
                throw new InvalidOperationException("native_browser_unavailable");
            entryTemplate.SetActive(false);
            foreach (Transform child in entries) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var listLayout = list.GetComponent<LayoutElement>() ?? list.gameObject.AddComponent<LayoutElement>();
            listLayout.minHeight = 120;
            listLayout.preferredHeight = -1;
            listLayout.flexibleHeight = 1;

            var filters = NativeUiTemplates.Row("SystemFilters", panel);
            filters.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 16, 0, 0);
            filters.SetSiblingIndex(list.GetSiblingIndex());
            steam = Filter(filters, "Steam", true);
            lan = Filter(filters, T("settings.lan"), false);

            var statusRow = NativeUiTemplates.Row("SystemStatus", panel, 28);
            statusRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 16, 0, 0);
            statusRow.SetSiblingIndex(footer.GetSiblingIndex());
            status = NativeUiTemplates.Label(statusRow, "");
            NativeUiTemplates.Size(status.gameObject, -1, 28, 1);
            statusRow.gameObject.SetActive(false);

            codeRegion = NativeUiTemplates.Rect("JoinSystemByCode", panel);
            codeRegion.SetSiblingIndex(footer.GetSiblingIndex());
            var codeLayout = codeRegion.gameObject.AddComponent<VerticalLayoutGroup>();
            codeLayout.spacing = 6;
            codeLayout.padding = new RectOffset(16, 16, 0, 0);
            codeLayout.childControlHeight = codeLayout.childControlWidth = true;
            codeLayout.childForceExpandHeight = false;
            var composer = NativeUiTemplates.Row("SystemCode", codeRegion);
            code = NativeUiTemplates.Input(composer, runtime.JoinCode, T("ui.join_hint"), 256);
            code.gameObject.name = "SystemCodeInput";
            code.onValueChanged.AddListener(value => {
                runtime.JoinCode = value; BindPasswordTarget(value); RefreshControls();
            });
            code.onSubmit.AddListener(_ => RequestJoin(code.text));
            joinByCode = NativeUiTemplates.Button(composer, T("menu.join_room"), () => RequestJoin(code.text), 110);
            var options = NativeUiTemplates.Row("JoinOptions", codeRegion, 32);
            NativeUiTemplates.Button(options, T("lobby.network_settings"), () => {
                advancedOpen = !advancedOpen; UpdateCodeLayout();
            }, 140);
            advanced = NativeUiTemplates.Rect("ConnectionSettings", codeRegion).gameObject;
            var optionsLayout = advanced.AddComponent<VerticalLayoutGroup>();
            optionsLayout.spacing = 6;
            optionsLayout.childControlHeight = optionsLayout.childControlWidth = true;
            optionsLayout.childForceExpandHeight = false;
            NativeUiTemplates.Size(advanced, -1, 78);
            var nameRow = NativeUiTemplates.Row("PlayerName", advanced.transform);
            NativeUiTemplates.Label(nameRow, T("room.player_name"), 100);
            nickname = NativeUiTemplates.Input(nameRow, runtime.Nickname, T("room.player_name"), 32);
            nickname.onValueChanged.AddListener(value => runtime.Nickname = value);
            var passwordRow = NativeUiTemplates.Row("SystemPassword", advanced.transform);
            NativeUiTemplates.Label(passwordRow, T("room.password"), 100);
            runtime.Password = "";
            passwordTargetCode = (code.text ?? "").Trim();
            password = NativeUiTemplates.Input(passwordRow, "", T("ui.optional"), 128);
            password.contentType = TMP_InputField.ContentType.Password;
            password.onValueChanged.AddListener(value => runtime.Password = value);
            UpdateCodeLayout();
        }

        private MultiToggle Filter(Transform parent, string text, bool useSteam)
        {
            var row = NativeUiTemplates.Row(useSteam ? "SteamFilter" : "LanFilter", parent);
            NativeUiTemplates.Size(row.gameObject, useSteam ? 140 : 160, 36);
            var source = entryTemplate.GetComponent<HierarchyReferences>().GetReference<MultiToggle>("EnabledToggle");
            var toggle = Util.KInstantiateUI<MultiToggle>(source.gameObject, row.gameObject, true);
            toggle.onClick = null; toggle.onDoubleClick = null;
            toggle.onEnter = null; toggle.onExit = null; toggle.onHold = null; toggle.onStopHold = null;
            toggle.onClick = () => {
                if (runtime.Session != null || runtime.Busy) return;
                if (runtime.UseSteam != useSteam) { code.text = ""; ClearPassword(); }
                runtime.SelectTransport(useSteam); Refresh();
            };
            NativeUiTemplates.Size(toggle.gameObject, 28, 28);
            var tooltip = toggle.GetComponent<ToolTip>();
            if (tooltip != null) { tooltip.OnToolTip = null; tooltip.SetSimpleTooltip(text); }
            NativeUiTemplates.Label(row, text);
            return toggle;
        }

        private void SetCodeOpen(bool value)
        {
            codeOpen = value;
            UpdateCodeLayout();
            if (value) Focus(code);
        }

        private void UpdateCodeLayout()
        {
            if (codeRegion == null) return;
            codeRegion.gameObject.SetActive(codeOpen);
            if (advanced != null) advanced.SetActive(advancedOpen);
            NativeUiTemplates.Size(codeRegion.gameObject, -1, advancedOpen ? 158 : 74);
            LayoutRebuilder.MarkLayoutForRebuild(panel);
        }

        private void Focus(KInputTextField input)
        { pendingFocus = input; focusFrame = Time.frameCount + 1; }

        private void BindPasswordTarget(string systemCode)
        {
            string target = (systemCode ?? "").Trim();
            if (target == passwordTargetCode) return;
            ClearPassword();
            passwordTargetCode = target;
        }

        private void ClearPassword()
        {
            if (runtime != null) runtime.Password = "";
            if (password != null) password.SetTextWithoutNotify("");
            passwordTargetCode = null;
        }

        private void Choose(RoomListing system)
        {
            if (!system.CanJoin || Connecting) return;
            code.text = system.Code;
            if (system.HasPassword)
            {
                SetCodeOpen(true); advancedOpen = true; UpdateCodeLayout();
                NativeUiTemplates.Text(status, T("browser.password_required"));
                status.transform.parent.gameObject.SetActive(true);
                Focus(password);
                return;
            }
            password.text = runtime.Password = "";
            RequestJoin(system.Code);
        }

        private void RequestJoin(string systemCode)
        {
            if (Connecting) return;
            if (!Protocol.ValidText(runtime.Nickname, 32))
            {
                codeOpen = advancedOpen = true; UpdateCodeLayout(); Focus(nickname);
                NativeUiTemplates.Text(status, T("error.invalid_name"));
                status.transform.parent.gameObject.SetActive(true);
                return;
            }
            joining = true;
            runtime.PreviewSystem((systemCode ?? "").Trim());
            Refresh();
        }

        private string StatusText()
        {
            string error = runtime.Error ?? runtime.Session?.LastError;
            if (error != null)
            {
                if (error.StartsWith("mod_sync", StringComparison.Ordinal)) return "";
                if (error == "wrong_password") return T("error.wrong_password");
                if (error == "room_full") return T("room.full");
                if (error == "invalid_address") return T("browser.invalid_code");
                if (error == "capability_mismatch") return T("error.capability_mismatch");
                if (error == "gameplay_mismatch") return T("error.gameplay_mismatch");
                if (error == "native_adapter_unavailable") return T("error.native_adapter_unavailable");
                return T("browser.connection_failed");
            }
            if (runtime.DependencyError != null) return T("browser.connection_failed");
            if (joining && runtime.Session?.Joined != true) return T("ui.connecting");
            return runtime.Searching ? T("browser.searching") : "";
        }

        private void RefreshControls()
        {
            bool busy = Connecting;
            refresh.isInteractable = !busy;
            if (joinByCode != null) joinByCode.isInteractable = !busy && !string.IsNullOrWhiteSpace(code.text);
            bool waitingForJoin = joining && Connecting;
            if (code != null) code.interactable = !waitingForJoin;
            if (nickname != null) nickname.interactable = !waitingForJoin;
            if (password != null) password.interactable = !waitingForJoin;
            bool filtersEnabled = runtime.Session == null && !runtime.Busy;
            RefreshFilter(steam, runtime.UseSteam, filtersEnabled, "Steam");
            RefreshFilter(lan, !runtime.UseSteam, filtersEnabled, T("settings.lan"));
        }

        private static void RefreshFilter(MultiToggle filter, bool selected, bool enabled, string text)
        {
            filter.ChangeState(selected ? 1 : 0);
            filter.enabled = enabled;
            foreach (var image in filter.GetComponentsInChildren<ImageToggleState>(true))
                image.SetState(enabled ? selected ? ImageToggleState.State.Active : ImageToggleState.State.Inactive :
                    selected ? ImageToggleState.State.DisabledActive : ImageToggleState.State.Disabled);
            var tooltip = filter.GetComponent<ToolTip>();
            if (tooltip != null) tooltip.SetSimpleTooltip(enabled ? text : T("error.already_connected"));
        }

        private void Refresh()
        {
            revision = runtime.Revision;
            lastBusy = runtime.Busy;
            lastSearching = runtime.Searching;
            if (runtime.Error != null) joining = false;
            RefreshControls();
            NativeUiTemplates.Text(status, StatusText());
            status.transform.parent.gameObject.SetActive(status.text.Length != 0);
            var systems = runtime.Listings.Where(MatchesProvider).ToArray();
            string signature = runtime.UseSteam + ":" + Connecting + ":" + runtime.Searching + ":" + string.Join("|", systems.Select(system =>
                system.Code + ":" + system.Name + ":" + system.Players + ":" + system.CanJoin + ":" + system.HasPassword + ":" + system.Started));
            if (signature == listSignature) return;
            listSignature = signature;
            foreach (var row in rows) { row.SetActive(false); Destroy(row); }
            rows.Clear();
            foreach (var system in systems) AddSystem(system);
            if (rows.Count == 0) AddEmpty();
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)entries);
        }

        private bool MatchesProvider(RoomListing system)
        {
            if (system == null) return false;
            try { return RoomAddress.Parse(system.Code).Steam == runtime.UseSteam; }
            catch (ArgumentException) { return false; }
        }

        private HierarchyReferences Row()
        {
            var row = Util.KInstantiateUI<HierarchyReferences>(entryTemplate, entries.gameObject, true);
            row.gameObject.name = "PublicStarSystem";
            var drag = row.GetComponent<DragMe>();
            if (drag != null) DestroyImmediate(drag);
            var indicator = row.transform.Find("DragReorderIndicator");
            if (indicator != null) indicator.gameObject.SetActive(false);
            row.GetReference<MultiToggle>("EnabledToggle").gameObject.SetActive(false);
            var background = row.GetReference<KImage>("BG");
            background.defaultState = KImage.ColorSelector.Inactive;
            background.ColorState = KImage.ColorSelector.Inactive;
            rows.Add(row.gameObject);
            return row;
        }

        private void AddSystem(RoomListing system)
        {
            var row = Row();
            var title = row.GetReference<LocText>("Title");
            NativeUiTemplates.Text(title, system.Name);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            var count = row.GetReference<LocText>("Version");
            NativeUiTemplates.Text(count, string.Format(T("ui.player_count"), system.Players) + " · " +
                T(!system.CanJoin ? "room.full" : system.Started ? "room.in_progress" : "browser.joinable"));
            count.textWrappingMode = TextWrappingModes.NoWrap;
            count.overflowMode = TextOverflowModes.Ellipsis;
            count.gameObject.SetActive(true);
            var tooltip = row.GetReference<ToolTip>("Description");
            tooltip.OnToolTip = null;
            tooltip.SetSimpleTooltip(T(system.Started ? "room.in_progress" : "room.waiting") + " · " +
                T(!system.CanJoin ? "room.full" : system.HasPassword ? "browser.password_required" : "browser.joinable"));
            var join = row.GetReference<KButton>("ManageButton");
            NativeUiTemplates.Bind(join, T(system.CanJoin ? "menu.join_room" : "room.full"), () => Choose(system));
            join.isInteractable = system.CanJoin && !Connecting;
        }

        private void AddEmpty()
        {
            var row = Row();
            NativeUiTemplates.Text(row.GetReference<LocText>("Title"), T(runtime.Searching ? "browser.searching" : "ui.empty_title"));
            row.GetReference<LocText>("Version").gameObject.SetActive(false);
            row.GetReference<KButton>("ManageButton").gameObject.SetActive(false);
            var tooltip = row.GetReference<ToolTip>("Description");
            tooltip.OnToolTip = null;
            tooltip.SetSimpleTooltip(T("ui.empty_hint"));
        }

        private void Update()
        {
            if (closed || runtime == null) return;
            if (revision != runtime.Revision || lastBusy != runtime.Busy || lastSearching != runtime.Searching) Refresh();
            if (pendingFocus != null && Time.frameCount >= focusFrame)
            {
                var input = pendingFocus; pendingFocus = null;
                if (input.gameObject.activeInHierarchy)
                {
                    UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(input.gameObject);
                    input.Select(); input.ActivateInputField();
                }
            }
        }

        internal void Close()
        {
            if (closed) return;
            closed = true;
            ClearPassword();
            if (runtime != null) runtime.BrowsingSystems = false;
            Deactivate();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
