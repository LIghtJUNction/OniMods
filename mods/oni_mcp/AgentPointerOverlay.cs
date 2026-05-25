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
        private readonly List<PointerVisual> visuals = new List<PointerVisual>();
        private Canvas canvas;
        private RectTransform rootRect;

        public static AgentPointerOverlay Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null)
                return;

            var obj = new GameObject("OniMcp_AgentPointerOverlay");
            Object.DontDestroyOnLoad(obj);
            Instance = obj.AddComponent<AgentPointerOverlay>();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void LateUpdate()
        {
            EnsureCanvas();
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

                var screen = WorldToTopLeftScreen(pointer.WorldPosition);
                if (screen.x < -40f || screen.y < -40f || screen.x > Screen.width + 40f || screen.y > Screen.height + 40f)
                    continue;

                var visual = Visual(visibleCount++);
                visual.Root.SetActive(true);
                visual.LabelRoot.SetActive(true);
                visual.BadgeRoot.SetActive(true);
                visual.Outline.color = new Color(0.02f, 0.025f, 0.03f, 0.92f);
                visual.Body.color = pointer.IsDragging ? Color.Lerp(pointer.Color, new Color(1f, 0.78f, 0.18f, 1f), 0.45f) : pointer.Color;
                visual.LabelBackground.color = new Color(0.025f, 0.03f, 0.035f, 0.82f);
                visual.BadgeBackground.color = new Color(0.025f, 0.03f, 0.035f, 0.9f);
                visual.Label.color = Color.white;
                visual.Badge.color = Color.white;
                visual.Label.SetText(Label(pointer));
                visual.Badge.SetText(ToolBadge(pointer));
                SetTopLeftRect(visual.Rect, screen.x - 2f, screen.y - 2f, 52f, 60f);
                SetTopLeftRect(visual.BadgeRect, screen.x + 34f, screen.y - 10f, 128f, 24f);
                SetTopLeftRect(visual.LabelRect, screen.x + 30f, screen.y + 18f, 220f, 28f);
            }

            for (int i = visibleCount; i < visuals.Count; i++)
            {
                visuals[i].Root.SetActive(false);
                visuals[i].LabelRoot.SetActive(false);
                visuals[i].BadgeRoot.SetActive(false);
            }
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

        private PointerVisual CreateVisual()
        {
            EnsureCanvas();

            var root = new GameObject("AgentPointer", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            var rect = root.GetComponent<RectTransform>();
            ConfigureTopLeft(rect);

            var outlineObj = new GameObject("CursorOutline", typeof(RectTransform), typeof(CanvasRenderer), typeof(CursorGraphic));
            outlineObj.transform.SetParent(root.transform, false);
            var outlineRect = outlineObj.GetComponent<RectTransform>();
            ConfigureTopLeft(outlineRect);
            SetTopLeftRect(outlineRect, 2f, 2f, 48f, 56f);
            var outline = outlineObj.GetComponent<CursorGraphic>();
            outline.raycastTarget = false;

            var bodyObj = new GameObject("CursorBody", typeof(RectTransform), typeof(CanvasRenderer), typeof(CursorGraphic));
            bodyObj.transform.SetParent(root.transform, false);
            var bodyRect = bodyObj.GetComponent<RectTransform>();
            ConfigureTopLeft(bodyRect);
            SetTopLeftRect(bodyRect, 0f, 0f, 48f, 56f);
            var body = bodyObj.GetComponent<CursorGraphic>();
            body.raycastTarget = false;

            var badgeRoot = new GameObject("ToolBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            badgeRoot.transform.SetParent(canvas.transform, false);
            var badgeRect = badgeRoot.GetComponent<RectTransform>();
            ConfigureTopLeft(badgeRect);
            var badgeBackground = badgeRoot.GetComponent<Image>();
            badgeBackground.raycastTarget = false;

            var badgeTextObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            badgeTextObj.transform.SetParent(badgeRoot.transform, false);
            var badgeTextRect = badgeTextObj.GetComponent<RectTransform>();
            badgeTextRect.anchorMin = Vector2.zero;
            badgeTextRect.anchorMax = Vector2.one;
            badgeTextRect.offsetMin = new Vector2(6f, 2f);
            badgeTextRect.offsetMax = new Vector2(-6f, -2f);
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

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(labelRoot.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 3f);
            textRect.offsetMax = new Vector2(-6f, -3f);
            var text = textObj.GetComponent<TextMeshProUGUI>();
            text.fontSize = 13f;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            root.SetActive(false);
            badgeRoot.SetActive(false);
            labelRoot.SetActive(false);
            return new PointerVisual
            {
                Root = root,
                Rect = rect,
                Outline = outline,
                Body = body,
                BadgeRoot = badgeRoot,
                BadgeRect = badgeRect,
                BadgeBackground = badgeBackground,
                Badge = badgeText,
                LabelRoot = labelRoot,
                LabelRect = labelRect,
                LabelBackground = background,
                Label = text
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

        private sealed class PointerVisual
        {
            public GameObject Root;
            public RectTransform Rect;
            public CursorGraphic Outline;
            public CursorGraphic Body;
            public GameObject BadgeRoot;
            public RectTransform BadgeRect;
            public Image BadgeBackground;
            public TextMeshProUGUI Badge;
            public GameObject LabelRoot;
            public RectTransform LabelRect;
            public Image LabelBackground;
            public TextMeshProUGUI Label;
        }

        private sealed class CursorGraphic : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                AddTriangle(vh, new Vector2(2f, 1f), new Vector2(2f, 42f), new Vector2(15f, 31f));
                AddTriangle(vh, new Vector2(2f, 1f), new Vector2(15f, 31f), new Vector2(35f, 31f));
                AddTriangle(vh, new Vector2(15f, 31f), new Vector2(23f, 53f), new Vector2(32f, 49f));
                AddTriangle(vh, new Vector2(15f, 31f), new Vector2(32f, 49f), new Vector2(24f, 29f));
            }

            private void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c)
            {
                int start = vh.currentVertCount;
                vh.AddVert(ToLocal(a), color, Vector2.zero);
                vh.AddVert(ToLocal(b), color, Vector2.zero);
                vh.AddVert(ToLocal(c), color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
            }

            private static Vector3 ToLocal(Vector2 topLeftPoint)
            {
                return new Vector3(topLeftPoint.x, -topLeftPoint.y, 0f);
            }
        }
    }
}
