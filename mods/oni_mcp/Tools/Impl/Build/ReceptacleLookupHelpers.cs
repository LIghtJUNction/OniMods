using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ReceptacleTools
    {
        private static Dictionary<string, object> ReceptacleInfo(SingleEntityReceptacle receptacle, bool includeOptions)
        {
            var result = TargetInfo(receptacle.gameObject);
            result["direction"] = receptacle.Direction.ToString();
            result["requestedEntity"] = receptacle.requestedEntityTag.IsValid ? receptacle.requestedEntityTag.Name : null;
            result["requestedAdditionalTag"] = receptacle.requestedEntityAdditionalFilterTag.IsValid ? receptacle.requestedEntityAdditionalFilterTag.Name : null;
            result["hasActiveRequest"] = receptacle.GetActiveRequest != null;
            result["activeRequestTags"] = receptacle.GetActiveRequest == null ? new List<string>() : receptacle.GetActiveRequest.tags.Select(tag => tag.Name).OrderBy(name => name).ToList();
            result["acceptedCategoryTags"] = receptacle.possibleDepositObjectTags.Select(tag => tag.Name).OrderBy(name => name).ToList();
            result["occupant"] = receptacle.Occupant == null ? null : TargetInfo(receptacle.Occupant);
            var uprootable = receptacle.Occupant == null ? null : receptacle.Occupant.GetComponent<Uprootable>();
            result["occupantMarkedForRemoval"] = uprootable != null && uprootable.IsMarkedForUproot;
            if (includeOptions)
                result["options"] = ReceptacleOptions(receptacle).Select(item => item.ToDictionary()).ToList();
            return result;
        }

        private static Dictionary<string, object> StorageTileInfo(StorageTile.Instance tile, bool includeOptions)
        {
            var result = TargetInfo(tile.gameObject);
            result["targetTag"] = tile.TargetTag == StorageTile.INVALID_TAG ? null : tile.TargetTag.Name;
            result["hasContents"] = tile.HasContents;
            result["hasAnyDesiredContents"] = tile.HasAnyDesiredContents;
            result["amountStoredKg"] = Math.Round(ToolUtil.SafeFloat(tile.AmountStored), 3);
            result["amountOfDesiredContentStoredKg"] = Math.Round(ToolUtil.SafeFloat(tile.AmountOfDesiredContentStored), 3);
            result["userMaxCapacityKg"] = Math.Round(ToolUtil.SafeFloat(tile.UserMaxCapacity), 3);
            result["isPendingChange"] = tile.IsPendingChange;
            var storage = tile.gameObject.GetComponent<Storage>();
            result["filterCategoryTags"] = storage == null ? new List<string>() : storage.storageFilters.Select(tag => tag.Name).OrderBy(name => name).ToList();
            if (includeOptions)
                result["options"] = StorageTileOptions(tile).Select(item => item.ToDictionary()).ToList();
            return result;
        }

        private static List<EntityOption> ReceptacleOptions(SingleEntityReceptacle receptacle)
        {
            var options = new Dictionary<Tag, EntityOption>();
            foreach (var category in receptacle.possibleDepositObjectTags)
            {
                foreach (var prefab in Assets.GetPrefabsWithTag(category))
                {
                    if (prefab == null || options.ContainsKey(prefab.PrefabID()) || !receptacle.IsValidEntity(prefab))
                        continue;
                    options[prefab.PrefabID()] = EntityOption.FromPrefab(prefab, category, AvailableAmount(receptacle.gameObject, prefab.PrefabID()));
                }
            }
            return options.Values.OrderBy(item => item.Name).ToList();
        }

        private static List<EntityOption> StorageTileOptions(StorageTile.Instance tile)
        {
            var storage = tile.gameObject.GetComponent<Storage>();
            if (storage == null)
                return new List<EntityOption>();

            var tags = new HashSet<Tag>();
            foreach (var filter in storage.storageFilters)
            {
                var discovered = DiscoveredResources.Instance?.GetDiscoveredResourcesFromTag(filter);
                if (discovered == null || discovered.Count == 0)
                    continue;
                foreach (var tag in discovered)
                    tags.Add(tag);
            }

            return tags
                .Select(tag => new EntityOption
                {
                    Tag = tag,
                    Name = tag.ProperNameStripLink(),
                    CategoryTag = null,
                    AvailableAmount = AvailableAmount(tile.gameObject, tag)
                })
                .OrderBy(item => item.Name)
                .ToList();
        }

        private static bool IsValidReceptacleOption(SingleEntityReceptacle receptacle, GameObject prefab)
        {
            if (prefab == null || !receptacle.IsValidEntity(prefab))
                return false;
            var kpid = prefab.GetComponent<KPrefabID>();
            return receptacle.possibleDepositObjectTags.Any(tag => kpid != null && kpid.HasTag(tag));
        }

        private static Tag AdditionalTagForPrefab(GameObject prefab)
        {
            var mutant = prefab.GetComponent<MutantPlant>();
            return mutant == null ? Tag.Invalid : mutant.SubSpeciesID;
        }

        private static float AvailableAmount(GameObject target, Tag tag)
        {
            var world = target.GetMyWorld();
            if (world == null || !tag.IsValid)
                return 0f;
            return ToolUtil.SafeFloat(world.worldInventory.GetTotalAmount(tag, includeRelatedWorlds: true));
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var building = go.GetComponent<Building>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            if (rect == null)
                return true;
            int cell = Grid.PosToCell(go);
            return Grid.IsValidCell(cell)
                && ToolUtil.CellMatchesWorld(cell, worldId)
                && Grid.CellColumn(cell) >= rect["x1"]
                && Grid.CellColumn(cell) <= rect["x2"]
                && Grid.CellRow(cell) >= rect["y1"]
                && Grid.CellRow(cell) <= rect["y2"];
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                || JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄；与 x1/y1/x2/y2 二选一", Required = false };
            parameters["x1"] = new McpToolParameter { Type = "integer", Description = "矩形左下/左上 X", Required = false };
            parameters["y1"] = new McpToolParameter { Type = "integer", Description = "矩形左下/左上 Y", Required = false };
            parameters["x2"] = new McpToolParameter { Type = "integer", Description = "矩形右上/右下 X", Required = false };
            parameters["y2"] = new McpToolParameter { Type = "integer", Description = "矩形右上/右下 Y", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；省略时不限世界", Required = false };
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["id"] = new McpToolParameter { Type = "integer", Description = "目标 KPrefabID.InstanceID；推荐", Required = false };
            parameters["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；未传 id 时使用", Required = false };
            parameters["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；未传 id 时使用", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时建议提供", Required = false };
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> ReceptacleControlParams()
        {
            return LookupParams(RectParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list、request、cancel_request、remove_occupant、cancel_remove 或 batch", Required = false, EnumValues = new List<string> { "list", "request", "cancel_request", "remove_occupant", "cancel_remove", "batch" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、请求对象或当前 occupant 筛选", Required = false },
                ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可请求实体选项，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["entityTag"] = new McpToolParameter { Type = "string", Description = "action=request 时请求的实体 prefab/tag", Required = false },
                ["additionalTag"] = new McpToolParameter { Type = "string", Description = "可选附加过滤 tag，例如突变植物 SubSpeciesID", Required = false },
                ["replaceExistingRequest"] = new McpToolParameter { Type = "boolean", Description = "action=request 时若已有请求，是否先取消旧请求，默认 true", Required = false },
                ["items"] = new McpToolParameter { Type = "array", Description = "action=batch 时数组；每项支持 id 或 x/y/worldId，并提供 action、entityTag/additionalTag；短字段 a/tag/w", Required = false },
                ["defaults"] = new McpToolParameter { Type = "object", Description = "action=batch 时合并到每项的默认参数；支持 action/a、entityTag/tag、worldId/w", Required = false },
                ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写操作必须为 true，确认创建/取消/移除实体请求", Required = false }
            }));
        }

        private static CallToolResult JsonResult(object payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }

        private class EntityOption
        {
            public Tag Tag { get; set; }
            public string Name { get; set; }
            public string CategoryTag { get; set; }
            public float AvailableAmount { get; set; }

            public static EntityOption FromPrefab(GameObject prefab, Tag category, float availableAmount)
            {
                return new EntityOption
                {
                    Tag = prefab.PrefabID(),
                    Name = ToolUtil.CleanName(prefab.GetProperName()),
                    CategoryTag = category.Name,
                    AvailableAmount = availableAmount
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["tag"] = Tag.Name,
                    ["name"] = Name,
                    ["categoryTag"] = CategoryTag,
                    ["availableAmount"] = Math.Round(ToolUtil.SafeFloat(AvailableAmount), 3)
                };
            }
        }
    }
}
