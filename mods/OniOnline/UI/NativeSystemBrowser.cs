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
        private RectTransform panel, list, codeRegion, canvasRect, filters, actions;
        private Transform entries;
        private GameObject entryTemplate;
        private ScrollRect scroll;
        private KButton refresh, joinByCode, codeToggle, close, steam, lan;
        private KInputTextField code, nickname, password, pendingFocus;
        private LocText status, title, description, emptyTitle, emptyHint;
        private GameObject advanced, emptyState;
        private Transform titleBar;
        private readonly List<GameObject> rows = new List<GameObject>();
        private int revision = -1, focusFrame;
        private string listSignature, passwordTargetCode;
        private bool codeOpen, advancedOpen, joining, closed, lastBusy, lastSearching;
        private bool scrollbarInitiallyActive;
        private Vector2 canvasSize;

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
                window.codeToggle = codeToggle;
                window.close = close;
                window.title = title;
                window.canvasRect = canvas.transform as RectTransform;
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
            list = (RectTransform)panel.Find("ListView");
            var footer = panel.Find("DetailsView");
            scroll = list.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null || entryTemplate.GetComponent<HierarchyReferences>() == null)
                throw new InvalidOperationException("native_browser_unavailable");
            entryTemplate.SetActive(false);
            foreach (Transform child in entries) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 14, 18);
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            titleBar = DirectChild(title.transform, panel);
            NativeUiTemplates.Style(title, 32, TextAlignmentOptions.MidlineLeft);
            NativeUiTemplates.Size(title.gameObject, -1, 48, 1);
            if (titleBar != null) NativeUiTemplates.Size(titleBar.gameObject, -1, 54);
            var listLayout = list.GetComponent<LayoutElement>() ?? list.gameObject.AddComponent<LayoutElement>();
            listLayout.minHeight = 80;
            listLayout.preferredHeight = -1;
            listLayout.flexibleHeight = 1;

            description = NativeUiTemplates.Label(panel, T("ui.empty_hint"));
            NativeUiTemplates.Style(description, 20, TextAlignmentOptions.MidlineLeft, true);
            description.alpha = 0.8f;
            NativeUiTemplates.Size(description.gameObject, -1, 48);
            filters = NativeUiTemplates.Row("SystemFilters", panel, 48);
            filters.GetComponent<HorizontalLayoutGroup>().spacing = 10;
            steam = Filter(filters, "Steam", true);
            lan = Filter(filters, T("settings.lan"), false);
            var filterSpace = NativeUiTemplates.Rect("FilterSpacing", filters);
            NativeUiTemplates.Size(filterSpace.gameObject, 0, 0, 1);
            refresh.transform.SetParent(filters, false);
            StyleButton(refresh, 144, 44);

            PrepareList();
            var statusRow = NativeUiTemplates.Row("SystemStatus", panel, 44);
            status = NativeUiTemplates.Label(statusRow, "");
            NativeUiTemplates.Style(status, 20, TextAlignmentOptions.MidlineLeft, true);
            NativeUiTemplates.Size(status.gameObject, -1, 44, 1);
            statusRow.gameObject.SetActive(false);

            codeRegion = NativeUiTemplates.Rect("JoinSystemByCode", panel);
            var codeLayout = codeRegion.gameObject.AddComponent<VerticalLayoutGroup>();
            codeLayout.spacing = 10;
            codeLayout.padding = new RectOffset(0, 0, 12, 0);
            codeLayout.childAlignment = TextAnchor.UpperLeft;
            codeLayout.childControlHeight = codeLayout.childControlWidth = true;
            codeLayout.childForceExpandHeight = false;
            var composer = NativeUiTemplates.Row("SystemCode", codeRegion, 44);
            code = NativeUiTemplates.Input(composer, runtime.JoinCode, T("ui.join_hint"), 256);
            code.gameObject.name = "SystemCodeInput";
            code.onValueChanged.AddListener(value => {
                runtime.JoinCode = value; BindPasswordTarget(value); RefreshControls();
            });
            code.onSubmit.AddListener(_ => RequestJoin(code.text));
            joinByCode = NativeUiTemplates.Button(composer, T("menu.join_room"), () => RequestJoin(code.text), 120);
            var options = NativeUiTemplates.Row("JoinOptions", codeRegion, 44);
            NativeUiTemplates.Button(options, T("lobby.network_settings"), () => {
                advancedOpen = !advancedOpen; UpdateCodeLayout();
            }, 160);
            advanced = NativeUiTemplates.Rect("ConnectionSettings", codeRegion).gameObject;
            var optionsLayout = advanced.AddComponent<VerticalLayoutGroup>();
            optionsLayout.spacing = 10;
            optionsLayout.childControlHeight = optionsLayout.childControlWidth = true;
            optionsLayout.childForceExpandHeight = false;
            NativeUiTemplates.Size(advanced, -1, 98);
            var nameRow = NativeUiTemplates.Row("PlayerName", advanced.transform, 44);
            NativeUiTemplates.Label(nameRow, T("room.player_name"), 100);
            nickname = NativeUiTemplates.Input(nameRow, runtime.Nickname, T("room.player_name"), 32);
            nickname.onValueChanged.AddListener(value => runtime.Nickname = value);
            var passwordRow = NativeUiTemplates.Row("SystemPassword", advanced.transform, 44);
            NativeUiTemplates.Label(passwordRow, T("room.password"), 100);
            runtime.Password = "";
            passwordTargetCode = (code.text ?? "").Trim();
            password = NativeUiTemplates.Input(passwordRow, "", T("ui.optional"), 128);
            password.contentType = TMP_InputField.ContentType.Password;
            password.onValueChanged.AddListener(value => runtime.Password = value);

            actions = NativeUiTemplates.Row("BrowserActions", panel, 54);
            codeToggle.transform.SetParent(actions, false);
            StyleButton(codeToggle, 260, 48);
            var actionSpace = NativeUiTemplates.Rect("ActionSpacing", actions);
            NativeUiTemplates.Size(actionSpace.gameObject, 0, 0, 1);
            close.transform.SetParent(actions, false);
            StyleButton(close, 140, 48);
            if (footer != null) footer.gameObject.SetActive(false);
            description.transform.SetAsLastSibling();
            filters.SetAsLastSibling();
            list.SetAsLastSibling();
            statusRow.SetAsLastSibling();
            codeRegion.SetAsLastSibling();
            actions.SetAsLastSibling();
            UpdateCodeLayout();
            UpdateGeometry();
        }

        private static Transform DirectChild(Transform child, Transform parent)
        {
            while (child != null && child.parent != parent) child = child.parent;
            return child;
        }

        private static void StyleButton(KButton button, float width, float height)
        {
            NativeUiTemplates.Size(button.gameObject, width, height);
            NativeUiTemplates.Style(button.GetComponentInChildren<LocText>(true), 20, TextAlignmentOptions.Center);
        }

        private KButton Filter(Transform parent, string text, bool useSteam)
        {
            var button = NativeUiTemplates.Button(parent, text, () => {
                if (runtime.Session != null || runtime.Busy) return;
                if (runtime.UseSteam != useSteam) { code.text = ""; ClearPassword(); }
                runtime.SelectTransport(useSteam); Refresh();
            }, 160);
            button.gameObject.name = useSteam ? "SteamFilter" : "LanFilter";
            var tooltip = button.GetComponent<ToolTip>();
            if (tooltip != null) { tooltip.OnToolTip = null; tooltip.SetSimpleTooltip(text); }
            return button;
        }

        private void PrepareList()
        {
            scrollbarInitiallyActive = scroll.verticalScrollbar != null && scroll.verticalScrollbar.gameObject.activeSelf;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = (RectTransform)entries;
            foreach (var old in content.GetComponents<LayoutGroup>()) DestroyImmediate(old);
            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(12, 12, 12, 12);
            group.spacing = 10;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            var fit = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            scroll.content = content;

            // Empty results belong in the viewport, not in a top-aligned mod row.
            var viewport = scroll.viewport == null ? content.parent : scroll.viewport;
            var empty = NativeUiTemplates.Rect("EmptySystemList", viewport);
            empty.anchorMin = Vector2.zero; empty.anchorMax = Vector2.one;
            empty.offsetMin = empty.offsetMax = Vector2.zero;
            emptyState = empty.gameObject;
            var emptyLayout = emptyState.AddComponent<VerticalLayoutGroup>();
            emptyLayout.padding = new RectOffset(28, 28, 20, 20);
            emptyLayout.spacing = 12;
            emptyLayout.childAlignment = TextAnchor.MiddleCenter;
            emptyLayout.childControlWidth = emptyLayout.childControlHeight = true;
            emptyLayout.childForceExpandWidth = true;
            emptyLayout.childForceExpandHeight = false;
            emptyTitle = NativeUiTemplates.Label(empty, "");
            NativeUiTemplates.Style(emptyTitle, 30, TextAlignmentOptions.Center, true);
            NativeUiTemplates.Size(emptyTitle.gameObject, -1, 50);
            emptyHint = NativeUiTemplates.Label(empty, T("ui.empty_hint"));
            NativeUiTemplates.Style(emptyHint, 20, TextAlignmentOptions.Center, true);
            NativeUiTemplates.Size(emptyHint.gameObject, -1, 70);
            emptyHint.alpha = 0.75f;
        }

        private void UpdateGeometry()
        {
            if (canvasRect == null || panel == null) return;
            Vector2 size = canvasRect.rect.size;
            if (size == canvasSize || size.x <= 0 || size.y <= 0) return;
            canvasSize = size;
            var fit = panel.GetComponent<ContentSizeFitter>();
            if (fit != null) fit.enabled = false;
            var panelLayout = panel.GetComponent<LayoutElement>();
            if (panelLayout != null) panelLayout.ignoreLayout = true;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(Mathf.Max(1, Mathf.Min(1120, size.x - 48)),
                Mathf.Max(1, Mathf.Min(820, size.y - 48)));
            bool narrow = panel.sizeDelta.x < 640;
            bool shortWindow = panel.sizeDelta.y < 600;
            filters.gameObject.SetActive(!codeOpen || !shortWindow);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(narrow ? 16 : 24, narrow ? 16 : 24, 12, 16);
            layout.spacing = shortWindow ? 8 : 10;
            description.gameObject.SetActive(!shortWindow && !codeOpen && rows.Count != 0);
            NativeUiTemplates.Style(title, narrow ? 28 : 32, TextAlignmentOptions.MidlineLeft);
            float providerWidth = narrow ? Mathf.Max(80, (panel.sizeDelta.x - 64) / 3) : 160;
            StyleButton(steam, providerWidth, 44);
            StyleButton(lan, providerWidth, 44);
            StyleButton(refresh, narrow ? providerWidth : 144, 44);
            StyleButton(codeToggle, narrow ? Mathf.Max(140, panel.sizeDelta.x - 164) : 260, 48);
            StyleButton(close, narrow ? 100 : 140, 48);
            foreach (var row in rows) SizeSystemRow(row.GetComponent<HierarchyReferences>(), narrow);
            LayoutRebuilder.MarkLayoutForRebuild(panel);
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
            if (!codeOpen) ClearInputFocus();
            else if (!advancedOpen)
            {
                nickname?.DeactivateInputField();
                password?.DeactivateInputField();
                if (pendingFocus == nickname || pendingFocus == password) pendingFocus = null;
            }
            codeRegion.gameObject.SetActive(codeOpen);
            list.gameObject.SetActive(!codeOpen);
            filters.gameObject.SetActive(!codeOpen || panel.rect.height >= 600);
            if (advanced != null) advanced.SetActive(advancedOpen);
            var region = NativeUiTemplates.Size(codeRegion.gameObject, -1, advancedOpen ? 220 : 110);
            region.flexibleHeight = 1;
            if (description != null) description.gameObject.SetActive(!codeOpen && panel.rect.height >= 600 && rows.Count != 0);
            NativeUiTemplates.Text(codeToggle.GetComponentInChildren<LocText>(true), T(codeOpen ? "menu.browse_lobbies" : "menu.join_by_code"));
            LayoutRebuilder.MarkLayoutForRebuild(panel);
        }

        private void Focus(KInputTextField input)
        { pendingFocus = input; focusFrame = Time.frameCount + 1; }

        private void ClearInputFocus()
        {
            pendingFocus = null;
            code?.DeactivateInputField();
            nickname?.DeactivateInputField();
            password?.DeactivateInputField();
            var events = UnityEngine.EventSystems.EventSystem.current;
            var selected = events == null ? null : events.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(transform)) events.SetSelectedGameObject(null);
        }

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

        private static void RefreshFilter(KButton filter, bool selected, bool enabled, string text)
        {
            filter.isInteractable = enabled;
            var label = filter.GetComponentInChildren<LocText>(true);
            label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            label.alpha = selected ? 1 : 0.75f;
            var background = filter.GetComponent<KImage>();
            if (background != null)
                background.ColorState = selected ? KImage.ColorSelector.Active : KImage.ColorSelector.Inactive;
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
            bool hasSystems = systems.Length != 0;
            description.gameObject.SetActive(hasSystems && !codeOpen && panel.rect.height >= 600);
            emptyState.SetActive(systems.Length == 0);
            entries.gameObject.SetActive(hasSystems);
            scroll.enabled = hasSystems;
            if (scroll.verticalScrollbar != null)
                scroll.verticalScrollbar.gameObject.SetActive(hasSystems && scrollbarInitiallyActive);
            if (!hasSystems)
            {
                scroll.StopMovement();
                var content = (RectTransform)entries;
                content.sizeDelta = new Vector2(content.sizeDelta.x, 0);
                scroll.verticalNormalizedPosition = 1;
            }
            NativeUiTemplates.Text(emptyTitle, T(runtime.Searching ? "browser.searching" : "ui.empty_title"));
            emptyHint.gameObject.SetActive(!runtime.Searching);
            if (runtime.Searching && systems.Length == 0 && !Connecting && runtime.Error == null && runtime.DependencyError == null)
                status.transform.parent.gameObject.SetActive(false);
            string signature = runtime.UseSteam + ":" + Connecting + ":" + runtime.Searching + ":" + string.Join("|", systems.Select(system =>
                system.Code + ":" + system.Name + ":" + system.Players + ":" + system.CanJoin + ":" + system.HasPassword + ":" + system.Started));
            if (signature == listSignature) return;
            listSignature = signature;
            foreach (var row in rows) { row.SetActive(false); Destroy(row); }
            rows.Clear();
            foreach (var system in systems) AddSystem(system);
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
            foreach (var old in row.GetComponents<LayoutGroup>()) DestroyImmediate(old);
            var fit = row.GetComponent<ContentSizeFitter>();
            if (fit != null) DestroyImmediate(fit);

            var title = row.GetReference<LocText>("Title");
            var details = row.GetReference<LocText>("Version");
            var join = row.GetReference<KButton>("ManageButton");
            var body = NativeUiTemplates.Rect("SystemRowContent", row.transform);
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(20, 12); body.offsetMax = new Vector2(-20, -12);
            var bodyLayout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            bodyLayout.spacing = 18;
            bodyLayout.childAlignment = TextAnchor.MiddleLeft;
            bodyLayout.childControlWidth = bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandHeight = bodyLayout.childForceExpandWidth = false;
            var textColumn = NativeUiTemplates.Rect("SystemIdentity", body);
            var textLayout = textColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            textLayout.spacing = 4;
            textLayout.childAlignment = TextAnchor.MiddleLeft;
            textLayout.childControlWidth = textLayout.childControlHeight = true;
            textLayout.childForceExpandWidth = true;
            textLayout.childForceExpandHeight = false;
            NativeUiTemplates.Size(textColumn.gameObject, -1, 64, 1);
            title.transform.SetParent(textColumn, false);
            details.transform.SetParent(textColumn, false);
            NativeUiTemplates.Style(title, 24, TextAlignmentOptions.MidlineLeft);
            NativeUiTemplates.Style(details, 18, TextAlignmentOptions.MidlineLeft);
            details.alpha = 0.8f;
            NativeUiTemplates.Size(title.gameObject, -1, 32, 1);
            NativeUiTemplates.Size(details.gameObject, -1, 28, 1);
            join.transform.SetParent(body, false);
            if (background.transform != row.transform)
            {
                background.transform.SetParent(row.transform, false);
                background.transform.SetAsFirstSibling();
                var rect = (RectTransform)background.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            // The mod entry's old icon/version/manage layout is no longer an
            // input to browser sizing. Keep only its native background and art.
            foreach (Transform child in row.transform)
                if (child != background.transform && child != body) child.gameObject.SetActive(false);
            SizeSystemRow(row, panel.rect.width < 640);
            rows.Add(row.gameObject);
            return row;
        }

        private static void SizeSystemRow(HierarchyReferences row, bool narrow)
        {
            NativeUiTemplates.Size(row.gameObject, -1, narrow ? 100 : 96, 1);
            StyleButton(row.GetReference<KButton>("ManageButton"), narrow ? 100 : 128, 44);
        }

        private void AddSystem(RoomListing system)
        {
            var row = Row();
            var title = row.GetReference<LocText>("Title");
            NativeUiTemplates.Text(title, system.Name);
            var count = row.GetReference<LocText>("Version");
            NativeUiTemplates.Text(count, string.Format(T("ui.player_count"), system.Players) + " · " +
                T(!system.CanJoin ? "room.full" : system.Started ? "room.in_progress" : "browser.joinable"));
            count.gameObject.SetActive(true);
            var tooltip = row.GetReference<ToolTip>("Description");
            tooltip.OnToolTip = null;
            tooltip.SetSimpleTooltip(system.Name + "\n" + T(system.Started ? "room.in_progress" : "room.waiting") + " · " +
                T(!system.CanJoin ? "room.full" : system.HasPassword ? "browser.password_required" : "browser.joinable"));
            var join = row.GetReference<KButton>("ManageButton");
            NativeUiTemplates.Bind(join, T(system.CanJoin ? "menu.join_room" : "room.full"), () => Choose(system));
            join.isInteractable = system.CanJoin && !Connecting;
        }

        private void Update()
        {
            if (closed || runtime == null) return;
            UpdateGeometry();
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
            ClearInputFocus();
            ClearPassword();
            if (runtime != null) runtime.BrowsingSystems = false;
            Deactivate();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
