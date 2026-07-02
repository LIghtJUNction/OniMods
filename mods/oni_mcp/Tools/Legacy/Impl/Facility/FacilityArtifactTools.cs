using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using STRINGS;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        public static McpTool ListArtifacts()
        {
            return new McpTool
            {
                Name = "artifacts_list",
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "analyzed_artifacts_list", "artifact_analysis_list" },
                Tags = new List<string> { "story", "artifact", "analysis", "space" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=artifact action=list",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 artifact id、名称、类型或分析站筛选", Required = false },
                    ["includeStations"] = new McpToolParameter { Type = "boolean", Description = "是否包含分析站状态，默认 true", Required = false },
                    ["includeWorldArtifacts"] = new McpToolParameter { Type = "boolean", Description = "是否包含场上 artifact 实例，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回 artifact 数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var analyzed = AnalyzedArtifactInfos()
                        .Where(info => MatchesQuery(info, query))
                        .Take(limit)
                        .ToList();
                    var payload = new Dictionary<string, object>
                    {
                        ["analyzedCount"] = ArtifactSelector.Instance?.AnalyzedArtifactCount ?? 0,
                        ["analyzedSpaceCount"] = ArtifactSelector.Instance?.AnalyzedSpaceArtifactCount ?? 0,
                        ["analyzedArtifacts"] = analyzed
                    };
                    if (ToolUtil.GetBool(args, "includeWorldArtifacts", true))
                        payload["worldArtifacts"] = WorldArtifactInfos(args).Where(info => MatchesQuery(info, query)).Take(limit).ToList();
                    if (ToolUtil.GetBool(args, "includeStations", true))
                        payload["stations"] = BuildingTargets(args, go => go.GetSMI<ArtifactAnalysisStation.StatesInstance>() != null, go => ArtifactStationInfo(go.GetSMI<ArtifactAnalysisStation.StatesInstance>()))
                            .Where(info => MatchesQuery(info, query))
                            .Take(limit)
                            .ToList();
                    return JsonResult(payload);
                }
            };
        }

        public static McpTool OpenArtifactReveal()
        {
            return new McpTool
            {
                Name = "artifact_reveal_open",
                Group = "story",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "artifact_lore_open", "artifact_analysis_reveal_open" },
                Tags = new List<string> { "story", "artifact", "analysis", "popup" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=artifact action=open",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["artifactId"] = new McpToolParameter { Type = "string", Description = "已分析 artifact prefab id，例如 artifact_officemug", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认打开游戏内弹窗", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to open an artifact reveal popup");
                    string artifactId = args["artifactId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(artifactId))
                        return CallToolResult.Error("artifactId is required");
                    if (ArtifactSelector.Instance == null || !ArtifactSelector.Instance.GetAnalyzedArtifactIDs().Contains(artifactId))
                        return CallToolResult.Error("artifactId has not been analyzed in this save");
                    var prefab = Assets.GetPrefab(artifactId);
                    if (prefab == null)
                        return CallToolResult.Error("artifactId does not resolve to an artifact prefab");
                    var before = ArtifactInfo(artifactId);
                    var evt = GameplayEventManager.Instance.StartNewEvent(Db.Get().GameplayEvents.ArtifactReveal);
                    var statesInstance = evt.smi as SimpleEvent.StatesInstance;
                    if (statesInstance == null)
                        return CallToolResult.Error("ArtifactReveal event did not return a SimpleEvent instance");
                    statesInstance.artifact = prefab;
                    string text = prefab.PrefabID().Name.ToUpperInvariant().Replace("ARTIFACT_", "");
                    string key = "STRINGS.UI.SPACEARTIFACTS." + text + ".ARTIFACT";
                    string desc = $"<b>{prefab.GetProperName()}</b>";
                    if (Strings.TryGet(key, out var result) && result != null && !result.String.IsNullOrWhiteSpace())
                        desc = desc + "\n\n" + result.String;
                    if (!desc.IsNullOrWhiteSpace())
                        statesInstance.SetTextParameter("desc", desc);
                    statesInstance.ShowEventPopup();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["artifact"] = before,
                        ["opened"] = true
                    });
                }
            };
        }


        private static List<Dictionary<string, object>> AnalyzedArtifactInfos()
        {
            if (ArtifactSelector.Instance == null)
                return new List<Dictionary<string, object>>();
            return ArtifactSelector.Instance.GetAnalyzedArtifactIDs()
                .Select(ArtifactInfo)
                .Where(info => info != null)
                .OrderBy(info => info["name"].ToString())
                .ToList();
        }

        private static Dictionary<string, object> ArtifactInfo(string artifactId)
        {
            var prefab = Assets.GetPrefab(artifactId);
            if (prefab == null)
            {
                return new Dictionary<string, object>
                {
                    ["artifactId"] = artifactId,
                    ["name"] = artifactId,
                    ["prefabAvailable"] = false
                };
            }
            var artifact = prefab.GetComponent<SpaceArtifact>();
            return new Dictionary<string, object>
            {
                ["artifactId"] = artifactId,
                ["name"] = ToolUtil.CleanName(prefab.GetProperName()),
                ["prefabAvailable"] = true,
                ["type"] = artifact == null ? "unknown" : artifact.artifactType.ToString(),
                ["tier"] = artifact == null || artifact.artifactTier == null ? null : artifact.artifactTier.name_key.ToString(),
                ["payloadDropChance"] = artifact == null || artifact.artifactTier == null ? (object)null : Math.Round(ToolUtil.SafeFloat(artifact.artifactTier.payloadDropChance), 4)
            };
        }

        private static IEnumerable<Dictionary<string, object>> WorldArtifactInfos(JObject args)
        {
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            foreach (var artifact in Components.SpaceArtifacts.Items)
            {
                if (artifact == null || !MatchesTarget(artifact.gameObject, rect, worldId))
                    continue;
                var info = TargetInfo(artifact.gameObject);
                info["artifactType"] = artifact.artifactType.ToString();
                info["charmed"] = artifact.gameObject.HasTag(GameTags.CharmedArtifact);
                info["analyzed"] = ArtifactSelector.Instance != null && ArtifactSelector.Instance.GetAnalyzedArtifactIDs().Contains(info["prefabId"].ToString());
                info["payloadDropChance"] = artifact.artifactTier == null ? (object)null : Math.Round(ToolUtil.SafeFloat(artifact.artifactTier.payloadDropChance), 4);
                yield return info;
            }
        }

        private static Dictionary<string, object> ArtifactStationInfo(ArtifactAnalysisStation.StatesInstance station)
        {
            var result = TargetInfo(station.gameObject);
            result["operational"] = station.GetComponent<Operational>()?.IsOperational ?? false;
            result["storedCharmedArtifactKg"] = Math.Round(ToolUtil.SafeFloat(station.storage.GetMassAvailable(GameTags.CharmedArtifact)), 3);
            result["storedItems"] = station.storage.GetItems().Select(StorageItemInfo).ToList();
            return result;
        }

        private static Dictionary<string, object> StorageItemInfo(GameObject item)
        {
            var kpid = item?.GetComponent<KPrefabID>();
            var primary = item?.GetComponent<PrimaryElement>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? item?.GetInstanceID() ?? -1,
                ["prefabId"] = kpid?.PrefabTag.Name ?? item?.name,
                ["name"] = item == null ? null : ToolUtil.CleanName(item.GetProperName()),
                ["massKg"] = Math.Round(ToolUtil.SafeFloat(primary?.Mass ?? 0f), 3),
                ["charmedArtifact"] = item != null && item.HasTag(GameTags.CharmedArtifact)
            };
        }
    }
}
