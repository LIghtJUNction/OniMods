using System;
using HarmonyLib;
using OniOnline.Network;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // The native message frame owns the visual language. This controller owns
    // only the chat data and input; it never subscribes to native tutorial mail.
    internal sealed class OnlineChatWindow : KScreen
    {
        private static string draftRoomId;
        private static string draftText;
        private OnlineRuntime runtime;
        private LocText heading;
        private TextMeshProUGUI status;
        private KInputTextField message;
        private KButton send;
        private OnlineChatTranscript transcript;
        private RectTransform canvasRect;
        private int revision = -1;
        private int focusFrame = -1;
        private RoomSession session;
        private string sendError;
        private bool closed;

        internal static OnlineChatWindow Create(OnlineRuntime runtime)
        {
            if (runtime == null || !NativeGameplayUi.Ready)
                throw new InvalidOperationException("native_chat_not_ready");
            var prefabs = ScreenPrefabs.Instance;
            var template = prefabs == null ? null : prefabs.MessageDialogFrame;
            var menu = ManagementMenu.Instance;
            var canvas = menu == null ? null : menu.GetComponentInParent<Canvas>();
            var rootCanvas = canvas == null ? null : canvas.rootCanvas;
            if (template == null || rootCanvas == null)
                throw new InvalidOperationException("native_chat_frame_unavailable");

            var staging = new GameObject("OniOnlineChatStaging");
            staging.SetActive(false);
            GameObject panel = null;
            try
            {
                panel = UnityEngine.Object.Instantiate(template.gameObject, staging.transform, false);
                panel.name = "OniOnlineChat";
                var native = panel.GetComponent<MessageDialogFrame>();
                var title = Field<LocText>(native, "title");
                var body = Field<RectTransform>(native, "body");
                var close = Field<KButton>(native, "closeButton");
                HideNativeElement(native, "nextMessageButton");
                HideNativeElement(native, "dontShowAgainElement");
                UnityEngine.Object.DestroyImmediate(native);

                var window = panel.AddComponent<OnlineChatWindow>();
                window.runtime = runtime;
                window.heading = title;
                title.key = "";
                window.canvasRect = (RectTransform)rootCanvas.transform;
                close.ClearOnClick();
                close.ClearOnPointerEvents();
                close.onClick += runtime.HideChat;
                window.Build(body);
                var layout = panel.GetComponent<LayoutElement>() ?? panel.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;
                window.PlaceWindow();
                panel.transform.SetParent(rootCanvas.transform, false);
                window.Activate();
                window.Refresh();
                return window;
            }
            catch
            {
                if (panel != null) UnityEngine.Object.Destroy(panel);
                throw;
            }
            finally
            {
                UnityEngine.Object.Destroy(staging);
            }
        }

        private static T Field<T>(MessageDialogFrame source, string name) where T : UnityEngine.Object
        {
            var field = AccessTools.Field(typeof(MessageDialogFrame), name);
            var result = field == null ? null : field.GetValue(source) as T;
            if (result == null) throw new InvalidOperationException("native_chat_prefab_" + name);
            return result;
        }

        private static void HideNativeElement(MessageDialogFrame source, string name)
        {
            var field = AccessTools.Field(typeof(MessageDialogFrame), name);
            var value = field == null ? null : field.GetValue(source);
            var component = value as Component;
            var item = component == null ? value as GameObject : component.gameObject;
            if (item != null) item.SetActive(false);
        }

        public override bool IsModal() { return false; }
        public override float GetSortKey() { return 25; }

        public override void OnKeyDown(KButtonEvent e)
        {
            if (e.TryConsume(global::Action.Escape))
            {
                runtime.HideChat();
                return;
            }
            if (message != null && message.isFocused) e.Consumed = true;
        }

        public override void OnKeyUp(KButtonEvent e)
        {
            if (message != null && message.isFocused) e.Consumed = true;
        }

        private void Build(RectTransform body)
        {
            var content = OnlineWidgets.Column(body, "Conversation", 6, 5);
            OnlineWidgets.Stretch((RectTransform)content);
            var style = (LocText)AccessTools.Field(typeof(ConfirmDialogScreen), "popupMessage")
                .GetValue(ScreenPrefabs.Instance.ConfirmDialogScreen);
            status = OnlineWidgets.Label(content, "", 16);
            CopyStyle(status, style);
            OnlineWidgets.Size(status.gameObject, -1, 22);
            Transform rows;
            var scroll = OnlineWidgets.Scroll(content, 200, out rows);
            scroll.gameObject.name = "ChatTranscript";
            var surface = scroll.GetComponent<Image>();
            if (surface != null) surface.color = Color.clear;
            var background = scroll.GetComponent<Outline>();
            if (background != null) background.enabled = false;
            UseNativeScrollbar(scroll);
            scroll.GetComponent<LayoutElement>().minHeight = 80;
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            var nativeConfirm = (GameObject)AccessTools.Field(typeof(ConfirmDialogScreen), "confirmButton")
                .GetValue(ScreenPrefabs.Instance.ConfirmDialogScreen);
            var latest = Util.KInstantiateUI<KButton>(nativeConfirm, scroll.gameObject, true);
            latest.gameObject.name = "ChatLatestMessages";
            latest.ClearOnClick();
            latest.ClearOnPointerEvents();
            var latestLabel = latest.GetComponentInChildren<LocText>(true);
            latestLabel.key = "";
            latestLabel.SetText(OnlineText.Get("chat.latest"));
            latest.onClick += () => transcript.FollowLatest();
            var latestRect = (RectTransform)latest.transform;
            latestRect.anchorMin = latestRect.anchorMax = latestRect.pivot = Vector2.zero;
            latestRect.anchoredPosition = new Vector2(4, 4);
            latestRect.sizeDelta = new Vector2(100, 28);
            latest.gameObject.SetActive(false);
            transcript = new OnlineChatTranscript(scroll, rows, latest.gameObject, runtime.ChatFontSize, style);

            var composer = OnlineWidgets.Row(content, 34);
            var nativeInput = (KInputTextField)AccessTools.Field(typeof(FileNameDialog), "inputField")
                .GetValue(ScreenPrefabs.Instance.FileNameDialog);
            message = Util.KInstantiateUI<KInputTextField>(nativeInput.gameObject, composer.gameObject, true);
            message.gameObject.name = "ChatMessageInput";
            message.text = "";
            message.contentType = TMP_InputField.ContentType.Standard;
            message.characterValidation = TMP_InputField.CharacterValidation.None;
            message.onValidateInput = null;
            message.lineType = TMP_InputField.LineType.SingleLine;
            message.characterLimit = Protocol.MaxChatLength;
            message.richText = false;
            message.textComponent.richText = false;
            var inputLabel = message.textComponent as LocText;
            if (inputLabel != null) inputLabel.key = "";
            message.readOnly = false;
            message.OnValueChangesPaused = null;
            // The input prefab belongs to FileNameDialog. Do not retain its
            // filename filtering or its submit/cancel callbacks.
            message.onValueChanged = new TMP_InputField.OnChangeEvent();
            message.onEndEdit = new TMP_InputField.SubmitEvent();
            message.onSubmit = new TMP_InputField.SubmitEvent();
            message.onSubmit.AddListener(_ => Send());
            var placeholder = message.placeholder as TMP_Text;
            if (placeholder != null)
            {
                var localizedPlaceholder = placeholder as LocText;
                if (localizedPlaceholder != null) localizedPlaceholder.key = "";
                placeholder.text = OnlineText.Get("ui.chat_placeholder");
                placeholder.richText = false;
            }
            OnlineWidgets.Size(message.gameObject, -1, 34, 1);
            send = Util.KInstantiateUI<KButton>(nativeConfirm, composer.gameObject, true);
            send.ClearOnClick();
            send.ClearOnPointerEvents();
            var sendLabel = send.GetComponentInChildren<LocText>(true);
            sendLabel.key = "";
            sendLabel.SetText(OnlineText.Get("ui.send"));
            send.onClick += Send;
            OnlineWidgets.Size(send.gameObject, 76, 34);
        }

        private static void UseNativeScrollbar(ScrollRect scroll)
        {
            var previous = scroll.verticalScrollbar;
            Scrollbar template = null;
            var prefabs = ScreenPrefabs.Instance;
            foreach (var screen in new Component[] { prefabs.LoadScreen, prefabs.modsMenu, prefabs.ControlsScreen })
            {
                if (screen == null) continue;
                foreach (var candidate in screen.GetComponentsInChildren<Scrollbar>(true))
                    if (candidate.handleRect != null && (candidate.direction == Scrollbar.Direction.BottomToTop ||
                        candidate.direction == Scrollbar.Direction.TopToBottom))
                    { template = candidate; break; }
                if (template != null) break;
            }
            if (template != null)
            {
                var native = Util.KInstantiateUI<Scrollbar>(template.gameObject, scroll.gameObject, true);
                native.gameObject.name = "ChatScrollbar";
                native.onValueChanged = new Scrollbar.ScrollEvent();
                native.direction = Scrollbar.Direction.BottomToTop;
                var rect = (RectTransform)native.transform;
                float width = Mathf.Clamp(Mathf.Abs(((RectTransform)template.transform).sizeDelta.x), 12, 20);
                rect.anchorMin = new Vector2(1, 0); rect.anchorMax = Vector2.one;
                rect.pivot = Vector2.one;
                rect.offsetMin = new Vector2(-width - 2, 2); rect.offsetMax = new Vector2(-2, -2);
                scroll.verticalScrollbar = native;
            }
            else scroll.verticalScrollbar = null;
            if (previous != null)
            {
                previous.gameObject.SetActive(false);
                Destroy(previous.gameObject);
            }
        }

        private static void CopyStyle(TMP_Text target, TMP_Text source)
        {
            if (source == null) return;
            target.font = source.font;
            target.fontSharedMaterial = source.fontSharedMaterial;
            target.color = source.color;
        }

        internal void FocusComposer() { focusFrame = Time.frameCount + 1; }

        private void Send()
        {
            if (runtime.Session?.SendChat(message.text) != true)
            {
                sendError = OnlineText.Get("chat.send_failed");
                Refresh();
                return;
            }
            sendError = null;
            draftText = message.text = "";
            transcript.FollowLatest();
            FocusComposer();
            Refresh();
        }

        private void Refresh()
        {
            var next = runtime.Session;
            if (!ReferenceEquals(next, session))
            {
                session = next;
                sendError = null;
                message.text = session?.RoomId != null && draftRoomId == session.RoomId ? draftText ?? "" : "";
                if (session?.RoomId != draftRoomId) draftRoomId = draftText = null;
            }
            revision = runtime.Revision;
            bool connected = session?.Joined == true && !session.Closed && !session.Reconnecting && !session.Leaving;
            heading.SetText(OnlineText.Get("chat.button"));
            heading.richText = false;
            status.text = session?.Reconnecting == true ? OnlineText.Get("ui.reconnecting") :
                !connected ? OnlineText.Get("chat.join_first") : sendError ?? "";
            status.gameObject.SetActive(status.text.Length != 0);
            message.interactable = connected;
            send.isInteractable = connected;
            transcript.Refresh(session?.RoomId, session?.Chat ?? new ChatLine[0], session?.Players ?? new PlayerInfo[0]);
        }

        private void PlaceWindow()
        {
            if (canvasRect == null) return;
            float width = Mathf.Min(500, Mathf.Max(1, canvasRect.rect.width - 36));
            float height = Mathf.Min(340, Mathf.Max(1, canvasRect.rect.height - 148));
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            var position = new Vector2(18, Mathf.Min(110, Mathf.Max(0, canvasRect.rect.height - height - 18)));
            var size = new Vector2(width, height);
            if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (canvasRect != null) PlaceWindow();
        }

        private void Update()
        {
            if (runtime == null) return;
            if (!NativeGameplayUi.Ready)
            {
                runtime.HideChat();
                return;
            }
            if (revision != runtime.Revision || session != runtime.Session) Refresh();
            if (focusFrame >= 0 && Time.frameCount >= focusFrame)
            {
                focusFrame = -1;
                if (message.interactable)
                {
                    UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(message.gameObject);
                    message.Select();
                    message.ActivateInputField();
                }
            }
        }

        private void LateUpdate() { transcript?.CompleteLayout(); }

        internal void Close()
        {
            if (closed) return;
            closed = true;
            if (session?.RoomId != null)
            {
                draftRoomId = session.RoomId;
                draftText = message == null ? "" : message.text;
            }
            Deactivate();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
