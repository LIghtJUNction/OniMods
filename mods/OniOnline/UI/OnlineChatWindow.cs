using OniOnline.Network;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal sealed class OnlineChatWindow : KScreen
    {
        private OnlineRuntime runtime;
        private TextMeshProUGUI heading;
        private TextMeshProUGUI status;
        private TextMeshProUGUI voiceStatus;
        private TMP_InputField message;
        private Button send;
        private Button join;
        private OnlineChatTranscript transcript;
        private int revision = -1;
        private int focusFrame = -1;
        private RoomSession session;

        internal static OnlineChatWindow Create(OnlineRuntime runtime)
        {
            var root = OnlineWidgets.CanvasRoot("OniOnlineChat", 30011);
            var blocker = OnlineWidgets.Rect("ChatInputBlocker", root.transform);
            OnlineWidgets.Stretch((RectTransform)blocker.transform);
            var shield = OnlineWidgets.Surface(blocker, Color.clear);
            shield.raycastTarget = true;
            shield.canvasRenderer.cullTransparentMesh = false;
            var panel = OnlineWidgets.Rect("OniOnlineChatPanel", root.transform);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(28, 128);
            rect.sizeDelta = new Vector2(570, 390);
            OnlineWidgets.Surface(panel, OnlineWidgets.PanelColor, true);
            var window = panel.AddComponent<OnlineChatWindow>();
            window.runtime = runtime;
            window.Build();
            window.Activate();
            window.Refresh();
            return window;
        }

        public override bool IsModal() { return true; }
        public override float GetSortKey() { return MODAL_SCREEN_SORT_KEY + 3; }

        public override void OnKeyDown(KButtonEvent e)
        {
            if (e.TryConsume(global::Action.Escape)) runtime.HideChat();
            e.Consumed = true;
        }

        public override void OnKeyUp(KButtonEvent e) { e.Consumed = true; }

        private void Build()
        {
            var content = OnlineWidgets.Column(transform, "Content", 12, 6);
            OnlineWidgets.Stretch((RectTransform)content);
            var header = OnlineWidgets.Row(content, 34);
            heading = OnlineWidgets.Label(header, OnlineText.Get("ui.chat_title"), 20);
            OnlineWidgets.Size(heading.gameObject, -1, 34, 1);
            heading.textWrappingMode = TextWrappingModes.NoWrap;
            heading.overflowMode = TextOverflowModes.Ellipsis;
            OnlineWidgets.Button(header, OnlineText.Get("ui.close"), runtime.HideChat, 80);
            status = OnlineWidgets.Label(content, "", 15);
            status.color = OnlineWidgets.MutedColor;
            OnlineWidgets.Size(status.gameObject, -1, 22);
            Transform body;
            var scroll = OnlineWidgets.Scroll(content, 200, out body);
            var layout = scroll.GetComponent<LayoutElement>();
            layout.minHeight = 80;
            layout.flexibleHeight = 1;
            var latest = OnlineWidgets.Button(scroll.transform, OnlineText.Get("chat.latest"),
                () => transcript.FollowLatest(), 140);
            var latestRect = (RectTransform)latest.transform;
            latestRect.anchorMin = latestRect.anchorMax = latestRect.pivot = Vector2.zero;
            latestRect.anchoredPosition = new Vector2(8, 8);
            latestRect.sizeDelta = new Vector2(140, 32);
            latest.gameObject.SetActive(false);
            transcript = new OnlineChatTranscript(scroll, body, latest, runtime.ChatFontSize);
            var composer = OnlineWidgets.Row(content, 40);
            message = OnlineWidgets.Input(composer, "", OnlineText.Get("ui.chat_placeholder"));
            message.name = "ChatMessageInput";
            message.characterLimit = Protocol.MaxChatLength;
            message.onSubmit.AddListener(_ => Send());
            send = OnlineWidgets.Button(composer, OnlineText.Get("ui.send"), Send, 82);
            join = OnlineWidgets.Button(content, OnlineText.Get("menu.browse_lobbies"), () =>
            {
                runtime.HideChat();
                runtime.BrowseSystems();
            }, -1);
            voiceStatus = OnlineWidgets.Label(content, "", 14);
            voiceStatus.color = OnlineWidgets.MutedColor;
            OnlineWidgets.Size(voiceStatus.gameObject, -1, 24);
        }

        internal void FocusComposer()
        {
            // A bare character rebound to this action must not enter the new draft.
            focusFrame = Time.frameCount + 1;
        }

        private void Send()
        {
            if (runtime.Session?.SendChat(message.text) == true)
            {
                message.text = "";
                transcript.FollowLatest();
                FocusComposer();
            }
            else
            {
                status.text = OnlineText.Get(runtime.Session?.Joined == true ?
                    "error.invalid_chat_or_rate" : "chat.join_first");
            }
        }

        private void Refresh()
        {
            session = runtime.Session;
            revision = runtime.Revision;
            bool connected = session?.Joined == true && !session.Closed && !session.Reconnecting;
            heading.text = OnlineText.Get("ui.chat_title") + (session == null ? "" : " · " + session.RoomName);
            status.text = OnlineText.Get(connected ? "ui.connected" :
                session?.Reconnecting == true ? "ui.reconnecting" : "chat.join_first");
            message.interactable = send.interactable = connected;
            join.gameObject.SetActive(!connected && session?.Reconnecting != true);
            transcript.Refresh(session?.RoomId, session?.Chat ?? new ChatLine[0], session?.Players ?? new PlayerInfo[0]);
        }

        private void Update()
        {
            if (runtime == null) return;
            if (revision != runtime.Revision || session != runtime.Session) Refresh();
            if (focusFrame >= 0 && Time.frameCount >= focusFrame)
            {
                focusFrame = -1;
                if (message.interactable)
                {
                    UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(message.gameObject);
                    message.ActivateInputField();
                }
            }
            string key = runtime.Voice?.LastError;
            voiceStatus.text = !string.IsNullOrEmpty(key) ? OnlineText.Get("error." + key) :
                !runtime.VoiceEnabled ? OnlineText.Get("voice.disabled") :
                runtime.Voice?.IsTalking == true ? OnlineText.Get("voice.talking") :
                string.Format(OnlineText.Get("voice.hold_hint"), OnlineInputControls.VoiceBinding);
        }

        private void LateUpdate() { transcript?.CompleteLayout(); }

        internal void Close()
        {
            var root = transform.root.gameObject;
            Deactivate();
            root.SetActive(false);
            Destroy(root);
        }
    }
}
