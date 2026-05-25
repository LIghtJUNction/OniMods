using System;
using System.Collections.Generic;
using OniMcp.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniMcp
{
    internal sealed class AgentPointerOverlay : MonoBehaviour
    {
        private const int MaxPointers = 16;
        private const int SortingOrder = 32000;
        private static readonly Color PointerShadow = new Color(0.03f, 0.025f, 0.02f, 0.45f);
        private static readonly Color PointerRim = new Color(0.12f, 0.085f, 0.055f, 0.98f);
        private static readonly Color PointerFill = new Color(0.91f, 0.82f, 0.62f, 1f);
        private static readonly Color PointerHighlight = new Color(1f, 0.96f, 0.78f, 0.72f);
        private static readonly Color DragFill = new Color(1f, 0.68f, 0.22f, 1f);
        private static readonly Color PanelFill = new Color(0.12f, 0.105f, 0.085f, 0.9f);
        private static readonly Color BadgeFill = new Color(0.18f, 0.145f, 0.095f, 0.95f);
        private static readonly Color TextWarmWhite = new Color(0.98f, 0.94f, 0.82f, 1f);
        private static readonly Color BubbleFill = new Color(0.94f, 0.86f, 0.66f, 0.96f);
        private static readonly Color BubbleText = new Color(0.16f, 0.11f, 0.07f, 1f);
        private readonly List<PointerVisual> visuals = new List<PointerVisual>();
        private Canvas canvas;
        private RectTransform rootRect;

        public static AgentPointerOverlay Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null)
                return;

            var obj = new GameObject("OniMcp_AgentPointerOverlay");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            Instance = obj.AddComponent<AgentPointerOverlay>();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void LateUpdate()
        {
            EnsureCanvas();
            if (!ShouldRenderPointers())
            {
                if (canvas != null)
                    canvas.enabled = false;
                HideVisuals();
                return;
            }

            if (canvas != null)
                canvas.enabled = true;

            var pointers = AgentPointerRegistry.States();
            int visibleCount = 0;

            foreach (var pointer in pointers)
            {
                if (visibleCount >= MaxPointers)
                    break;
                if (pointer == null || !pointer.Visible || pointer.Cell < 0)
                    continue;
                if (ClusterManager.Instance != null && pointer.WorldId >= 0 && pointer.WorldId != ClusterManager.Instance.activeWorldId)
                    continue;

                var targetScreen = WorldToTopLeftScreen(pointer.WorldPosition);
                if (targetScreen.x < -40f || targetScreen.y < -40f || targetScreen.x > Screen.width + 40f || targetScreen.y > Screen.height + 40f)
                    continue;

                var visual = Visual(visibleCount++);
                var screen = targetScreen;
                visual.Root.SetActive(true);
                visual.LabelRoot.SetActive(true);
                visual.BadgeRoot.SetActive(true);
                bool dragFeedback = pointer.IsDragging || pointer.DragFeedbackUntil > System.DateTime.UtcNow;
                SetCursorDragging(visual, dragFeedback);
                Color accent = dragFeedback ? Color.Lerp(pointer.Color, DragFill, 0.55f) : pointer.Color;
                visual.Shadow.color = PointerShadow;
                visual.Rim.color = PointerRim;
                visual.Body.color = Color.Lerp(accent, dragFeedback ? DragFill : PointerFill, 0.72f);
                visual.Highlight.color = PointerHighlight;
                visual.Accent.color = Color.Lerp(accent, PointerFill, 0.18f);
                visual.Reticle.color = Color.Lerp(accent, Color.white, 0.15f);
                visual.LabelBackground.color = PanelFill;
                visual.BadgeBackground.color = BadgeFill;
                visual.LabelAccent.color = Color.Lerp(accent, PointerFill, 0.2f);
                visual.BadgeAccent.color = Color.Lerp(accent, PointerFill, 0.15f);
                visual.Label.color = TextWarmWhite;
                visual.Badge.color = TextWarmWhite;
                visual.Label.SetText(Label(pointer));
                visual.Badge.SetText(ToolBadge(pointer));
                SetTopLeftRect(visual.Rect, screen.x - 16f, screen.y - 14f, 74f, 76f);
                SetTopLeftRect(visual.BadgeRect, screen.x + 36f, screen.y - 12f, 132f, 25f);
                SetTopLeftRect(visual.LabelRect, screen.x + 31f, screen.y + 18f, 224f, 29f);
                UpdateBubble(visual, pointer, screen, accent);
                UpdateDragSelection(visual, pointer, accent);
            }

            HideVisuals(visibleCount);
        }

        private void EnsureCanvas()
        {
            if (canvas != null)
                return;

            var canvasObj = new GameObject("OniMcp_AgentPointerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObj.transform.SetParent(transform, false);
            rootRect = canvasObj.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        }

        private PointerVisual Visual(int index)
        {
            while (visuals.Count <= index)
                visuals.Add(CreateVisual());
            return visuals[index];
        }

        private void HideVisuals(int startIndex = 0)
        {
            for (int i = startIndex; i < visuals.Count; i++)
            {
                visuals[i].Root.SetActive(false);
                visuals[i].LabelRoot.SetActive(false);
                visuals[i].BadgeRoot.SetActive(false);
                visuals[i].BubbleRoot.SetActive(false);
                if (visuals[i].DragRoot != null)
                    visuals[i].DragRoot.SetActive(false);
                visuals[i].DragAnimActive = false;
            }
        }

        private static bool ShouldRenderPointers()
        {
            return Game.Instance != null
                && global::SaveGame.Instance != null
                && ClusterManager.Instance != null
                && ClusterManager.Instance.worldCount > 0
                && Camera.main != null;
        }

        private PointerVisual CreateVisual()
        {
            EnsureCanvas();

            var root = new GameObject("AgentPointer", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            var rect = root.GetComponent<RectTransform>();
            ConfigureTopLeft(rect);

            var reticleObj = new GameObject("TargetReticle", typeof(RectTransform), typeof(CanvasRenderer), typeof(ReticleGraphic));
            reticleObj.transform.SetParent(root.transform, false);
            var reticleRect = reticleObj.GetComponent<RectTransform>();
            ConfigureTopLeft(reticleRect);
            SetTopLeftRect(reticleRect, 0f, 0f, 34f, 34f);
            var reticle = reticleObj.GetComponent<ReticleGraphic>();
            reticle.raycastTarget = false;

            var shadowObj = new GameObject("CursorShadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(CursorGraphic));
            shadowObj.transform.SetParent(root.transform, false);
            var shadowRect = shadowObj.GetComponent<RectTransform>();
            ConfigureTopLeft(shadowRect);
            SetTopLeftRect(shadowRect, 18f, 17f, 48f, 56f);
            var shadow = shadowObj.GetComponent<CursorGraphic>();
            shadow.Layer = CursorLayer.Rim;
            shadow.raycastTarget = false;

            var rimObj = new GameObject("CursorRim", typeof(RectTransform), typeof(CanvasRenderer), typeof(CursorGraphic));
            rimObj.transform.SetParent(root.transform, false);
            var rimRect = rimObj.GetComponent<RectTransform>();
            ConfigureTopLeft(rimRect);
            SetTopLeftRect(rimRect, 14f, 12f, 48f, 56f);
            var rim = rimObj.GetComponent<CursorGraphic>();
            rim.Layer = CursorLayer.Rim;
            rim.raycastTarget = false;

            var bodyObj = new GameObject("CursorBody", typeof(RectTransform), typeof(CanvasRenderer), typeof(CursorGraphic));
            bodyObj.transform.SetParent(root.transform, false);
            var bodyRect = bodyObj.GetComponent<RectTransform>();
            ConfigureTopLeft(bodyRect);
            SetTopLeftRect(bodyRect, 18f, 16f, 39f, 47f);
            var body = bodyObj.GetComponent<CursorGraphic>();
            body.Layer = CursorLayer.Body;
            body.raycastTarget = false;

            var highlightObj = new GameObject("CursorHighlight", typeof(RectTransform), typeof(CanvasRenderer), typeof(CursorGraphic));
            highlightObj.transform.SetParent(root.transform, false);
            var highlightRect = highlightObj.GetComponent<RectTransform>();
            ConfigureTopLeft(highlightRect);
            SetTopLeftRect(highlightRect, 22f, 20f, 28f, 30f);
            var highlight = highlightObj.GetComponent<CursorGraphic>();
            highlight.Layer = CursorLayer.Highlight;
            highlight.raycastTarget = false;

            var accentObj = new GameObject("CursorAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(CursorGraphic));
            accentObj.transform.SetParent(root.transform, false);
            var accentRect = accentObj.GetComponent<RectTransform>();
            ConfigureTopLeft(accentRect);
            SetTopLeftRect(accentRect, 30f, 39f, 22f, 22f);
            var accent = accentObj.GetComponent<CursorGraphic>();
            accent.Layer = CursorLayer.Accent;
            accent.raycastTarget = false;

            var badgeRoot = new GameObject("ToolBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            badgeRoot.transform.SetParent(canvas.transform, false);
            var badgeRect = badgeRoot.GetComponent<RectTransform>();
            ConfigureTopLeft(badgeRect);
            var badgeBackground = badgeRoot.GetComponent<Image>();
            badgeBackground.raycastTarget = false;

            var badgeAccent = CreateImage(badgeRoot.transform, "Accent");
            var badgeAccentRect = badgeAccent.GetComponent<RectTransform>();
            badgeAccentRect.anchorMin = new Vector2(0f, 0f);
            badgeAccentRect.anchorMax = new Vector2(0f, 1f);
            badgeAccentRect.pivot = new Vector2(0f, 1f);
            badgeAccentRect.offsetMin = new Vector2(0f, 0f);
            badgeAccentRect.offsetMax = new Vector2(4f, 0f);

            var badgeTextObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            badgeTextObj.transform.SetParent(badgeRoot.transform, false);
            var badgeTextRect = badgeTextObj.GetComponent<RectTransform>();
            badgeTextRect.anchorMin = Vector2.zero;
            badgeTextRect.anchorMax = Vector2.one;
            badgeTextRect.offsetMin = new Vector2(9f, 2f);
            badgeTextRect.offsetMax = new Vector2(-7f, -2f);
            var badgeText = badgeTextObj.GetComponent<TextMeshProUGUI>();
            badgeText.fontSize = 12f;
            badgeText.alignment = TextAlignmentOptions.Left;
            badgeText.textWrappingMode = TextWrappingModes.NoWrap;
            badgeText.raycastTarget = false;

            var labelRoot = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            labelRoot.transform.SetParent(canvas.transform, false);
            var labelRect = labelRoot.GetComponent<RectTransform>();
            ConfigureTopLeft(labelRect);
            var background = labelRoot.GetComponent<Image>();
            background.raycastTarget = false;

            var labelAccent = CreateImage(labelRoot.transform, "Accent");
            var labelAccentRect = labelAccent.GetComponent<RectTransform>();
            labelAccentRect.anchorMin = new Vector2(0f, 0f);
            labelAccentRect.anchorMax = new Vector2(0f, 1f);
            labelAccentRect.pivot = new Vector2(0f, 1f);
            labelAccentRect.offsetMin = new Vector2(0f, 0f);
            labelAccentRect.offsetMax = new Vector2(4f, 0f);

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(labelRoot.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(9f, 3f);
            textRect.offsetMax = new Vector2(-7f, -3f);
            var text = textObj.GetComponent<TextMeshProUGUI>();
            text.fontSize = 13f;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            var bubbleRoot = new GameObject("ChatBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bubbleRoot.transform.SetParent(canvas.transform, false);
            var bubbleRect = bubbleRoot.GetComponent<RectTransform>();
            ConfigureTopLeft(bubbleRect);
            var bubbleBackground = bubbleRoot.GetComponent<Image>();
            bubbleBackground.raycastTarget = false;

            var bubbleAccent = CreateImage(bubbleRoot.transform, "Accent");
            var bubbleAccentRect = bubbleAccent.GetComponent<RectTransform>();
            bubbleAccentRect.anchorMin = new Vector2(0f, 0f);
            bubbleAccentRect.anchorMax = new Vector2(0f, 1f);
            bubbleAccentRect.pivot = new Vector2(0f, 1f);
            bubbleAccentRect.offsetMin = new Vector2(0f, 0f);
            bubbleAccentRect.offsetMax = new Vector2(5f, 0f);

            var tailObj = new GameObject("Tail", typeof(RectTransform), typeof(CanvasRenderer), typeof(BubbleTailGraphic));
            tailObj.transform.SetParent(bubbleRoot.transform, false);
            var tailRect = tailObj.GetComponent<RectTransform>();
            ConfigureTopLeft(tailRect);
            SetTopLeftRect(tailRect, 12f, 0f, 24f, 16f);
            var bubbleTail = tailObj.GetComponent<BubbleTailGraphic>();
            bubbleTail.raycastTarget = false;

            var bubbleTextObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            bubbleTextObj.transform.SetParent(bubbleRoot.transform, false);
            var bubbleTextRect = bubbleTextObj.GetComponent<RectTransform>();
            bubbleTextRect.anchorMin = Vector2.zero;
            bubbleTextRect.anchorMax = Vector2.one;
            bubbleTextRect.offsetMin = new Vector2(12f, 7f);
            bubbleTextRect.offsetMax = new Vector2(-10f, -7f);
            var bubbleText = bubbleTextObj.GetComponent<TextMeshProUGUI>();
            bubbleText.fontSize = 13f;
            bubbleText.alignment = TextAlignmentOptions.Left;
            bubbleText.textWrappingMode = TextWrappingModes.Normal;
            bubbleText.overflowMode = TextOverflowModes.Ellipsis;
            bubbleText.raycastTarget = false;

            var dragRoot = new GameObject("DragSelection", typeof(RectTransform));
            dragRoot.transform.SetParent(canvas.transform, false);
            var dragRootRect = dragRoot.GetComponent<RectTransform>();
            dragRootRect.anchorMin = Vector2.zero;
            dragRootRect.anchorMax = Vector2.zero;
            dragRootRect.pivot = Vector2.zero;
            dragRootRect.anchoredPosition = Vector2.zero;
            dragRootRect.sizeDelta = Vector2.zero;

            var dragFillRt = CreateDragImage(dragRoot.transform, "Fill", new Color(0.2f, 0.95f, 1f, 0.15f));
            var dragTopRt = CreateDragImage(dragRoot.transform, "TopBorder", new Color(0.2f, 1f, 1f, 0.85f));
            var dragBottomRt = CreateDragImage(dragRoot.transform, "BottomBorder", new Color(0.2f, 1f, 1f, 0.85f));
            var dragLeftRt = CreateDragImage(dragRoot.transform, "LeftBorder", new Color(0.2f, 1f, 1f, 0.85f));
            var dragRightRt = CreateDragImage(dragRoot.transform, "RightBorder", new Color(0.2f, 1f, 1f, 0.85f));

            root.SetActive(false);
            badgeRoot.SetActive(false);
            labelRoot.SetActive(false);
            bubbleRoot.SetActive(false);
            dragRoot.SetActive(false);
            return new PointerVisual
            {
                Root = root,
                Rect = rect,
                Reticle = reticle,
                Shadow = shadow,
                Rim = rim,
                Body = body,
                Highlight = highlight,
                Accent = accent,
                BadgeRoot = badgeRoot,
                BadgeRect = badgeRect,
                BadgeBackground = badgeBackground,
                BadgeAccent = badgeAccent,
                Badge = badgeText,
                LabelRoot = labelRoot,
                LabelRect = labelRect,
                LabelBackground = background,
                LabelAccent = labelAccent,
                Label = text,
                BubbleRoot = bubbleRoot,
                BubbleRect = bubbleRect,
                BubbleBackground = bubbleBackground,
                BubbleAccent = bubbleAccent,
                BubbleTailRect = tailRect,
                BubbleTail = bubbleTail,
                Bubble = bubbleText,
                DragRoot = dragRoot,
                DragFillRect = dragFillRt,
                DragFill = dragFillRt.GetComponent<Image>(),
                DragTopBorder = dragTopRt,
                DragBottomBorder = dragBottomRt,
                DragLeftBorder = dragLeftRt,
                DragRightBorder = dragRightRt
            };
        }

        private static Image CreateImage(Transform parent, string name)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            ConfigureTopLeft(rect);
            var image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static Vector2 WorldToTopLeftScreen(Vector3 world)
        {
            var camera = Camera.main;
            if (camera == null)
                return Vector2.zero;
            var screen = camera.WorldToScreenPoint(world);
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        private static string Label(AgentPointerState pointer)
        {
            string label = string.IsNullOrWhiteSpace(pointer.Label) ? "agent" : pointer.Label;
            if (!Grid.IsValidCell(pointer.Cell))
                return label;
            Grid.CellToXY(pointer.Cell, out int x, out int y);
            return pointer.IsDragging ? $"{label} drag ({x},{y})" : $"{label} ({x},{y})";
        }

        private static string ToolBadge(AgentPointerState pointer)
        {
            string tool = string.IsNullOrWhiteSpace(pointer.ToolLabel) ? pointer.CurrentTool : pointer.ToolLabel;
            if (string.IsNullOrWhiteSpace(tool))
                tool = "Inspect";
            if (pointer.CurrentTool == "build")
            {
                string prefab = TrimLong(string.IsNullOrWhiteSpace(pointer.BuildPrefabId) ? "Build" : pointer.BuildPrefabId, 13);
                string material = TrimLong(string.IsNullOrWhiteSpace(pointer.BuildMaterial) ? "auto" : pointer.BuildMaterial, 10);
                return prefab + " / " + material;
            }
            return TrimLong(tool, 18);
        }

        private static string TrimLong(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
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

        private static void SetCursorDragging(PointerVisual visual, bool dragging)
        {
            if (visual.CursorDragging == dragging)
                return;
            visual.CursorDragging = dragging;
            visual.Shadow.Dragging = dragging;
            visual.Rim.Dragging = dragging;
            visual.Body.Dragging = dragging;
            visual.Highlight.Dragging = dragging;
            visual.Accent.Dragging = dragging;
            visual.Shadow.SetVerticesDirty();
            visual.Rim.SetVerticesDirty();
            visual.Body.SetVerticesDirty();
            visual.Highlight.SetVerticesDirty();
            visual.Accent.SetVerticesDirty();
        }

        private static void UpdateBubble(PointerVisual visual, AgentPointerState pointer, Vector2 screen, Color accent)
        {
            bool visible = !string.IsNullOrWhiteSpace(pointer.Message) && pointer.MessageExpiresAt > System.DateTime.UtcNow;
            visual.BubbleRoot.SetActive(visible);
            if (!visible)
                return;

            visual.BubbleBackground.color = BubbleFill;
            visual.BubbleAccent.color = Color.Lerp(accent, PointerFill, 0.25f);
            visual.BubbleTail.color = BubbleFill;
            visual.Bubble.color = BubbleText;
            visual.Bubble.SetText(pointer.Message);
            float width = pointer.Message.Length > 52 ? 300f : pointer.Message.Length > 24 ? 250f : 190f;
            float preferred = Mathf.Ceil(visual.Bubble.GetPreferredValues(pointer.Message, width - 22f, 80f).y);
            float height = Mathf.Clamp(preferred + 18f, 34f, 92f);
            SetTopLeftRect(visual.BubbleRect, screen.x + 28f, screen.y - height - 22f, width, height);
            SetTopLeftRect(visual.BubbleTailRect, 14f, height - 1f, 24f, 16f);
        }

        private static void UpdateDragSelection(PointerVisual visual, AgentPointerState pointer, Color accent)
        {
            bool shouldShow = (pointer.IsDragging || pointer.DragFeedbackUntil > System.DateTime.UtcNow)
                && pointer.DragStartCell >= 0 && pointer.DragCurrentCell >= 0;

            if (!shouldShow)
            {
                if (visual.DragRoot != null)
                    visual.DragRoot.SetActive(false);
                visual.DragAnimActive = false;
                return;
            }

            Grid.CellToXY(pointer.DragStartCell, out int sx, out int sy);
            Grid.CellToXY(pointer.DragCurrentCell, out int ex, out int ey);

            int minX = Math.Min(sx, ex);
            int minY = Math.Min(sy, ey);
            int maxX = Math.Max(sx, ex);
            int maxY = Math.Max(sy, ey);

            var camera = Camera.main;
            if (camera == null)
                return;

            var bottomLeftScreen = camera.WorldToScreenPoint(new Vector3(minX, minY, -100f));
            var topRightScreen = camera.WorldToScreenPoint(new Vector3(maxX + 1f, maxY + 1f, -100f));

            Vector2 targetMin = new Vector2(bottomLeftScreen.x, bottomLeftScreen.y);
            Vector2 targetMax = new Vector2(topRightScreen.x, topRightScreen.y);

            float now = Time.unscaledTime;

            if (!visual.DragAnimActive)
            {
                visual.DragAnimCurrentMin = targetMin;
                visual.DragAnimCurrentMax = targetMin;
                visual.DragAnimStartTime = now;
            }
            else if ((visual.DragAnimTargetMin - targetMin).sqrMagnitude > 0.01f ||
                     (visual.DragAnimTargetMax - targetMax).sqrMagnitude > 0.01f)
            {
                visual.DragAnimCurrentMin = GetDragCurrentMin(visual);
                visual.DragAnimCurrentMax = GetDragCurrentMax(visual);
                visual.DragAnimStartTime = now;
            }

            visual.DragAnimTargetMin = targetMin;
            visual.DragAnimTargetMax = targetMax;
            visual.DragAnimActive = true;

            const float dragAnimDuration = 0.35f;
            float t = Mathf.Clamp01((now - visual.DragAnimStartTime) / dragAnimDuration);
            t = 1f - Mathf.Pow(1f - t, 3f);

            Vector2 currentMin = Vector2.Lerp(visual.DragAnimCurrentMin, visual.DragAnimTargetMin, t);
            Vector2 currentMax = Vector2.Lerp(visual.DragAnimCurrentMax, visual.DragAnimTargetMax, t);

            Color fillColor = new Color(accent.r, accent.g, accent.b, 0.15f);
            Color borderColor = new Color(
                Mathf.Min(1f, accent.r * 1.3f),
                Mathf.Min(1f, accent.g * 1.3f),
                Mathf.Min(1f, accent.b * 1.3f),
                0.85f);

            visual.DragFill.color = fillColor;
            SetImageColor(visual.DragTopBorder, borderColor);
            SetImageColor(visual.DragBottomBorder, borderColor);
            SetImageColor(visual.DragLeftBorder, borderColor);
            SetImageColor(visual.DragRightBorder, borderColor);

            float left = currentMin.x;
            float bottom = currentMin.y;
            float width = Mathf.Max(0f, currentMax.x - currentMin.x);
            float height = Mathf.Max(0f, currentMax.y - currentMin.y);
            const float borderWidth = 2f;

            SetDragRect(visual.DragFillRect, left, bottom, width, height);
            SetDragRect(visual.DragTopBorder, left, bottom + height - borderWidth, width, borderWidth);
            SetDragRect(visual.DragBottomBorder, left, bottom, width, borderWidth);
            SetDragRect(visual.DragLeftBorder, left, bottom, borderWidth, height);
            SetDragRect(visual.DragRightBorder, left + width - borderWidth, bottom, borderWidth, height);

            visual.DragRoot.SetActive(true);
        }

        private static Vector2 GetDragCurrentMin(PointerVisual visual)
        {
            if (visual.DragFillRect == null)
                return visual.DragAnimTargetMin;
            return visual.DragFillRect.anchoredPosition;
        }

        private static Vector2 GetDragCurrentMax(PointerVisual visual)
        {
            if (visual.DragFillRect == null)
                return visual.DragAnimTargetMax;
            return visual.DragFillRect.anchoredPosition + visual.DragFillRect.sizeDelta;
        }

        private static void SetDragRect(RectTransform rect, float x, float y, float width, float height)
        {
            if (rect == null) return;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
        }

        private static void SetImageColor(RectTransform rect, Color color)
        {
            if (rect == null) return;
            var image = rect.GetComponent<Image>();
            if (image != null)
                image.color = color;
        }

        private static RectTransform CreateDragImage(Transform parent, string name, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            var image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private sealed class PointerVisual
        {
            public bool CursorDragging;
            public GameObject Root;
            public RectTransform Rect;
            public ReticleGraphic Reticle;
            public CursorGraphic Shadow;
            public CursorGraphic Rim;
            public CursorGraphic Body;
            public CursorGraphic Highlight;
            public CursorGraphic Accent;
            public GameObject BadgeRoot;
            public RectTransform BadgeRect;
            public Image BadgeBackground;
            public Image BadgeAccent;
            public TextMeshProUGUI Badge;
            public GameObject LabelRoot;
            public RectTransform LabelRect;
            public Image LabelBackground;
            public Image LabelAccent;
            public TextMeshProUGUI Label;
            public GameObject BubbleRoot;
            public RectTransform BubbleRect;
            public Image BubbleBackground;
            public Image BubbleAccent;
            public RectTransform BubbleTailRect;
            public BubbleTailGraphic BubbleTail;
            public TextMeshProUGUI Bubble;
            public GameObject DragRoot;
            public RectTransform DragFillRect;
            public Image DragFill;
            public RectTransform DragTopBorder;
            public RectTransform DragBottomBorder;
            public RectTransform DragLeftBorder;
            public RectTransform DragRightBorder;
            public bool DragAnimActive;
            public Vector2 DragAnimCurrentMin;
            public Vector2 DragAnimCurrentMax;
            public Vector2 DragAnimTargetMin;
            public Vector2 DragAnimTargetMax;
            public float DragAnimStartTime;
        }

        private enum CursorLayer
        {
            Rim,
            Body,
            Highlight,
            Accent
        }

        private sealed class CursorGraphic : MaskableGraphic
        {
            public CursorLayer Layer { get; set; }
            public bool Dragging { get; set; }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (Dragging)
                {
                    PopulateDrag(vh);
                    return;
                }

                switch (Layer)
                {
                    case CursorLayer.Body:
                        AddPolygon(vh,
                            new Vector2(2f, 3f),
                            new Vector2(3f, 38f),
                            new Vector2(13f, 30f),
                            new Vector2(21f, 46f),
                            new Vector2(29f, 42f),
                            new Vector2(22f, 27f),
                            new Vector2(37f, 27f));
                        break;
                    case CursorLayer.Highlight:
                        AddPolygon(vh,
                            new Vector2(2f, 3f),
                            new Vector2(3f, 19f),
                            new Vector2(9f, 16f),
                            new Vector2(25f, 24f),
                            new Vector2(31f, 24f));
                        break;
                    case CursorLayer.Accent:
                        AddPolygon(vh,
                            new Vector2(2f, 1f),
                            new Vector2(9f, 17f),
                            new Vector2(17f, 13f),
                            new Vector2(11f, 1f));
                        break;
                    default:
                        AddPolygon(vh,
                            new Vector2(0f, 0f),
                            new Vector2(0f, 45f),
                            new Vector2(13f, 35f),
                            new Vector2(22f, 56f),
                            new Vector2(36f, 49f),
                            new Vector2(28f, 33f),
                            new Vector2(48f, 33f));
                        break;
                }
            }

            private void PopulateDrag(VertexHelper vh)
            {
                switch (Layer)
                {
                    case CursorLayer.Body:
                        AddPolygon(vh,
                            new Vector2(2f, 12f),
                            new Vector2(11f, 3f),
                            new Vector2(35f, 7f),
                            new Vector2(38f, 25f),
                            new Vector2(29f, 40f),
                            new Vector2(11f, 35f));
                        break;
                    case CursorLayer.Highlight:
                        AddPolygon(vh,
                            new Vector2(4f, 12f),
                            new Vector2(12f, 5f),
                            new Vector2(30f, 8f),
                            new Vector2(17f, 16f));
                        break;
                    case CursorLayer.Accent:
                        AddPolygon(vh,
                            new Vector2(4f, 3f),
                            new Vector2(13f, 20f),
                            new Vector2(21f, 12f),
                            new Vector2(14f, 1f));
                        break;
                    default:
                        AddPolygon(vh,
                            new Vector2(0f, 13f),
                            new Vector2(12f, 0f),
                            new Vector2(43f, 4f),
                            new Vector2(48f, 27f),
                            new Vector2(34f, 49f),
                            new Vector2(8f, 42f));
                        break;
                }
            }

            private void AddPolygon(VertexHelper vh, params Vector2[] points)
            {
                if (points == null || points.Length < 3)
                    return;
                int start = vh.currentVertCount;
                for (int i = 0; i < points.Length; i++)
                    vh.AddVert(ToLocal(points[i]), color, Vector2.zero);
                for (int i = 1; i < points.Length - 1; i++)
                    vh.AddTriangle(start, start + i, start + i + 1);
            }

            private static Vector3 ToLocal(Vector2 topLeftPoint)
            {
                return new Vector3(topLeftPoint.x, -topLeftPoint.y, 0f);
            }
        }

        private sealed class ReticleGraphic : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                AddQuad(vh, 3f, 3f, 14f, 6f);
                AddQuad(vh, 3f, 3f, 6f, 14f);
                AddQuad(vh, 20f, 3f, 31f, 6f);
                AddQuad(vh, 28f, 3f, 31f, 14f);
                AddQuad(vh, 3f, 28f, 14f, 31f);
                AddQuad(vh, 3f, 20f, 6f, 31f);
                AddQuad(vh, 20f, 28f, 31f, 31f);
                AddQuad(vh, 28f, 20f, 31f, 31f);
            }

            private void AddQuad(VertexHelper vh, float x1, float y1, float x2, float y2)
            {
                int start = vh.currentVertCount;
                vh.AddVert(ToLocal(new Vector2(x1, y1)), color, Vector2.zero);
                vh.AddVert(ToLocal(new Vector2(x1, y2)), color, Vector2.zero);
                vh.AddVert(ToLocal(new Vector2(x2, y2)), color, Vector2.zero);
                vh.AddVert(ToLocal(new Vector2(x2, y1)), color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }

            private static Vector3 ToLocal(Vector2 topLeftPoint)
            {
                return new Vector3(topLeftPoint.x, -topLeftPoint.y, 0f);
            }
        }

        private sealed class BubbleTailGraphic : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                int start = vh.currentVertCount;
                vh.AddVert(ToLocal(new Vector2(2f, 0f)), color, Vector2.zero);
                vh.AddVert(ToLocal(new Vector2(21f, 0f)), color, Vector2.zero);
                vh.AddVert(ToLocal(new Vector2(8f, 15f)), color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
            }

            private static Vector3 ToLocal(Vector2 topLeftPoint)
            {
                return new Vector3(topLeftPoint.x, -topLeftPoint.y, 0f);
            }
        }
    }
}
