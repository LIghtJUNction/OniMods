using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniMcp
{
    internal sealed class ToolCallSpeechOverlay : MonoBehaviour
    {
        private const int SortingOrder = 32000;
        private static readonly Color BubbleFill = new Color(0.94f, 0.86f, 0.66f, 0.96f);
        private static readonly Color BubbleAccent = new Color(0.91f, 0.68f, 0.22f, 1f);
        private static readonly Color BubbleText = new Color(0.16f, 0.11f, 0.07f, 1f);

        private Canvas canvas;
        private GameObject bubbleRoot;
        private RectTransform bubbleRect;
        private Image bubbleBackground;
        private Image accent;
        private TextMeshProUGUI text;
        private Transform bubbleTarget;
        private bool followTarget;
        private float hideAt;

        public static ToolCallSpeechOverlay Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null)
                return;

            var obj = new GameObject("OniMcp_ToolCallSpeechOverlay");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            Instance = obj.AddComponent<ToolCallSpeechOverlay>();
        }

        public static bool ShowNearPlayerMouse(string message, float seconds = 5f)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            EnsureInstance();
            return Instance != null && Instance.ShowBubble(message.Trim(), seconds, null);
        }

        public static bool ShowNearDuplicant(string message, MinionIdentity dupe, float seconds = 5f)
        {
            if (string.IsNullOrWhiteSpace(message) || dupe == null)
                return false;

            EnsureInstance();
            return Instance != null && Instance.ShowBubble(message.Trim(), seconds, dupe.transform);
        }

        public static bool NotifyMissingDescription(string toolName)
        {
            string title = "Oni MCP tool text required";
            string detail = string.IsNullOrWhiteSpace(toolName)
                ? "Tool call missing task text."
                : "Tool call missing task text: " + toolName;
            return ShowNotification(title, detail);
        }

        private void Awake()
        {
            Instance = this;
        }

        private void LateUpdate()
        {
            if (bubbleRoot == null)
                return;

            if (Time.unscaledTime >= hideAt)
            {
                bubbleRoot.SetActive(false);
                return;
            }

            if (followTarget)
                PositionBubbleNearTarget();
            else
                PositionBubbleAtMouse();
        }

        private bool ShowBubble(string message, float seconds, Transform target)
        {
            if (Camera.main == null || global::SaveGame.Instance == null)
                return ShowNotification("Oni MCP", message);

            EnsureCanvas();
            bubbleTarget = target;
            followTarget = target != null;
            text.SetText(TrimLong(message, 220));
            bubbleBackground.color = BubbleFill;
            accent.color = BubbleAccent;
            text.color = BubbleText;
            bubbleRoot.SetActive(true);
            hideAt = Time.unscaledTime + Mathf.Max(0.5f, seconds);
            if (followTarget)
                PositionBubbleNearTarget();
            else
                PositionBubbleAtMouse();
            return true;
        }

        private void EnsureCanvas()
        {
            if (canvas != null)
                return;

            var canvasObj = new GameObject("OniMcp_ToolCallSpeechCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObj.transform.SetParent(transform, false);

            var root = canvasObj.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0f, 1f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            bubbleRoot = new GameObject("ToolCallSpeechBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bubbleRoot.transform.SetParent(canvas.transform, false);
            bubbleRect = bubbleRoot.GetComponent<RectTransform>();
            ConfigureTopLeft(bubbleRect);
            bubbleBackground = bubbleRoot.GetComponent<Image>();
            bubbleBackground.raycastTarget = false;

            var accentObj = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            accentObj.transform.SetParent(bubbleRoot.transform, false);
            var accentRect = accentObj.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 1f);
            accentRect.offsetMin = Vector2.zero;
            accentRect.offsetMax = new Vector2(5f, 0f);
            accent = accentObj.GetComponent<Image>();
            accent.raycastTarget = false;

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(bubbleRoot.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 8f);
            textRect.offsetMax = new Vector2(-12f, -8f);

            text = textObj.GetComponent<TextMeshProUGUI>();
            text.fontSize = 13f;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;

            bubbleRoot.SetActive(false);
        }

        private void PositionBubbleAtMouse()
        {
            Vector3 mouse = Input.mousePosition;
            string value = text == null ? string.Empty : text.text;
            float width = value.Length > 52 ? 320f : Mathf.Clamp(value.Length * 8f + 42f, 140f, 320f);
            float height = Mathf.Clamp(text.GetPreferredValues(value, width - 26f, 0f).y + 18f, 40f, 108f);
            float x = Mathf.Clamp(mouse.x + 18f, 8f, Screen.width - width - 8f);
            float y = Mathf.Clamp(Screen.height - mouse.y + 18f, 8f, Screen.height - height - 8f);
            SetTopLeftRect(bubbleRect, x, y, width, height);
        }

        private void PositionBubbleNearTarget()
        {
            if (bubbleTarget == null || Camera.main == null)
            {
                bubbleRoot.SetActive(false);
                return;
            }

            Vector3 head = bubbleTarget.position + Vector3.up * 1.5f;
            Vector3 screen = Camera.main.WorldToScreenPoint(head);
            bool visible = screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;
            bubbleRoot.SetActive(visible);
            if (!visible)
                return;

            string value = text == null ? string.Empty : text.text;
            float width = value.Length > 52 ? 320f : Mathf.Clamp(value.Length * 8f + 42f, 140f, 320f);
            float height = Mathf.Clamp(text.GetPreferredValues(value, width - 26f, 0f).y + 18f, 40f, 108f);
            float x = Mathf.Clamp(screen.x + 18f, 8f, Screen.width - width - 8f);
            float y = Mathf.Clamp(Screen.height - screen.y - height - 10f, 8f, Screen.height - height - 8f);
            SetTopLeftRect(bubbleRect, x, y, width, height);
        }

        private static bool ShowNotification(string title, string message)
        {
            try
            {
                if (NotificationManager.Instance == null)
                    return false;

                var notification = new Notification(
                    title,
                    NotificationType.Neutral,
                    null,
                    message,
                    true,
                    0f,
                    null,
                    null,
                    null,
                    true,
                    false,
                    true);
                notification.playSound = false;
                notification.GameTime = Time.time;
                notification.Time = KTime.Instance != null ? KTime.Instance.UnscaledGameTime : Time.unscaledTime;
                NotificationManager.Instance.AddNotification(notification);
                return true;
            }
            catch (Exception ex)
            {
                Support.OniMcpLog.Warning("[OniMcp] Failed to show tool-call notification: " + ex.Message);
                return false;
            }
        }

        private static string TrimLong(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, max - 1) + ".";
        }

        private static void ConfigureTopLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }

        private static void SetTopLeftRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
        }
    }
}
