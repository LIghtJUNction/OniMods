using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OniOnline.Multiplayer;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OniOnline.UI
{
    internal interface IWorldViewModel
    {
        IReadOnlyList<WorldViewEntry> Worlds { get; }
        long Revision { get; }
        bool ShareOwnWorld { get; }
        void SetSharing(bool enabled);
        void RequestWorld(string ownerId);
        void CloseWorldView();
    }

    internal sealed class WorldOverviewWindow : KScreen
    {
        private static readonly Color32 UnknownColor = new Color32(13, 14, 18, 255);
        private static readonly Regex LinkTags = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static bool ExperimentalWorkTools
        {
            get
            {
#if ONIONLINE_RUNTIME_PROBE
                return true;
#else
                return false;
#endif
            }
        }
        private readonly Dictionary<int, WorldCell> visibleCells = new Dictionary<int, WorldCell>();
        private readonly List<GameObject> dupeMarkers = new List<GameObject>();
        private readonly Dictionary<int, RectTransform> entityMarkers = new Dictionary<int, RectTransform>();
        private WorldCoordinator Coordinator { get { return model as WorldCoordinator; } }
        private PlanetManifest remotePlanet;
        private WorldInterestRegion region;
        private long subscriptionRevision, visualSequence = -1, motionSequence = -1;
        private bool temperatureOverlay, pendingSubscription, watchingDetails;
        private string detailsEpoch;
        private Vector2 panRemainder;
        private float nextTextureAt, subscribeAt, motionReceivedAt;
        private long latestMotionTick, previousMotionTick;
        private WorldInterestReplica renderedReplica;
        private Color32[] pixels;
        private Button temperatureButton;
        private TextMeshProUGUI viewportLabel;
        private IWorldViewModel model;
        private GameObject canvasRoot;
        private Transform listBody;
        private RectTransform mapRect;
        private RawImage mapImage;
        private AspectRatioFitter mapAspect;
        private TextMeshProUGUI selectedTitle;
        private TextMeshProUGUI selectedOwner;
        private TextMeshProUGUI stateLabel;
        private TextMeshProUGUI emptyLabel;
        private TextMeshProUGUI details;
        private TextMeshProUGUI sharingLabel;
        private Button sharingButton;
        private Button refreshButton;
        private Button plansButton;
        private Button digPlanButton;
        private Button mineTasks, memberTasks, mergedTasks, lowerPriority, higherPriority;
        private TextMeshProUGUI priorityLabel, taskStatus;
        private WorldTaskOverlay taskOverlay;
        private RemoteWorkCoordinator taskEvents;
        private WorkCell? selectionStart, selectionEnd;
        private string selectionPlanetId, selectionEpoch, layerActorId, lastDraftId;
        private int taskLayer, taskPriority = 5;
        private bool taskDirty = true;
        private float nextTaskPoll;
        private string taskMessage;
        private string worldListSignature;
        private Texture2D texture;
        private string selectedOwnerId;
        private WorldSnapshot displayedSnapshot;
        private long displayedSequence = -1;
        private long revision = long.MinValue;
        private int inspectedIndex = -1;
        private bool closing;

        internal WorkCell? TaskSelectionStart { get { return selectionStart; } }
        internal WorkCell? TaskSelectionEnd { get { return selectionEnd; } }
        internal int TaskSelectionCount { get { string error; return SelectedTaskCells(out error).Length; } }
        internal int TaskPriority { get { return taskPriority; } }
        internal int TaskLayer { get { return taskLayer; } }
        internal string TaskFeedback { get { return taskStatus?.text; } }
        internal int VisibleTaskMarkCount { get { return taskOverlay?.MarkCount ?? 0; } }
        internal int PendingTaskMarkCount { get { return taskOverlay?.PendingCount ?? 0; } }
        internal GameObject MapInputObject { get { return mapRect?.gameObject; } }
        internal WorldInterestRegion MapRegion { get { return region; } }
        internal int MapWorldWidth { get { return remotePlanet?.Width ?? displayedSnapshot?.World?.Width ?? 0; } }
        internal int MapWorldHeight { get { return remotePlanet?.Height ?? displayedSnapshot?.World?.Height ?? 0; } }
        internal bool TryMapCellScreenPosition(WorkCell cell, out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
            int width = MapWorldWidth;
            if (width < 1 || cell.X < 0 || cell.Y < 0 || cell.X >= width || cell.Y >= MapWorldHeight ||
                !mapImage.enabled || !region.Contains(cell.Y * width + cell.X, width) ||
                !visibleCells.ContainsKey(cell.Y * width + cell.X)) return false;
            var bounds = mapRect.rect;
            var point = new Vector2(bounds.xMin + (cell.X - region.X + .5f) * bounds.width / region.Width,
                bounds.yMin + (cell.Y - region.Y + .5f) * bounds.height / region.Height);
            screenPosition = RectTransformUtility.WorldToScreenPoint(null, mapRect.TransformPoint(point));
            return true;
        }

        internal static WorldOverviewWindow Create(IWorldViewModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var root = OnlineWidgets.CanvasRoot("OniOnlineWorldOverview", 30010);
            var shade = OnlineWidgets.Rect("Shade", root.transform);
            OnlineWidgets.Stretch((RectTransform)shade.transform);
            OnlineWidgets.Surface(shade, new Color(0, 0, 0, 0.6f));
            var panel = OnlineWidgets.Rect("WorldOverviewPanel", root.transform);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1100, 880);
            OnlineWidgets.Surface(panel, OnlineWidgets.PanelColor, true);
            var shadow = panel.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.35f);
            shadow.effectDistance = new Vector2(5, -5);
            var window = panel.AddComponent<WorldOverviewWindow>();
            window.canvasRoot = root;
            window.model = model;
            window.Build();
            window.Activate();
            return window;
        }

        public override bool IsModal() { return true; }
        public override float GetSortKey() { return MODAL_SCREEN_SORT_KEY + 2; }
        public override void OnKeyDown(KButtonEvent e)
        {
            if (e.TryConsume(global::Action.Escape)) Close();
            e.Consumed = true;
        }
        public override void OnKeyUp(KButtonEvent e) { e.Consumed = true; }

        internal void DestroyWindow()
        {
            closing = true;
            DetachTaskEvents();
            StopObservation();
            ClearMap();
            if (canvasRoot != null) Destroy(canvasRoot);
        }

        private static string T(string key) { return OnlineText.Get(key); }
        private static string Format(string key, params object[] values)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), values);
        }

        private static void Fill(RectTransform rect, float left, float bottom, float right, float top)
        {
            OnlineWidgets.Stretch(rect);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static TextMeshProUGUI Caption(Transform parent, string text, int size, float height)
        {
            var label = OnlineWidgets.Label(parent, text, size);
            OnlineWidgets.Size(label.gameObject, -1, height);
            return label;
        }

        private void Build()
        {
            var titleBar = OnlineWidgets.Rect("TitleBar", transform);
            var titleRect = (RectTransform)titleBar.transform;
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = Vector2.one;
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.sizeDelta = new Vector2(0, 40);
            OnlineWidgets.Surface(titleBar, OnlineWidgets.AccentColor);
            var title = OnlineWidgets.Label(titleBar.transform, T("world.title"), 20);
            Fill(title.rectTransform, 52, 2, 52, 2);
            title.alignment = TextAlignmentOptions.Center;
            var close = OnlineWidgets.Button(titleBar.transform, "", Close, 34);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = Vector2.one;
            closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-3, -3);
            closeRect.sizeDelta = new Vector2(34, 34);
            OnlineWidgets.Tint(close, new Color(0.40f, 0.22f, 0.31f));
            foreach (float angle in new[] { -45f, 45f })
            {
                var stroke = OnlineWidgets.Rect("CloseStroke", close.transform);
                var strokeRect = (RectTransform)stroke.transform;
                strokeRect.sizeDelta = new Vector2(15, 2);
                strokeRect.localRotation = Quaternion.Euler(0, 0, angle);
                OnlineWidgets.Surface(stroke, OnlineWidgets.TextColor).raycastTarget = false;
            }

            var content = OnlineWidgets.Column(transform, "Content", 16, 10);
            Fill((RectTransform)content, 0, 0, 0, 40);
            var shareRow = OnlineWidgets.Row(content, 40);
            var sharingTitle = OnlineWidgets.Label(shareRow, T("world.sharing"), 18);
            OnlineWidgets.Size(sharingTitle.gameObject, -1, 40, 1);
            sharingButton = OnlineWidgets.Button(shareRow, "", () => model.SetSharing(!model.ShareOwnWorld), 110);
            sharingLabel = sharingButton.GetComponentInChildren<TextMeshProUGUI>();
            var shareHint = Caption(content, T("world.sharing_hint"), 15, 36);
            shareHint.color = OnlineWidgets.MutedColor;
            shareHint.textWrappingMode = TextWrappingModes.Normal;
            OnlineWidgets.Divider(content);

            var body = OnlineWidgets.Rect("WorldsAndMap", content);
            OnlineWidgets.Size(body, -1, 500, 1).flexibleHeight = 1;
            var horizontal = body.AddComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 14;
            horizontal.childControlWidth = horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = true;
            var sidebar = OnlineWidgets.Column(body.transform, "WorldList", 0, 8);
            OnlineWidgets.Size(sidebar.gameObject, 252, 0).flexibleHeight = 1;
            var listScroll = OnlineWidgets.Scroll(sidebar, 460, out listBody);
            listScroll.GetComponent<LayoutElement>().minHeight = 120;
            listScroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            refreshButton = OnlineWidgets.Button(sidebar, T("world.refresh"), RefreshSelected, -1);
            var chat = OnlineWidgets.Button(sidebar, T("ui.chat_title"), OpenChat, -1);
            chat.gameObject.name = "OpenRoomChat";
            OnlineWidgets.Tint(chat, OnlineWidgets.AccentColor);
            plansButton = OnlineWidgets.Button(sidebar, T("work.title"), () => OpenPlans(false), -1);
            plansButton.gameObject.SetActive(ExperimentalWorkTools);
            var team = OnlineWidgets.Button(sidebar, T("team.title"), () => OnlineRuntime.Instance?.ShowTeam(), -1);
            team.gameObject.SetActive(ExperimentalWorkTools);
            var main = OnlineWidgets.Column(body.transform, "WorldView", 0, 6);
            OnlineWidgets.Size(main.gameObject, -1, 0, 1).flexibleHeight = 1;
            selectedTitle = Caption(main, T("world.select_world"), 21, 28);
            selectedTitle.textWrappingMode = TextWrappingModes.NoWrap;
            selectedTitle.overflowMode = TextOverflowModes.Ellipsis;
            selectedOwner = Caption(main, "", 16, 24);
            selectedOwner.color = OnlineWidgets.MutedColor;
            stateLabel = Caption(main, "", 16, 24);

            var mapFrame = OnlineWidgets.Rect("MapFrame", main);
            OnlineWidgets.Surface(mapFrame, UnknownColor, true);
            var mapSize = OnlineWidgets.Size(mapFrame, -1, 400, 1);
            mapSize.minHeight = 180; mapSize.flexibleHeight = 1;
            var viewport = OnlineWidgets.Rect("MapViewport", mapFrame.transform);
            Fill((RectTransform)viewport.transform, 8, 8, 8, 8);
            viewport.AddComponent<RectMask2D>();
            var map = OnlineWidgets.Rect("ReadOnlyMap", viewport.transform);
            mapRect = (RectTransform)map.transform;
            mapRect.anchorMin = mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapRect.pivot = new Vector2(0.5f, 0.5f);
            mapImage = map.AddComponent<RawImage>();
            mapImage.color = Color.white;
            mapImage.enabled = false;
            mapAspect = map.AddComponent<AspectRatioFitter>();
            mapAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            mapAspect.aspectRatio = 1;
            var pointer = map.AddComponent<WorldMapPointer>();
            if (ExperimentalWorkTools)
            {
                pointer.Click = SelectTaskCell;
                pointer.SelectionStart = SelectTaskCell;
                pointer.SelectionMove = DragTaskSelection;
            }
            else pointer.Click = InspectCell;
            pointer.Hover = InspectCell;
            pointer.Exit = StopHover;
            pointer.Pan = DragMap;
            pointer.Zoom = ZoomMap;
            var overlay = OnlineWidgets.Rect("TaskOverlay", mapRect);
            OnlineWidgets.Stretch((RectTransform)overlay.transform);
            taskOverlay = overlay.AddComponent<WorldTaskOverlay>(); taskOverlay.raycastTarget = false;
            overlay.SetActive(ExperimentalWorkTools);
            emptyLabel = OnlineWidgets.Label(viewport.transform, T("world.no_worlds"), 18);
            Fill(emptyLabel.rectTransform, 24, 12, 24, 12);
            emptyLabel.alignment = TextAlignmentOptions.Center;
            emptyLabel.color = OnlineWidgets.MutedColor;
            emptyLabel.textWrappingMode = TextWrappingModes.Normal;
            var navigation = OnlineWidgets.Row(main, 40);
            OnlineWidgets.Button(navigation, "←", () => PanRegion(-16, 0), 38);
            OnlineWidgets.Button(navigation, "↑", () => PanRegion(0, 16), 38);
            OnlineWidgets.Button(navigation, "↓", () => PanRegion(0, -16), 38);
            OnlineWidgets.Button(navigation, "→", () => PanRegion(16, 0), 38);
            OnlineWidgets.Button(navigation, "+", () => ZoomMap(1), 38);
            OnlineWidgets.Button(navigation, "−", () => ZoomMap(-1), 38);
            temperatureButton = OnlineWidgets.Button(navigation, T("view.temperature"), ToggleTemperature, 112);
            viewportLabel = OnlineWidgets.Label(navigation, "", 14);
            OnlineWidgets.Size(viewportLabel.gameObject, -1, 40, 1);
            var layers = OnlineWidgets.Row(main, 40);
            mineTasks = OnlineWidgets.Button(layers, T("work.mine"), () => SetTaskLayer(0), -1);
            mineTasks.gameObject.name = "TaskLayerMine";
            memberTasks = OnlineWidgets.Button(layers, T("work.member"), () => SetTaskLayer(1), -1);
            memberTasks.gameObject.name = "TaskLayerMember";
            mergedTasks = OnlineWidgets.Button(layers, T("work.merged"), () => SetTaskLayer(2), -1);
            mergedTasks.gameObject.name = "TaskLayerMerged";
            layers.gameObject.SetActive(ExperimentalWorkTools);
            var taskTools = OnlineWidgets.Row(main, 40);
            lowerPriority = OnlineWidgets.Button(taskTools, "−", () => SetTaskPriority(taskPriority - 1), 36);
            lowerPriority.gameObject.name = "TaskPriorityDecrease";
            priorityLabel = OnlineWidgets.Label(taskTools, "", 16); OnlineWidgets.Size(priorityLabel.gameObject, 100, 40);
            priorityLabel.alignment = TextAlignmentOptions.Center;
            higherPriority = OnlineWidgets.Button(taskTools, "+", () => SetTaskPriority(taskPriority + 1), 36);
            higherPriority.gameObject.name = "TaskPriorityIncrease";
            digPlanButton = OnlineWidgets.Button(taskTools, T("work.propose_dig"), SubmitDigSelection, -1);
            digPlanButton.gameObject.name = "TaskSubmitDig";
            OnlineWidgets.Tint(digPlanButton, OnlineWidgets.AccentColor);
            digPlanButton.interactable = false;
            taskTools.gameObject.SetActive(ExperimentalWorkTools);
            taskStatus = Caption(main, T("taskview.hint"), 15, 42);
            taskStatus.gameObject.name = "TaskFeedback";
            taskStatus.textWrappingMode = TextWrappingModes.Normal; taskStatus.color = OnlineWidgets.MutedColor;
            taskStatus.gameObject.SetActive(ExperimentalWorkTools);
            details = Caption(main, T("world.inspect_hint"), 16, 52);
            details.textWrappingMode = TextWrappingModes.Normal;
            details.color = OnlineWidgets.MutedColor;
            taskEvents = ExperimentalWorkTools ? Coordinator?.Work : null;
            if (taskEvents != null)
            { taskEvents.Changed += InvalidateTasks; taskEvents.SubmissionChanged += OnTaskSubmission; }
            Refresh();
        }

        private void Update()
        {
            if (closing || model == null) return;
            if (pendingSubscription && Time.unscaledTime >= subscribeAt) SendSubscription();
            if (model.Revision != revision) Refresh();
            if (remotePlanet != null)
            {
                DrawInterest();
                DrawMotion();
                RefreshRemoteDetails();
            }
            if (ExperimentalWorkTools && Coordinator?.Work != null && FindSelectedPlanet() != null && Time.unscaledTime >= nextTaskPoll)
            {
                nextTaskPoll = Time.unscaledTime + 3;
                PollTaskLayer();
            }
            if (taskDirty) RefreshTaskOverlay();
        }

        private void Refresh()
        {
            revision = model.Revision;
            sharingLabel.text = T(model.ShareOwnWorld ? "ui.enabled" : "ui.disabled");
            OnlineWidgets.Tint(sharingButton, model.ShareOwnWorld ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            var worlds = model.Worlds ?? Array.Empty<WorldViewEntry>();
            var selected = worlds.FirstOrDefault(item => item?.Descriptor != null && item.Descriptor.OwnerId == selectedOwnerId);
            if (selected == null)
            {
                selected = worlds.FirstOrDefault(item => item?.Descriptor != null && item.IsLocal)
                    ?? worlds.FirstOrDefault(item => item?.Descriptor != null);
                selectedOwnerId = selected?.Descriptor.OwnerId;
                ClearMap();
                if (selected != null) BeginObservation(selected);
                else StopObservation();
            }
            RebuildWorldList(worlds);
            refreshButton.interactable = selected != null;
            plansButton.interactable = FindSelectedPlanet() != null;
            if (selected == null)
            {
                selectedTitle.text = T("world.select_world");
                selectedOwner.text = "";
                stateLabel.text = "";
                ShowEmpty("world.no_worlds");
                return;
            }
            temperatureButton.interactable = !selected.IsLocal && Coordinator?.PlanetForOwner(selectedOwnerId) != null;
            if (!temperatureButton.interactable && displayedSnapshot?.World != null)
                viewportLabel.text = Format("view.region", region.X, region.Y, region.Width, region.Height);
            selectedTitle.text = selected.Descriptor.Name ?? "";
            selectedOwner.text = (selected.OwnerName ?? "") + " · " + T(ExperimentalWorkTools ? "taskview.with_plans" : "world.read_only");
            stateLabel.text = WorldStatus(selected);
            stateLabel.color = selected.Connected || selected.IsLocal ? OnlineWidgets.OnlineColor : OnlineWidgets.MutedColor;
            if (selected.LastReceivedUtc != System.DateTime.MinValue)
                stateLabel.text += " · " + Format("world.last_received", selected.LastReceivedUtc.ToLocalTime().ToString("HH:mm:ss"));
            var manifest = !selected.IsLocal ? Coordinator?.PlanetForOwner(selectedOwnerId) : null;
            if (manifest != null)
            {
                if (remotePlanet == null || remotePlanet.PlanetId != manifest.PlanetId) BeginObservation(selected);
                stateLabel.text = T(Coordinator.Interest.LastError == "world_interest_denied" ? "world.not_shared" :
                    Coordinator.Interest.Stale ? "view.waiting_region" : "view.region_live");
                DrawInterest();
                return;
            }
            if (remotePlanet != null) { StopObservation(); ClearMap(); }
            if (!selected.IsLocal && !selected.Descriptor.Shared)
            {
                ShowEmpty("world.not_shared");
                return;
            }
            var snapshot = selected.Snapshot;
            if (snapshot == null || snapshot.World == null || snapshot.World.OwnerId != selectedOwnerId
                || snapshot.World.Id != selected.Descriptor.Id || snapshot.World.Epoch != selected.Descriptor.Epoch)
            {
                ShowEmpty("world.waiting");
                return;
            }
            if (snapshot != displayedSnapshot || snapshot.Sequence != displayedSequence) DrawSnapshot(snapshot);
        }

        private void BeginObservation(WorldViewEntry entry)
        {
            var manifest = entry.IsLocal ? null : Coordinator?.PlanetForOwner(entry.Descriptor.OwnerId);
            if (remotePlanet != null && manifest?.PlanetId == remotePlanet.PlanetId &&
                selectedOwnerId == entry.Descriptor.OwnerId) return;
            StopObservation();
            ClearSelection();
            taskMessage = lastDraftId = null;
            layerActorId = null; nextTaskPoll = 0;
            ClearMap();
            if (manifest == null)
            {
                // Remote state is requested only after the authenticated planet manifest
                // can bound a viewport. Missing metadata never falls back to a whole-world transfer.
                if (entry.IsLocal) model.RequestWorld(entry.Descriptor.OwnerId);
                return;
            }
            Coordinator.StopLegacyObservation();
            remotePlanet = manifest;
            region = new WorldInterestRegion { Width = Math.Min(64, manifest.Width), Height = Math.Min(64, manifest.Height) };
            region.X = (manifest.Width - region.Width) / 2;
            region.Y = (manifest.Height - region.Height) / 2;
            ScheduleSubscription();
        }

        private void StopObservation()
        {
            if (remotePlanet != null)
            {
                Coordinator?.CellDetails?.Stop();
                Coordinator?.Interest?.Stop();
            }
            remotePlanet = null;
            pendingSubscription = watchingDetails = false;
            detailsEpoch = null;
        }

        private void ScheduleSubscription()
        {
            if (remotePlanet == null) return;
            pendingSubscription = true;
            subscribeAt = Time.unscaledTime + .12f;
            Coordinator?.CellDetails?.Stop();
            detailsEpoch = null;
            watchingDetails = false;
            ClearMap(false);
            emptyLabel.text = T("view.waiting_region");
            emptyLabel.gameObject.SetActive(true);
            viewportLabel.text = Format("view.region", region.X, region.Y, region.Width, region.Height);
            OnlineWidgets.Tint(temperatureButton, temperatureOverlay ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
        }

        private void SendSubscription()
        {
            pendingSubscription = false;
            Coordinator?.Interest.Subscribe(selectedOwnerId, remotePlanet.PlanetId, new WorldInterestSubscription
            {
                Revision = ++subscriptionRevision, Region = region,
                Channels = WorldInterestChannels.Visual | WorldInterestChannels.Entities |
                    (temperatureOverlay ? WorldInterestChannels.Temperature : WorldInterestChannels.None),
                NumericIntervalTicks = 30, HoverIndex = -1
            });
        }

        private void PanRegion(int x, int y)
        {
            int width = MapWorldWidth, height = MapWorldHeight;
            if (width < 1 || height < 1 || region.Width < 1 || region.Height < 1) return;
            int nextX = Mathf.Clamp(region.X + x, 0, width - region.Width);
            int nextY = Mathf.Clamp(region.Y + y, 0, height - region.Height);
            if (nextX == region.X && nextY == region.Y) return;
            region.X = nextX; region.Y = nextY;
            RefreshViewport();
        }

        private void DragMap(Vector2 delta)
        {
            if (MapWorldWidth < 1 || mapRect.rect.width <= 0 || mapRect.rect.height <= 0) return;
            float scale = Mathf.Max(.001f, canvasRoot.GetComponent<Canvas>().scaleFactor);
            panRemainder += new Vector2(-delta.x * region.Width / (mapRect.rect.width * scale),
                -delta.y * region.Height / (mapRect.rect.height * scale));
            int x = (int)panRemainder.x, y = (int)panRemainder.y;
            if (x == 0 && y == 0) return;
            panRemainder -= new Vector2(x, y);
            PanRegion(x, y);
        }

        private void ZoomMap(float amount)
        {
            int worldWidth = MapWorldWidth, worldHeight = MapWorldHeight;
            if (worldWidth < 1 || worldHeight < 1 || amount == 0) return;
            int maximum = remotePlanet == null ? Math.Max(worldWidth, worldHeight) : 64;
            int side = Mathf.Clamp(Mathf.RoundToInt(Math.Max(region.Width, region.Height) * (amount > 0 ? .75f : 1.33334f)), Math.Min(8, maximum), maximum);
            int width = Math.Min(side, worldWidth), height = Math.Min(side, worldHeight);
            if (region.Width == width && region.Height == height) return;
            region.X = Mathf.Clamp(region.X + (region.Width - width) / 2, 0, worldWidth - width);
            region.Y = Mathf.Clamp(region.Y + (region.Height - height) / 2, 0, worldHeight - height);
            region.Width = width; region.Height = height;
            RefreshViewport();
        }

        private void RefreshViewport()
        {
            if (remotePlanet != null) { ScheduleSubscription(); return; }
            // Crop the existing explored snapshot only. Local navigation never reads Grid.
            ClearSelection(); inspectedIndex = -1;
            details.text = T("world.inspect_hint");
            DrawCachedSnapshot();
        }

        private void ToggleTemperature()
        {
            if (remotePlanet == null) return;
            temperatureOverlay = !temperatureOverlay;
            ScheduleSubscription();
        }

        private void EnsureTexture(int width, int height)
        {
            if (texture != null && texture.width == width && texture.height == height) return;
            if (texture != null) Destroy(texture);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "OniOnlineVisibleRegion";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
        }

        private static Color32 CellColor(WorldInterestCell cell, bool temperature)
        {
            if (temperature && (cell.Fields & WorldInterestChannels.Temperature) != 0)
            {
                float celsius = cell.Temperature - 273.15f;
                return Color.Lerp(new Color(.18f, .38f, .82f), new Color(.95f, .24f, .17f), Mathf.InverseLerp(-20, 80, celsius));
            }
            uint rgba = cell.Color;
            return new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), 255);
        }

        private void DrawInterest()
        {
            var interest = Coordinator?.Interest;
            if (remotePlanet == null || interest == null || pendingSubscription) return;
            if (interest.Stale || interest.PlanetId != remotePlanet.PlanetId || interest.OwnerId != selectedOwnerId)
            {
                // No old cache is exposed while authorization or the authority epoch is uncertain.
                mapImage.enabled = false;
                visibleCells.Clear(); ClearEntityMarkers();
                renderedReplica = null; visualSequence = -1;
                if (detailsEpoch != null) StopHover();
                digPlanButton.interactable = false;
                taskOverlay?.Clear(); taskDirty = true;
                emptyLabel.text = T(interest.LastError == "world_interest_denied" ? "world.not_shared" : "view.waiting_region");
                emptyLabel.gameObject.SetActive(true);
                return;
            }
            var replica = interest.VisualReplica;
            if (replica == null || replica.Sequence == 0 ||
                (replica == renderedReplica && replica.Sequence == visualSequence) || Time.unscaledTime < nextTextureAt) return;
            nextTextureAt = Time.unscaledTime + .1f;
            EnsureTexture(region.Width, region.Height);
            if (pixels == null || pixels.Length != region.Width * region.Height) pixels = new Color32[region.Width * region.Height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = UnknownColor;
            visibleCells.Clear();
            foreach (var pair in replica.Cells)
            {
                var value = pair.Value;
                if (!value.Explored || !region.Contains(value.Index, remotePlanet.Width)) continue;
                int offset = (value.Index / remotePlanet.Width - region.Y) * region.Width + value.Index % remotePlanet.Width - region.X;
                pixels[offset] = CellColor(value, temperatureOverlay);
                visibleCells[value.Index] = new WorldCell { Index = value.Index, ElementHash = value.ElementHash, Color = value.Color };
            }
            texture.SetPixels32(pixels); texture.Apply(false, false);
            mapImage.texture = texture; mapImage.enabled = true;
            mapAspect.aspectRatio = (float)region.Width / region.Height;
            emptyLabel.gameObject.SetActive(false);
            taskDirty = true;
            renderedReplica = replica; visualSequence = replica.Sequence;
            taskDirty = true;
        }

        private void ClearEntityMarkers()
        {
            foreach (var marker in entityMarkers.Values)
                if (marker != null) { marker.gameObject.SetActive(false); Destroy(marker.gameObject); }
            entityMarkers.Clear();
            motionSequence = -1;
            latestMotionTick = previousMotionTick = 0;
        }

        private void DrawMotion()
        {
            var interest = Coordinator?.Interest;
            var motion = interest?.MotionReplica;
            if (remotePlanet == null || pendingSubscription || interest.Stale || !mapImage.enabled || motion == null) return;
            if (motion.Sequence != motionSequence)
            {
                previousMotionTick = latestMotionTick;
                latestMotionTick = motion.LatestTick;
                motionReceivedAt = Time.unscaledTime;
                motionSequence = motion.Sequence;
                foreach (int id in entityMarkers.Keys.Where(id => !motion.Entities.ContainsKey(id)).ToArray())
                { Destroy(entityMarkers[id].gameObject); entityMarkers.Remove(id); }
            }
            double renderTick = previousMotionTick + (Time.unscaledTime - motionReceivedAt) * 5;
            foreach (var pair in motion.Entities)
            {
                WorldInterestEntity entity;
                if (!motion.SampleEntity(pair.Key, renderTick, out entity, index => visibleCells.ContainsKey(index))) continue;
                bool visible = region.Contains(entity.CellIndex, remotePlanet.Width) && visibleCells.ContainsKey(entity.CellIndex);
                RectTransform marker;
                if (!entityMarkers.TryGetValue(pair.Key, out marker))
                {
                    var go = OnlineWidgets.Rect("VisibleEntity", mapRect);
                    marker = (RectTransform)go.transform;
                    bool animal = entity.Kind == 2;
                    marker.sizeDelta = animal ? new Vector2(6, 6) : new Vector2(5, 10);
                    marker.localRotation = Quaternion.Euler(0, 0, animal ? 45 : 0);
                    OnlineWidgets.Surface(go, animal ? new Color(.56f, .9f, .67f) : new Color(.98f, .84f, .42f)).raycastTarget = false;
                    entityMarkers.Add(pair.Key, marker);
                }
                marker.gameObject.SetActive(visible);
                if (!visible) continue;
                var anchor = new Vector2((entity.X - region.X) / region.Width, (entity.Y - region.Y) / region.Height);
                marker.anchorMin = marker.anchorMax = anchor;
                marker.anchoredPosition = Vector2.zero;
            }
        }

        private void StopHover()
        {
            if (remotePlanet == null) return;
            watchingDetails = false; detailsEpoch = null;
            Coordinator?.CellDetails?.Stop();
            details.text = T("world.inspect_hint");
        }

        private void RequestRemoteDetails()
        {
            watchingDetails = true;
            detailsEpoch = null;
            var interest = Coordinator?.Interest;
            if (interest == null || interest.Stale || !visibleCells.ContainsKey(inspectedIndex))
            { Coordinator?.CellDetails?.Stop(); return; }
            if (!string.IsNullOrEmpty(interest.BoundEpoch) &&
                Coordinator.CellDetails.Watch(selectedOwnerId, remotePlanet.PlanetId, interest.BoundEpoch, inspectedIndex))
                detailsEpoch = interest.BoundEpoch;
        }

        private void RefreshRemoteDetails()
        {
            if (!watchingDetails || remotePlanet == null) return;
            bool explored = visibleCells.ContainsKey(inspectedIndex);
            if (!explored || Coordinator.Interest.Stale)
            { details.text = T(explored ? "view.waiting_details" : "world.unknown_cell"); return; }
            if (detailsEpoch != Coordinator.Interest.BoundEpoch) RequestRemoteDetails();
            var result = Coordinator.CellDetails.Current;
            if (Coordinator.CellDetails.Stale || result == null || result.Epoch != detailsEpoch || result.Cell.Index != inspectedIndex)
            { details.text = Format("view.cell_pending", inspectedIndex % remotePlanet.Width, inspectedIndex / remotePlanet.Width); return; }
            WorldInterestCell cell = result.Cell;
            Element element = ElementLoader.FindElementByHash((SimHashes)cell.ElementHash);
            string name = element == null ? cell.ElementHash.ToString(CultureInfo.InvariantCulture) : LinkTags.Replace(element.name, "");
            details.text = Format("world.cell_details", inspectedIndex % remotePlanet.Width, inspectedIndex / remotePlanet.Width, name,
                cell.Mass.ToString("0.##", CultureInfo.CurrentCulture), (cell.Temperature - 273.15f).ToString("0.#", CultureInfo.CurrentCulture));
        }

        private void RebuildWorldList(IReadOnlyList<WorldViewEntry> worlds)
        {
            string signature = selectedOwnerId + ":" + string.Join("|", worlds.Where(w => w?.Descriptor != null)
                .Select(w => w.Descriptor.OwnerId + w.OwnerName + w.Descriptor.Name + w.IsLocal + WorldStatus(w)));
            if (signature == worldListSignature) return;
            worldListSignature = signature;
            for (int i = listBody.childCount - 1; i >= 0; i--)
            {
                var child = listBody.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            foreach (var entry in worlds)
            {
                if (entry?.Descriptor == null) continue;
                string ownerId = entry.Descriptor.OwnerId;
                var button = OnlineWidgets.Button(listBody, "", () => SelectWorld(ownerId), -1);
                OnlineWidgets.Size(button.gameObject, -1, 94);
                OnlineWidgets.Tint(button, ownerId == selectedOwnerId ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
                var label = button.GetComponentInChildren<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.TopLeft;
                label.fontSize = 17;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.rectTransform.offsetMin = new Vector2(10, 7);
                label.rectTransform.offsetMax = new Vector2(-10, -7);
                label.text = (entry.OwnerName ?? "") + (entry.IsLocal ? " · " + T("world.local") : "")
                    + "\n" + (entry.Descriptor.Name ?? "") + "\n" + WorldStatus(entry);
            }
        }

        private string WorldStatus(WorldViewEntry entry)
        {
            if (!entry.IsLocal && !entry.Connected) return T("world.reconnecting");
            var interest = Coordinator?.Interest;
            if (!entry.IsLocal && interest?.OwnerId == entry.Descriptor.OwnerId && interest.PlanetId != null)
                return T(interest.LastError == "world_interest_denied" ? "world.not_shared" :
                    interest.Stale ? "world.waiting" : "world.connected");
            if (!entry.IsLocal && !entry.Descriptor.Shared) return T("world.not_shared");
            return T(entry.Snapshot == null ? "world.waiting" : "world.connected");
        }

        internal void SelectWorld(string ownerId)
        {
            if (selectedOwnerId != ownerId)
            {
                selectedOwnerId = ownerId;
                ClearMap();
            }
            var selected = model.Worlds.FirstOrDefault(item => item?.Descriptor?.OwnerId == ownerId);
            if (selected != null) BeginObservation(selected);
            Refresh();
        }

        private PlanetManifest FindSelectedPlanet()
        {
            return OnlineRuntime.Instance?.Multiplayer?.StarSystem.State?.Members
                .FirstOrDefault(member => member.PeerId == selectedOwnerId)?.Planet;
        }

        private void OpenPlans(bool draftDig)
        {
            if (!ExperimentalWorkTools) return;
            var planet = FindSelectedPlanet();
            var runtime = OnlineRuntime.Instance;
            if (planet == null || runtime == null) return;
            WorkCell? cell = null;
            if (draftDig)
            {
                int width = remotePlanet?.Width ?? displayedSnapshot?.World?.Width ?? 0;
                if (width < 1 || !visibleCells.ContainsKey(inspectedIndex)) return;
                cell = new WorkCell(inspectedIndex % width, inspectedIndex / width);
            }
            string owner = selectedOwnerId;
            Close();
            runtime.ShowWorkPlans(owner, planet.PlanetId, cell);
        }

        private void OpenChat()
        {
            var runtime = OnlineRuntime.Instance;
            if (runtime == null) return;
            // Chat overlays the current authorized view; its subscription stays alive.
            runtime.ShowChat();
        }

        private void DetachTaskEvents()
        {
            if (taskEvents == null) return;
            taskEvents.Changed -= InvalidateTasks;
            taskEvents.SubmissionChanged -= OnTaskSubmission;
            taskEvents = null;
        }
        private void InvalidateTasks() { taskDirty = true; }
        private void OnTaskSubmission(string draftId, RemoteWorkReceipt receipt)
        {
            if (draftId == lastDraftId) taskMessage = ReceiptText(receipt);
            if (receipt.Accepted && !receipt.DryRun) nextTaskPoll = 0;
            taskDirty = true;
        }
        private string ReceiptText(RemoteWorkReceipt receipt)
        {
            if (receipt == null || receipt.Reason == "work_draft") return T("taskview.local_draft");
            if (receipt.Reason == "work_queued" || receipt.Reason == "work_confirming") return T("work.pending");
            if (receipt.Reason == "work_native_validation_required") return T("work.unavailable");
            if (receipt.Reason == "work_not_authorized") return T("taskview.not_authorized");
            if (receipt.Reason == "work_response_unconfirmed" || receipt.Reason == "work_execution_uncertain") return T("work.unconfirmed");
            if (receipt.Reason == "work_not_natural_solid") return T("taskview.solid_only");
            if (receipt.Reason == "work_untracked_native_order" || receipt.Reason == "work_native_construction_conflict") return T("taskview.native_conflict");
            if (receipt.Reason == "work_stale_world" || receipt.Reason == "work_world_unavailable") return T("taskview.stale_selection");
            return receipt.Accepted ? T(receipt.DryRun ? "work.pending" : "work.confirmed") : T("work.rejected");
        }
        private void SetTaskPriority(int value)
        { taskPriority = Mathf.Clamp(value, 1, 9); taskDirty = true; }
        private void SetTaskLayer(int value)
        {
            taskLayer = value;
            if (value == 1)
            {
                var peers = OnlineRuntime.Instance?.Session?.Players.Where(p => p.Id != OnlineRuntime.Instance.Session.LocalPlayerId).ToArray();
                if (peers != null && peers.Length > 0)
                {
                    int previous = Array.FindIndex(peers, p => p.Id == layerActorId);
                    layerActorId = peers[(previous + 1) % peers.Length].Id;
                }
            }
            nextTaskPoll = 0; taskMessage = null; taskDirty = true;
        }
        private void PollTaskLayer()
        {
            var session = OnlineRuntime.Instance?.Session;
            var manifest = FindSelectedPlanet();
            if (session == null || manifest == null) return;
            string actor = taskLayer == 1 ? layerActorId : session.LocalPlayerId;
            if (taskLayer != 1 || actor != null) Coordinator.Work.RequestPlans(selectedOwnerId, manifest.PlanetId, taskLayer == 2, actor);
        }
        private bool TryToolCell(Vector2 localPosition, out WorkCell cell)
        {
            cell = default(WorkCell);
            if (!mapImage.enabled || (remotePlanet != null && (pendingSubscription || Coordinator.Interest.Stale))) return false;
            int width = remotePlanet?.Width ?? displayedSnapshot?.World?.Width ?? 0;
            int height = remotePlanet?.Height ?? displayedSnapshot?.World?.Height ?? 0;
            if (width < 1 || height < 1 || mapRect.rect.width <= 0 || mapRect.rect.height <= 0) return false;
            Rect bounds = mapRect.rect;
            float x = (localPosition.x - bounds.xMin) / bounds.width, y = (localPosition.y - bounds.yMin) / bounds.height;
            if (x < 0 || x >= 1 || y < 0 || y >= 1) return false;
            cell = new WorkCell(region.X + Mathf.FloorToInt(x * region.Width), region.Y + Mathf.FloorToInt(y * region.Height));
            return true;
        }
        private string CurrentTaskEpoch()
        { return remotePlanet != null ? Coordinator?.Interest.BoundEpoch : Coordinator?.Work.Authority.Planet?.AuthorityEpoch; }
        private void SelectTaskCell(Vector2 position)
        {
            WorkCell cell;
            if (!TryToolCell(position, out cell)) return;
            selectionStart = selectionEnd = cell;
            selectionPlanetId = FindSelectedPlanet()?.PlanetId; selectionEpoch = CurrentTaskEpoch();
            taskMessage = null; taskDirty = true;
            InspectCell(position);
        }
        private void DragTaskSelection(Vector2 position)
        {
            WorkCell cell;
            if (!selectionStart.HasValue || !TryToolCell(position, out cell)) return;
            selectionEnd = cell; taskMessage = null; taskDirty = true;
        }
        private void ClearSelection()
        { selectionStart = selectionEnd = null; selectionPlanetId = selectionEpoch = null; taskDirty = true; }
        private WorkCell[] SelectedTaskCells(out string error)
        {
            error = null;
            if (!selectionStart.HasValue || !selectionEnd.HasValue) { error = T("taskview.hint"); return Array.Empty<WorkCell>(); }
            var manifest = FindSelectedPlanet();
            if (manifest == null || selectionPlanetId != manifest.PlanetId || string.IsNullOrEmpty(selectionEpoch) || selectionEpoch != CurrentTaskEpoch() ||
                !mapImage.enabled || (remotePlanet != null && (pendingSubscription || Coordinator.Interest.Stale)))
            { error = T("taskview.stale_selection"); return Array.Empty<WorkCell>(); }
            var a = selectionStart.Value; var b = selectionEnd.Value;
            int left = Math.Min(a.X, b.X), right = Math.Max(a.X, b.X), bottom = Math.Min(a.Y, b.Y), top = Math.Max(a.Y, b.Y);
            long count = (long)(right - left + 1) * (top - bottom + 1);
            if (count > RemoteWorkWire.MaxCells)
            { error = Format("taskview.too_large", RemoteWorkWire.MaxCells); return Array.Empty<WorkCell>(); }
            var result = new List<WorkCell>((int)count);
            for (int y = bottom; y <= top; y++) for (int x = left; x <= right; x++)
            {
                WorldCell visible;
                if (!visibleCells.TryGetValue(y * manifest.Width + x, out visible))
                { error = T("taskview.unexplored"); return Array.Empty<WorkCell>(); }
                Element element = ElementLoader.FindElementByHash((SimHashes)visible.ElementHash);
                if (element == null || !element.IsSolid)
                { error = T("taskview.solid_only"); return Array.Empty<WorkCell>(); }
                result.Add(new WorkCell(x, y));
            }
            return result.ToArray();
        }
        private void SubmitDigSelection()
        {
            if (!ExperimentalWorkTools) return;
            string error;
            var cells = SelectedTaskCells(out error);
            if (error != null || cells.Length == 0) { taskMessage = error; taskDirty = true; return; }
            var work = Coordinator?.Work; var manifest = FindSelectedPlanet();
            if (work == null || manifest == null) return;
            try
            {
                var draft = work.Draft(manifest.PlanetId, RemoteWorkKind.Dig, cells, taskPriority);
                lastDraftId = draft.RequestId;
                // Draft first, then send. The overlay is immediately available even with no grant or connection.
                var grant = selectedOwnerId == OnlineRuntime.Instance.Session.LocalPlayerId ? null : work.GetReceivedGrant(selectedOwnerId, manifest.PlanetId);
                bool currentScope = selectedOwnerId == OnlineRuntime.Instance.Session.LocalPlayerId ||
                    (grant != null && grant.AuthorityEpoch == CurrentTaskEpoch());
                bool sent = currentScope && work.SubmitDraft(selectedOwnerId, draft, true);
                taskMessage = sent ? ReceiptText(work.GetSubmission(draft.RequestId)) : T("taskview.local_draft");
                ClearSelection();
                taskLayer = 0; nextTaskPoll = 0;
                RefreshTaskOverlay();
            }
            catch (InvalidOperationException) { taskMessage = T("taskview.capacity"); taskDirty = true; }
        }
        private void RefreshTaskOverlay()
        {
            taskDirty = false;
            if (taskOverlay == null) return;
            if (!ExperimentalWorkTools) { taskOverlay.Clear(); return; }
            var work = Coordinator?.Work; var manifest = FindSelectedPlanet(); var session = OnlineRuntime.Instance?.Session;
            string error;
            WorkCell[] selected = SelectedTaskCells(out error);
            digPlanButton.interactable = selected.Length > 0;
            lowerPriority.interactable = taskPriority > 1; higherPriority.interactable = taskPriority < 9;
            priorityLabel.text = Format("taskview.priority", taskPriority);
            OnlineWidgets.Tint(mineTasks, taskLayer == 0 ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            OnlineWidgets.Tint(memberTasks, taskLayer == 1 ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            OnlineWidgets.Tint(mergedTasks, taskLayer == 2 ? OnlineWidgets.AccentColor : OnlineWidgets.ButtonColor);
            var actor = session?.Players.FirstOrDefault(p => p.Id == layerActorId);
            memberTasks.GetComponentInChildren<TextMeshProUGUI>().text = taskLayer == 1 && actor != null ? actor.Number + " " + actor.Name : T("work.member");
            memberTasks.interactable = session?.Players.Any(p => p.Id != session.LocalPlayerId) == true;
            taskStatus.text = (taskMessage ?? (selectionStart.HasValue ? error ?? Format("taskview.selected", selected.Length, taskPriority) : T("taskview.hint"))) + "\n" + T("taskview.legend");
            if (work == null || manifest == null || session == null || !mapImage.enabled || (remotePlanet != null && (pendingSubscription || Coordinator.Interest.Stale)))
            { taskOverlay.Clear(); return; }
            int width = manifest.Width;
            var viewport = region;
            string planActor = taskLayer == 1 ? layerActorId : session.LocalPlayerId;
            var marks = new Dictionary<int, WorldTaskMark>();
            var palette = session.Players.ToDictionary(p => p.Id, p => OnlineWidgets.PlayerColor(p.Number));
            var grant = selectedOwnerId == session.LocalPlayerId ? null : work.GetReceivedGrant(selectedOwnerId, manifest.PlanetId);
            bool currentScope = selectedOwnerId == session.LocalPlayerId || (grant != null && grant.AuthorityEpoch == CurrentTaskEpoch());
            if (currentScope && (taskLayer != 1 || planActor != null))
                foreach (var plan in work.GetPlans(selectedOwnerId, manifest.PlanetId, taskLayer == 2, planActor))
                {
                    int index = plan.Cell.Y * width + plan.Cell.X;
                    if (!viewport.Contains(index, width) || !visibleCells.ContainsKey(index)) continue;
                    Color color; if (!palette.TryGetValue(plan.ActorId, out color)) color = OnlineWidgets.MutedColor;
                    marks[index] = new WorldTaskMark { Cell = plan.Cell, Color = color, Phase = WorldTaskPhase.Confirmed };
                }
            var canonicalCells = new HashSet<int>(marks.Keys);
            if (taskLayer == 0)
                foreach (var draft in work.OwnDrafts(manifest.PlanetId))
                {
                    var receipt = work.GetSubmission(draft.RequestId);
                    // Only current authority plans may supply a confirmed marker. An old
                    // successful draft must not resurrect a completed or revoked order.
                    if (receipt?.Accepted == true && !receipt.DryRun) continue;
                    WorldTaskPhase phase = receipt == null || receipt.Reason == "work_draft" ? WorldTaskPhase.Draft :
                        receipt.Reason == "work_queued" || receipt.Reason == "work_confirming" || (receipt.Accepted && receipt.DryRun) ? WorldTaskPhase.Pending :
                        receipt.Reason == "work_response_unconfirmed" || receipt.Reason == "work_execution_uncertain" ? WorldTaskPhase.Unconfirmed : WorldTaskPhase.Rejected;
                    foreach (var cell in draft.Cells)
                    {
                        int index = cell.Y * width + cell.X;
                        if (viewport.Contains(index, width) && visibleCells.ContainsKey(index) &&
                            (phase != WorldTaskPhase.Rejected || !canonicalCells.Contains(index)))
                            marks[index] = new WorldTaskMark { Cell = cell, Color = OnlineWidgets.PlayerColor(session.Players.FirstOrDefault(p => p.Id == session.LocalPlayerId)?.Number ?? 1), Phase = phase };
                    }
                }
            if (selectedOwnerId != session.LocalPlayerId && ((taskLayer == 1 && (grant == null || (grant.Permissions & RemoteWorkPermission.ViewPlans) == 0)) ||
                (taskLayer == 2 && (grant == null || (grant.Permissions & (RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewPlans)) == 0))))
                taskStatus.text = T("taskview.layer_denied");
            if (!currentScope && taskLayer != 0) taskStatus.text = T("taskview.layer_denied");
            if (currentScope && taskLayer == 2 && work.ExecutionError(selectedOwnerId, manifest.PlanetId) != null) taskStatus.text = T("work.result_uncertain");
            taskOverlay.Show(viewport, marks.Values, selectionStart, selectionEnd, selected.Length > 0);
        }

        private void RefreshSelected()
        {
            if (remotePlanet != null) ScheduleSubscription();
            else if (model.Worlds.Any(entry => entry.IsLocal && entry.Descriptor.OwnerId == selectedOwnerId))
                model.RequestWorld(selectedOwnerId);
        }

        private void ShowEmpty(string key)
        {
            ClearMap();
            emptyLabel.text = T(key);
            emptyLabel.gameObject.SetActive(true);
        }

        private void DrawSnapshot(WorldSnapshot snapshot)
        {
            int width = snapshot.World.Width, height = snapshot.World.Height;
            if (width < 1 || height < 1 || width > 1024 || height > 1024)
            { ShowEmpty("world.invalid_snapshot"); return; }
            bool replaced = displayedSnapshot?.World == null || displayedSnapshot.World.Id != snapshot.World.Id ||
                displayedSnapshot.World.OwnerId != snapshot.World.OwnerId || displayedSnapshot.World.Epoch != snapshot.World.Epoch ||
                displayedSnapshot.World.Width != width || displayedSnapshot.World.Height != height;
            displayedSnapshot = snapshot; displayedSequence = snapshot.Sequence;
            if (replaced)
            {
                int centreX = width / 2, centreY = height / 2;
                var dupe = (snapshot.Dupes ?? Array.Empty<WorldDupe>()).FirstOrDefault(d => d != null && d.Index >= 0 && d.Index < (long)width * height);
                if (dupe != null) { centreX = dupe.Index % width; centreY = dupe.Index / width; }
                else
                {
                    var explored = (snapshot.Cells ?? Array.Empty<WorldCell>()).Where(c => c.Index >= 0 && c.Index < (long)width * height).ToArray();
                    if (explored.Length > 0)
                    { centreX = (int)explored.Average(c => c.Index % width); centreY = (int)explored.Average(c => c.Index / width); }
                }
                region = new WorldInterestRegion { Width = Math.Min(64, width), Height = Math.Min(64, height) };
                region.X = Mathf.Clamp(centreX - region.Width / 2, 0, width - region.Width);
                region.Y = Mathf.Clamp(centreY - region.Height / 2, 0, height - region.Height);
                ClearSelection(); inspectedIndex = -1; panRemainder = Vector2.zero;
            }
            DrawCachedSnapshot();
        }

        private void DrawCachedSnapshot()
        {
            var snapshot = displayedSnapshot;
            if (snapshot?.World == null || region.Width < 1 || region.Height < 1) return;
            int width = snapshot.World.Width;
            int previousCell = inspectedIndex;
            visibleCells.Clear();
            EnsureTexture(region.Width, region.Height);
            if (pixels == null || pixels.Length != region.Width * region.Height) pixels = new Color32[region.Width * region.Height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = UnknownColor;
            foreach (var cell in snapshot.Cells ?? Array.Empty<WorldCell>())
            {
                if (!region.Contains(cell.Index, width)) continue;
                int pixel = (cell.Index / width - region.Y) * region.Width + cell.Index % width - region.X;
                visibleCells[cell.Index] = cell;
                uint rgba = cell.Color;
                pixels[pixel] = new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), 255);
            }
            texture.SetPixels32(pixels); texture.Apply(false, false);
            mapImage.texture = texture; mapImage.enabled = true;
            mapAspect.aspectRatio = (float)region.Width / region.Height;
            emptyLabel.gameObject.SetActive(false); taskDirty = true;
            viewportLabel.text = Format("view.region", region.X, region.Y, region.Width, region.Height);
            foreach (var marker in dupeMarkers) if (marker != null) Destroy(marker);
            dupeMarkers.Clear();
            foreach (var dupe in snapshot.Dupes ?? Array.Empty<WorldDupe>())
            {
                if (dupe == null || !visibleCells.ContainsKey(dupe.Index)) continue;
                var marker = OnlineWidgets.Rect("VisibleDuplicant", mapRect);
                var markerRect = (RectTransform)marker.transform;
                var anchor = new Vector2((dupe.Index % width - region.X + .5f) / region.Width,
                    (dupe.Index / width - region.Y + .5f) / region.Height);
                markerRect.anchorMin = markerRect.anchorMax = anchor;
                markerRect.sizeDelta = new Vector2(5, 5);
                OnlineWidgets.Surface(marker, new Color(.98f, .84f, .42f), true).raycastTarget = false;
                dupeMarkers.Add(marker);
            }
            if (previousCell >= 0 && visibleCells.ContainsKey(previousCell)) ShowCellDetails(previousCell);
            else details.text = T("world.inspect_hint");
        }

        private void InspectCell(Vector2 localPosition)
        {
            WorkCell cell;
            if (TryToolCell(localPosition, out cell)) ShowCellDetails(cell.Y * MapWorldWidth + cell.X);
        }

        private void ShowCellDetails(int index)
        {
            bool changed = inspectedIndex != index;
            inspectedIndex = index;
            if (remotePlanet != null)
            {
                if (changed || !watchingDetails) RequestRemoteDetails();
                RefreshRemoteDetails();
                return;
            }
            taskDirty = true;
            WorldCell cell;
            if (!visibleCells.TryGetValue(index, out cell))
            {
                details.text = T("world.unknown_cell");
                return;
            }
            int width = displayedSnapshot.World.Width;
            Element element = ElementLoader.FindElementByHash((SimHashes)cell.ElementHash);
            string name = element == null ? cell.ElementHash.ToString(CultureInfo.InvariantCulture) : LinkTags.Replace(element.name, "");
            details.text = Format("world.cell_details", index % width, index / width, name,
                cell.Mass.ToString("0.##", CultureInfo.CurrentCulture),
                (cell.Temperature - 273.15f).ToString("0.#", CultureInfo.CurrentCulture));
            var names = (displayedSnapshot.Dupes ?? Array.Empty<WorldDupe>())
                .Where(dupe => dupe != null && dupe.Index == index)
                .Select(dupe => dupe.Name ?? "").ToArray();
            if (names.Length > 0) details.text += "\n" + Format("world.dupes", string.Join(", ", names));
        }

        private void ClearMap(bool releaseTexture = true)
        {
            if (mapImage != null)
            {
                mapImage.texture = null;
                mapImage.enabled = false;
            }
            if (releaseTexture)
            {
                if (texture != null) Destroy(texture);
                texture = null;
            }
            displayedSnapshot = null;
            displayedSequence = -1;
            inspectedIndex = -1;
            ClearSelection();
            if (taskOverlay != null) taskOverlay.Clear();
            taskDirty = true;
            if (digPlanButton != null) digPlanButton.interactable = false;
            visibleCells.Clear();
            foreach (var marker in dupeMarkers)
                if (marker != null) { marker.SetActive(false); Destroy(marker); }
            dupeMarkers.Clear();
            ClearEntityMarkers();
            renderedReplica = null;
            visualSequence = -1;
            pixels = null;
            if (details != null) details.text = T("world.inspect_hint");
            if (viewportLabel != null) viewportLabel.text = "";
        }

        private void Close()
        {
            if (closing) return;
            closing = true;
            DetachTaskEvents();
            StopObservation();
            model.CloseWorldView();
        }

        protected override void OnCleanUp()
        {
            DetachTaskEvents();
            StopObservation();
            ClearMap();
            base.OnCleanUp();
        }
    }

    internal sealed class WorldMapPointer : MonoBehaviour, IPointerClickHandler, IPointerMoveHandler,
        IPointerExitHandler, IScrollHandler, IDragHandler, IPointerDownHandler, IBeginDragHandler
    {
        internal Action<Vector2> Click, Hover, Pan, SelectionStart, SelectionMove;
        private bool leftDragged;
        internal Action<float> Zoom;
        internal System.Action Exit;
        private static bool CanInteract
        { get { return !OnlineInputControls.EditingText && OnlineRuntime.Instance?.ChatOpen != true; } }
        private bool TryPosition(PointerEventData data, out Vector2 position)
        {
            if (!CanInteract) { position = Vector2.zero; return false; }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,
                data.position, data.pressEventCamera, out position);
        }
        public void OnPointerClick(PointerEventData data)
        {
            Vector2 position;
            if (data.button == PointerEventData.InputButton.Left && !leftDragged && TryPosition(data, out position)) Click?.Invoke(position);
        }
        public void OnPointerDown(PointerEventData data)
        {
            leftDragged = false;
            Vector2 position;
            if (data.button == PointerEventData.InputButton.Left && TryPosition(data, out position)) SelectionStart?.Invoke(position);
        }
        public void OnBeginDrag(PointerEventData data)
        { if (CanInteract && data.button == PointerEventData.InputButton.Left) leftDragged = true; }
        public void OnPointerMove(PointerEventData data)
        {
            Vector2 position;
            if (!data.dragging && TryPosition(data, out position)) Hover?.Invoke(position);
        }
        public void OnPointerExit(PointerEventData data) { Exit?.Invoke(); }
        public void OnScroll(PointerEventData data) { if (CanInteract) Zoom?.Invoke(data.scrollDelta.y); }
        public void OnDrag(PointerEventData data)
        {
            if (!CanInteract) return;
            if (data.button == PointerEventData.InputButton.Left)
            {
                Vector2 position;
                if (TryPosition(data, out position)) SelectionMove?.Invoke(position);
            }
            else Pan?.Invoke(data.delta);
        }
    }
}
