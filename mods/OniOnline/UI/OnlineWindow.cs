using System;
using System.Linq;
using System.Collections.Generic;
using OniOnline.Network;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    public sealed class OnlineWindow : KScreen
    {
        private OnlineRuntime runtime;
        private TextMeshProUGUI status;
        private TextMeshProUGUI connection;
        private TextMeshProUGUI roomTitle;
        private TextMeshProUGUI memberCount;
        private TextMeshProUGUI chatCount;
        private TextMeshProUGUI waiting;
        private TMP_InputField code;
        private TMP_InputField message;
        private TMP_InputField portInput;
        private Transform rooms;
        private Transform members;
        private Transform chatBody;
        private GameObject lobbyView;
        private GameObject roomView;
        private GameObject emptyRooms;
        private GameObject emptyChat;
        private ScrollRect chatScroll;
        private OnlineChatTranscript chatTranscript;
        private Button latestChat;
        private Button networkSettingsButton;
        private GameObject networkSettings;
        private bool showNetworkSettings;
        private Button start;
        private Button leave;
        private Button enabledButton;
        private Button lan;
        private Button steam;
        private Button refresh;
        private Button worldsButton;
        private Transform clockBar;
        private TextMeshProUGUI clockStatus;
        private double nextNetworkRefresh;
        private Button send;
        private Button bansButton;
        private GameObject moderationView;
        private string moderationTarget;
        private int revision = -1;
        private string roomListSignature;
        private string memberListSignature;
        private readonly List<Selectable> connectionControls = new List<Selectable>();


        internal static OnlineWindow Create(OnlineRuntime runtime)
        {
            var root = OnlineWidgets.CanvasRoot("OniOnlineWindow", 30000);
            var shade = OnlineWidgets.Rect("Shade", root.transform);
            OnlineWidgets.Stretch((RectTransform)shade.transform);
            OnlineWidgets.Surface(shade, new Color(0, 0, 0, 0.60f));
            var panel = OnlineWidgets.Rect("OniOnlinePanel", root.transform);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1120, 844);
            OnlineWidgets.Surface(panel, OnlineWidgets.PanelColor, true);
            var shadow = panel.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.35f);
            shadow.effectDistance = new Vector2(5, -5);
            var window = panel.AddComponent<OnlineWindow>();
            window.runtime = runtime;
            window.Build();
            window.Activate();
            return window;
        }

        public override bool IsModal() { return true; }
        public override float GetSortKey() { return MODAL_SCREEN_SORT_KEY + 1; }
        public override void OnKeyDown(KButtonEvent e)
        {
            if (e.TryConsume(global::Action.Escape))
            { if (moderationView != null) CloseModeration(); else runtime.HideWindow(); }
            e.Consumed = true;
        }
        public override void OnKeyUp(KButtonEvent e) { e.Consumed = true; }
        private static string T(string key) { return OnlineText.Get(key); }

        private static TextMeshProUGUI Caption(Transform parent, string text, int size = 18, float height = 24)
        {
            var label = OnlineWidgets.Label(parent, text, size);
            OnlineWidgets.Size(label.gameObject, -1, height);
            return label;
        }

        private static void Ellipsis(TextMeshProUGUI label)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void Fill(RectTransform rect, float left, float bottom, float right, float top)
        {
            OnlineWidgets.Stretch(rect);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static Transform Section(Transform parent, string name, string text, int height = 32)
        {
            var row = OnlineWidgets.Row(parent, height);
            row.name = name;
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(10, 10, 0, 0);
            OnlineWidgets.Surface(row.gameObject, new Color(0.30f, 0.315f, 0.385f), true);
            var label = OnlineWidgets.Label(row, text, 18);
            OnlineWidgets.Size(label.gameObject, -1, height, 1);
            Ellipsis(label);
            return row;
        }

        private static void CompactButton(Button button, int fontSize = 16)
        {
            button.GetComponentInChildren<TextMeshProUGUI>().fontSize = fontSize;
        }

        private void Build()
        {
            var bar = OnlineWidgets.Rect("TitleBar", transform);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0, 1); barRect.anchorMax = Vector2.one;
            barRect.pivot = new Vector2(0.5f, 1); barRect.sizeDelta = new Vector2(0, 44);
            OnlineWidgets.Surface(bar, OnlineWidgets.AccentColor, true);
            var logo = OnlineArtwork.Load(OnlineArtwork.LogoFile);
            if (logo != null)
            {
                var mark = OnlineWidgets.Rect("ModLogo", bar.transform);
                var markRect = (RectTransform)mark.transform;
                markRect.anchorMin = markRect.anchorMax = new Vector2(0, 0.5f);
                markRect.sizeDelta = new Vector2(34, 34);
                markRect.anchoredPosition = new Vector2(28, 0);
                var image = mark.AddComponent<Image>();
                image.sprite = logo; image.preserveAspect = true; image.raycastTarget = false;
            }
            var title = OnlineWidgets.Label(bar.transform, T("mode.online"), 22);
            Fill(title.rectTransform, 64, 2, 64, 2);
            title.alignment = TextAlignmentOptions.Center;
            var close = OnlineWidgets.Button(bar.transform, "×", runtime.HideWindow, 36);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = Vector2.one; closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-5, -5); closeRect.sizeDelta = new Vector2(34, 34);
            OnlineWidgets.Tint(close, new Color(0.40f, 0.22f, 0.31f));
            close.GetComponentInChildren<TextMeshProUGUI>().text = "";
            foreach (float angle in new[] { -45f, 45f })
            {
                var stroke = OnlineWidgets.Rect("CloseStroke", close.transform);
                var strokeRect = (RectTransform)stroke.transform;
                strokeRect.sizeDelta = new Vector2(15, 2);
                strokeRect.localRotation = Quaternion.Euler(0, 0, angle);
                OnlineWidgets.Surface(stroke, OnlineWidgets.TextColor).raycastTarget = false;
            }

            var content = OnlineWidgets.Column(transform, "WindowContent", 12, 8);
            Fill((RectTransform)content, 0, 0, 0, 44);
            var top = OnlineWidgets.Row(content, 40);
            top.name = "ConnectionStatus";
            top.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(10, 6, 0, 0);
            OnlineWidgets.Surface(top.gameObject, OnlineWidgets.InsetColor, true);
            connection = OnlineWidgets.Label(top, "", 16);
            OnlineWidgets.Size(connection.gameObject, -1, 40, 1);
            Ellipsis(connection);
            worldsButton = OnlineWidgets.Button(top, T("world.open"), runtime.ShowWorldOverview, 110);
            CompactButton(worldsButton);
            var preview = OnlineWidgets.Label(top, T("ui.preview"), 16);
            preview.color = OnlineWidgets.MutedColor;
            preview.alignment = TextAlignmentOptions.MidlineRight;
            OnlineWidgets.Size(preview.gameObject, 165, 40);
            Ellipsis(preview);

            clockBar = OnlineWidgets.Row(content, 24);
            clockStatus = OnlineWidgets.Label(clockBar, "", 16);
            OnlineWidgets.Size(clockStatus.gameObject, -1, 24, 1);
            clockStatus.color = OnlineWidgets.MutedColor;
            clockStatus.text = T("time.independent");

            var stack = OnlineWidgets.Rect("Pages", content);
            var pageSize = OnlineWidgets.Size(stack, -1, 580);
            pageSize.minHeight = 0; pageSize.flexibleHeight = 1;
            lobbyView = BuildLobby(stack.transform);
            roomView = BuildRoom(stack.transform);

            status = Caption(content, "", 17, 32);
            status.richText = false;
            status.textWrappingMode = TextWrappingModes.Normal;
            OnlineWidgets.Divider(content);
            var notice = Caption(content, T("ui.phase1_notice"), 14, 34);
            notice.textWrappingMode = TextWrappingModes.Normal;
            notice.color = OnlineWidgets.MutedColor;
            Refresh();
        }

        private GameObject Page(Transform parent, string name, out Transform left, out Transform right)
        {
            var page = OnlineWidgets.Rect(name, parent);
            OnlineWidgets.Stretch((RectTransform)page.transform);
            var row = page.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12; row.childControlHeight = row.childControlWidth = true;
            row.childForceExpandHeight = true; row.childForceExpandWidth = false;
            left = OnlineWidgets.Column(page.transform, "Sidebar", 10, 8);
            OnlineWidgets.Size(left.gameObject, 302, 0).flexibleHeight = 1;
            OnlineWidgets.Surface(left.gameObject, new Color(0.285f, 0.30f, 0.36f), true);
            right = OnlineWidgets.Column(page.transform, "Main", 0, 8);
            OnlineWidgets.Size(right.gameObject, -1, 0, 1).flexibleHeight = 1;
            return page;
        }

        private GameObject BuildLobby(Transform parent)
        {
            Transform left, right;
            var page = Page(parent, "Lobby", out left, out right);
            Section(left, "PlayerHeader", T("ui.profile"));
            var nickname = OnlineWidgets.Input(left, runtime.Nickname, T("room.player_name"));
            nickname.name = "PlayerNickname";
            connectionControls.Add(nickname);
            nickname.characterLimit = 32;
            nickname.onValueChanged.AddListener(value => runtime.Nickname = value);
            nickname.onEndEdit.AddListener(value => {
                if (Protocol.ValidText(value, 32)) PlayerPrefs.SetString("OniOnline.Nickname", value);
            });
            OnlineWidgets.Space(left, 2);
            networkSettingsButton = OnlineWidgets.Button(left, T("lobby.network_settings") + "  +", ToggleNetworkSettings, -1);
            networkSettingsButton.name = "NetworkSettingsToggle";
            CompactButton(networkSettingsButton);
            Transform settingsContent;
            var settingsScroll = OnlineWidgets.Scroll(left, 100, out settingsContent);
            settingsScroll.name = "NetworkSettings";
            var settingsSize = settingsScroll.GetComponent<LayoutElement>();
            settingsSize.minHeight = 0; settingsSize.flexibleHeight = 1;
            networkSettings = settingsScroll.gameObject;
            settingsContent.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(8, 8, 8, 8);
            settingsContent.GetComponent<VerticalLayoutGroup>().spacing = 8;
            var enableRow = OnlineWidgets.Row(settingsContent, 40);
            var enableLabel = OnlineWidgets.Label(enableRow, T("settings.enable_online"), 17);
            OnlineWidgets.Size(enableLabel.gameObject, -1, 40, 1);
            Ellipsis(enableLabel);
            enabledButton = OnlineWidgets.Button(enableRow, "", () => runtime.SetEnabled(!runtime.Enabled), 94);
            CompactButton(enabledButton);
            Caption(settingsContent, T("ui.connection"), 16, 24).color = OnlineWidgets.MutedColor;
            var transport = OnlineWidgets.Row(settingsContent, 40);
            lan = OnlineWidgets.Button(transport, T("settings.lan"), () => runtime.SelectTransport(false), -1);
            steam = OnlineWidgets.Button(transport, T("room.public_steam"), () => runtime.SelectTransport(true), -1);
            CompactButton(lan); CompactButton(steam);
            connectionControls.Add(lan); connectionControls.Add(steam);
            Caption(settingsContent, T("ui.port"), 16, 24).color = OnlineWidgets.MutedColor;
            var port = OnlineWidgets.Input(settingsContent, runtime.Port, "27140");
            port.name = "NetworkPort";
            portInput = port;
            port.characterLimit = 5; port.contentType = TMP_InputField.ContentType.IntegerNumber;
            port.onValueChanged.AddListener(value => runtime.Port = value);
            connectionControls.Add(port);
            Caption(settingsContent, T("room.password"), 16, 24).color = OnlineWidgets.MutedColor;
            var password = OnlineWidgets.Input(settingsContent, runtime.Password, T("ui.optional"));
            password.name = "NetworkPassword";
            password.characterLimit = 128; password.contentType = TMP_InputField.ContentType.Password;
            password.onValueChanged.AddListener(value => runtime.Password = value);
            connectionControls.Add(password);
            var hint = Caption(settingsContent, T("ui.password_hint"), 14, 52);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.color = OnlineWidgets.MutedColor;
            networkSettings.SetActive(showNetworkSettings);

            var heading = Section(right, "PublicSystemsHeader", T("ui.room_list"), 48);
            refresh = OnlineWidgets.Button(heading, T("ui.refresh"), () => {
                if (runtime.Busy) runtime.Leave(); else runtime.Search();
            }, 100);
            refresh.name = "RefreshPublicSystems";
            CompactButton(refresh);
            var subtitle = Caption(right, T("ui.lobby_subtitle"), 16, 26);
            subtitle.color = OnlineWidgets.MutedColor;
            var list = OnlineWidgets.Scroll(right, 240, out rooms);
            list.name = "PublicSystems";
            var listSize = list.GetComponent<LayoutElement>();
            listSize.minHeight = 80; listSize.flexibleHeight = 1;
            rooms.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(8, 8, 8, 8);
            emptyRooms = OnlineWidgets.Column(list.viewport, "EmptyRooms", 24, 12).gameObject;
            Fill((RectTransform)emptyRooms.transform, 0, 0, 0, 0);
            emptyRooms.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            OnlineWidgets.Space(emptyRooms.transform, 0, true);
            var emptyTitle = Caption(emptyRooms.transform, T("ui.empty_title"), 19, 36);
            emptyTitle.alignment = TextAlignmentOptions.Center;
            var emptyHint = Caption(emptyRooms.transform, T("ui.empty_hint"), 17, 42);
            emptyHint.alignment = TextAlignmentOptions.Center; emptyHint.color = OnlineWidgets.MutedColor;
            OnlineWidgets.Space(emptyRooms.transform, 0, true);
            OnlineWidgets.Space(right, 4);
            Section(right, "DirectJoinHeader", T("ui.direct_join"));
            var joining = OnlineWidgets.Row(right, 42);
            var address = OnlineWidgets.Input(joining, runtime.JoinCode, T("ui.join_hint"));
            address.name = "DirectJoinCode";
            address.onValueChanged.AddListener(value => runtime.JoinCode = value);
            connectionControls.Add(address);
            var join = OnlineWidgets.Button(joining, T("menu.join_room"), runtime.Join, 120);
            join.name = "DirectJoin";
            connectionControls.Add(join);
            return page;
        }

        private void ToggleNetworkSettings()
        {
            showNetworkSettings = !showNetworkSettings;
            networkSettings.SetActive(showNetworkSettings);
            networkSettingsButton.GetComponentInChildren<TextMeshProUGUI>().text =
                T("lobby.network_settings") + (showNetworkSettings ? "  -" : "  +");
        }

        private GameObject BuildRoom(Transform parent)
        {
            Transform left, right;
            var page = Page(parent, "Room", out left, out right);
            roomTitle = Caption(left, "", 20, 48); Ellipsis(roomTitle);
            var heading = Section(left, "SystemMembersHeader", T("ui.members"), 40);
            memberCount = OnlineWidgets.Label(heading, "", 18);
            memberCount.color = OnlineWidgets.MutedColor;
            memberCount.alignment = TextAlignmentOptions.MidlineRight;
            OnlineWidgets.Size(memberCount.gameObject, 34, 40);
            bansButton = OnlineWidgets.Button(heading, T("moderation.bans"), () => ShowModeration(null), 60);
            bansButton.name = "ManageBans";
            CompactButton(bansButton, 14);
            var roster = OnlineWidgets.Scroll(left, 220, out members);
            roster.name = "SystemMembers";
            var rosterSize = roster.GetComponent<LayoutElement>();
            rosterSize.minHeight = 120; rosterSize.flexibleHeight = 1;
            members.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(4, 4, 4, 4);
            members.GetComponent<VerticalLayoutGroup>().spacing = 4;
            OnlineWidgets.Space(left, 2); OnlineWidgets.Divider(left);
            Caption(left, T("ui.invite"), 16, 26).color = OnlineWidgets.MutedColor;
            code = OnlineWidgets.Input(left, "", ""); code.readOnly = true; code.name = "RoomInviteCode";
            code.textComponent.fontSize = 16;
            var copy = OnlineWidgets.Button(left, T("room.copy_code"), () => GUIUtility.systemCopyBuffer = runtime.RoomCode, -1);
            copy.name = "CopyRoomCode"; CompactButton(copy);
            waiting = Caption(left, T("ui.wait_host"), 15, 40); waiting.color = OnlineWidgets.MutedColor;
            waiting.textWrappingMode = TextWrappingModes.Normal;
            var roomActions = OnlineWidgets.Row(left, 40);
            start = OnlineWidgets.Button(roomActions, T("ui.start_game"), runtime.StartOrEnter, -1);
            start.name = "StartOrEnterGame"; CompactButton(start);
            OnlineWidgets.Tint(start, OnlineWidgets.AccentColor);
            leave = OnlineWidgets.Button(roomActions, T("ui.leave"), runtime.Leave, -1);
            leave.name = "LeaveSystem"; CompactButton(leave);

            var chatHeading = Section(right, "RoomChatHeader", T("ui.chat_title"), 40);
            chatCount = OnlineWidgets.Label(chatHeading, "", 16);
            OnlineWidgets.Size(chatCount.gameObject, 100, 40); chatCount.alignment = TextAlignmentOptions.MidlineRight;
            chatCount.color = OnlineWidgets.MutedColor;
            chatScroll = OnlineWidgets.Scroll(right, 330, out chatBody);
            chatScroll.name = "ChatTranscript";
            var chatSize = chatScroll.GetComponent<LayoutElement>();
            chatSize.minHeight = 80; chatSize.flexibleHeight = 1;
            chatBody.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 0, 0);
            chatBody.GetComponent<VerticalLayoutGroup>().spacing = 0;
            emptyChat = OnlineWidgets.Rect("EmptyChat", chatScroll.viewport);
            OnlineWidgets.Stretch((RectTransform)emptyChat.transform);
            var welcome = OnlineWidgets.Label(emptyChat.transform, T("ui.chat_empty"), 18);
            welcome.color = OnlineWidgets.MutedColor; welcome.alignment = TextAlignmentOptions.Center;
            OnlineWidgets.Stretch(welcome.rectTransform);
            var sending = OnlineWidgets.Row(right, 44);
            sending.name = "ChatComposer";
            message = OnlineWidgets.Input(sending, "", T("ui.chat_placeholder"));
            message.name = "ChatMessageInput";
            message.characterLimit = Protocol.MaxChatLength;
            message.onSubmit.AddListener(value => SendChat());
            send = OnlineWidgets.Button(sending, T("ui.send"), SendChat, 90);
            send.name = "SendChatMessage";
            latestChat = OnlineWidgets.Button(chatScroll.transform, T("chat.latest"), () => chatTranscript.FollowLatest(), 140);
            latestChat.name = "ChatLatestMessages";
            var latestRect = (RectTransform)latestChat.transform;
            latestRect.anchorMin = latestRect.anchorMax = Vector2.zero;
            latestRect.pivot = Vector2.zero;
            latestRect.anchoredPosition = new Vector2(10, 10);
            latestRect.sizeDelta = new Vector2(140, 34);
            CompactButton(latestChat, 15);
            OnlineWidgets.Tint(latestChat, OnlineWidgets.AccentColor);
            latestChat.gameObject.SetActive(false);
            chatTranscript = new OnlineChatTranscript(chatScroll, chatBody, latestChat, runtime.ChatFontSize);
            return page;
        }

        private void SendChat()
        {
            if (runtime.Session != null && runtime.Session.SendChat(message.text))
            { message.text = ""; message.ActivateInputField(); }
            else { runtime.Error = "invalid_chat_or_rate"; runtime.Revision++; }
        }

        private void CloseModeration()
        {
            if (moderationView != null) { moderationView.SetActive(false); Destroy(moderationView); }
            moderationView = null; moderationTarget = null;
        }

        private void ShowModeration(string playerId)
        {
            var session = runtime.Session;
            if (session == null || !session.IsHost || !session.Joined) return;
            var target = session.Players.FirstOrDefault(p => p.Id == playerId && p.Id != session.LocalPlayerId);
            if (playerId != null && target == null) return;
            CloseModeration(); moderationTarget = playerId;
            moderationView = OnlineWidgets.Rect("RoomModeration", transform);
            OnlineWidgets.Stretch((RectTransform)moderationView.transform);
            OnlineWidgets.Surface(moderationView, new Color(0, 0, 0, 0.82f));
            var body = OnlineWidgets.Column(moderationView.transform, "ModerationControls", 18, 10);
            var rect = (RectTransform)body;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f); rect.sizeDelta = new Vector2(740, 650);
            OnlineWidgets.Surface(body.gameObject, OnlineWidgets.PanelColor, true);
            Caption(body, T("moderation.title"), 22, 32);
            if (target != null)
            {
                var name = Caption(body, target.Name, 20, 30); name.richText = false;
                var reason = OnlineWidgets.Input(body, "", T("moderation.reason")); reason.characterLimit = 128;
                var actions = OnlineWidgets.Row(body, 40);
                OnlineWidgets.Button(actions, T("moderation.kick"), () =>
                {
                    if (!session.RemovePlayer(playerId, false, reason.text)) runtime.Error = "moderation_failed";
                    CloseModeration(); runtime.Revision++;
                }, -1);
                var ban = OnlineWidgets.Button(actions, T("moderation.ban"), () =>
                {
                    if (!session.RemovePlayer(playerId, true, reason.text)) runtime.Error = "moderation_failed";
                    CloseModeration(); runtime.Revision++;
                }, -1);
                OnlineWidgets.Tint(ban, OnlineWidgets.AccentColor);
            }
            var hint = Caption(body, T(runtime.UseSteam ? "moderation.scope_steam" : "moderation.scope_lan"), 16, 92);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.color = OnlineWidgets.MutedColor;
            Caption(body, T("moderation.bans"), 19, 28);
            Transform list;
            var scroll = OnlineWidgets.Scroll(body, 120, out list);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            if (session.Bans.Length == 0) Caption(list, T("moderation.empty"), 17, 38);
            foreach (var entry in session.Bans)
            {
                var row = OnlineWidgets.Row(list, 68);
                var label = OnlineWidgets.Label(row, entry.Name + "\n" + entry.Reason, 17);
                label.richText = false; label.textWrappingMode = TextWrappingModes.Normal;
                OnlineWidgets.Size(label.gameObject, -1, 68, 1);
                string id = entry.Id;
                OnlineWidgets.Button(row, T("moderation.unban"), () =>
                { session.Unban(id); ShowModeration(moderationTarget); }, 110);
            }
            OnlineWidgets.Button(body, T("moderation.close"), CloseModeration, -1);
        }

        private void Update()
        {
            if (runtime == null) return;
            bool retryTick = runtime.Session != null && runtime.Session.Reconnecting && UnityEngine.Time.realtimeSinceStartupAsDouble >= nextNetworkRefresh;
            if (revision != runtime.Revision || retryTick)
            { Refresh(); nextNetworkRefresh = UnityEngine.Time.realtimeSinceStartupAsDouble + 1; }
        }

        private void LateUpdate()
        {
            if (roomView.activeInHierarchy) chatTranscript.CompleteLayout();
        }

        private static void Clear(Transform parent)
        {
            foreach (Transform child in parent) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        }

        private void Refresh()
        {
            revision = runtime.Revision;
            var session = runtime.Session;
            if (moderationView != null && (session == null || !session.IsHost || !session.Joined ||
                (moderationTarget != null && !session.Players.Any(p => p.Id == moderationTarget)))) CloseModeration();
            var multiplayer = runtime.Multiplayer;
            bool showClock = session != null && session.Started && Game.Instance != null && Game.Instance.baseAlreadyCreated && multiplayer != null;
            clockBar.gameObject.SetActive(showClock);
            string detail = runtime.ErrorDetail ?? session?.LastErrorDetail;
            worldsButton.interactable = session != null && !string.IsNullOrEmpty(session.LocalPlayerId);
            bool inRoom = session != null && !runtime.BrowsingSystems;
            lobbyView.SetActive(!inRoom); roomView.SetActive(inRoom);
            foreach (var control in connectionControls) control.interactable = session == null && !runtime.Busy;
            refresh.GetComponentInChildren<TextMeshProUGUI>().text = T(runtime.Busy ? "ui.cancel" : "ui.refresh");
            portInput.interactable = !inRoom && !runtime.Busy && !runtime.UseSteam;
            message.interactable = send.interactable = session != null && (session.Joined || session.Reconnecting);
            string dependency = runtime.DependencyError;
            string error = runtime.Error ?? session?.LastError;
            if (!showNetworkSettings && error != null && error.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                ToggleNetworkSettings();
            connection.text = (runtime.UseSteam ? "Steam P2P" : T("settings.lan")) + "  /  " +
                (session != null && session.Reconnecting ? T("ui.reconnecting") + " · " + (int)Math.Ceiling(session.RetryInSeconds) + "s" :
                T(session == null ? runtime.Busy ? "ui.connecting" : "ui.not_connected" : session.Joined ? "ui.connected" : "ui.connecting")) +
                (session != null && session.SmoothedRttMilliseconds > 0 ? " · " + (int)session.SmoothedRttMilliseconds + " ms" : "");
            connection.color = session != null && session.Joined ? OnlineWidgets.OnlineColor : OnlineWidgets.MutedColor;
            status.text = dependency != null ? T(dependency) : error != null ? T(error.StartsWith("dependency.") ? error : "error." + error) : "";
            if (error != null && !string.IsNullOrEmpty(detail)) status.text += " · " + detail;
            OnlineWidgets.Size(status.gameObject, -1, string.IsNullOrEmpty(detail) ? 32 : 72);
            if (status.text.Length == 0 && multiplayer?.LastError != null) status.text = T(multiplayer.LastError);
            status.gameObject.SetActive(status.text.Length > 0);
            status.color = new Color(0.94f, 0.72f, 0.52f);
            code.text = runtime.RoomCode;
            enabledButton.GetComponentInChildren<TextMeshProUGUI>().text = T(runtime.Enabled ? "ui.enabled" : "ui.disabled");
            OnlineWidgets.Tint(enabledButton, runtime.Enabled ? new Color(0.30f, 0.43f, 0.34f) : OnlineWidgets.ButtonColor);
            OnlineWidgets.Tint(lan, !runtime.UseSteam ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            OnlineWidgets.Tint(steam, runtime.UseSteam ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            start.interactable = session != null && session.Joined && (session.IsHost || session.Started) && Game.Instance == null;
            start.GetComponentInChildren<TextMeshProUGUI>().text = T(session != null && session.Started ? "ui.enter_game" : "ui.start_game");
            waiting.gameObject.SetActive(session != null && !session.IsHost && !session.Started);
            leave.interactable = session != null || runtime.Busy;
            roomTitle.text = session?.RoomName ?? "";
            RefreshRooms();
            RefreshMembers(session);
            RefreshChat(session);
        }

        private void RefreshRooms()
        {
            string signature = runtime.Busy + ":" + string.Join("|", runtime.Listings.Select(r =>
                r.Code + ":" + r.Name + ":" + r.Players + ":" + r.Started + ":" + r.HasPassword + ":" + r.CanJoin));
            if (signature == roomListSignature) return;
            roomListSignature = signature;
            Clear(rooms);
            emptyRooms.SetActive(runtime.Listings.Count == 0);
            foreach (var room in runtime.Listings)
            {
                var chosen = room;
                var row = OnlineWidgets.Row(rooms, 94);
                row.name = "PublicSystem";
                row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 10, 10, 10);
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
                OnlineWidgets.Surface(row.gameObject, OnlineWidgets.PanelColor, true);
                var accent = OnlineWidgets.Rect("SystemMarker", row);
                OnlineWidgets.Size(accent, 4, 60);
                OnlineWidgets.Surface(accent, OnlineWidgets.AccentColor).raycastTarget = false;
                var details = OnlineWidgets.Column(row, "RoomDetails", 0, 0);
                OnlineWidgets.Size(details.gameObject, -1, 60, 1);
                var label = Caption(details, room.Name, 20, 34); Ellipsis(label);
                var meta = Caption(details, string.Format(T("ui.player_count"), room.Players) +
                    "   ·   " + T(room.Started ? "room.in_progress" : "room.waiting") +
                    (room.HasPassword ? "   ·   " + T("room.password") : ""), 15, 26);
                Ellipsis(meta);
                meta.color = OnlineWidgets.MutedColor;
                var join = OnlineWidgets.Button(row, T(!room.CanJoin ? "room.full" : "lobby.inspect"),
                    () => runtime.PreviewSystem(chosen.Code), 128);
                join.name = "InspectPublicSystem";
                CompactButton(join);
                join.interactable = room.CanJoin && !runtime.Busy;
            }
        }

        private void RefreshMembers(RoomSession session)
        {
            bansButton.gameObject.SetActive(session != null && session.IsHost);
            var players = session?.Players ?? new PlayerInfo[0];
            memberCount.text = players.Length.ToString();
            chatCount.text = string.Format(T("ui.player_count"), players.Length);
            string signature = (session?.IsHost == true) + ":" + session?.LocalPlayerId + ":" +
                string.Join("|", players.Select(p => p.Id + ":" + p.Name + ":" + p.Number + ":" + p.Connected));
            if (signature == memberListSignature) return;
            memberListSignature = signature;
            Clear(members);
            for (int i = 0; i < players.Length; i++)
            {
                var row = OnlineWidgets.Row(members, 56);
                row.name = "SystemMember";
                row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(6, 6, 6, 6);
                OnlineWidgets.Surface(row.gameObject, OnlineWidgets.PanelColor);
                var badge = OnlineWidgets.Rect("PlayerNumber", row);
                OnlineWidgets.Size(badge, 32, 36);
                OnlineWidgets.Surface(badge, OnlineWidgets.InsetColor, true).raycastTarget = false;
                var number = OnlineWidgets.Label(badge.transform, players[i].Number.ToString(), 20);
                OnlineWidgets.Stretch(number.rectTransform);
                number.alignment = TextAlignmentOptions.Center;
                number.color = OnlineWidgets.PlayerColor(players[i].Number);
                var identity = OnlineWidgets.Column(row, "PlayerIdentity", 0, 0);
                OnlineWidgets.Size(identity.gameObject, -1, 42, 1);
                var label = Caption(identity, players[i].Name, 17, 23);
                Ellipsis(label);
                string playerStatus = !players[i].Connected ? T("ui.player_offline") :
                    i == 0 ? T("ui.host") : T("ui.connected");
                if (session != null && players[i].Id == session.LocalPlayerId) playerStatus += " · " + T("ui.you");
                var state = Caption(identity, playerStatus, 13, 19);
                state.color = !players[i].Connected ? OnlineWidgets.MutedColor : OnlineWidgets.PlayerColor(players[i].Number);
                Ellipsis(state);
                if (!players[i].Connected) label.color = OnlineWidgets.MutedColor;
                if (i != 0 && session != null && session.IsHost)
                {
                    string id = players[i].Id;
                    var manage = OnlineWidgets.Button(row, T("moderation.manage"), () => ShowModeration(id), 66);
                    manage.name = "ManageSystemMember"; CompactButton(manage, 14);
                }
            }
        }

        private void RefreshChat(RoomSession session)
        {
            var lines = session?.Chat ?? new ChatLine[0];
            emptyChat.SetActive(lines.Length == 0);
            chatTranscript.Refresh(session?.RoomId, lines, session?.Players ?? new PlayerInfo[0]);
        }
    }
}
