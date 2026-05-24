using System;
using System.Collections.Generic;
using OniMcp.Support;
using OniMcp.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniMcp
{
    internal sealed class PlanningViewOverlay : MonoBehaviour
    {
        public const string ModeName = "OniMcpPromptPlanning";
        public static readonly HashedString ModeId = new HashedString(ModeName);

        private const int MaxVisibleMarks = 20;
        private const float MinLabelWidth = 220f;
        private const float MaxLabelWidth = 520f;
        private const float LabelHeight = 34f;

        private static readonly Color FillColor = new Color(0.25f, 0.95f, 1f, 0.14f);
        private static readonly Color BorderColor = new Color(0.25f, 1f, 1f, 0.95f);
        private static readonly Color AltFillColor = new Color(1f, 0.65f, 0.18f, 0.13f);
        private static readonly Color AltBorderColor = new Color(1f, 0.75f, 0.25f, 0.95f);

        private readonly List<MarkVisual> markVisuals = new List<MarkVisual>();
        private readonly List<PromptLabel> labels = new List<PromptLabel>();
        private Canvas overlayCanvas;
        private RectTransform overlayRoot;
        private int visibleCount;

        public static PlanningViewOverlay Instance { get; private set; }
        public static bool IsVisible { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null)
                return;

            var obj = new GameObject("OniMcp_PlanningViewOverlay");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            Instance = obj.AddComponent<PlanningViewOverlay>();
        }

        public static void SetVisible(bool visible)
        {
            EnsureInstance();
            IsVisible = visible;
            Instance.SetLabelsVisible(visible);
            OniMcpLog.Debug("[OniMcp] Planning view overlay visible=" + visible);
        }

        public static void Toggle()
        {
            SetVisible(!IsVisible);
        }

        private void OnPrefabInit()
        {
            Instance = this;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void LateUpdate()
        {
            if (!IsVisible)
            {
                visibleCount = 0;
                SetOverlayVisible(false);
                return;
            }

            EnsureOverlayCanvas();
            SetOverlayVisible(true);
            RefreshRenderedMarks();
        }

        private void RefreshRenderedMarks()
        {
            var snapshots = EditMarkTools.GetPendingSnapshots(MaxVisibleMarks);
            visibleCount = 0;
            int activeWorldId = ClusterManager.Instance != null ? ClusterManager.Instance.activeWorldId : 0;

            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                if (ClusterManager.Instance != null && snapshot.WorldId != activeWorldId)
                    continue;

                Rect screenRect;
                if (!TryGetScreenRect(snapshot, out screenRect))
                    continue;

                bool alt = visibleCount % 2 == 1;
                UpdateMarkVisual(visibleCount, screenRect, alt ? AltFillColor : FillColor, alt ? AltBorderColor : BorderColor);
                UpdateLabel(visibleCount, snapshot, screenRect);
                visibleCount++;
            }

            for (int i = visibleCount; i < markVisuals.Count; i++)
                markVisuals[i].Root.SetActive(false);

            for (int i = visibleCount; i < labels.Count; i++)
                labels[i].Root.SetActive(false);
        }

        private static bool TryGetScreenRect(EditMarkTools.EditMarkSnapshot snapshot, out Rect rect)
        {
            rect = default(Rect);
            Camera camera = Camera.main;
            if (camera == null)
                return false;

            var minWorld = new Vector3(snapshot.X1 - 0.5f, snapshot.Y1 - 0.5f, 0f);
            var maxWorld = new Vector3(snapshot.X2 + 0.5f, snapshot.Y2 + 0.5f, 0f);
            var a = camera.WorldToScreenPoint(minWorld);
            var b = camera.WorldToScreenPoint(maxWorld);
            if (a.z <= 0f && b.z <= 0f)
                return false;

            a.y = Screen.height - a.y;
            b.y = Screen.height - b.y;
            rect = Rect.MinMaxRect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x),
                Mathf.Max(a.y, b.y));

            return rect.xMax >= 0f
                && rect.yMax >= 0f
                && rect.xMin <= Screen.width
                && rect.yMin <= Screen.height
                && rect.width >= 2f
                && rect.height >= 2f;
        }

        private void EnsureOverlayCanvas()
        {
            if (overlayCanvas != null)
                return;

            var canvasObj = new GameObject("OniMcp_PlanningViewOverlayCanvas", typeof(RectTransform));
            canvasObj.transform.SetParent(transform, false);
            overlayRoot = canvasObj.GetComponent<RectTransform>();
            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 30000;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            overlayRoot.anchorMin = Vector2.zero;
            overlayRoot.anchorMax = Vector2.one;
            overlayRoot.pivot = new Vector2(0f, 1f);
            overlayRoot.offsetMin = Vector2.zero;
            overlayRoot.offsetMax = Vector2.zero;
        }

        private void UpdateMarkVisual(int index, Rect screenRect, Color fill, Color border)
        {
            while (markVisuals.Count <= index)
                markVisuals.Add(CreateMarkVisual());

            var visual = markVisuals[index];
            visual.Root.SetActive(true);
            visual.Fill.color = fill;
            visual.TopBorder.color = border;
            visual.BottomBorder.color = border;
            visual.LeftBorder.color = border;
            visual.RightBorder.color = border;

            const float borderWidth = 2f;
            SetTopLeftRect(visual.Rect, screenRect.xMin, screenRect.yMin, screenRect.width, screenRect.height);
            SetTopLeftRect(visual.Fill.rectTransform, 0f, 0f, screenRect.width, screenRect.height);
            SetTopLeftRect(visual.TopBorder.rectTransform, 0f, 0f, screenRect.width, borderWidth);
            SetTopLeftRect(visual.BottomBorder.rectTransform, 0f, screenRect.height - borderWidth, screenRect.width, borderWidth);
            SetTopLeftRect(visual.LeftBorder.rectTransform, 0f, 0f, borderWidth, screenRect.height);
            SetTopLeftRect(visual.RightBorder.rectTransform, screenRect.width - borderWidth, 0f, borderWidth, screenRect.height);
        }

        private MarkVisual CreateMarkVisual()
        {
            EnsureOverlayCanvas();
            var root = new GameObject("PromptPlanMark", typeof(RectTransform));
            root.transform.SetParent(overlayCanvas.transform, false);
            var rootRect = root.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rootRect);

            return new MarkVisual
            {
                Root = root,
                Rect = rootRect,
                Fill = CreateImage(root.transform, "Fill"),
                TopBorder = CreateImage(root.transform, "TopBorder"),
                BottomBorder = CreateImage(root.transform, "BottomBorder"),
                LeftBorder = CreateImage(root.transform, "LeftBorder"),
                RightBorder = CreateImage(root.transform, "RightBorder")
            };
        }

        private static Image CreateImage(Transform parent, string name)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rect);

            var image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private void UpdateLabel(int index, EditMarkTools.EditMarkSnapshot snapshot, Rect screenRect)
        {
            while (labels.Count <= index)
                labels.Add(CreateLabel());

            var label = labels[index];
            label.Root.SetActive(true);
            label.Text.SetText(FormatLabelText(snapshot));

            float width = Mathf.Clamp(screenRect.width - 8f, MinLabelWidth, MaxLabelWidth);
            float x = Mathf.Clamp(screenRect.xMin + 4f, 8f, Mathf.Max(8f, Screen.width - width - 8f));
            float y = screenRect.yMin + 4f;
            if (screenRect.height < LabelHeight + 12f)
                y = screenRect.yMin - LabelHeight - 4f;
            if (y < 8f)
                y = screenRect.yMax + 4f;
            y = Mathf.Clamp(y, 8f, Mathf.Max(8f, Screen.height - LabelHeight - 8f));

            SetTopLeftRect(label.Rect, x, y, width, LabelHeight);
            label.Root.SetActive(true);
        }

        private PromptLabel CreateLabel()
        {
            EnsureOverlayCanvas();
            var root = new GameObject("PromptPlanLabel", typeof(RectTransform));
            root.transform.SetParent(overlayCanvas.transform, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.sizeDelta = new Vector2(MinLabelWidth, LabelHeight);

            var background = root.AddComponent<Image>();
            background.color = new Color(0.03f, 0.035f, 0.04f, 0.82f);
            background.raycastTarget = false;

            var textObj = new GameObject("Text", typeof(RectTransform));
            textObj.transform.SetParent(root.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-8f, -4f);

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.richText = false;
            text.fontSize = 13f;
            text.color = new Color(0.93f, 0.98f, 1f, 1f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            if (Localization.FontAsset != null)
                text.font = Localization.FontAsset;

            return new PromptLabel
            {
                Root = root,
                Rect = rootRect,
                Text = text
            };
        }

        private static string FormatLabelText(EditMarkTools.EditMarkSnapshot snapshot)
        {
            string prompt = snapshot.Prompt ?? "";
            prompt = prompt.Replace("\r", " ").Replace("\n", " ").Trim();
            if (prompt.Length == 0)
                prompt = "(empty prompt)";
            if (prompt.Length > 140)
                prompt = prompt.Substring(0, 137) + "...";

            return string.IsNullOrEmpty(snapshot.AreaId) ? prompt : snapshot.AreaId + ": " + prompt;
        }

        private void SetLabelsVisible(bool visible)
        {
            SetOverlayVisible(visible);
        }

        private void SetOverlayVisible(bool visible)
        {
            if (overlayCanvas != null)
                overlayCanvas.gameObject.SetActive(visible);

            if (!visible)
            {
                for (int i = 0; i < markVisuals.Count; i++)
                    markVisuals[i].Root.SetActive(false);

                for (int i = 0; i < labels.Count; i++)
                    labels[i].Root.SetActive(false);
            }
        }

        private static void ConfigureTopLeftRect(RectTransform rect)
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

        private sealed class MarkVisual
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Fill;
            public Image TopBorder;
            public Image BottomBorder;
            public Image LeftBorder;
            public Image RightBorder;
        }

        private sealed class PromptLabel
        {
            public GameObject Root;
            public RectTransform Rect;
            public TextMeshProUGUI Text;
        }
    }
}
