using System;
using HarmonyLib;
using OniOnline.Runtime;
using OniOnline.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace OniOnline.Patches
{
    [HarmonyPatch(typeof(MainMenu), "OnPrefabInit")]
    internal static class MainMenuOnlinePatch
    {
        private static void Postfix(MainMenu __instance)
        {
            try
            {
                var original = AccessTools.Field(typeof(MainMenu), "Button_NewGame").GetValue(__instance) as KButton;
                if (original == null || original.transform.parent.Find("OniOnlineBrowseLobbies") != null) return;
                var clone = UnityEngine.Object.Instantiate(original.gameObject, original.transform.parent, false);
                clone.name = "OniOnlineBrowseLobbies";
                clone.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
                var button = clone.GetComponent<KButton>();
                button.ClearOnClick(); button.ClearOnPointerEvents();
                button.onClick += () => OnlineRuntime.Instance?.BrowseSystems();
                foreach (var label in clone.GetComponentsInChildren<LocText>(true))
                    label.SetText(OnlineText.Get("menu.browse_lobbies"));
            }
            catch (Exception error) { Debug.LogWarning("[OniOnline] Main menu integration failed: " + error.GetType().Name); }
        }
    }

    [HarmonyPatch(typeof(ClusterCategorySelectionScreen), "OnSpawn")]
    internal static class OnlineCategoryPatch
    {
        private static void Postfix(ClusterCategorySelectionScreen __instance)
        {
            try
            {
                var runtime = OnlineRuntime.Instance;
                var source = __instance.spacedOutStyle?.button;
                if (runtime == null || source == null || source.transform.parent.Find("OniOnlineCategory") != null) return;
                // Native cards contain batched animation/material state that must not
                // be cloned after spawn. This entry is an independent static UI button.
                var sourceRect = source.GetComponent<RectTransform>();
                float width = sourceRect.rect.width > 0 ? sourceRect.rect.width : 142;
                float height = sourceRect.rect.height > 0 ? sourceRect.rect.height : 300;
                var card = OnlineWidgets.Rect("OniOnlineCategory", source.transform.parent);
                var rect = (RectTransform)card.transform;
                rect.sizeDelta = new Vector2(width, height);
                OnlineWidgets.Size(card, width, height);
                var layout = card.GetComponent<LayoutElement>();
                layout.preferredHeight = layout.minHeight = height;
                layout.minWidth = width;
                var frame = card.AddComponent<Image>();
                frame.color = Color.white;
                var button = card.AddComponent<Button>();
                button.targetGraphic = frame;
                var colors = button.colors;
                colors.normalColor = new Color(0.08f, 0.09f, 0.14f);
                colors.highlightedColor = new Color(0.86f, 0.78f, 0.65f);
                colors.selectedColor = colors.highlightedColor;
                colors.pressedColor = new Color(0.61f, 0.76f, 0.72f);
                colors.disabledColor = new Color(0.22f, 0.23f, 0.28f);
                colors.fadeDuration = 0.12f;
                button.colors = colors;
                button.onClick.AddListener(() => runtime.SelectOnline(__instance));
                BuildArtwork(card.transform, height);
                if (source.transform.parent.GetComponent<LayoutGroup>() == null)
                {
                    rect.anchorMin = sourceRect.anchorMin; rect.anchorMax = sourceRect.anchorMax; rect.pivot = sourceRect.pivot;
                    rect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(width + 8, 0);
                }
                var description = AccessTools.Field(typeof(ClusterCategorySelectionScreen), "descriptionArea").GetValue(__instance) as LocText;
                var events = card.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(data => { if (description != null) description.SetText(OnlineText.Get("ui.phase1_notice")); });
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(data => { if (description != null) description.SetText(""); });
                events.triggers.Add(enter); events.triggers.Add(exit);
                var panel = AccessTools.Field(typeof(ClusterCategorySelectionScreen), "panel").GetValue(__instance) as RectTransform;
                if (panel != null) panel.sizeDelta += new Vector2(width + 8, 0);
            }
            catch (Exception error) { Debug.LogWarning("[OniOnline] Online category integration failed: " + error.GetType().Name); }
        }

        private static void BuildArtwork(Transform card, float height)
        {
            float titleHeight = Mathf.Clamp(height * 0.085f, 20, 28);
            var title = OnlineWidgets.Rect("Title", card);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0, 1); titleRect.anchorMax = Vector2.one;
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.offsetMin = new Vector2(2, -titleHeight);
            titleRect.offsetMax = new Vector2(-2, -2);
            var titleImage = title.AddComponent<Image>();
            titleImage.color = new Color(0.29f, 0.31f, 0.38f);
            titleImage.raycastTarget = false;
            var label = OnlineWidgets.Label(title.transform, OnlineText.Get("mode.online"), 14);
            label.color = new Color(0.94f, 0.92f, 0.88f);
            label.alignment = TMPro.TextAlignmentOptions.Center;
            OnlineWidgets.Stretch(label.rectTransform);

            var viewport = OnlineWidgets.Rect("ArtworkViewport", card);
            var viewportRect = (RectTransform)viewport.transform;
            OnlineWidgets.Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(2, 2);
            viewportRect.offsetMax = new Vector2(-2, -titleHeight - 1);
            var backdrop = viewport.AddComponent<Image>();
            backdrop.color = new Color(0.10f, 0.12f, 0.20f);
            backdrop.raycastTarget = false;
            viewport.AddComponent<RectMask2D>();
            var sprite = OnlineArtwork.Load();
            if (sprite == null) return;
            var artwork = OnlineWidgets.Rect("Artwork", viewport.transform);
            var artworkRect = (RectTransform)artwork.transform;
            OnlineWidgets.Stretch(artworkRect);
            var image = artwork.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var aspect = artwork.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = sprite.rect.width / sprite.rect.height;
        }
    }
}
