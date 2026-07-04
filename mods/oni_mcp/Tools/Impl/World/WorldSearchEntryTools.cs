using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldSearchTools
    {
        public static McpTool SearchWorld()
        {
            return new McpTool
            {
                Name = "world_search",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "map_search", "world_find", "find_on_map" },
                Tags = new List<string> { "world", "map", "search", "find", "elements", "buildings", "items", "dupes", "地图", "搜索", "查找" },
                Description = "兼容旧工具：请改用 read_control domain=world action=search。按自然语言/query 在同一区域内搜索格子元素、建筑、物品和复制人；支持 nearX/nearY 最近排序。",
                Parameters = CommonSearchParameters(includeKinds: true),
                Handler = args =>
                {
                    if (Game.Instance == null)
                    return CallToolResult.Error("Game not initialized");

                var request = SearchRequest.From(args, "clusters");
                if (request.HasPattern)
                    return CallToolResult.Text(JsonConvert.SerializeObject(BuildPatternResult(request), McpJsonUtil.Settings));

                var results = new List<SearchHit>();
                    if (request.IncludeKind("cells") || request.IncludeKind("elements"))
                        results.AddRange(SearchCells(request));
                    if (request.IncludeKind("buildings"))
                        results.AddRange(SearchBuildings(request));
                    if (request.IncludeKind("items") || request.IncludeKind("resources"))
                        results.AddRange(SearchItems(request));
                    if (request.IncludeKind("dupes") || request.IncludeKind("duplicants"))
                        results.AddRange(SearchDupes(request));

                    return CallToolResult.Text(JsonConvert.SerializeObject(BuildResult("world_search", request, results), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SearchCells()
        {
            return new McpTool
            {
                Name = "world_search_cells",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "map_search_cells", "world_find_cells", "element_cells_search" },
                Tags = new List<string> { "world", "map", "search", "cells", "elements", "temperature", "mass", "地图", "格子", "元素" },
                Description = "兼容入口：请优先使用 world_search kinds=cells/elements。",
                Parameters = CellSearchParameters(),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var request = SearchRequest.From(args, "hits");
                    return CallToolResult.Text(JsonConvert.SerializeObject(BuildResult("world_search_cells", request, SearchCells(request)), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SearchObjects()
        {
            return new McpTool
            {
                Name = "world_search_objects",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "map_search_objects", "world_find_objects", "object_search" },
                Tags = new List<string> { "world", "map", "search", "buildings", "items", "dupes", "objects", "地图", "对象", "建筑", "物品" },
                Description = "兼容入口：请优先使用 world_search kinds=buildings/items/dupes。",
                Parameters = ObjectSearchParameters(),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var request = SearchRequest.From(args, "hits");
                    var results = new List<SearchHit>();
                    if (request.IncludeKind("buildings"))
                        results.AddRange(SearchBuildings(request));
                    if (request.IncludeKind("items") || request.IncludeKind("resources"))
                        results.AddRange(SearchItems(request));
                    if (request.IncludeKind("dupes") || request.IncludeKind("duplicants"))
                        results.AddRange(SearchDupes(request));

                    return CallToolResult.Text(JsonConvert.SerializeObject(BuildResult("world_search_objects", request, results), McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> CommonSearchParameters(bool includeKinds)
        {
            var parameters = BaseAreaParameters();
            parameters["query"] = new McpToolParameter { Type = "string", Description = "搜索词，可匹配元素 ID/名称、建筑名/prefabId、物品名/prefabId、复制人名；留空时按条件返回", Required = false };
            parameters["pattern"] = new McpToolParameter { Type = "string", Description = "连续格子排列搜索，例如 粉砂岩-泥土-氧气、SiltStone>Dirt>Oxygen、砂岩|粉砂岩-* -氧气、Dirt{3}；优先于普通 query", Required = false };
            parameters["sequence"] = new McpToolParameter { Type = "string", Description = "pattern 的别名：连续元素/状态排列；支持 * 任意格、A|B 备选、term{N} 重复", Required = false };
            parameters["direction"] = new McpToolParameter { Type = "string", Description = "pattern 搜索方向：horizontal、vertical 或 both，默认 both；会同时匹配正反方向", Required = false, EnumValues = new List<string> { "horizontal", "vertical", "both" } };
            parameters["matchMode"] = new McpToolParameter { Type = "string", Description = "pattern/query 匹配模式：exact 精确；smart 规范化/包含；fuzzy 允许少量拼写误差。默认 smart", Required = false, EnumValues = new List<string> { "exact", "smart", "fuzzy" } };
            if (includeKinds)
                parameters["kinds"] = new McpToolParameter { Type = "array", Description = "搜索类型数组或逗号字符串：cells,elements,buildings,items,resources,dupes；默认 all", Required = false };
            parameters["returnMode"] = new McpToolParameter { Type = "string", Description = "返回形态：clusters 返回连通区域/对象组，summary 只返回统计，hits 返回逐项命中。默认 clusters", Required = false, EnumValues = new List<string> { "clusters", "summary", "hits" } };
            AddCellFilters(parameters);
            AddSortFilters(parameters);
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> CellSearchParameters()
        {
            var parameters = BaseAreaParameters();
            parameters["query"] = new McpToolParameter { Type = "string", Description = "元素 ID/名称搜索词，例如 Water/Oxygen/Cuprite；留空时按条件返回", Required = false };
            parameters["returnMode"] = new McpToolParameter { Type = "string", Description = "兼容入口默认 hits；可传 clusters 或 summary 获取压缩视图", Required = false, EnumValues = new List<string> { "hits", "clusters", "summary" } };
            AddCellFilters(parameters);
            AddSortFilters(parameters);
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> ObjectSearchParameters()
        {
            var parameters = BaseAreaParameters();
            parameters["query"] = new McpToolParameter { Type = "string", Description = "对象搜索词，可匹配建筑名/prefabId、物品名/prefabId/元素、复制人名", Required = false };
            parameters["kinds"] = new McpToolParameter { Type = "array", Description = "对象类型数组或逗号字符串：buildings,items,resources,dupes；默认 buildings,items,dupes", Required = false };
            parameters["returnMode"] = new McpToolParameter { Type = "string", Description = "兼容入口默认 hits；可传 clusters 或 summary 获取压缩视图", Required = false, EnumValues = new List<string> { "hits", "clusters", "summary" } };
            AddSortFilters(parameters);
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> BaseAreaParameters()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；留空时默认当前相机附近", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；留空时默认当前相机附近", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；留空时默认当前相机附近", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；留空时默认当前相机附近", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界；传 -1 搜全部世界（对象搜索可用）", Required = false },
                ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只搜索已揭示格子，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回结果，默认 50，最大 300", Required = false },
                ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最大格子扫描量，默认 2500，最大 20000", Required = false }
            };
        }

        private static void AddCellFilters(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["state"] = new McpToolParameter { Type = "string", Description = "元素状态过滤：any/gas/liquid/solid/vacuum", Required = false, EnumValues = new List<string> { "any", "gas", "liquid", "solid", "vacuum" } };
            parameters["solid"] = new McpToolParameter { Type = "boolean", Description = "是否只返回固体/非固体格；不传则不过滤", Required = false };
            parameters["minMassKg"] = new McpToolParameter { Type = "number", Description = "最低质量 kg", Required = false };
            parameters["maxMassKg"] = new McpToolParameter { Type = "number", Description = "最高质量 kg", Required = false };
            parameters["minTempC"] = new McpToolParameter { Type = "number", Description = "最低温度 C", Required = false };
            parameters["maxTempC"] = new McpToolParameter { Type = "number", Description = "最高温度 C", Required = false };
        }

        private static void AddSortFilters(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["nearX"] = new McpToolParameter { Type = "integer", Description = "按距该 X 最近排序；需同时提供 nearY", Required = false };
            parameters["nearY"] = new McpToolParameter { Type = "integer", Description = "按距该 Y 最近排序；需同时提供 nearX", Required = false };
            parameters["sort"] = new McpToolParameter { Type = "string", Description = "排序：nearest、mass、temperature、kind；默认 nearest（有 nearX/nearY）否则 kind", Required = false, EnumValues = new List<string> { "nearest", "mass", "temperature", "kind" } };
        }
    }
}
