using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // Selects an already discovered asteroid in the loaded native colony.
    // It neither copies worlds nor changes the loaded save.
    internal sealed class SystemPlanetSelectionWindow : KScreen
    {
        private static SystemPlanetSelectionWindow current;
        private GameObject root;
        private OnlineRuntime runtime;
        private System.Action selected;
        private Transform content;
        private string signature;
        private float nextRefresh;
        private string homeSystemId;
        private bool closed;

        internal static SystemPlanetSelectionWindow Create(OnlineRuntime runtime, System.Action selected)
        {
            if (current != null && !current.closed) { current.Activate(); return current; }
            var root = OnlineWidgets.CanvasRoot("OniOnlineAsteroidSelection", 30016);
            var shade = OnlineWidgets.Rect("Shade", root.transform);
            OnlineWidgets.Stretch((RectTransform)shade.transform);
            OnlineWidgets.Surface(shade, new Color(0, 0, 0, 0.7f));
            var panel = OnlineWidgets.Column(root.transform, "AsteroidPicker", 16, 12);
            var rect = (RectTransform)panel;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(660, 600);
            OnlineWidgets.Surface(panel.gameObject, OnlineWidgets.PanelColor, true);
            var window = panel.gameObject.AddComponent<SystemPlanetSelectionWindow>();
            window.root = root; window.runtime = runtime; window.selected = selected;
            window.homeSystemId = OnlineColonyState.Current?.HomeSystemId;
            current = window;
            window.Build(panel); window.Activate(); return window;
        }

        public override bool IsModal() { return true; }
        public override float GetSortKey() { return MODAL_SCREEN_SORT_KEY + 4; }
        public override void OnKeyDown(KButtonEvent e)
        { if (e.TryConsume(global::Action.Escape)) Close(); e.Consumed = true; }
        public override void OnKeyUp(KButtonEvent e) { e.Consumed = true; }

        private void Build(Transform panel)
        {
            var heading = OnlineWidgets.Row(panel, 44);
            heading.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 2, 2, 2);
            OnlineWidgets.Surface(heading.gameObject, OnlineWidgets.AccentColor, true);
            var title = OnlineWidgets.Label(heading, OnlineText.Get("entry.available_asteroids"), 23);
            OnlineWidgets.Size(title.gameObject, -1, 40, 1);
            OnlineWidgets.Button(heading, OnlineText.Get("moderation.close"), Close, 80);
            var help = OnlineWidgets.Label(panel, OnlineText.Get("entry.current_save"), 17);
            help.textWrappingMode = TextWrappingModes.Normal;
            OnlineWidgets.Size(help.gameObject, -1, 54); help.color = OnlineWidgets.MutedColor;
            var scroll = OnlineWidgets.Scroll(panel, 380, out content);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            var footer = OnlineWidgets.Label(panel, OnlineText.Get("entry.keep_colony"), 16);
            footer.textWrappingMode = TextWrappingModes.Normal;
            OnlineWidgets.Size(footer.gameObject, -1, 46); footer.color = OnlineWidgets.MutedColor;
            Refresh();
        }

        private bool CanChoose()
        { return !runtime.Busy && runtime.Multiplayer?.StarSystem.Admitted != true &&
            runtime.Multiplayer?.StarSystem.TravelConfirmed != true && runtime.Session?.Reconnecting != true && runtime.Session?.Leaving != true; }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            if (OnlineColonyState.Current?.HomeSystemId != homeSystemId)
            { Close(); return; }
            nextRefresh = Time.unscaledTime + 0.25f; Refresh();
        }

        private void Refresh()
        {
            var colony = OnlineColonyState.Current;
            var worlds = colony?.AvailablePlanets() ?? new WorldContainer[0];
            string next = (colony?.SelectedNativeWorldId ?? -1) + ":" + CanChoose() + ":" +
                string.Join("|", worlds.Select(w => w.id + ":" + w.GetProperName()));
            if (next == signature) return;
            signature = next;
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (worlds.Length == 0)
            {
                var empty = OnlineWidgets.Label(content, OnlineText.Get("entry.no_asteroids"), 18);
                empty.textWrappingMode = TextWrappingModes.Normal;
                OnlineWidgets.Size(empty.gameObject, -1, 80); return;
            }
            foreach (var world in worlds)
            {
                int worldId = world.id;
                var button = OnlineWidgets.Button(content, world.GetProperName(), () => Select(worldId), -1);
                button.gameObject.name = "SelectNativeAsteroid_" + worldId;
                OnlineWidgets.Size(button.gameObject, -1, 64);
                button.interactable = CanChoose();
                if (colony.SelectedNativeWorldId == worldId) OnlineWidgets.Tint(button, OnlineWidgets.AccentColor);
                var label = button.GetComponentInChildren<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.rectTransform.offsetMin = new Vector2(18, 4);
                if (colony.SelectedNativeWorldId == worldId)
                    label.text = world.GetProperName() + " · " + OnlineText.Get("entry.selected");
            }
        }

        private void Select(int worldId)
        {
            if (!CanChoose() || OnlineColonyState.Current?.Select(worldId) != true) { Refresh(); return; }
            runtime.Revision++; selected?.Invoke(); Close();
        }

        private void Close()
        {
            if (closed) return;
            closed = true; if (current == this) current = null;
            Deactivate(); if (root != null) { root.SetActive(false); Destroy(root); }
        }
    }
}
