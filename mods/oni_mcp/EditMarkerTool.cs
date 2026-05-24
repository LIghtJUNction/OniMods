using System;
using System.IO;
using TMPro;
using Newtonsoft.Json;
using OniMcp.Support;
using OniMcp.Tools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OniMcp
{
    public class EditMarkerTool : InterfaceTool
    {
        private const int MinDragCells = 1;
        public const string IconName = "oni_mcp_edit_mark_tool";
        private const int SelectionOverlaySortingOrder = 5000;

        private bool dragging;
        private Vector3 selectionDownPos;
        private Vector3 currentPos;
        private string promptText = "";
        private string statusText = "";
        private int x1;
        private int y1;
        private int x2;
        private int y2;
        private EditMarkerPromptScreen promptScreen;
        private RectSelectionOverlay selectionOverlay;

        public static EditMarkerTool Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null)
                return;

            var obj = new GameObject("OniMcp_EditMarkerTool");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            Instance = obj.AddComponent<EditMarkerTool>();
        }

        public static void RegisterIconSprite()
        {
            try
            {
                if (Assets.Sprites != null && Assets.Sprites.ContainsKey(new HashedString(IconName)))
                    return;

                string path = ResolveIconPath();
                if (string.IsNullOrEmpty(path))
                    return;

                var sprite = SpriteLoader.LoadSpriteFile(path);
                if (sprite == null)
                    return;
                sprite.name = IconName;
                if (Assets.Sprites != null)
                    Assets.Sprites[new HashedString(IconName)] = sprite;
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to register edit marker icon: " + ex.Message);
            }
        }

        private static string ResolveIconPath()
        {
            string dir = OniMcpPaths.ModPath;
            if (string.IsNullOrEmpty(dir))
                return null;

            string[] candidates =
            {
                Path.Combine(dir, "assets", "edit_mark_tool.png"),
                Path.Combine(dir, "edit_mark_tool.png")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        public static void ActivateFromMenu(object _)
        {
            EnsureInstance();
            if (PlayerController.Instance != null)
                PlayerController.Instance.ActivateTool(Instance);
            else
                Instance.ActivateTool();
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;
        }

        protected override void OnActivateTool()
        {
            base.OnActivateTool();
            dragging = false;
            ClosePromptScreen();
            HideSelectionOverlay();
            statusText = "Drag to select an area for the agent";
            OniMcpLog.Debug("[OniMcp] EditMarkerTool activated");
        }

        protected override void OnDeactivateTool(InterfaceTool newTool)
        {
            dragging = false;
            ClosePromptScreen();
            HideSelectionOverlay();
            base.OnDeactivateTool(newTool);
            OniMcpLog.Debug("[OniMcp] EditMarkerTool deactivated");
        }

        public override void OnLeftClickDown(Vector3 cursorPos)
        {
            if (IsPromptVisible())
                return;

            dragging = true;
            selectionDownPos = cursorPos;
            currentPos = cursorPos;
            statusText = "Release mouse, then enter the edit prompt";
            ShowSelectionOverlay();
            UpdateSelectionOverlay();
            base.OnLeftClickDown(cursorPos);
        }

        public override void OnMouseMove(Vector3 cursorPos)
        {
            if (dragging)
            {
                currentPos = cursorPos;
                UpdateSelectionOverlay();
            }
            base.OnMouseMove(cursorPos);
        }

        public override void OnLeftClickUp(Vector3 cursorPos)
        {
            if (IsPromptVisible())
                return;

            if (dragging)
                CompleteSelection(cursorPos);

            base.OnLeftClickUp(cursorPos);
        }

        public override void OnRightClickDown(Vector3 cursorPos, KButtonEvent e)
        {
            if (CancelCurrentSelection())
                e.Consumed = true;
            else
                base.OnRightClickDown(cursorPos, e);
        }

        public override void OnRightClickUp(Vector3 cursorPos)
        {
            if (!CancelCurrentSelection())
                base.OnRightClickUp(cursorPos);
        }

        public override void OnKeyDown(KButtonEvent e)
        {
            if (KButtonEventSafety.SafeIsAction(e, Action.Escape) || KButtonEventSafety.SafeIsAction(e, Action.BuildingCancel))
            {
                if (CancelCurrentSelection())
                    KButtonEventSafety.SafeTryConsume(e, KButtonEventSafety.SafeIsAction(e, Action.Escape) ? Action.Escape : Action.BuildingCancel);
                return;
            }

            base.OnKeyDown(e);
        }

        public override bool ShowHoverUI()
        {
            return !IsPromptVisible();
        }

        private void Update()
        {
            if (dragging)
                UpdateSelectionOverlay();
        }

        private void ShowSelectionOverlay()
        {
            if (selectionOverlay == null)
                selectionOverlay = RectSelectionOverlay.Create();
            selectionOverlay.Show();
        }

        private void HideSelectionOverlay()
        {
            if (selectionOverlay != null)
                selectionOverlay.Hide();
        }

        private void UpdateSelectionOverlay()
        {
            if (selectionOverlay == null || Camera.main == null)
                return;

            var start = Camera.main.WorldToScreenPoint(selectionDownPos);
            var end = Camera.main.WorldToScreenPoint(currentPos);
            if (start.z < 0f || end.z < 0f)
                return;

            var rect = Rect.MinMaxRect(
                Mathf.Min(start.x, end.x),
                Mathf.Min(start.y, end.y),
                Mathf.Max(start.x, end.x),
                Mathf.Max(start.y, end.y));
            selectionOverlay.SetRect(rect);
        }

        private bool IsPromptVisible()
        {
            return promptScreen != null && promptScreen.IsActive();
        }

        private void ShowPromptScreen()
        {
            ClosePromptScreen();
            promptScreen = EditMarkerPromptScreen.Create(
                statusText,
                value => promptText = value ?? "",
                CreateRequest,
                CancelPrompt);
        }

        private void ClosePromptScreen()
        {
            if (promptScreen != null)
            {
                promptScreen.CloseSilently();
                promptScreen = null;
            }
        }

        private void CancelPrompt()
        {
            promptScreen = null;
            promptText = "";
            statusText = "";
        }

        private bool CancelCurrentSelection()
        {
            bool hadSelection = dragging || IsPromptVisible() || !string.IsNullOrEmpty(promptText);
            dragging = false;
            currentPos = Vector3.zero;
            selectionDownPos = Vector3.zero;
            promptText = "";
            statusText = "";
            HideSelectionOverlay();
            ClosePromptScreen();
            if (hadSelection)
                OniMcpLog.Debug("[OniMcp] EditMarkerTool selection cancelled");
            return hadSelection;
        }

        private void CreateRequest()
        {
            try
            {
                var result = EditMarkTools.CreateFromSelection(x1, y1, x2, y2, promptText);
                promptScreen = null;
                statusText = "Edit mark request created";
                OniMcpLog.Debug("[OniMcp] Edit mark request created: " + JsonConvert.SerializeObject(result));
            }
            catch (Exception ex)
            {
                statusText = "Create failed: " + ex.Message;
                OniMcpLog.Warning("[OniMcp] Failed to create edit mark request: " + ex);
            }
        }

        private void CompleteSelection(Vector3 cursorPos)
        {
            dragging = false;
            currentPos = cursorPos;
            HideSelectionOverlay();
            if (!TryResolveRect(selectionDownPos, cursorPos, out x1, out y1, out x2, out y2))
            {
                statusText = "Invalid selected area";
                OniMcpLog.Debug("[OniMcp] EditMarkerTool invalid selection");
                return;
            }

            promptText = "";
            statusText = $"Area: ({x1},{y1}) - ({x2},{y2})";
            ShowPromptScreen();
            OniMcpLog.Debug($"[OniMcp] EditMarkerTool selected area ({x1},{y1}) - ({x2},{y2})");
        }

        private static bool TryResolveRect(Vector3 start, Vector3 end, out int x1, out int y1, out int x2, out int y2)
        {
            x1 = y1 = x2 = y2 = 0;
            int startCell = Grid.PosToCell(start);
            int endCell = Grid.PosToCell(end);
            if (!Grid.IsValidCell(startCell) || !Grid.IsValidCell(endCell))
                return false;

            Grid.CellToXY(startCell, out int sx, out int sy);
            Grid.CellToXY(endCell, out int ex, out int ey);
            x1 = Math.Min(sx, ex);
            y1 = Math.Min(sy, ey);
            x2 = Math.Max(sx, ex);
            y2 = Math.Max(sy, ey);
            return x2 - x1 + 1 >= MinDragCells && y2 - y1 + 1 >= MinDragCells;
        }

        private sealed class RectSelectionOverlay
        {
            private readonly GameObject root;
            private readonly RectTransform rootRect;
            private readonly RectTransform fillRect;
            private readonly RectTransform topBorder;
            private readonly RectTransform bottomBorder;
            private readonly RectTransform leftBorder;
            private readonly RectTransform rightBorder;

            private RectSelectionOverlay(GameObject root, RectTransform rootRect, RectTransform fillRect, RectTransform topBorder, RectTransform bottomBorder, RectTransform leftBorder, RectTransform rightBorder)
            {
                this.root = root;
                this.rootRect = rootRect;
                this.fillRect = fillRect;
                this.topBorder = topBorder;
                this.bottomBorder = bottomBorder;
                this.leftBorder = leftBorder;
                this.rightBorder = rightBorder;
            }

            public static RectSelectionOverlay Create()
            {
                var root = new GameObject("OniMcp_EditMarkerSelectionOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                UnityEngine.Object.DontDestroyOnLoad(root);

                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SelectionOverlaySortingOrder;

                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

                var rootRect = root.GetComponent<RectTransform>();
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.pivot = Vector2.zero;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;

                var fillRect = CreateImage(root.transform, "Fill", new Color(0.15f, 0.95f, 1f, 0.18f));
                var topBorder = CreateImage(root.transform, "TopBorder", new Color(0.2f, 1f, 1f, 0.95f));
                var bottomBorder = CreateImage(root.transform, "BottomBorder", new Color(0.2f, 1f, 1f, 0.95f));
                var leftBorder = CreateImage(root.transform, "LeftBorder", new Color(0.2f, 1f, 1f, 0.95f));
                var rightBorder = CreateImage(root.transform, "RightBorder", new Color(0.2f, 1f, 1f, 0.95f));
                root.SetActive(false);
                return new RectSelectionOverlay(root, rootRect, fillRect, topBorder, bottomBorder, leftBorder, rightBorder);
            }

            public void Show()
            {
                if (root != null)
                    root.SetActive(true);
            }

            public void Hide()
            {
                if (root != null)
                    root.SetActive(false);
            }

            public void SetRect(Rect screenRect)
            {
                if (root == null)
                    return;

                rootRect.sizeDelta = new Vector2(Screen.width, Screen.height);
                SetRectTransform(fillRect, screenRect.xMin, screenRect.yMin, screenRect.width, screenRect.height);

                const float borderWidth = 2f;
                SetRectTransform(topBorder, screenRect.xMin, screenRect.yMax - borderWidth, screenRect.width, borderWidth);
                SetRectTransform(bottomBorder, screenRect.xMin, screenRect.yMin, screenRect.width, borderWidth);
                SetRectTransform(leftBorder, screenRect.xMin, screenRect.yMin, borderWidth, screenRect.height);
                SetRectTransform(rightBorder, screenRect.xMax - borderWidth, screenRect.yMin, borderWidth, screenRect.height);
            }

            private static RectTransform CreateImage(Transform parent, string name, Color color)
            {
                var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                child.transform.SetParent(parent, false);
                var image = child.GetComponent<Image>();
                image.color = color;
                image.raycastTarget = false;

                var rect = child.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                return rect;
            }

            private static void SetRectTransform(RectTransform rect, float x, float y, float width, float height)
            {
                rect.anchoredPosition = new Vector2(x, y);
                rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
            }
        }

        private sealed class EditMarkerPromptScreen : KScreen
        {
            private const int PromptSortingOrder = 5100;
            private Action<string> onTextChanged;
            private System.Action onCreate;
            private System.Action onCancel;
            private KInputTextField inputField;
            private KButton createButton;
            private bool closingSilently;

            public override float GetSortKey()
            {
                return EDITING_SCREEN_SORT_KEY;
            }

            public override bool IsModal()
            {
                return true;
            }

            public static EditMarkerPromptScreen Create(string areaText, Action<string> onTextChanged, System.Action onCreate, System.Action onCancel)
            {
                var root = new GameObject("OniMcp_EditMarkerPrompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(root);

                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = PromptSortingOrder;

                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;

                var rootRect = root.GetComponent<RectTransform>();
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;

                var screen = root.AddComponent<EditMarkerPromptScreen>();
                screen.onTextChanged = onTextChanged;
                screen.onCreate = onCreate;
                screen.onCancel = onCancel;
                screen.Build(areaText);
                screen.Activate();
                return screen;
            }

            public void CloseSilently()
            {
                closingSilently = true;
                if (IsActive())
                    Deactivate();
            }

            public override void OnKeyDown(KButtonEvent e)
            {
                if (e.TryConsume(Action.Escape))
                {
                    Cancel();
                    return;
                }
                base.OnKeyDown(e);
                e.Consumed = true;
            }

            protected override void OnActivate()
            {
                base.OnActivate();
                SetIsEditing(true);
                if (CameraController.Instance != null)
                    CameraController.Instance.DisableUserCameraControl = true;
                if (inputField != null)
                {
                    inputField.Select();
                    inputField.ActivateInputField();
                }
            }

            protected override void OnDeactivate()
            {
                if (CameraController.Instance != null)
                    CameraController.Instance.DisableUserCameraControl = false;
                SetIsEditing(false);
                if (!closingSilently && onCancel != null)
                    onCancel();
                base.OnDeactivate();
            }

            private void Build(string areaText)
            {
                var panel = CreatePanel(transform);
                CreateText(panel, "Title", "MCP Edit Mark", 24, FontStyles.Bold, new Vector2(24f, -52f), new Vector2(-24f, -20f));
                CreateText(panel, "Area", areaText, 15, FontStyles.Normal, new Vector2(24f, -84f), new Vector2(-24f, -58f));
                CreateText(panel, "Hint", "Enter an edit prompt. The client agent should plan first, then act.", 14, FontStyles.Normal, new Vector2(24f, -116f), new Vector2(-24f, -88f));

                inputField = CreateInput(panel);
                inputField.onValueChanged.AddListener(value =>
                {
                    onTextChanged?.Invoke(value);
                    RefreshCreateButton();
                });

                createButton = CreateButton(panel, "CreateButton", "Create request", new Vector2(-284f, 24f), new Vector2(-144f, 62f), Create);
                CreateButton(panel, "CancelButton", "Cancel", new Vector2(-132f, 24f), new Vector2(-24f, 62f), Cancel);
                RefreshCreateButton();
            }

            private RectTransform CreatePanel(Transform parent)
            {
                var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panel.transform.SetParent(parent, false);
                var rect = panel.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(560f, 330f);
                rect.anchoredPosition = Vector2.zero;

                var image = panel.GetComponent<Image>();
                image.color = new Color(0.07f, 0.08f, 0.09f, 0.96f);
                image.raycastTarget = true;
                return rect;
            }

            private KInputTextField CreateInput(Transform parent)
            {
                var inputRoot = new GameObject("PromptInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(KInputTextField));
                inputRoot.transform.SetParent(parent, false);
                var rect = inputRoot.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.offsetMin = new Vector2(24f, 82f);
                rect.offsetMax = new Vector2(-24f, -126f);

                var image = inputRoot.GetComponent<Image>();
                image.color = new Color(0.015f, 0.018f, 0.022f, 0.96f);
                image.raycastTarget = true;

                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                viewport.transform.SetParent(inputRoot.transform, false);
                var viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = new Vector2(10f, 8f);
                viewportRect.offsetMax = new Vector2(-10f, -8f);

                var text = CreateText(viewportRect, "Text", "", 16, FontStyles.Normal, Vector2.zero, Vector2.zero);
                Stretch(text.rectTransform);
                text.alignment = TextAlignmentOptions.TopLeft;
                text.textWrappingMode = TextWrappingModes.Normal;

                var placeholder = CreateText(viewportRect, "Placeholder", "Describe what the agent should change in this area...", 16, FontStyles.Italic, Vector2.zero, Vector2.zero);
                Stretch(placeholder.rectTransform);
                placeholder.color = new Color(0.72f, 0.76f, 0.78f, 0.55f);
                placeholder.alignment = TextAlignmentOptions.TopLeft;
                placeholder.textWrappingMode = TextWrappingModes.Normal;

                var input = inputRoot.GetComponent<KInputTextField>();
                input.textViewport = viewportRect;
                input.textComponent = text;
                input.placeholder = placeholder;
                input.lineType = TMP_InputField.LineType.MultiLineNewline;
                input.contentType = TMP_InputField.ContentType.Standard;
                input.characterLimit = 2000;
                input.caretColor = new Color(0.4f, 1f, 1f, 1f);
                input.customCaretColor = true;
                input.selectionColor = new Color(0.2f, 0.8f, 1f, 0.35f);
                return input;
            }

            private KButton CreateButton(Transform parent, string name, string label, Vector2 offsetMin, Vector2 offsetMax, System.Action onClick)
            {
                var buttonRoot = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(KImage), typeof(KButton));
                buttonRoot.transform.SetParent(parent, false);
                var rect = buttonRoot.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.offsetMin = offsetMin;
                rect.offsetMax = offsetMax;

                var image = buttonRoot.GetComponent<KImage>();
                image.color = new Color(0.14f, 0.18f, 0.20f, 1f);
                image.raycastTarget = true;

                var button = buttonRoot.GetComponent<KButton>();
                button.soundPlayer = new ButtonSoundPlayer();
                button.bgImage = image;
                button.additionalKImages = new KImage[0];
                button.onClick += onClick;

                var text = CreateText(buttonRoot.transform, "Label", label, 15, FontStyles.Bold, Vector2.zero, Vector2.zero);
                Stretch(text.rectTransform);
                text.alignment = TextAlignmentOptions.Center;
                return button;
            }

            private TextMeshProUGUI CreateText(Transform parent, string name, string value, int size, FontStyles style, Vector2 offsetMin, Vector2 offsetMax)
            {
                var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(parent, false);
                var rect = textObject.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.offsetMin = offsetMin;
                rect.offsetMax = offsetMax;

                var text = textObject.GetComponent<TextMeshProUGUI>();
                text.text = value;
                text.fontSize = size;
                text.fontStyle = style;
                text.color = new Color(0.91f, 0.94f, 0.95f, 1f);
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.overflowMode = TextOverflowModes.Ellipsis;
                if (Localization.FontAsset != null)
                    text.font = Localization.FontAsset;
                return text;
            }

            private void Stretch(RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            private void RefreshCreateButton()
            {
                if (createButton != null && inputField != null)
                    createButton.isInteractable = !string.IsNullOrWhiteSpace(inputField.text);
            }

            private void Create()
            {
                if (inputField == null || string.IsNullOrWhiteSpace(inputField.text))
                    return;

                closingSilently = true;
                onTextChanged?.Invoke(inputField.text);
                onCreate?.Invoke();
                Deactivate();
            }

            private void Cancel()
            {
                closingSilently = true;
                onCancel?.Invoke();
                Deactivate();
            }
        }
    }
}
