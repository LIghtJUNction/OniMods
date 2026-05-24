using System;
using OniMcp.Server;
using OniMcp.Support;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniMcp.Config
{
    public class OniMcpOptionsDialog : KScreen
    {
        private KInputTextField hostInput;
        private KInputTextField portInput;
        private KInputTextField tokenInput;
        private KToggle authToggle;
        private LocText statusText;

        public static void Show()
        {
            try
            {
                var parent = ResolveParent();
                if (parent == null)
                {
                    OniMcpLog.Warning("[OniMcp] Cannot open options dialog: UI parent not available.");
                    return;
                }

                var go = new GameObject("OniMcpOptionsDialog", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(OniMcpOptionsDialog));
                go.transform.SetParent(parent, false);
                var canvas = go.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 2000;
                go.GetComponent<OniMcpOptionsDialog>().Activate();
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to open options dialog: " + ex);
            }
        }

        private static Transform ResolveParent()
        {
            if (GameScreenManager.Instance != null && GameScreenManager.Instance.ssOverlayCanvas != null)
                return GameScreenManager.Instance.ssOverlayCanvas.transform;
            if (KScreenManager.Instance != null)
                return KScreenManager.Instance.transform;
            return null;
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            Build();
        }

        protected override void OnDeactivate()
        {
            Destroy(gameObject);
            base.OnDeactivate();
        }

        private void Build()
        {
            var panel = CreatePanel(transform);
            CreateText(panel, "Title", "ONI MCP Server Options", 24, FontStyles.Bold, new Vector2(24f, -54f), new Vector2(-24f, -20f));

            var options = OniMcpOptions.Current;
            hostInput = CreateLabeledInput(panel, "Host", "Host", options.Host, 94f);
            portInput = CreateLabeledInput(panel, "Port", "Port", options.Port.ToString(), 150f);
            authToggle = CreateToggle(panel, "Require token", options.AuthEnabled, 206f);
            tokenInput = CreateLabeledInput(panel, "Token", "Token", options.AuthToken, 262f);

            CreateText(panel, "Endpoint", "Endpoint: " + options.EndpointUrl, 14, FontStyles.Normal, new Vector2(24f, -324f), new Vector2(-24f, -298f));
            statusText = CreateText(panel, "Status", "Config: " + OniMcpOptions.ConfigPath, 13, FontStyles.Normal, new Vector2(24f, -356f), new Vector2(-24f, -330f));
            statusText.color = new Color(0.72f, 0.80f, 0.84f, 1f);

            CreateButton(panel, "SaveButton", "Save and restart", new Vector2(-330f, 24f), new Vector2(-170f, 62f), Save);
            CreateButton(panel, "CancelButton", "Close", new Vector2(-150f, 24f), new Vector2(-24f, 62f), Deactivate);
        }

        private RectTransform CreatePanel(Transform parent)
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(620f, 430f);
            rect.anchoredPosition = Vector2.zero;

            var image = panel.GetComponent<Image>();
            image.color = new Color(0.07f, 0.08f, 0.09f, 0.97f);
            image.raycastTarget = true;
            return rect;
        }

        private KInputTextField CreateLabeledInput(Transform parent, string label, string name, string value, float top)
        {
            CreateText(parent, label + "Label", label, 15, FontStyles.Bold, new Vector2(24f, -top), new Vector2(178f, -top + 28f));
            var inputRoot = new GameObject(name + "Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(KInputTextField));
            inputRoot.transform.SetParent(parent, false);
            var rect = inputRoot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(190f, -top);
            rect.offsetMax = new Vector2(-24f, -top + 34f);

            var image = inputRoot.GetComponent<Image>();
            image.color = new Color(0.015f, 0.018f, 0.022f, 0.96f);
            image.raycastTarget = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(inputRoot.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, new Vector2(8f, 4f), new Vector2(-8f, -4f));

            var text = CreateText(viewportRect, "Text", value ?? "", 15, FontStyles.Normal, Vector2.zero, Vector2.zero);
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.overflowMode = TextOverflowModes.Overflow;

            var input = inputRoot.GetComponent<KInputTextField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.text = value ?? "";
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 256;
            input.caretColor = new Color(0.4f, 1f, 1f, 1f);
            input.customCaretColor = true;
            input.selectionColor = new Color(0.2f, 0.8f, 1f, 0.35f);
            return input;
        }

        private KToggle CreateToggle(Transform parent, string label, bool value, float top)
        {
            CreateText(parent, "AuthLabel", label, 15, FontStyles.Bold, new Vector2(24f, -top), new Vector2(178f, -top + 28f));
            var root = new GameObject("AuthToggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(KImage), typeof(KToggle));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(190f, -top);
            rect.offsetMax = new Vector2(224f, -top + 34f);

            var image = root.GetComponent<KImage>();
            image.color = value ? new Color(0.2f, 0.55f, 0.58f, 1f) : new Color(0.16f, 0.18f, 0.19f, 1f);
            image.raycastTarget = true;

            var toggle = root.GetComponent<KToggle>();
            toggle.bgImage = image;
            toggle.isOn = value;
            toggle.onClick += () =>
            {
                toggle.isOn = !toggle.isOn;
                image.color = toggle.isOn ? new Color(0.2f, 0.55f, 0.58f, 1f) : new Color(0.16f, 0.18f, 0.19f, 1f);
            };
            return toggle;
        }

        private KButton CreateButton(Transform parent, string name, string label, Vector2 offsetMin, Vector2 offsetMax, System.Action onClick)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(KImage), typeof(KButton));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var image = root.GetComponent<KImage>();
            image.color = new Color(0.14f, 0.18f, 0.20f, 1f);
            image.raycastTarget = true;

            var button = root.GetComponent<KButton>();
            button.soundPlayer = new ButtonSoundPlayer();
            button.bgImage = image;
            button.additionalKImages = new KImage[0];
            button.onClick += onClick;

            var text = CreateText(root.transform, "Label", label, 15, FontStyles.Bold, Vector2.zero, Vector2.zero);
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private LocText CreateText(Transform parent, string name, string value, int size, FontStyles style, Vector2 offsetMin, Vector2 offsetMax)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(LocText));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var text = textObject.GetComponent<LocText>();
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

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private void Save()
        {
            int port;
            if (!int.TryParse(portInput.text, out port) || port < 1024 || port > 65535)
            {
                SetStatus("Port must be between 1024 and 65535.", true);
                return;
            }

            var options = new OniMcpOptions
            {
                Host = hostInput.text,
                Port = port,
                AuthEnabled = authToggle != null && authToggle.isOn,
                AuthToken = tokenInput.text,
                ScreenshotCleanupEnabled = OniMcpOptions.Current.ScreenshotCleanupEnabled,
                ScreenshotRetentionMinutes = OniMcpOptions.Current.ScreenshotRetentionMinutes,
                ScreenshotMaxFiles = OniMcpOptions.Current.ScreenshotMaxFiles
            };

            try
            {
                OniMcpOptions.Save(options);
                if (McpHttpServer.Instance != null)
                    McpHttpServer.Instance.RestartServer();
                SetStatus("Saved. Server endpoint: " + OniMcpOptions.Current.EndpointUrl, false);
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message, true);
                OniMcpLog.Warning("[OniMcp] Failed to save options: " + ex);
            }
        }

        private void SetStatus(string message, bool error)
        {
            if (statusText == null)
                return;
            statusText.text = message;
            statusText.color = error ? new Color(1f, 0.45f, 0.35f, 1f) : new Color(0.55f, 0.95f, 0.85f, 1f);
        }
    }
}
