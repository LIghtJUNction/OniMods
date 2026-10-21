using System.Collections.Generic;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal sealed class StarSystemMapWindow : KScreen
    {
        private OnlineRuntime runtime;
        private GameObject root;
        private SystemHexMap map;
        private Transform memberList;
        private Transform preparation;
        private Transform sourceActions;
        private Transform focusCard;
        private Transform nameCard;
        private Transform steps;
        private RectTransform sidebarRect, actionsRect;
        private TextMeshProUGUI title, status, planet, landing, memberCount;
        private TextMeshProUGUI preparationTitle, sourceHelp, focusName, focusPlanet;
        private TextMeshProUGUI positionStep, planetStep, joinStep;
        private TMP_InputField nameInput;
        private Button arrive, choosePlanet, create, load, observe, home, host;
        private int revision = -1;
        private string memberSignature;
        private string selectedMember;
        private int? requestedQ;
        private int requestedR;
        private StarSystemState displayedState;
        private bool closed;
        private readonly List<MemberRow> memberRows = new List<MemberRow>();

        private sealed class MemberRow
        {
            internal Button Button;
            internal TextMeshProUGUI Number, Name, Planet;
            internal string PeerId;
            internal int Q, R;
            internal bool HasPosition;
        }

        private static string T(string key) { return OnlineText.Get(key); }
        internal SystemHexMap Map { get { return map; } }
        internal string EntryStatus { get { return status.text; } }
        internal bool CanJoinSystem { get { return arrive.gameObject.activeSelf && arrive.interactable; } }

        internal static StarSystemMapWindow Create(OnlineRuntime runtime)
        {
            var root = OnlineWidgets.CanvasRoot("OniOnlineStarMap", 30012);
            var backdrop = OnlineWidgets.Rect("Backdrop", root.transform);
            OnlineWidgets.Stretch((RectTransform)backdrop.transform);
            OnlineWidgets.Surface(backdrop, new Color(0.045f, 0.043f, 0.065f, 1));
            var panel = OnlineWidgets.Rect("StarMap", root.transform);
            OnlineWidgets.Stretch((RectTransform)panel.transform);
            ((RectTransform)panel.transform).offsetMin = new Vector2(20, 20);
            ((RectTransform)panel.transform).offsetMax = new Vector2(-20, -20);
            var window = panel.AddComponent<StarSystemMapWindow>();
            window.root = root; window.runtime = runtime;
            window.Build(); window.Activate(); return window;
        }

        public override bool IsModal() { return true; }
        public override float GetSortKey() { return MODAL_SCREEN_SORT_KEY + 2; }
        public override void OnKeyDown(KButtonEvent e)
        { if (e.TryConsume(global::Action.Escape)) Close(); e.Consumed = true; }
        public override void OnKeyUp(KButtonEvent e) { e.Consumed = true; }
        internal void Close()
        {
            if (closed) return;
            closed = true; Deactivate();
            if (root != null) { root.SetActive(false); Destroy(root); }
        }

        private static TextMeshProUGUI Label(Transform parent, string text, int size, int height)
        {
            var label = OnlineWidgets.Label(parent, text, size);
            label.textWrappingMode = TextWrappingModes.Normal;
            OnlineWidgets.Size(label.gameObject, -1, height); return label;
        }

        private static Transform Card(Transform parent, string name)
        {
            var column = OnlineWidgets.Column(parent, name, 12, 8);
            OnlineWidgets.Surface(column.gameObject, OnlineWidgets.PanelColor, true);
            return column;
        }

        private Button Action(Transform parent, string name, string key, System.Action click, bool primary = false)
        {
            var button = OnlineWidgets.Button(parent, T(key), click, -1);
            button.gameObject.name = name;
            if (primary) OnlineWidgets.Tint(button, OnlineWidgets.AccentColor);
            return button;
        }

        private void Build()
        {
            var heading = OnlineWidgets.Row(transform, 44);
            var headingRect = (RectTransform)heading;
            headingRect.anchorMin = new Vector2(0, 1); headingRect.anchorMax = Vector2.one;
            headingRect.pivot = new Vector2(0.5f, 1); headingRect.sizeDelta = new Vector2(0, 44);
            heading.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(14, 4, 2, 2);
            OnlineWidgets.Surface(heading.gameObject, OnlineWidgets.AccentColor, true);
            title = Label(heading, "", 23, 40);
            OnlineWidgets.Size(title.gameObject, -1, 40, 1);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            OnlineWidgets.Button(heading, T("entry.browse"), () => { Close(); runtime.BrowseSystems(); }, 174);
            OnlineWidgets.Button(heading, T("system.chat"), () => { Close(); runtime.ShowWindow(); }, 156);
            OnlineWidgets.Button(heading, T("moderation.close"), Close, 76);

            var body = OnlineWidgets.Rect("Body", transform);
            OnlineWidgets.Stretch((RectTransform)body.transform);
            ((RectTransform)body.transform).offsetMax = new Vector2(0, -56);

            // The complete sidebar can scroll at small window sizes. Its controls never
            // squeeze the native minimum button height or spill over the chart.
            Transform side;
            var sidebar = OnlineWidgets.Scroll(body.transform, 0, out side);
            sidebar.gameObject.name = "SystemSidebar";
            var sideRect = (RectTransform)sidebar.transform; sidebarRect = sideRect;
            sideRect.anchorMin = new Vector2(1, 0); sideRect.anchorMax = Vector2.one;
            sideRect.offsetMin = new Vector2(-356, 0); sideRect.offsetMax = Vector2.zero;
            side.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(10, 10, 10, 10);
            side.GetComponent<VerticalLayoutGroup>().spacing = 12;

            var primaryActions = OnlineWidgets.Column(body.transform, "SystemPrimaryActions", 10, 8);
            actionsRect = (RectTransform)primaryActions;
            actionsRect.anchorMin = new Vector2(1, 0); actionsRect.anchorMax = new Vector2(1, 0);
            actionsRect.pivot = new Vector2(1, 0);
            actionsRect.sizeDelta = new Vector2(356, 60);
            OnlineWidgets.Surface(primaryActions.gameObject, OnlineWidgets.PanelColor, true);

            var overview = Card(side, "SystemStatus");
            OnlineWidgets.SectionHeader(overview, T("entry.overview"));
            status = Label(overview, "", 17, 80);

            nameCard = Card(side, "HomeSystemName");
            Label(nameCard, T("ui.room_name"), 16, 24).color = OnlineWidgets.MutedColor;
            nameInput = OnlineWidgets.Input(nameCard, runtime.RoomName, T("ui.room_name"));
            nameInput.gameObject.name = "SystemName";
            nameInput.characterLimit = 64;
            nameInput.onValueChanged.AddListener(value => runtime.RoomName = value);
            nameInput.onEndEdit.AddListener(_ => runtime.SaveHomeName());

            var members = Card(side, "MemberCard");
            OnlineWidgets.SectionHeader(members, T("map.players"));
            var memberScroll = OnlineWidgets.Scroll(members, 204, out memberList);
            memberScroll.gameObject.name = "SystemPlayers";
            memberList.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(6, 6, 6, 6);
            memberList.GetComponent<VerticalLayoutGroup>().spacing = 6;

            focusCard = Card(side, "SelectedMember");
            OnlineWidgets.SectionHeader(focusCard, T("entry.selected_member"));
            focusName = Label(focusCard, "", 20, 30);
            focusName.textWrappingMode = TextWrappingModes.NoWrap;
            focusName.overflowMode = TextOverflowModes.Ellipsis;
            focusPlanet = Label(focusCard, "", 17, 72);
            observe = Action(focusCard, "ObserveSystemMember", "map.observe",
                () => { Close(); runtime.ObserveWorld(selectedMember); }, true);

            preparation = Card(side, "SystemPreparation");
            preparationTitle = OnlineWidgets.SectionHeader(preparation, T("entry.choose_source"));
            landing = Label(preparation, "", 17, 52);
            planet = Label(preparation, "", 19, 84);
            choosePlanet = Action(preparation, "ChooseSystemAsteroid", "planet.select", OpenPlanetSelection);
            sourceActions = OnlineWidgets.Column(primaryActions, "SourceActions", 0, 8);
            create = Action(sourceActions, "CreateSystemAsteroid", "map.create_planet",
                () => { Close(); runtime.CreateRiftColony(); }, true);
            load = Action(sourceActions, "LoadSystemAsteroid", "map.load_planet",
                () => { Close(); runtime.LoadRiftColony(); });
            sourceHelp = Label(preparation, "", 15, 110);
            sourceHelp.color = OnlineWidgets.MutedColor;
            arrive = Action(primaryActions, "JoinSelectedSystem", "entry.join",
                () => { runtime.ConfirmRift(); Refresh(); }, true);
            host = Action(primaryActions, "OpenOwnSystem", "system.open", runtime.HostFromStarMap, true);
            home = Action(primaryActions, "ReturnFromSystem", "planet.return_home", () => { Close(); runtime.ReturnHome(); });

            var mapRoot = OnlineWidgets.Rect("HexMap", body.transform);
            var mapRect = (RectTransform)mapRoot.transform;
            OnlineWidgets.Stretch(mapRect); mapRect.offsetMax = new Vector2(-368, 0);
            OnlineWidgets.Surface(mapRoot, new Color(0.075f, 0.073f, 0.105f), true);
            mapRoot.AddComponent<RectMask2D>();
            var mapHeading = OnlineWidgets.SectionHeader(mapRoot.transform, T("art.system_map"), 36);
            var mapHeadingRect = (RectTransform)mapHeading.transform.parent;
            mapHeadingRect.anchorMin = new Vector2(0, 1); mapHeadingRect.anchorMax = Vector2.one;
            mapHeadingRect.pivot = new Vector2(0.5f, 1); mapHeadingRect.sizeDelta = new Vector2(0, 36);
            mapHeading.rectTransform.offsetMax = new Vector2(-160, -2);
            memberCount = OnlineWidgets.Label(mapHeadingRect, "", 15);
            memberCount.alignment = TextAlignmentOptions.MidlineRight;
            memberCount.color = OnlineWidgets.MutedColor;
            memberCount.rectTransform.anchorMin = memberCount.rectTransform.anchorMax = Vector2.one;
            memberCount.rectTransform.pivot = Vector2.one;
            memberCount.rectTransform.sizeDelta = new Vector2(150, 36);
            memberCount.rectTransform.anchoredPosition = new Vector2(-12, 0);

            steps = OnlineWidgets.Row(mapRoot.transform, 44);
            steps.gameObject.name = "SystemJoinSteps";
            var stepsRect = (RectTransform)steps;
            stepsRect.anchorMin = new Vector2(0, 1); stepsRect.anchorMax = Vector2.one;
            stepsRect.pivot = new Vector2(0.5f, 1);
            stepsRect.anchoredPosition = new Vector2(0, -44);
            stepsRect.sizeDelta = new Vector2(-24, 44);
            positionStep = StepLabel(steps, "entry.choose_position_step");
            planetStep = StepLabel(steps, "entry.choose_planet_step");
            joinStep = StepLabel(steps, "entry.confirm_step");

            var footer = OnlineWidgets.Row(mapRoot.transform, 36);
            var footerRect = (RectTransform)footer;
            footerRect.anchorMin = Vector2.zero; footerRect.anchorMax = new Vector2(1, 0);
            footerRect.pivot = new Vector2(0.5f, 0);
            footerRect.sizeDelta = new Vector2(-20, 36);
            var hint = Label(footer, T("art.navigation"), 15, 36);
            OnlineWidgets.Size(hint.gameObject, -1, 36, 1);
            hint.color = OnlineWidgets.MutedColor;
            var recenter = OnlineWidgets.Button(footer, T("entry.recenter"), () => map.ResetView(), 100);
            recenter.gameObject.name = "SystemMapRecenter";
            // A native button remains forty units high even in the chart footer.
            footerRect.sizeDelta = new Vector2(-20, 40);

            var graphic = OnlineWidgets.Rect("HexGraphic", mapRoot.transform);
            OnlineWidgets.Stretch((RectTransform)graphic.transform);
            ((RectTransform)graphic.transform).offsetMin = new Vector2(12, 48);
            ((RectTransform)graphic.transform).offsetMax = new Vector2(-12, -96);
            map = graphic.AddComponent<SystemHexMap>();
            map.raycastTarget = true; map.Selected = Select;
            Refresh();
        }

        private static TextMeshProUGUI StepLabel(Transform parent, string key)
        {
            var label = Label(parent, T(key), 16, 40);
            OnlineWidgets.Size(label.gameObject, -1, 40, 1);
            label.alignment = TextAlignmentOptions.Center; return label;
        }

        private void OpenPlanetSelection()
        { SystemPlanetSelectionWindow.Create(runtime, Refresh); }

        private MemberRow CreateMemberRow()
        {
            var row = new MemberRow();
            row.Button = OnlineWidgets.Button(memberList, "", () =>
            { if (row.HasPosition) map.Focus(row.Q, row.R); }, -1);
            row.Button.gameObject.name = "SystemMember";
            OnlineWidgets.Size(row.Button.gameObject, -1, 66);
            row.Button.GetComponentInChildren<TextMeshProUGUI>().gameObject.SetActive(false);
            var badge = OnlineWidgets.Rect("PlayerNumber", row.Button.transform);
            var rect = (RectTransform)badge.transform;
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(0, 1);
            rect.pivot = Vector2.zero;
            rect.offsetMin = new Vector2(5, 5); rect.offsetMax = new Vector2(43, -5);
            OnlineWidgets.Surface(badge, OnlineWidgets.InsetColor).raycastTarget = false;
            row.Number = OnlineWidgets.Label(badge.transform, "", 23);
            OnlineWidgets.Stretch(row.Number.rectTransform); row.Number.alignment = TextAlignmentOptions.Center;
            row.Name = OnlineWidgets.Label(row.Button.transform, "", 18);
            OnlineWidgets.Stretch(row.Name.rectTransform);
            row.Name.rectTransform.offsetMin = new Vector2(53, 29); row.Name.rectTransform.offsetMax = new Vector2(-10, -6);
            row.Planet = OnlineWidgets.Label(row.Button.transform, "", 15);
            OnlineWidgets.Stretch(row.Planet.rectTransform);
            row.Planet.rectTransform.offsetMin = new Vector2(53, 5); row.Planet.rectTransform.offsetMax = new Vector2(-10, -33);
            foreach (var label in new[] { row.Name, row.Planet })
            { label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis; }
            return row;
        }

        private void RefreshMembers(StarSystemState state, PlayerInfo[] roster)
        {
            string signature = (state?.RoomId ?? "") + ":" + (state?.Revision ?? 0) + ":" +
                string.Join("|", roster.Select(p => p.Id + p.Name + p.Number + p.Connected)) + ":" +
                string.Join("|", (state?.Members ?? new SystemMember[0]).Select(m => m.PeerId + m.Planet?.PlanetId + m.Planet?.Name));
            memberCount.text = string.Format(T("art.players_count"), roster.Length);
            if (signature != memberSignature)
            {
                memberSignature = signature;
                var players = roster.ToDictionary(p => p.Id);
                int index = 0;
                if (state != null) foreach (var member in state.Members)
                {
                    if (index == memberRows.Count) memberRows.Add(CreateMemberRow());
                    var row = memberRows[index++];
                    row.Button.gameObject.SetActive(true); row.Button.interactable = true; row.HasPosition = true;
                    row.PeerId = member.PeerId; row.Q = member.Q; row.R = member.R;
                    PlayerInfo player; players.TryGetValue(member.PeerId, out player);
                    row.Number.text = (player?.Number ?? 0).ToString();
                    row.Number.color = OnlineWidgets.PlayerColor(player?.Number ?? 0);
                    row.Name.text = player?.Name ?? T("entry.unknown_player");
                    row.Name.color = member.Connected ? OnlineWidgets.TextColor : OnlineWidgets.MutedColor;
                    row.Planet.text = (member.Planet?.Name ?? T("entry.position_reserved")) +
                        (member.Connected ? "" : " · " + T("ui.player_offline"));
                    row.Planet.color = OnlineWidgets.MutedColor;
                }
                var placed = new HashSet<string>((state?.Members ?? new SystemMember[0]).Select(m => m.PeerId));
                foreach (var player in roster.Where(p => !placed.Contains(p.Id)))
                {
                    if (index == memberRows.Count) memberRows.Add(CreateMemberRow());
                    var row = memberRows[index++];
                    row.Button.gameObject.SetActive(true); row.Button.interactable = false; row.HasPosition = false;
                    row.PeerId = player.Id; row.Number.text = player.Number.ToString();
                    row.Number.color = OnlineWidgets.PlayerColor(player.Number);
                    row.Name.text = player.Name;
                    row.Name.color = player.Connected ? OnlineWidgets.TextColor : OnlineWidgets.MutedColor;
                    row.Planet.text = T("art.waiting_landing") + (player.Connected ? "" : " · " + T("ui.player_offline"));
                    row.Planet.color = OnlineWidgets.MutedColor;
                }
                for (int i = index; i < memberRows.Count; i++) memberRows[i].Button.gameObject.SetActive(false);
            }
            foreach (var row in memberRows)
                if (row.Button.gameObject.activeSelf) OnlineWidgets.Tint(row.Button,
                    selectedMember == row.PeerId ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
        }

        private void Select(int q, int r)
        {
            var system = runtime.Multiplayer?.StarSystem;
            var member = (system?.State ?? displayedState)?.Members.FirstOrDefault(m => m.Q == q && m.R == r);
            selectedMember = member?.Planet == null ? null : member.PeerId;
            if (member == null && system != null && !system.Admitted && system.ChooseLanding(q, r))
            { requestedQ = q; requestedR = r; }
            Refresh();
        }

        private void Update() { if (revision != runtime.Revision) Refresh(); }

        private void Refresh()
        {
            revision = runtime.Revision;
            var session = runtime.Session;
            var system = runtime.Multiplayer?.StarSystem;
            var state = system?.State;
            var selected = runtime.SelectedPlanet;
            bool preview = session != null && !session.IsHost && system?.Admitted != true;
            var reservation = state?.Members.FirstOrDefault(m => m.PeerId == session?.LocalPlayerId && m.Planet == null);
            bool confirmed = system?.TravelConfirmed == true;
            bool active = !runtime.Busy && session?.Reconnecting != true && session?.Leaving != true;
            title.text = state?.Name ?? runtime.RoomName;
            nameCard.gameObject.SetActive(session == null);
            if (!nameInput.isFocused && nameInput.text != runtime.RoomName) nameInput.SetTextWithoutNotify(runtime.RoomName);
            var roster = session?.Players ?? new[] { new PlayerInfo { Id = selected?.OwnerId ?? "", Name = runtime.Nickname, Number = 1, Connected = true } };
            if (state == null && selected != null)
                state = new StarSystemState { RoomId = selected.HomeSystemId, SystemId = selected.HomeSystemId,
                    Name = runtime.RoomName, Revision = 1,
                    Members = new[] { new SystemMember { PeerId = selected.OwnerId, Planet = selected, Connected = true } } };
            displayedState = state;
            map.SetState(state, roster, session?.LocalPlayerId ?? selected?.OwnerId);
            RefreshMembers(state, roster);

            string systemError = system?.LastError;
            if (reservation != null && requestedQ == reservation.Q && requestedR == reservation.R &&
                (systemError == "planet_position_taken" || systemError == "planet_choose_position" || systemError == "planet_invalid_position"))
                systemError = null;
            string error = runtime.DependencyError ?? systemError ?? runtime.Error;
            status.text = error != null ? T(error.StartsWith("dependency.") ? error : "error." + error)
                : session?.Reconnecting == true ? T("ui.reconnecting")
                : session?.Leaving == true ? T("entry.leaving")
                : system?.Admitted == true ? T("entry.connected")
                : confirmed ? T("entry.waiting_confirmation")
                : session == null ? T("system.offline") : T("entry.preview");
            status.color = error != null ? new Color(0.92f, 0.66f, 0.54f)
                : system?.Admitted == true ? OnlineWidgets.OnlineColor : OnlineWidgets.TextColor;
            steps.gameObject.SetActive(preview);
            positionStep.color = reservation != null ? OnlineWidgets.OnlineColor : OnlineWidgets.TextColor;
            planetStep.color = selected != null ? OnlineWidgets.OnlineColor : OnlineWidgets.MutedColor;
            joinStep.color = confirmed ? OnlineWidgets.OnlineColor : OnlineWidgets.MutedColor;
            preparation.gameObject.SetActive(session == null || preview);
            preparationTitle.text = T(session == null ? "entry.your_asteroid" : "entry.choose_source");
            landing.gameObject.SetActive(preview);
            landing.text = reservation != null ? string.Format(T("entry.position_ready"), reservation.Q, reservation.R)
                : requestedQ.HasValue ? string.Format(T("entry.position_pending"), requestedQ.Value, requestedR)
                : T("map.choose_position");
            planet.text = selected != null ? T("entry.selected_asteroid") + "\n" + selected.Name : T("entry.no_planet");
            choosePlanet.gameObject.SetActive(selected != null);
            choosePlanet.interactable = active && !confirmed;
            sourceActions.gameObject.SetActive(selected == null);
            bool canChooseSource = active && runtime.DependencyError == null && (session == null || reservation != null) && !confirmed;
            create.interactable = canChooseSource && Game.Instance == null;
            load.interactable = canChooseSource && Game.Instance == null && MainMenu.Instance != null;
            sourceHelp.text = selected != null ? T("entry.keep_colony")
                : preview && reservation == null ? T("entry.choose_position_first")
                : Game.Instance != null ? T("entry.load_help") : T("entry.create_first");
            arrive.gameObject.SetActive(preview && selected != null);
            arrive.GetComponentInChildren<TextMeshProUGUI>().text = T(confirmed ? "entry.waiting_confirmation" : "entry.join");
            arrive.interactable = active && reservation != null && !confirmed && runtime.DependencyError == null;
            host.gameObject.SetActive(session == null && selected != null);
            host.interactable = active && runtime.DependencyError == null && Game.Instance != null;
            home.gameObject.SetActive(session != null);
            home.interactable = session?.Leaving != true;
            home.GetComponentInChildren<TextMeshProUGUI>().text = T(session?.IsHost == true ? "planet.close_system"
                : preview ? "entry.leave_preview" : "planet.return_home");
            int actionCount = (sourceActions.gameObject.activeSelf ? 2 : 0) +
                (arrive.gameObject.activeSelf ? 1 : 0) + (host.gameObject.activeSelf ? 1 : 0) + (home.gameObject.activeSelf ? 1 : 0);
            float actionsHeight = actionCount == 0 ? 0 : 20 + actionCount * 40 + (actionCount - 1) * 8;
            actionsRect.gameObject.SetActive(actionCount != 0);
            actionsRect.sizeDelta = new Vector2(356, actionsHeight);
            sidebarRect.offsetMin = new Vector2(-356, actionCount == 0 ? 0 : actionsHeight + 12);

            var focused = state?.Members.FirstOrDefault(m => m.PeerId == selectedMember && m.Planet != null);
            focusCard.gameObject.SetActive(focused != null);
            if (focused != null)
            {
                var owner = roster.FirstOrDefault(p => p.Id == focused.PeerId);
                focusName.text = (owner?.Number ?? 0) + " · " + (owner?.Name ?? T("entry.unknown_player"));
                focusPlanet.text = focused.Planet.Name + (focused.Connected ? "" : "\n" + T("ui.player_offline"));
                observe.gameObject.SetActive(session != null);
                observe.interactable = active && focused.Connected && session != null;
            }
        }
    }
}
