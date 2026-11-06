using System;
using System.Collections.Generic;
using HarmonyLib;
using OniOnline.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    // Clone authored assets, never a live animation controller. The native
    // ButtonConfig keeps this entry's frame, typography, hover and sound behavior.
    internal sealed class NativeOnlineCategory : MonoBehaviour
    {
        private ClusterCategorySelectionScreen screen;
        private RectTransform panel, canvas;
        private Vector3 nativeScale;
        private Vector2 previousCanvas;

        internal static void Attach(ClusterCategorySelectionScreen screen, OnlineRuntime runtime)
        {
            if (screen == null || runtime == null) return;
            var activeSource = DlcManager.IsExpansion1Active() ? screen.spacedOutStyle : screen.vanillaStyle;
            if (activeSource?.button == null) throw Unavailable();
            var parent = activeSource.button.transform.parent;
            if (parent == null || parent.Find("OniOnlineCategory") != null) return;
            var prefab = FindPrefab(screen);
            var source = DlcManager.IsExpansion1Active() ? prefab.spacedOutStyle : prefab.vanillaStyle;
            RequireUninitialized(prefab, source);
            var panel = AccessTools.Field(typeof(ClusterCategorySelectionScreen), "panel")?.GetValue(screen) as RectTransform;
            var description = AccessTools.Field(typeof(ClusterCategorySelectionScreen), "descriptionArea")?.GetValue(screen) as LocText;
            var nativeCanvas = screen.GetComponentInParent<Canvas>();
            var rootCanvas = nativeCanvas == null ? null : nativeCanvas.rootCanvas;
            if (panel == null || description == null || rootCanvas == null) throw Unavailable();

            var staging = new GameObject("OniOnlineCategoryStaging");
            staging.SetActive(false);
            GameObject card = null;
            try
            {
                // force_active=false alone does not prevent an active prefab's
                // Awake. Its inactive parent is the lifecycle boundary.
                card = UnityEngine.Object.Instantiate(source.button.gameObject, staging.transform, false);
                card.SetActive(false);
                card.name = "OniOnlineCategory";
                var button = card.GetComponent<MultiToggle>();
                if (button == null || button.IsInitialized() || button.isSpawned) throw Unavailable();
                var config = new ClusterCategorySelectionScreen.ButtonConfig {
                    button = button,
                    headerLabel = Map(source.headerLabel, source.button.transform, card.transform),
                    kanim = Map(source.kanim, source.button.transform, card.transform)
                };
                if (config.kanim == null || config.kanim.HasBatchInstanceData) throw Unavailable();
                button.onClick = button.onEnter = button.onExit = button.onHold = button.onStopHold = null;
                button.onDoubleClick = null;
                config.headerLabel.key = "";
                config.Init(description, OnlineText.Get("mode.online_description"), OnlineText.Get("mode.online"));
                button.onClick = () => {
                    if (screen != null && screen.IsActive() && screen.IsScreenActive() &&
                        ReferenceEquals(OnlineRuntime.Instance, runtime)) runtime.SelectOnline(screen);
                };

                card.transform.SetParent(parent, false);
                card.transform.SetAsLastSibling();
                ExpandNativeLayout(parent, activeSource.button.GetComponent<RectTransform>(), card.GetComponent<RectTransform>(), panel);
                var fit = card.AddComponent<NativeOnlineCategory>();
                fit.screen = screen; fit.panel = panel;
                fit.canvas = rootCanvas.transform as RectTransform;
                fit.nativeScale = panel.localScale;
                card.SetActive(true);
                if (parent is RectTransform row) LayoutRebuilder.ForceRebuildLayoutImmediate(row);
                fit.Fit();
            }
            catch
            {
                if (card != null) { card.SetActive(false); UnityEngine.Object.Destroy(card); }
                throw;
            }
            finally { UnityEngine.Object.Destroy(staging); }
        }

        private static ClusterCategorySelectionScreen FindPrefab(ClusterCategorySelectionScreen current)
        {
            var flow = MainMenu.Instance == null ? null : MainMenu.Instance.GetComponent<NewGameFlow>();
            if (flow?.newGameFlowScreens != null)
                foreach (var entry in flow.newGameFlowScreens)
                    if (entry is ClusterCategorySelectionScreen candidate && candidate != current &&
                        !candidate.IsInitialized() && !candidate.isSpawned && !candidate.gameObject.scene.IsValid())
                        return candidate;
            throw Unavailable();
        }

        private static void RequireUninitialized(ClusterCategorySelectionScreen prefab,
            ClusterCategorySelectionScreen.ButtonConfig source)
        {
            if (prefab == null || prefab.IsInitialized() || prefab.isSpawned || source?.button == null ||
                source.headerLabel == null || source.kanim == null || source.button.IsInitialized() ||
                source.button.isSpawned || source.kanim.HasBatchInstanceData) throw Unavailable();
            foreach (var animation in source.button.GetComponentsInChildren<KAnimControllerBase>(true))
                if (animation.HasBatchInstanceData || animation.CurrentAnim != null) throw Unavailable();
        }

        private static T Map<T>(T original, Transform sourceRoot, Transform copyRoot) where T : Component
        {
            if (original == null) throw Unavailable();
            var indices = new Stack<int>();
            var node = original.transform;
            while (node != sourceRoot)
            {
                if (node == null) throw Unavailable();
                indices.Push(node.GetSiblingIndex());
                node = node.parent;
            }
            node = copyRoot;
            while (indices.Count != 0)
            {
                int index = indices.Pop();
                if (index >= node.childCount) throw Unavailable();
                node = node.GetChild(index);
            }
            var components = original.GetComponents<T>();
            int slot = Array.IndexOf(components, original);
            var copies = node.GetComponents<T>();
            if (slot < 0 || slot >= copies.Length) throw Unavailable();
            return copies[slot];
        }

        private static void ExpandNativeLayout(Transform parent, RectTransform source, RectTransform card, RectTransform panel)
        {
            if (source == null || card == null) throw Unavailable();
            float width = source.rect.width;
            if (width <= 0) width = LayoutUtility.GetPreferredWidth(source);
            if (width <= 0) throw Unavailable();
            var horizontal = parent.GetComponent<HorizontalLayoutGroup>();
            float spacing = horizontal == null ? 0 : horizontal.spacing;
            var grid = parent.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                int count = 0;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if ((child == card || child.gameObject.activeSelf) && child.GetComponent<MultiToggle>() != null) count++;
                }
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = count;
                spacing = grid.spacing.x;
            }
            if (parent.GetComponent<LayoutGroup>() == null)
            {
                // Preserve authored anchors and sizes when the native version
                // positions its cards directly rather than using a layout group.
                var cards = new List<RectTransform>();
                for (int i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if ((child == card || child.gameObject.activeSelf) && child.GetComponent<MultiToggle>() != null)
                        cards.Add((RectTransform)child);
                }
                if (cards.Count > 2)
                {
                    float distance = float.MaxValue;
                    for (int i = 1; i < cards.Count - 1; i++)
                        distance = Mathf.Min(distance, Mathf.Abs(cards[i].anchoredPosition.x - cards[i - 1].anchoredPosition.x));
                    if (distance != float.MaxValue && distance > width) spacing = distance - width;
                }
                float start = -(cards.Count - 1) * (width + spacing) * 0.5f;
                for (int i = 0; i < cards.Count; i++)
                {
                    cards[i].anchorMin = source.anchorMin; cards[i].anchorMax = source.anchorMax; cards[i].pivot = source.pivot;
                    cards[i].anchoredPosition = new Vector2(start + i * (width + spacing), source.anchoredPosition.y);
                }
            }
            panel.sizeDelta += new Vector2(width + Mathf.Max(0, spacing), 0);
        }

        private void LateUpdate()
        {
            if (screen == null || !screen.IsActive() || !screen.IsScreenActive() || canvas == null) return;
            var size = canvas.rect.size;
            if (size != previousCanvas) Fit();
        }

        private void Fit()
        {
            if (panel == null || canvas == null || panel.rect.width <= 0 || panel.rect.height <= 0) return;
            previousCanvas = canvas.rect.size;
            float width = panel.rect.width * Mathf.Abs(nativeScale.x);
            float height = panel.rect.height * Mathf.Abs(nativeScale.y);
            float scale = FitScale(width, height, previousCanvas.x - 32, previousCanvas.y - 32);
            panel.localScale = nativeScale * scale;
        }

        internal static float FitScale(float width, float height, float availableWidth, float availableHeight)
        {
            if (width <= 0 || height <= 0 || availableWidth <= 0 || availableHeight <= 0 ||
                float.IsNaN(width + height + availableWidth + availableHeight) ||
                float.IsInfinity(width + height + availableWidth + availableHeight)) return 1;
            return Mathf.Min(1, Mathf.Min(availableWidth / width, availableHeight / height));
        }

        private static InvalidOperationException Unavailable()
        { return new InvalidOperationException("native_online_category_unavailable"); }
    }
}
