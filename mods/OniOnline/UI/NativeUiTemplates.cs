using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal static class NativeUiTemplates
    {
        internal static T Field<T>(Component source, string name) where T : UnityEngine.Object
        {
            var field = source == null ? null : AccessTools.Field(source.GetType(), name);
            var value = field == null ? null : field.GetValue(source) as T;
            if (value == null) throw new InvalidOperationException("native_browser_unavailable");
            return value;
        }

        internal static Canvas RootCanvas()
        {
            var menu = MainMenu.Instance;
            var canvas = menu == null || !menu.IsActive() ? null : menu.GetComponentInParent<Canvas>();
            if (canvas == null && ManagementMenu.Instance != null)
                canvas = ManagementMenu.Instance.GetComponentInParent<Canvas>();
            if (canvas == null && Global.Instance != null && Global.Instance.globalCanvas != null)
                canvas = Global.Instance.globalCanvas.GetComponent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("native_browser_unavailable");
            return canvas.rootCanvas;
        }

        internal static void Text(LocText label, string value)
        {
            label.key = "";
            label.richText = false;
            // LocText's authored-text parser must not expand player names as hotkeys.
            ((TMP_Text)label).SetText(value ?? "");
        }

        internal static void Bind(KButton button, string text, System.Action click)
        {
            button.ClearOnClick();
            button.ClearOnPointerEvents();
            Text(button.GetComponentInChildren<LocText>(true), text);
            button.onClick += click;
        }

        internal static KButton Button(Transform parent, string text, System.Action click, float width = 140)
        {
            var source = Field<GameObject>(ScreenPrefabs.Instance.ConfirmDialogScreen, "confirmButton");
            var button = Util.KInstantiateUI<KButton>(source, parent.gameObject, true);
            Bind(button, text, click);
            Style(button.GetComponentInChildren<LocText>(true), 20, TextAlignmentOptions.Center);
            Size(button.gameObject, width, 44);
            return button;
        }

        internal static KInputTextField Input(Transform parent, string value, string placeholder, int limit)
        {
            var source = Field<KInputTextField>(ScreenPrefabs.Instance.FileNameDialog, "inputField");
            var input = Util.KInstantiateUI<KInputTextField>(source.gameObject, parent.gameObject, true);
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterValidation = TMP_InputField.CharacterValidation.None;
            input.onValidateInput = null;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = limit;
            input.richText = false;
            input.textComponent.richText = false;
            Style(input.textComponent, 20, TextAlignmentOptions.MidlineLeft);
            input.readOnly = false;
            input.OnValueChangesPaused = null;
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onSubmit = new TMP_InputField.SubmitEvent();
            var inputLabel = input.textComponent as LocText;
            if (inputLabel != null) inputLabel.key = "";
            var hint = input.placeholder as TMP_Text;
            if (hint != null)
            {
                var localizedHint = hint as LocText;
                if (localizedHint != null) localizedHint.key = "";
                hint.richText = false;
                hint.text = placeholder;
                Style(hint, 20, TextAlignmentOptions.MidlineLeft);
            }
            input.text = value ?? "";
            Size(input.gameObject, -1, 44, 1);
            return input;
        }

        internal static LocText Label(Transform parent, string text, float width = -1)
        {
            var source = Field<LocText>(ScreenPrefabs.Instance.ConfirmDialogScreen, "popupMessage");
            var label = Util.KInstantiateUI<LocText>(source.gameObject, parent.gameObject, true);
            Text(label, text);
            label.raycastTarget = false;
            Style(label, 20, TextAlignmentOptions.MidlineLeft);
            Size(label.gameObject, width, 36, width < 0 ? 1 : 0);
            return label;
        }

        // Native prefabs carry typography for their original use. In particular,
        // a mod version label must not determine a browser heading's font size.
        internal static void Style(TMP_Text label, float size, TextAlignmentOptions alignment,
            bool wrap = false)
        {
            var native = label as LocText;
            if (native != null)
            {
                // Resolve the native font/palette once, then detach its shared
                // size preset. LocText.Awake would otherwise restore that size
                // when the inactive browser is finally activated.
                if (native.textStyleSetting != null)
                    SetTextStyleSetting.ApplyStyle(native, native.textStyleSetting);
                native.allowOverride = true;
                native.textStyleSetting = null;
            }
            label.enableAutoSizing = false;
            label.fontSize = size;
            label.alignment = alignment;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
        }

        internal static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        internal static RectTransform Row(string name, Transform parent, float height = 36)
        {
            var rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            Size(rect.gameObject, -1, height);
            return rect;
        }

        internal static LayoutElement Size(GameObject item, float width, float height, float flexibleWidth = 0)
        {
            var layout = item.GetComponent<LayoutElement>() ?? item.AddComponent<LayoutElement>();
            layout.minWidth = Mathf.Max(0, width);
            layout.preferredWidth = width;
            layout.minHeight = layout.preferredHeight = height;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = 0;
            return layout;
        }
    }
}
