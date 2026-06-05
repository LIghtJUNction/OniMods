using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniMcp
{
    internal sealed class CoordinateGridOverlay : MonoBehaviour
    {
        private const int SortingOrder = 31500;
        private const float LineWidth = 2f;
        private const int MaxLineObjects = 260;
        private const int MaxLabelObjects = 420;

        private static readonly Color MajorLine = new Color(0.02f, 0.9f, 1f, 0.95f);
        private static readonly Color MinorLine = new Color(0.02f, 0.9f, 1f, 0.42f);
        private static readonly Color LabelFill = new Color(0.02f, 0.025f, 0.03f, 0.86f);
        private static readonly Color LabelText = new Color(0.93f, 1f, 1f, 1f);

        private readonly List<Image> lines = new List<Image>();
        private readonly List<LabelVisual> labels = new List<LabelVisual>();
        private Canvas canvas;
        private RectTransform root;
        private OverlayRequest request;
        private Coroutine hideCoroutine;

        public static CoordinateGridOverlay Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null)
                return;

            var obj = new GameObject("OniMcp_CoordinateGridOverlay");
            DontDestroyOnLoad(obj);
            Instance = obj.AddComponent<CoordinateGridOverlay>();
        }

        public static void Show(OverlayRequest request)
        {
            EnsureInstance();
            Instance.request = request;
            Instance.EnsureCanvas();
            Instance.canvas.enabled = true;
            Instance.Refresh();
            if (Instance.hideCoroutine != null)
                Instance.StopCoroutine(Instance.hideCoroutine);
            Instance.hideCoroutine = Instance.StartCoroutine(Instance.HideAfter(request.VisibleSeconds));
        }

        public static void Hide()
        {
            if (Instance == null)
                return;
            Instance.SetVisibleCount(0, 0);
            if (Instance.canvas != null)
                Instance.canvas.enabled = false;
            Instance.request = null;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void LateUpdate()
        {
            if (request != null && canvas != null && canvas.enabled)
                Refresh();
        }

        private IEnumerator HideAfter(float seconds)
        {
            int frames = Math.Max(8, Mathf.CeilToInt(Mathf.Max(0.25f, seconds) * 60f));
            for (int i = 0; i < frames; i++)
                yield return null;
            Hide();
        }

        private void EnsureCanvas()
        {
            if (canvas != null)
                return;

            var canvasObj = new GameObject("OniMcp_CoordinateGridCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObj.transform.SetParent(transform, false);
            root = canvasObj.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0f, 1f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.enabled = false;

            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        }

        private void Refresh()
        {
            if (request == null || Camera.main == null)
                return;

            int lineCount = 0;
            int labelCount = 0;
            int step = Math.Max(1, request.Step);
            int majorEvery = Math.Max(step, request.MajorEvery);

            if (request.ShowGrid)
            {
                for (int x = request.X1; x <= request.X2 + 1 && lineCount < MaxLineObjects; x++)
                {
                    float worldX = x - 0.5f;
                    if (TryWorldLineToScreen(worldX, request.Y1 - 0.5f, worldX, request.Y2 + 0.5f, out Rect rect))
                        UpdateLine(lineCount++, rect, ((x - request.X1) % majorEvery == 0) ? MajorLine : MinorLine);
                }

                for (int y = request.Y1; y <= request.Y2 + 1 && lineCount < MaxLineObjects; y++)
                {
                    float worldY = y - 0.5f;
                    if (TryWorldLineToScreen(request.X1 - 0.5f, worldY, request.X2 + 0.5f, worldY, out Rect rect))
                        UpdateLine(lineCount++, rect, ((y - request.Y1) % majorEvery == 0) ? MajorLine : MinorLine);
                }
            }

            if (request.ShowCoordinates)
            {
                for (int x = request.X1; x <= request.X2 && labelCount < MaxLabelObjects; x += step)
                {
                    if (TryWorldToTopLeft(x, request.Y2 + 0.65f, out Vector2 top))
                        UpdateLabel(labelCount++, x.ToString(), top.x - 18f, top.y - 18f, 42f, 20f);
                    if (TryWorldToTopLeft(x, request.Y1 - 0.65f, out Vector2 bottom))
                        UpdateLabel(labelCount++, x.ToString(), bottom.x - 18f, bottom.y - 2f, 42f, 20f);
                }

                for (int y = request.Y1; y <= request.Y2 && labelCount < MaxLabelObjects; y += step)
                {
                    if (TryWorldToTopLeft(request.X1 - 0.85f, y, out Vector2 left))
                        UpdateLabel(labelCount++, y.ToString(), left.x - 42f, left.y - 10f, 40f, 20f);
                    if (TryWorldToTopLeft(request.X2 + 0.85f, y, out Vector2 right))
                        UpdateLabel(labelCount++, y.ToString(), right.x + 2f, right.y - 10f, 40f, 20f);
                }
            }

            if (request.IncludeCellLabels)
            {
                int cells = Math.Max(1, (request.X2 - request.X1 + 1) * (request.Y2 - request.Y1 + 1));
                int labelStep = Math.Max(step, Mathf.CeilToInt(Mathf.Sqrt(cells / 140f)));
                for (int y = request.Y1; y <= request.Y2 && labelCount < MaxLabelObjects; y += labelStep)
                {
                    for (int x = request.X1; x <= request.X2 && labelCount < MaxLabelObjects; x += labelStep)
                    {
                        if (TryWorldToTopLeft(x, y, out Vector2 center))
                            UpdateLabel(labelCount++, x + "," + y, center.x - 28f, center.y - 9f, 56f, 18f);
                    }
                }
            }

            SetVisibleCount(lineCount, labelCount);
        }

        private bool TryWorldLineToScreen(float x1, float y1, float x2, float y2, out Rect rect)
        {
            rect = default(Rect);
            if (!TryWorldToTopLeft(x1, y1, out Vector2 a) || !TryWorldToTopLeft(x2, y2, out Vector2 b))
                return false;

            float xMin = Mathf.Min(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y);
            float width = Mathf.Max(LineWidth, Mathf.Abs(a.x - b.x));
            float height = Mathf.Max(LineWidth, Mathf.Abs(a.y - b.y));
            rect = new Rect(xMin, yMin, width, height);
            return rect.xMax >= -20f && rect.yMax >= -20f && rect.xMin <= Screen.width + 20f && rect.yMin <= Screen.height + 20f;
        }

        private bool TryWorldToTopLeft(float x, float y, out Vector2 screen)
        {
            screen = Vector2.zero;
            var point = Camera.main.WorldToScreenPoint(new Vector3(x, y, 0f));
            if (point.z <= 0f)
                return false;
            screen = new Vector2(point.x, Screen.height - point.y);
            return true;
        }

        private void UpdateLine(int index, Rect rect, Color color)
        {
            while (lines.Count <= index)
                lines.Add(CreateLine());
            var line = lines[index];
            line.gameObject.SetActive(true);
            line.color = color;
            SetTopLeftRect(line.rectTransform, rect.xMin, rect.yMin, rect.width, rect.height);
        }

        private Image CreateLine()
        {
            var obj = new GameObject("CoordGridLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(canvas.transform, false);
            var image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            ConfigureTopLeft(image.rectTransform);
            return image;
        }

        private void UpdateLabel(int index, string text, float x, float y, float width, float height)
        {
            while (labels.Count <= index)
                labels.Add(CreateLabel());
            var label = labels[index];
            label.Root.SetActive(true);
            label.Background.color = LabelFill;
            label.Text.color = LabelText;
            label.Text.SetText(text);
            SetTopLeftRect(label.Rect, x, y, width, height);
        }

        private LabelVisual CreateLabel()
        {
            var rootObj = new GameObject("CoordGridLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rootObj.transform.SetParent(canvas.transform, false);
            var rect = rootObj.GetComponent<RectTransform>();
            ConfigureTopLeft(rect);

            var background = rootObj.GetComponent<Image>();
            background.raycastTarget = false;

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(rootObj.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2f, 1f);
            textRect.offsetMax = new Vector2(-2f, -1f);

            var text = textObj.GetComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.richText = false;
            text.fontSize = 12f;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            if (Localization.FontAsset != null)
                text.font = Localization.FontAsset;

            return new LabelVisual { Root = rootObj, Rect = rect, Background = background, Text = text };
        }

        private void SetVisibleCount(int lineCount, int labelCount)
        {
            for (int i = lineCount; i < lines.Count; i++)
                lines[i].gameObject.SetActive(false);
            for (int i = labelCount; i < labels.Count; i++)
                labels[i].Root.SetActive(false);
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

        public sealed class OverlayRequest
        {
            public int X1;
            public int Y1;
            public int X2;
            public int Y2;
            public int Step = 5;
            public int MajorEvery = 10;
            public bool ShowGrid = true;
            public bool ShowCoordinates = true;
            public bool IncludeCellLabels;
            public float VisibleSeconds = 2f;
        }

        private sealed class LabelVisual
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Background;
            public TextMeshProUGUI Text;
        }
    }
}
