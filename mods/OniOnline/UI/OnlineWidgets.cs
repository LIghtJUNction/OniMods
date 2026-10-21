using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal static class OnlineWidgets
    {
        internal static readonly Color PanelColor = new Color(0.255f, 0.27f, 0.33f);
        internal static readonly Color InsetColor = new Color(0.18f, 0.19f, 0.235f);
        internal static readonly Color ButtonColor = new Color(0.36f, 0.385f, 0.45f);
        internal static readonly Color AccentColor = new Color(0.53f, 0.245f, 0.385f);
        internal static readonly Color TextColor = new Color(0.94f, 0.935f, 0.91f);
        internal static readonly Color MutedColor = new Color(0.69f, 0.71f, 0.75f);
        internal static readonly Color BorderColor = new Color(0.08f, 0.085f, 0.105f);
        internal static readonly Color OnlineColor = new Color(0.58f, 0.77f, 0.60f);

        private static readonly Color[] PlayerColors = {
            new Color(0.80f, 0.68f, 0.43f), new Color(0.49f, 0.73f, 0.73f),
            new Color(0.78f, 0.52f, 0.48f), new Color(0.70f, 0.62f, 0.81f),
            new Color(0.62f, 0.75f, 0.51f), new Color(0.74f, 0.60f, 0.70f) };

        internal static Color PlayerColor(int number)
        { return PlayerColors[(int)((uint)Mathf.Max(1, number) - 1) % PlayerColors.Length]; }

        internal static TextMeshProUGUI SectionHeader(Transform parent, string text, int height = 32)
        {
            var row = Rect("SectionHeader", parent);
            Surface(row, new Color(0.30f, 0.315f, 0.385f), true);
            Size(row, -1, height);
            var label = Label(row.transform, text, 18);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(10, 2);
            label.rectTransform.offsetMax = new Vector2(-10, -2);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        internal static GameObject Rect(string name, Transform parent)
        {
            var item = new GameObject(name, typeof(RectTransform));
            if (parent != null) item.transform.SetParent(parent, false);
            return item;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        internal static LayoutElement Size(GameObject item, float width = -1, float height = 40, float flexibleWidth = 0)
        {
            var layout = item.GetComponent<LayoutElement>() ?? item.AddComponent<LayoutElement>();
            layout.minWidth = width >= 0 ? width : 0;
            if (width >= 0) layout.preferredWidth = width;
            layout.preferredHeight = layout.minHeight = height;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = 0;
            return layout;
        }

        internal static Image Surface(GameObject item, Color color, bool border = false)
        {
            var image = item.AddComponent<Image>();
            image.color = color;
            if (border)
            {
                var outline = item.AddComponent<Outline>();
                outline.effectColor = BorderColor;
                outline.effectDistance = new Vector2(1, -1);
                outline.useGraphicAlpha = false;
            }
            return image;
        }

        internal static TextMeshProUGUI Label(Transform parent, string text, int size = 18)
        {
            var item = Rect("Label", parent);
            var label = item.AddComponent<TextMeshProUGUI>();
            label.font = Localization.FontAsset ?? TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.color = TextColor;
            label.richText = false;
            label.text = text;
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }

        internal static Transform Column(Transform parent, string name, int padding = 0, float spacing = 8)
        {
            var item = Rect(name, parent);
            var group = item.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.spacing = spacing;
            group.childControlHeight = group.childControlWidth = true;
            group.childForceExpandHeight = false; group.childForceExpandWidth = true;
            var layout = item.AddComponent<LayoutElement>();
            layout.minWidth = 0; layout.flexibleHeight = 0;
            return item.transform;
        }

        internal static Transform Row(Transform parent, float height = 40)
        {
            var row = Rect("Row", parent);
            var group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8; group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlHeight = group.childControlWidth = true;
            group.childForceExpandHeight = true; group.childForceExpandWidth = false;
            Size(row, -1, height);
            return row.transform;
        }

        internal static void Space(Transform parent, float height = 4, bool expand = false)
        {
            var item = Rect("Space", parent);
            var layout = Size(item, -1, height);
            if (expand) layout.flexibleHeight = 1;
        }

        internal static void Divider(Transform parent)
        {
            var item = Rect("Divider", parent);
            Surface(item, new Color(0.11f, 0.12f, 0.15f)).raycastTarget = false;
            Size(item, -1, 1);
        }

        internal static Button Button(Transform parent, string text, System.Action click, float width = 150)
        {
            var item = Rect("Button", parent);
            var image = Surface(item, Color.white, true);
            var button = item.AddComponent<Button>();
            button.targetGraphic = image;
            Tint(button, ButtonColor);
            button.onClick.AddListener(() => click());
            var label = Label(item.transform, text, 18);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(9, 2);
            label.rectTransform.offsetMax = new Vector2(-9, -2);
            Size(item, width, 40, width < 0 ? 1 : 0);
            return button;
        }

        internal static void Tint(Button button, Color normal)
        {
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = Color.Lerp(normal, Color.white, 0.21f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = Color.Lerp(normal, Color.black, 0.20f);
            colors.disabledColor = new Color(0.24f, 0.25f, 0.295f, 0.65f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        internal static TMP_InputField Input(Transform parent, string value, string placeholder, float width = -1)
        {
            var item = Rect("Input", parent);
            var image = Surface(item, Color.white, true);
            var input = item.AddComponent<TMP_InputField>();
            input.targetGraphic = image;
            var colors = input.colors;
            colors.normalColor = InsetColor;
            colors.highlightedColor = new Color(0.235f, 0.25f, 0.30f);
            colors.selectedColor = new Color(0.26f, 0.275f, 0.33f);
            colors.disabledColor = new Color(0.21f, 0.22f, 0.265f);
            colors.fadeDuration = 0.08f;
            input.colors = colors;
            var viewport = Rect("Viewport", item.transform);
            Stretch((RectTransform)viewport.transform);
            ((RectTransform)viewport.transform).offsetMin = new Vector2(11, 5);
            ((RectTransform)viewport.transform).offsetMax = new Vector2(-11, -5);
            viewport.AddComponent<RectMask2D>();
            var label = Label(viewport.transform, "", 18);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(label.rectTransform);
            var hint = Label(viewport.transform, placeholder, 17);
            hint.color = MutedColor;
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(hint.rectTransform);
            input.textViewport = (RectTransform)viewport.transform;
            input.textComponent = label;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 256;
            input.text = value;
            input.customCaretColor = true;
            input.caretColor = TextColor;
            input.selectionColor = new Color(0.65f, 0.4f, 0.52f, 0.6f);
            Size(item, width, 40, width < 0 ? 1 : 0);
            return input;
        }

        internal static ScrollRect Scroll(Transform parent, float height, out Transform content)
        {
            var item = Rect("Scroll", parent);
            Surface(item, InsetColor, true);
            var scroll = item.AddComponent<ScrollRect>();
            var viewport = Rect("Viewport", item.transform);
            Stretch((RectTransform)viewport.transform);
            ((RectTransform)viewport.transform).offsetMin = new Vector2(2, 2);
            ((RectTransform)viewport.transform).offsetMax = new Vector2(-2, -2);
            viewport.AddComponent<RectMask2D>();
            var body = Column(viewport.transform, "Content", 12, 8);
            var rect = (RectTransform)body;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1); rect.sizeDelta = Vector2.zero;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = (RectTransform)viewport.transform; scroll.content = rect;
            scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 28;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var track = Rect("Scrollbar", item.transform);
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(1, 0); trackRect.anchorMax = Vector2.one;
            trackRect.pivot = Vector2.one;
            trackRect.offsetMin = new Vector2(-11, 2); trackRect.offsetMax = new Vector2(-2, -2);
            Surface(track, new Color(0.135f, 0.145f, 0.18f));
            var handle = Rect("Handle", track.transform);
            Stretch((RectTransform)handle.transform);
            var handleImage = Surface(handle, new Color(0.46f, 0.48f, 0.55f));
            var scrollbar = track.AddComponent<Scrollbar>();
            scrollbar.handleRect = (RectTransform)handle.transform;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 4;
            Size(item, -1, height);
            content = body;
            return scroll;
        }

        internal static GameObject CanvasRoot(string name, int order)
        {
            var root = Rect(name, null);
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1200, 960);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<GraphicRaycaster>();
            return root;
        }

        internal static GameObject CreateLauncher(OnlineRuntime runtime)
        {
            var root = CanvasRoot("OniOnlineChatLauncher", 29990);
            var button = Button(root.transform, OnlineText.Get("system.map"), () => runtime.OpenStarMap(), 136);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1); rect.anchoredPosition = new Vector2(-18, -18);
            rect.sizeDelta = new Vector2(136, 34);
            root.AddComponent<OnlineLauncherVisibility>().Button = button.gameObject;
            return root;
        }
    }

    internal sealed class OnlineLauncherVisibility : MonoBehaviour
    {
        internal GameObject Button;
        private void Update()
        {
            bool visible = Game.Instance != null && Game.Instance.baseAlreadyCreated;
            if (Button != null && Button.activeSelf != visible) Button.SetActive(visible);
        }
    }
}
