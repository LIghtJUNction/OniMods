using System;
using System.IO;
using System.Reflection;
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

                var fillRect = CreateImage(root.transform, "Fill", new Color(1f, 0.70f, 0.16f, 0.18f));
                var topBorder = CreateImage(root.transform, "TopBorder", new Color(1f, 0.82f, 0.26f, 0.95f));
                var bottomBorder = CreateImage(root.transform, "BottomBorder", new Color(1f, 0.82f, 0.26f, 0.95f));
                var leftBorder = CreateImage(root.transform, "LeftBorder", new Color(1f, 0.82f, 0.26f, 0.95f));
                var rightBorder = CreateImage(root.transform, "RightBorder", new Color(1f, 0.82f, 0.26f, 0.95f));
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

        private sealed class EditMarkerPromptScreen
        {
            private const int CharacterLimit = 2000;
            private static readonly FieldInfo InfoDialogContentContainerField = typeof(InfoDialogScreen).GetField("contentContainer", BindingFlags.Instance | BindingFlags.NonPublic);
            private Action<string> onTextChanged;
            private System.Action onCreate;
            private System.Action onCancel;
            private InfoDialogScreen dialog;
            private KInputTextField inputField;
            private KButton createButton;
            private bool closingSilently;

            public static EditMarkerPromptScreen Create(string areaText, Action<string> onTextChanged, System.Action onCreate, System.Action onCancel)
            {
                if (ScreenPrefabs.Instance == null || ScreenPrefabs.Instance.InfoDialogScreen == null || GameScreenManager.Instance == null)
                    throw new InvalidOperationException("ONI screen prefabs are not ready");

                var dialog = GameScreenManager.Instance.StartScreen(ScreenPrefabs.Instance.InfoDialogScreen.gameObject, null, GameScreenManager.UIRenderTarget.ScreenSpaceOverlay) as InfoDialogScreen;
                if (dialog == null)
                    throw new InvalidOperationException("Failed to create ONI info dialog");

                var screen = new EditMarkerPromptScreen();
                screen.onTextChanged = onTextChanged;
                screen.onCreate = onCreate;
                screen.onCancel = onCancel;
                screen.dialog = dialog;
                screen.Build(areaText);
                return screen;
            }

            public bool IsActive()
            {
                return dialog != null && dialog.IsActive();
            }

            public void CloseSilently()
            {
                closingSilently = true;
                if (dialog != null && dialog.IsActive())
                    dialog.Deactivate();
            }

            private void Build(string areaText)
            {
                dialog.SetHeader("MCP Edit Mark");
                dialog.AddPlainText(areaText);
                dialog.AddPlainText("Enter an edit prompt. The client agent should plan first, then act.");
                inputField = AddPromptInput(dialog);

                inputField.onValueChanged.AddListener(value =>
                {
                    onTextChanged?.Invoke(value);
                    RefreshCreateButton();
                });

                dialog.AddOption("Paste clipboard", d => PasteClipboard());
                dialog.AddOption("Cancel", d => d.Deactivate());
                dialog.AddOption(true, out createButton, out LocText createButtonText);
                createButtonText.text = "Create request";
                createButton.onClick += Create;
                dialog.onDeactivateFn = OnDialogDeactivate;

                FocusInput();
                RefreshCreateButton();
            }

            private static KInputTextField AddPromptInput(InfoDialogScreen dialog)
            {
                var contentContainer = InfoDialogContentContainerField?.GetValue(dialog) as GameObject;
                if (contentContainer == null)
                    throw new InvalidOperationException("ONI info dialog content container is not ready");

                var inputRoot = new GameObject("OniMcp_EditMarkerPromptInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(KInputTextField), typeof(LayoutElement));
                inputRoot.transform.SetParent(contentContainer.transform, false);

                var layout = inputRoot.GetComponent<LayoutElement>();
                layout.minWidth = 360f;
                layout.preferredWidth = 420f;
                layout.flexibleWidth = 1f;
                layout.minHeight = 126f;
                layout.preferredHeight = 126f;
                layout.flexibleHeight = 0f;

                var rect = inputRoot.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(420f, 126f);

                var image = inputRoot.GetComponent<Image>();
                image.color = new Color(0.055f, 0.060f, 0.070f, 1f);
                image.raycastTarget = true;

                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                viewport.transform.SetParent(inputRoot.transform, false);
                var viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = new Vector2(12f, 10f);
                viewportRect.offsetMax = new Vector2(-12f, -10f);

                var text = CreateInputText(viewportRect, "Text", "", 16, FontStyles.Normal, new Color(0.93f, 0.94f, 0.91f, 1f));
                var placeholder = CreateInputText(viewportRect, "Placeholder", "Describe what the agent should change in this area...", 16, FontStyles.Italic, new Color(0.72f, 0.74f, 0.70f, 0.62f));

                var input = inputRoot.GetComponent<KInputTextField>();
                input.textViewport = viewportRect;
                input.textComponent = text;
                input.placeholder = placeholder;
                input.lineType = TMP_InputField.LineType.MultiLineNewline;
                input.contentType = TMP_InputField.ContentType.Standard;
                input.characterLimit = CharacterLimit;
                input.richText = true;
                input.shouldHideMobileInput = false;
                input.text = "";
                inputRoot.AddComponent<PromptInputFocusKeeper>().Initialize(input);
                return input;
            }

            private static TextMeshProUGUI CreateInputText(Transform parent, string name, string value, int size, FontStyles style, Color color)
            {
                var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(parent, false);
                var rect = textObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var text = textObject.GetComponent<TextMeshProUGUI>();
                text.text = value;
                text.fontSize = size;
                text.fontStyle = style;
                text.color = color;
                text.raycastTarget = false;
                text.alignment = TextAlignmentOptions.TopLeft;
                text.textWrappingMode = TextWrappingModes.Normal;
                text.overflowMode = TextOverflowModes.Overflow;
                if (Localization.FontAsset != null)
                    text.font = Localization.FontAsset;
                return text;
            }

            private void FocusInput()
            {
                if (inputField == null)
                    return;

                Input.imeCompositionMode = IMECompositionMode.On;
                if (inputField.textComponent != null)
                    Input.compositionCursorPos = RectTransformUtility.WorldToScreenPoint(null, inputField.textComponent.rectTransform.position);
                inputField.Select();
                inputField.ActivateInputField();
            }

            private void OnDialogDeactivate()
            {
                Input.imeCompositionMode = IMECompositionMode.Auto;
                if (!closingSilently && onCancel != null)
                    onCancel();
            }

            private void RefreshCreateButton()
            {
                if (createButton != null && inputField != null)
                    createButton.isInteractable = !string.IsNullOrWhiteSpace(inputField.text);
            }

            private void PasteClipboard()
            {
                if (inputField == null)
                    return;

                PromptInputFocusKeeper.InsertText(inputField, GUIUtility.systemCopyBuffer);
                FocusInput();
                RefreshCreateButton();
            }

            private void Create()
            {
                if (inputField == null || string.IsNullOrWhiteSpace(inputField.text))
                    return;

                closingSilently = true;
                onTextChanged?.Invoke(inputField.text);
                onCreate?.Invoke();
                if (dialog != null && dialog.IsActive())
                    dialog.Deactivate();
            }
        }

        private sealed class PromptInputFocusKeeper : MonoBehaviour
        {
            private KInputTextField inputField;
            private string lastText = "";

            public void Initialize(KInputTextField input)
            {
                inputField = input;
                lastText = input != null ? input.text ?? "" : "";
            }

            private void LateUpdate()
            {
                if (inputField == null || !inputField.isActiveAndEnabled)
                    return;

                Input.imeCompositionMode = IMECompositionMode.On;
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                if (eventSystem != null && eventSystem.currentSelectedGameObject != inputField.gameObject && !Input.GetMouseButton(0))
                    eventSystem.SetSelectedGameObject(inputField.gameObject);
                if (!inputField.isFocused)
                    inputField.ActivateInputField();
                if (inputField.textComponent != null)
                    Input.compositionCursorPos = RectTransformUtility.WorldToScreenPoint(null, inputField.textComponent.rectTransform.position);

                ApplyClipboardShortcut();
                ApplyFallbackInputString();
                lastText = inputField.text ?? "";
            }

            private void ApplyClipboardShortcut()
            {
                bool modifier = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
                if (modifier && Input.GetKeyDown(KeyCode.V))
                    InsertText(inputField, GUIUtility.systemCopyBuffer);
            }

            private void ApplyFallbackInputString()
            {
                string rawInput = Input.inputString;
                if (string.IsNullOrEmpty(rawInput) || (inputField.text ?? "") != lastText)
                    return;

                string textToInsert = FilterTextInput(rawInput);
                if (!string.IsNullOrEmpty(textToInsert))
                {
                    InsertText(inputField, textToInsert);
                    return;
                }

                if (rawInput.IndexOf('\b') >= 0)
                    Backspace();
            }

            private static string FilterTextInput(string rawInput)
            {
                var builder = new System.Text.StringBuilder(rawInput.Length);
                for (int i = 0; i < rawInput.Length; i++)
                {
                    char c = rawInput[i];
                    if (c >= ' ' || c == '\t' || c == '\r' || c == '\n')
                        builder.Append(c == '\r' ? '\n' : c);
                }
                return builder.ToString();
            }

            public static void InsertText(KInputTextField inputField, string textToInsert)
            {
                if (inputField == null || string.IsNullOrEmpty(textToInsert))
                    return;

                string current = inputField.text ?? "";
                int start = Mathf.Clamp(Math.Min(inputField.selectionStringAnchorPosition, inputField.selectionStringFocusPosition), 0, current.Length);
                int end = Mathf.Clamp(Math.Max(inputField.selectionStringAnchorPosition, inputField.selectionStringFocusPosition), 0, current.Length);
                int allowed = inputField.characterLimit > 0 ? Math.Max(0, inputField.characterLimit - (current.Length - (end - start))) : textToInsert.Length;
                if (allowed < textToInsert.Length)
                    textToInsert = textToInsert.Substring(0, allowed);
                if (string.IsNullOrEmpty(textToInsert))
                    return;

                inputField.text = current.Substring(0, start) + textToInsert + current.Substring(end);
                int caret = start + textToInsert.Length;
                inputField.stringPosition = caret;
                inputField.selectionStringAnchorPosition = caret;
                inputField.selectionStringFocusPosition = caret;
            }

            private void Backspace()
            {
                string current = inputField.text ?? "";
                if (current.Length == 0)
                    return;

                int start = Mathf.Clamp(Math.Min(inputField.selectionStringAnchorPosition, inputField.selectionStringFocusPosition), 0, current.Length);
                int end = Mathf.Clamp(Math.Max(inputField.selectionStringAnchorPosition, inputField.selectionStringFocusPosition), 0, current.Length);
                if (start == end)
                {
                    if (start == 0)
                        return;
                    start--;
                }

                inputField.text = current.Remove(start, end - start);
                inputField.stringPosition = start;
                inputField.selectionStringAnchorPosition = start;
                inputField.selectionStringFocusPosition = start;
            }
        }
    }
}
