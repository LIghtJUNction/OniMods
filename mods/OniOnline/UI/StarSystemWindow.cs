using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // Printing pod story entry. The chart remains the single place for hosting,
    // choosing a landing and selecting an asteroid.
    internal sealed class StarSystemWindow : KScreen
    {
        private GameObject root;
        private bool closed;

        internal static StarSystemWindow Create(OnlineRuntime runtime)
        {
            var root = OnlineWidgets.CanvasRoot("OniOnlinePlanetaryEngine", 30015);
            var shade = OnlineWidgets.Rect("Shade", root.transform);
            OnlineWidgets.Stretch((RectTransform)shade.transform);
            OnlineWidgets.Surface(shade, new Color(0, 0, 0, 0.65f));
            var panel = OnlineWidgets.Column(root.transform, "PlanetaryEngineEntry", 16, 14);
            var rect = (RectTransform)panel;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(720, 620);
            OnlineWidgets.Surface(panel.gameObject, OnlineWidgets.PanelColor, true);
            var window = panel.gameObject.AddComponent<StarSystemWindow>(); window.root = root;
            var heading = OnlineWidgets.Row(panel, 44);
            heading.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 2, 2, 2);
            OnlineWidgets.Surface(heading.gameObject, OnlineWidgets.AccentColor, true);
            var title = OnlineWidgets.Label(heading, OnlineText.Get("engine.title"), 24);
            OnlineWidgets.Size(title.gameObject, -1, 40, 1);
            OnlineWidgets.Button(heading, OnlineText.Get("moderation.close"), window.Close, 80);
            OnlineWidgets.SectionHeader(panel, OnlineText.Get("entry.engine_manual"));
            Transform body;
            var scroll = OnlineWidgets.Scroll(panel, 292, out body);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            var story = OnlineWidgets.Label(body, OnlineText.Get("engine.story"), 21);
            story.textWrappingMode = TextWrappingModes.Normal;
            story.alignment = TextAlignmentOptions.TopLeft;
            OnlineWidgets.Size(story.gameObject, -1, 242);
            var preview = OnlineWidgets.Label(panel, OnlineText.Get("engine.preview"), 17);
            preview.textWrappingMode = TextWrappingModes.Normal;
            preview.color = OnlineWidgets.MutedColor;
            OnlineWidgets.Size(preview.gameObject, -1, 76);
            var open = OnlineWidgets.Button(panel, OnlineText.Get("entry.engine_chart"),
                () => { window.Close(); runtime.ShowSystemMap(); }, -1);
            open.gameObject.name = "PlanetaryEngineOpenChart";
            OnlineWidgets.Tint(open, OnlineWidgets.AccentColor);
            window.Activate(); return window;
        }

        public override bool IsModal() { return true; }
        public override float GetSortKey() { return MODAL_SCREEN_SORT_KEY + 3; }
        public override void OnKeyDown(KButtonEvent e)
        { if (e.TryConsume(global::Action.Escape)) Close(); e.Consumed = true; }
        public override void OnKeyUp(KButtonEvent e) { e.Consumed = true; }
        internal void ShowEngine() { Activate(); }
        internal void Close()
        {
            if (closed) return;
            closed = true; Deactivate();
            if (root != null) { root.SetActive(false); Destroy(root); }
        }
    }
}
