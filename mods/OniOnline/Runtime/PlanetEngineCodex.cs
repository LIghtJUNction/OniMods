using System;
using System.Collections.Generic;
using HarmonyLib;

namespace OniOnline.Runtime
{
    internal static class PlanetEngineCodex
    {
        internal const string EntryId = "ONIONLINEPLANETARYENGINE";
        private const string CategoryId = "STORYTRAITS";

        internal static CodexEntry CreateEntry()
        {
            string name = OnlineText.Get("engine.title");
            var containers = new List<ContentContainer>();
            CodexEntryGenerator.GenerateTitleContainers(name, containers);
            containers.Add(new ContentContainer(new List<ICodexWidget>
            {
                new CodexText(OnlineText.Get("engine.story"), CodexTextStyle.Body),
                new CodexSpacer(),
                new CodexText(OnlineText.Get("entry.engine_manual"), CodexTextStyle.Subtitle),
                new CodexDividerLine(),
                new CodexText(OnlineText.Get("engine.preview"), CodexTextStyle.Body),
                new CodexSpacer(),
                new CodexText(OnlineText.Get("engine.chart_help"), CodexTextStyle.Body)
            }, ContentContainer.ContentLayout.Vertical));
            return new CodexEntry(CategoryId, containers, name)
            {
                id = EntryId,
                parentId = CategoryId,
                sortString = name,
                iconAssetName = "codexIconStoryTraits"
            };
        }

        internal static void Open()
        {
            ManagementMenu.Instance?.OpenCodexToEntry(EntryId);
        }
    }

    [HarmonyPatch(typeof(CodexCache), nameof(CodexCache.CollectEntries))]
    internal static class PlanetEngineCodexEntryPatch
    {
        private static void Postfix(string folder, List<CodexEntry> __result)
        {
            if (!string.Equals(folder, "StoryTraits", StringComparison.OrdinalIgnoreCase)) return;
            // Join the native collection before it builds category links and search.
            __result.Add(PlanetEngineCodex.CreateEntry());
        }
    }
}
