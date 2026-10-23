using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // Only native buttons are added; the game retains its chart, selection and panels.
    internal sealed class StarSystemPanel : MonoBehaviour
    {
        private ClusterMapScreen screen;
        private SystemHexMap map;
        private GameObject actions;
        private KButton connection, context, scope, buttonTemplate;
        internal bool Ready { get; private set; }
        internal int ActionCount { get { return labels.Count; } }
        internal string ButtonTemplateSource { get { return "ScreenPrefabs.ConfirmDialogScreen"; } }
        private readonly Dictionary<KButton, TMP_Text> labels = new Dictionary<KButton, TMP_Text>();
        private string selectedPeer;
        private int selectedWorld = -1;
        private int selectedQ, selectedR;
        private bool homeView = true, ownsDirectory;
        private int revision = -1;
        private double nextRefresh, nextLayout;
        private GridLayoutGroup toolbarLayout;
        private readonly Vector3[] corners = new Vector3[4];
        internal int ToolbarColumns { get { return toolbarLayout?.constraintCount ?? 0; } }
        internal float NativePanelLeft { get; private set; }
        internal float[] ToolbarBounds
        {
            get
            {
                if (actions == null) return new float[0];
                var rect = (RectTransform)actions.transform;
                rect.GetWorldCorners(corners);
                var low = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                var high = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
                return new[] { low.x, low.y, high.x - low.x, high.y - low.y };
            }
        }
        private OnlineRuntime Runtime { get { return OnlineRuntime.Instance; } }

        internal static StarSystemPanel Attach(ClusterMapScreen screen)
        {
            var panel = screen.GetComponent<StarSystemPanel>();
            if (panel != null) return panel;
            panel = screen.gameObject.AddComponent<StarSystemPanel>();
            panel.enabled = false;
            try
            {
                panel.screen = screen;
                panel.map = SystemHexMap.Attach(screen, OnlineRuntime.Instance);
                panel.map.Selected += panel.Select;
                panel.Build();
                panel.Ready = true;
                panel.enabled = true;
                return panel;
            }
            catch
            {
                // Failed construction must not leave an Update loop with partial controls.
                // Keep the first exception visible after removing only this attempt's UI.
                try { panel.Cleanup(); }
                catch (Exception cleanupError) { Debug.LogWarning("[OniOnline] Failed map attachment cleanup: " + cleanupError); }
                try { DestroyImmediate(panel); }
                catch (Exception cleanupError) { Debug.LogWarning("[OniOnline] Failed map component cleanup: " + cleanupError); }
                throw;
            }
        }

        private void Build()
        {
            // ClusterMapScreen.closeButton is unassigned in the real 744825 prefab.
            // The native dialog prefab owns fully wired standard text buttons.
            var dialog = ScreenPrefabs.Instance?.ConfirmDialogScreen;
            var nativeButton = dialog == null ? null : AccessTools.Field(typeof(ConfirmDialogScreen), "confirmButton")?.GetValue(dialog) as GameObject;
            buttonTemplate = nativeButton == null ? null : nativeButton.GetComponent<KButton>();
            if (buttonTemplate == null)
                throw new InvalidOperationException("OniOnline: native confirmation-button prefab is unavailable.");
            actions = new GameObject("OniOnlineNativeMapActions", typeof(RectTransform), typeof(GridLayoutGroup));
            var rect = (RectTransform)actions.transform;
            rect.SetParent(screen.transform, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(12, 18);
            toolbarLayout = actions.GetComponent<GridLayoutGroup>();
            toolbarLayout.spacing = new Vector2(8, 8);
            toolbarLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            toolbarLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            connection = NativeButton("OniOnlineConnection", () => {
                if (Runtime.Session == null) Runtime.HostFromStarMap(); else Runtime.ReturnHome();
            });
            NativeButton("OniOnlineBrowse", () => Runtime.BrowseSystems(), "entry.browse");
            NativeButton("OniOnlineChat", () => Runtime.ShowChat(), "system.chat");
            context = NativeButton("OniOnlineSelectedPlanet", ApplySelection);
            scope = NativeButton("OniOnlineMapScope", () => SetHomeView(!homeView));
            Refresh();
            LayoutToolbar();
        }

        private KButton NativeButton(string name, System.Action click, string textKey = null)
        {
            var button = Util.KInstantiateUI<KButton>(buttonTemplate.gameObject, actions, true);
            button.name = name;
            button.ClearOnClick(); button.ClearOnPointerEvents();
            button.onClick += click;
            if (button.fgImage != null && button.fgImage != button.bgImage)
                button.fgImage.enabled = false;
            var label = button.GetComponentInChildren<LocText>(true);
            if (label == null) throw new InvalidOperationException("OniOnline: native button label is unavailable.");
            label.gameObject.SetActive(true);
            label.SetText(textKey == null ? "" : OnlineText.Get(textKey));
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            labels.Add(button, label);
            var size = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 80; size.flexibleWidth = 1; size.preferredHeight = 42;
            var tooltip = button.GetComponent<ToolTip>();
            if (tooltip != null) tooltip.SetSimpleTooltip(textKey == null ? "" : OnlineText.Get(textKey));
            return button;
        }

        internal void Open()
        {
            SetHomeView(SystemHexMap.IsLocalSystem(Runtime));
            if (homeView) map.ResetView();
        }

        internal void CloseOnlineView()
        {
            StopDirectory();
            map?.SetHomeView(true);
            homeView = true;
        }

        private void SetHomeView(bool value)
        {
            homeView = value;
            selectedPeer = null; selectedWorld = -1;
            map.SetHomeView(value);
            if (!value) StartDirectory(); else StopDirectory();
            map.ResetView();
            Refresh();
        }

        private void Select(int q, int r)
        {
            var runtime = Runtime;
            var system = runtime.Multiplayer?.StarSystem;
            var selection = StarMapSelectionPolicy.Resolve(system?.State, homeView, SystemHexMap.IsLocalSystem(runtime),
                map.SelectedNativeWorldId, q, r);
            selectedQ = q; selectedR = r;
            selectedPeer = selection.PeerId;
            selectedWorld = selection.NativeWorldId;
            bool nativeOccupied = runtime.StarMapDirectory?.Stale != false ||
                runtime.StarMapDirectory.Entries.Any(e => e.Q == q && e.R == r);
            if (!homeView && !selection.RoomCellOccupied && !nativeOccupied && system != null && !system.Admitted)
                system.ChooseLanding(q, r);
            Refresh();
        }

        private void ApplySelection()
        {
            var runtime = Runtime;
            if (selectedPeer != null && selectedPeer != runtime.Session?.LocalPlayerId)
            { runtime.ObserveWorld(selectedPeer); return; }
            if (homeView && selectedWorld >= 0)
            {
                if (OnlineColonyState.Current?.Select(selectedWorld) == true) runtime.Revision++;
                Refresh(); return;
            }
            runtime.ConfirmRift();
            Refresh();
        }

        private void Refresh()
        {
            var runtime = Runtime;
            if (runtime == null) return;
            revision = runtime.Revision;
            var session = runtime.Session;
            var system = runtime.Multiplayer?.StarSystem;
            bool active = !runtime.Busy && session?.Reconnecting != true && session?.Leaving != true;
            SetText(connection, session == null ? "system.open" : session.IsHost ? "planet.close_system" : "planet.return_home");
            connection.isInteractable = active && (session != null || runtime.DependencyError == null && runtime.SelectedPlanet != null);
            scope.gameObject.SetActive(session != null && !SystemHexMap.IsLocalSystem(runtime));
            SetText(scope, homeView ? "native.room_chart" : "native.home_chart");
            bool peer = selectedPeer != null && selectedPeer != session?.LocalPlayerId;
            bool canChoose = system?.Admitted != true && system?.TravelConfirmed != true;
            bool reservation = system?.State?.Members.Any(m => m.PeerId == session?.LocalPlayerId && m.Planet == null && m.Q == selectedQ && m.R == selectedR) == true;
            SetText(context, peer ? "map.observe" : homeView ? "native.select_asteroid" : system?.TravelConfirmed == true
                ? "entry.waiting_confirmation" : reservation ? "entry.join" : "native.select_position");
            context.isInteractable = active && (peer || homeView && selectedWorld >= 0 && canChoose ||
                !homeView && reservation && canChoose && runtime.SelectedPlanet != null);
            nextLayout = 0;
            string hint = runtime.DependencyError ?? system?.LastError ?? runtime.Error;
            var tooltip = connection.GetComponent<ToolTip>();
            if (tooltip != null) tooltip.SetSimpleTooltip(hint == null ? runtime.RoomName : OnlineText.Get(
                hint.StartsWith("dependency.", StringComparison.Ordinal) ? hint : "error." + hint));
        }

        private void SetText(KButton button, string key) { labels[button].text = OnlineText.Get(key); }

        private void Update()
        {
            bool visible = screen.IsScreenActive() && screen.GetMode() == ClusterMapScreen.Mode.Default;
            if (actions.activeSelf != visible) actions.SetActive(visible);
            if (!visible)
            {
                StopDirectory();
                map.SuspendRemoteView();
                return;
            }
            if (!homeView) StartDirectory();
            if (revision == Runtime?.Revision && Time.realtimeSinceStartupAsDouble < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartupAsDouble + 0.5;
            Refresh();
        }

        private void LateUpdate()
        {
            if (!Ready || actions == null || !actions.activeInHierarchy || Time.realtimeSinceStartupAsDouble < nextLayout) return;
            nextLayout = Time.realtimeSinceStartupAsDouble + 0.25;
            LayoutToolbar();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (Ready) { nextLayout = 0; LayoutToolbar(); }
        }

        private void LayoutToolbar()
        {
            var parent = (RectTransform)screen.transform;
            float right = parent.rect.xMax - 12;
            var bottomBand = new Rect(parent.rect.xMin, parent.rect.yMin, parent.rect.width, 118);
            var details = DetailsScreen.Instance;
            var nativeRoots = new List<Transform>();
            if (details != null) { nativeRoots.Add(details.transform); if (details.UserMenuPanel != null) nativeRoots.Add(details.UserMenuPanel); }
            foreach (var nativeRoot in nativeRoots)
                foreach (var graphic in nativeRoot.GetComponentsInChildren<Graphic>(false))
                {
                    if (!graphic.enabled || graphic.color.a <= .001f || graphic.canvasRenderer.cull) continue;
                    var text = graphic as TMP_Text;
                    if (text != null && string.IsNullOrEmpty(text.text)) continue;
                    graphic.rectTransform.GetWorldCorners(corners);
                    var low = parent.InverseTransformPoint(corners[0]);
                    var high = parent.InverseTransformPoint(corners[2]);
                    var bounds = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
                    // Ignore full-screen backgrounds; reserve the actual right-hand
                    // native panel graphics that overlap the bottom toolbar band.
                    if (bounds.center.x <= parent.rect.center.x || !bounds.Overlaps(bottomBand)) continue;
                    right = Mathf.Min(right, bounds.xMin - 12);
                }
            NativePanelLeft = RectTransformUtility.WorldToScreenPoint(null,
                parent.TransformPoint(new Vector3(right + 12, parent.rect.yMin, 0))).x;
            float available = Mathf.Max(80, right - parent.rect.xMin - 12);
            int visible = labels.Keys.Count(button => button != null && button.gameObject.activeSelf);
            if (visible == 0) return;
            int columns = Mathf.Clamp(Mathf.FloorToInt((available + 8) / 188), 1, visible);
            int rows = Mathf.CeilToInt((float)visible / columns);
            var target = (RectTransform)actions.transform;
            var size = new Vector2(available, rows * 42 + (rows - 1) * 8);
            var cells = new Vector2((available - (columns - 1) * 8) / columns, 42);
            if (toolbarLayout.constraintCount == columns && toolbarLayout.cellSize == cells && target.sizeDelta == size) return;
            toolbarLayout.constraintCount = columns;
            toolbarLayout.cellSize = cells;
            target.sizeDelta = size;
            LayoutRebuilder.MarkLayoutForRebuild(target);
        }

        private void StartDirectory()
        {
            Runtime?.StarMapDirectory?.Start();
            ownsDirectory = true;
        }
        private void StopDirectory()
        {
            if (!ownsDirectory) return;
            ownsDirectory = false;
            Runtime?.StarMapDirectory?.Stop();
        }
        private void Cleanup()
        {
            Ready = false;
            enabled = false;
            var oldMap = map; map = null;
            var oldActions = actions; actions = null;
            labels.Clear();
            connection = context = scope = buttonTemplate = null;
            if (oldMap != null) oldMap.Selected -= Select;
            if (oldActions != null) { oldActions.SetActive(false); Destroy(oldActions); }
            StopDirectory();
            oldMap?.SuspendRemoteView();
        }
        private void OnDestroy() { Cleanup(); }
    }
}
