using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Network;
using OniOnline.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // Binds multiplayer state to the game's own grid, camera and hex selection.
    internal sealed class SystemHexMap : MonoBehaviour
    {
        private ClusterMapScreen screen;
        private OnlineRuntime runtime;
        private AxialI origin;
        private bool homeView = true, hasOrigin, remoteApplied, restoringNative;
        private string signature;
        private double nextRefresh;
        private Transform markerLayer;
        private ClusterMapHex[] hexes;
        private ClusterMapHex remoteSelection, savedNativeSelection;
        private ClusterGridEntity savedNativeEntity;
        private readonly Dictionary<string, GameObject> markers = new Dictionary<string, GameObject>();
        private readonly Dictionary<GameObject, bool> nativeLayers = new Dictionary<GameObject, bool>();
        internal System.Action<int, int> Selected;
        internal bool Remote { get { return !homeView && !IsLocalSystem(runtime); } }
        internal bool HomeView { get { return homeView; } }
        internal bool RemoteInteractionActive
        { get { return StarMapSelectionPolicy.RemoteInteractionActive(Remote, screen != null && screen.IsScreenActive(),
            screen != null && screen.GetMode() == ClusterMapScreen.Mode.Default); } }
        internal int SelectedNativeWorldId { get; private set; } = -1;

        internal static SystemHexMap Attach(ClusterMapScreen screen, OnlineRuntime runtime)
        {
            var map = screen.GetComponent<SystemHexMap>();
            if (map == null) map = screen.gameObject.AddComponent<SystemHexMap>();
            map.screen = screen; map.runtime = runtime;
            map.hexes = screen.cellVisContainer.GetComponentsInChildren<ClusterMapHex>(true);
            if (map.markerLayer == null)
            {
                var layer = new GameObject("OniOnlineNativeMapMarkers", typeof(RectTransform));
                map.markerLayer = layer.transform;
                map.markerLayer.SetParent(screen.terrainVisContainer.transform.parent, false);
                map.markerLayer.localPosition = screen.terrainVisContainer.transform.localPosition;
                map.markerLayer.localScale = screen.terrainVisContainer.transform.localScale;
            }
            return map;
        }

        internal static bool IsLocalSystem(OnlineRuntime runtime)
        {
            var colony = OnlineColonyState.Current;
            var state = runtime?.Multiplayer?.StarSystem.State;
            return colony != null && ClusterGrid.Instance != null && ClusterMapScreen.Instance != null &&
                (state == null ? runtime?.Session == null || runtime.Session.IsHost : state.SystemId == colony.HomeSystemId);
        }

        internal void SetHomeView(bool value)
        {
            homeView = value;
            signature = null; remoteSelection = null; SelectedNativeWorldId = -1;
            SetState(runtime.Multiplayer?.StarSystem.State, runtime.Session?.Players ?? new PlayerInfo[0], runtime.Session?.LocalPlayerId);
        }

        private void Update()
        {
            if (screen == null || runtime == null || !screen.IsScreenActive() || Time.realtimeSinceStartupAsDouble < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartupAsDouble + 0.5;
            SetState(runtime.Multiplayer?.StarSystem.State, runtime.Session?.Players ?? new PlayerInfo[0], runtime.Session?.LocalPlayerId);
        }

        internal void SetState(StarSystemState state, PlayerInfo[] roster, string localPeerId = null)
        {
            if (screen == null) return;
            bool remote = Remote;
            if (remote)
            {
                origin = new AxialI(0, 0); hasOrigin = true;
                ApplyRemoteVisuals();
            }
            else
            {
                RestoreNativeVisuals();
                if (markerLayer != null) markerLayer.gameObject.SetActive(screen.IsScreenActive() && screen.GetMode() == ClusterMapScreen.Mode.Default);
                int worldId = runtime.SelectedPlanet?.NativeWorldId ?? -1;
                var world = worldId < 0 ? null : ClusterManager.Instance?.GetWorld(worldId);
                var entity = world != null && world.IsDiscovered && !world.IsModuleInterior ? world.GetComponent<AsteroidGridEntity>() : null;
                hasOrigin = entity != null;
                if (hasOrigin) origin = entity.Location;
            }
            var catalog = remote && runtime.StarMapDirectory?.Stale == false ? runtime.StarMapDirectory.Entries : new StarMapEntry[0];
            string next = remote + ":" + hasOrigin + ":" + origin.Q + ":" + origin.R + ":" + state?.RoomId + ":" + state?.Revision + ":" +
                string.Join("|", roster.Select(p => p.Id + ":" + p.Number + ":" + p.Name + ":" + p.Connected)) + ":" +
                string.Join("|", catalog.Select(e => e.Q + ":" + e.R + ":" + e.Name + ":" + e.Discovered));
            if (next == signature) return;
            signature = next; ClearMarkers();
            if (!hasOrigin || state == null || !remote && !IsLocalSystem(runtime)) return;
            foreach (var entry in catalog)
                AddMarker("native_" + entry.Q + "_" + entry.R, entry.Q, entry.R,
                    entry.Discovered ? entry.Name : "?", entry.Discovered ? Color.white : new Color(.6f, .6f, .6f));
            var players = roster.ToDictionary(p => p.Id);
            foreach (var member in state.Members.Where(m => remote || m.PeerId != localPeerId))
            {
                PlayerInfo player; players.TryGetValue(member.PeerId, out player);
                AddMarker(member.PeerId, member.Q, member.R, (player?.Number ?? 0) + " · " +
                    (member.Planet?.Name ?? OnlineText.Get("entry.position_reserved")),
                    member.Connected ? OnlineWidgets.PlayerColor(player?.Number ?? 0) : OnlineWidgets.MutedColor);
            }
        }

        private void AddMarker(string key, int q, int r, string name, Color color)
        {
            AxialI location;
            if (!TryLocation(q, r, out location)) return;
            var marker = OnlineWidgets.Rect("OniOnlineVisitor_" + key, markerLayer);
            var rect = (RectTransform)marker.transform;
            rect.localPosition = location.ToWorld(); rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(1.3f, 1.3f);
            var icon = marker.AddComponent<Image>();
            icon.sprite = Def.GetUISpriteFromMultiObjectAnim(Assets.GetAnim(AsteroidGridEntity.DEFAULT_ASTEROID_ICON_ANIM));
            icon.preserveAspect = true; icon.raycastTarget = false;
            icon.color = color;
            var label = OnlineWidgets.Label(marker.transform, name, 18);
            label.fontSize = .19f; label.richText = false; label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center; label.color = color;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, 0);
            label.rectTransform.pivot = new Vector2(.5f, 1);
            label.rectTransform.sizeDelta = new Vector2(2.8f, .48f);
            label.rectTransform.anchoredPosition = new Vector2(0, -.06f);
            markers.Add(key, marker);
        }

        internal void ApplyRemoteVisuals()
        {
            if (restoringNative) return;
            if (!RemoteInteractionActive)
            { SuspendRemoteView(); return; }
            if (markerLayer != null) markerLayer.gameObject.SetActive(true);
            if (!remoteApplied)
            {
                savedNativeSelection = (ClusterMapHex)AccessTools.Field(typeof(ClusterMapScreen), "m_selectedHex").GetValue(screen);
                savedNativeEntity = (ClusterGridEntity)AccessTools.Field(typeof(ClusterMapScreen), "m_selectedEntity").GetValue(screen);
                if (ClusterMapSelectTool.Instance != null)
                    AccessTools.Method(typeof(ClusterMapScreen), "SetSelectedEntity").Invoke(screen, new object[] { null, false });
            }
            foreach (var layer in new[] { screen.terrainVisContainer, screen.mobileVisContainer, screen.telescopeVisContainer,
                screen.POIVisContainer, screen.DebriVisContainer, screen.FXVisContainer })
            {
                if (layer == null) continue;
                if (!nativeLayers.ContainsKey(layer)) nativeLayers.Add(layer, layer.activeSelf);
                layer.SetActive(false);
            }
            var selectionMarker = (SelectMarker)AccessTools.Field(typeof(ClusterMapScreen), "m_selectMarker").GetValue(screen);
            if (selectionMarker != null)
            {
                if (!nativeLayers.ContainsKey(selectionMarker.gameObject)) nativeLayers.Add(selectionMarker.gameObject, selectionMarker.gameObject.activeSelf);
                selectionMarker.gameObject.SetActive(false);
            }
            remoteApplied = true;
            ApplyRemoteHexStates();
        }

        internal void ApplyRemoteHexStates()
        {
            if (!RemoteInteractionActive) return;
            var state = runtime.Multiplayer?.StarSystem.State;
            var known = new HashSet<AxialI>();
            if (state != null) foreach (var member in state.Members) known.Add(new AxialI(member.R, member.Q));
            if (runtime.StarMapDirectory?.Stale == false)
                foreach (var entry in runtime.StarMapDirectory.Entries) known.Add(new AxialI(entry.R, entry.Q));
            foreach (var hex in hexes)
            {
                hex.SetRevealed(known.Contains(hex.location) ? ClusterRevealLevel.Visible : ClusterRevealLevel.Hidden);
                hex.UpdateToggleState(hex == remoteSelection ? ClusterMapHex.ToggleState.Selected : ClusterMapHex.ToggleState.Unselected);
            }
        }

        internal void SuspendRemoteView()
        {
            RestoreNativeVisuals();
            if (markerLayer != null) markerLayer.gameObject.SetActive(false);
        }

        private void RestoreNativeVisuals()
        {
            if (!remoteApplied) return;
            remoteApplied = false;
            restoringNative = true;
            try
            {
                foreach (var layer in nativeLayers) if (layer.Key != null) layer.Key.SetActive(layer.Value);
                nativeLayers.Clear();
                if (ClusterGrid.Instance != null && screen != null)
                {
                    foreach (var hex in hexes) if (hex != null) hex.SetRevealed(ClusterGrid.Instance.GetCellRevealLevel(hex.location));
                    // A newly entered native destination mode already owns the rocket's
                    // selection. Restoring an older asteroid would change its path origin.
                    bool defaultMode = screen.GetMode() == ClusterMapScreen.Mode.Default;
                    if (defaultMode) AccessTools.Field(typeof(ClusterMapScreen), "m_selectedHex").SetValue(screen, savedNativeSelection);
                    if (StarMapSelectionPolicy.RestoreNativeSelection(screen.IsScreenActive(), defaultMode) && ClusterMapSelectTool.Instance != null)
                        AccessTools.Method(typeof(ClusterMapScreen), "SetSelectedEntity").Invoke(screen, new object[] { savedNativeEntity, false });
                    if (screen.IsScreenActive())
                        AccessTools.Method(typeof(ClusterMapScreen), "UpdateVis").Invoke(screen, new object[] { false });
                }
                savedNativeSelection = null; savedNativeEntity = null;
            }
            finally { restoringNative = false; }
        }

        private bool TryLocation(int q, int r, out AxialI location)
        {
            location = new AxialI(origin.R + r, origin.Q + q);
            return hasOrigin && ClusterGrid.Instance != null && ClusterGrid.Instance.IsValidCell(location);
        }

        internal void Select(int q, int r)
        {
            AxialI location;
            if (!TryLocation(q, r, out location)) return;
            var hex = screen.GetClusterMapHexAtLocation(location);
            if (hex != null) screen.SelectHex(hex);
        }
        internal void Focus(int q, int r)
        {
            AxialI location;
            if (!TryLocation(q, r, out location)) return;
            screen.SetTargetFocusPosition(location, 0); Select(q, r);
        }
        internal void ResetView() { Focus(0, 0); }

        internal void OnNativeSelect(ClusterMapHex hex)
        {
            if (!hasOrigin || !screen.IsScreenActive() || screen.GetMode() != ClusterMapScreen.Mode.Default) return;
            if (Remote)
            {
                remoteSelection = hex;
                // Remote markers never select a native object from the local colony underneath.
                ClusterMapSelectTool.Instance.Select(null);
                AccessTools.Field(typeof(ClusterMapScreen), "m_selectedHex").SetValue(screen, hex);
                ApplyRemoteVisuals();
                SelectedNativeWorldId = -1;
            }
            else
            {
                var world = ClusterGrid.Instance.GetVisibleEntitiesAtCell(hex.location).OfType<AsteroidGridEntity>().FirstOrDefault();
                SelectedNativeWorldId = world == null ? -1 : world.GetComponent<WorldContainer>()?.id ?? -1;
            }
            Selected?.Invoke(hex.location.Q - origin.Q, hex.location.R - origin.R);
        }

        private void ClearMarkers()
        {
            foreach (var marker in markers.Values) if (marker != null) { marker.SetActive(false); Destroy(marker); }
            markers.Clear();
        }
        private void OnDestroy()
        {
            homeView = true;
            RestoreNativeVisuals(); ClearMarkers();
            if (markerLayer != null) Destroy(markerLayer.gameObject);
        }
    }

    [HarmonyPatch(typeof(ClusterMapScreen), nameof(ClusterMapScreen.SelectHex))]
    internal static class NativeSystemHexSelectionPatch
    {
        private static bool Prefix(ClusterMapScreen __instance, ClusterMapHex newSelectionHex)
        {
            var map = __instance.GetComponent<SystemHexMap>();
            if (newSelectionHex == null || map?.RemoteInteractionActive != true) return true;
            map.OnNativeSelect(newSelectionHex); return false;
        }
        private static void Postfix(ClusterMapScreen __instance, ClusterMapHex newSelectionHex)
        {
            var map = __instance.GetComponent<SystemHexMap>();
            if (newSelectionHex != null && map?.Remote == false) map.OnNativeSelect(newSelectionHex);
        }
    }

    [HarmonyPatch(typeof(ClusterMapScreen), "UpdateVis")]
    internal static class NativeRemoteSystemVisualsPatch
    {
        private static void Postfix(ClusterMapScreen __instance) { __instance.GetComponent<SystemHexMap>()?.ApplyRemoteVisuals(); }
    }
    internal static class NativeRemoteMapInteraction
    {
        internal static SystemHexMap ActiveMap
        {
            get
            {
                var map = ClusterMapScreen.Instance?.GetComponent<SystemHexMap>();
                return map?.RemoteInteractionActive == true ? map : null;
            }
        }
    }

    [HarmonyPatch(typeof(ClusterMapSelectTool), "UpdateHoveredSelectables")]
    internal static class NativeRemoteHoverObjectsPatch
    {
        private static readonly System.Reflection.FieldInfo Hovered =
            AccessTools.Field(typeof(ClusterMapSelectTool), "m_hoveredSelectables");
        internal static int HoveredCount
        {
            get
            {
                var tool = ClusterMapSelectTool.Instance;
                var list = tool == null ? null : Hovered?.GetValue(tool) as List<KSelectable>;
                return list?.Count ?? -1;
            }
        }
        private static bool Prefix(ClusterMapSelectTool __instance)
        {
            if (NativeRemoteMapInteraction.ActiveMap == null) return true;
            var hovered = Hovered.GetValue(__instance) as List<KSelectable>;
            if (hovered == null) throw new InvalidOperationException("OniOnline: native map hover list is unavailable.");
            hovered.Clear();
            // LateUpdate still clears any previous highlight and updates native hover UI.
            return false;
        }
    }

    [HarmonyPatch(typeof(ClusterMapScreen), "UpdateHexToggleStates")]
    internal static class NativeRemoteHexStatesPatch
    {
        private static bool Prefix(ClusterMapScreen __instance)
        {
            var map = __instance.GetComponent<SystemHexMap>();
            if (map?.RemoteInteractionActive != true) return true;
            // OnHoverHex/OnUnhoverHex keep updating their original location state.
            map.ApplyRemoteHexStates();
            return false;
        }
    }

    [HarmonyPatch(typeof(ClusterMapHex), "OnHover")]
    internal static class NativeRemoteHexTooltipPatch
    {
        private static readonly System.Reflection.MethodInfo HoverColors =
            AccessTools.Method(typeof(ClusterMapHex), "UpdateHoverColors");
        private static bool Prefix(ClusterMapHex __instance)
        {
            if (NativeRemoteMapInteraction.ActiveMap == null) return true;
            var tooltip = __instance.GetComponent<ToolTip>();
            if (tooltip != null)
            {
                tooltip.ClearMultiStringTooltip();
                tooltip.AddMultiStringTooltip(OnlineText.Get("native.room_chart"), __instance.informationTooltipStyle);
            }
            HoverColors.Invoke(__instance, new object[] { true });
            ClusterMapScreen.Instance.OnHoverHex(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(ClusterMapHex), "TrySelect")]
    internal static class NativeRemoteHexClickPatch
    {
        private static bool Prefix(ClusterMapHex __instance)
        {
            if (NativeRemoteMapInteraction.ActiveMap == null) return true;
            ClusterMapScreen.Instance.SelectHex(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(ClusterMapHex), "TryGoTo")]
    internal static class NativeRemoteHexWorldSwitchPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (NativeRemoteMapInteraction.ActiveMap == null) return true;
            __result = false;
            return false;
        }
    }

}
