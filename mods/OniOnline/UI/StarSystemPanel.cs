using OniOnline.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal sealed class StarSystemPanel : MonoBehaviour
    {
        private ClusterMapScreen map;
        private Button dock;
        private StarSystemWindow window;
        internal bool IsEditing { get { return window != null; } }

        internal static StarSystemPanel Attach(ClusterMapScreen screen)
        {
            var component = screen.GetComponent<StarSystemPanel>();
            if (component == null)
            {
                component = screen.gameObject.AddComponent<StarSystemPanel>();
                component.map = screen;
                component.dock = OnlineWidgets.Button(screen.transform, OnlineText.Get("system.map"), component.Open, 260);
                component.dock.gameObject.name = "OniOnlineSystemDock";
                var rect = (RectTransform)component.dock.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
                rect.pivot = new Vector2(0.5f, 0); rect.anchoredPosition = new Vector2(0, 20);
                rect.sizeDelta = new Vector2(260, 42);
                OnlineWidgets.Tint(component.dock, OnlineWidgets.AccentColor);
            }
            return component;
        }
        internal void Open()
        { OnlineRuntime.Instance.ShowSystemMap(); }
        internal void ShowEngine()
        { if (window == null) window = StarSystemWindow.Create(OnlineRuntime.Instance); window.ShowEngine(); }
        internal void CloseWindow() { if (window != null) window.Close(); window = null; }
        private void Update()
        {
            bool visible = map.GetMode() == ClusterMapScreen.Mode.Default;
            if (dock.gameObject.activeSelf != visible) dock.gameObject.SetActive(visible);
            if (!visible) CloseWindow();
        }
        private void OnDisable() { CloseWindow(); }
        private void OnDestroy() { CloseWindow(); if (dock != null) Destroy(dock.gameObject); }
    }
}
