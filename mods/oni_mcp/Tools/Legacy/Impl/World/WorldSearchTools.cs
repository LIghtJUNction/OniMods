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
    public static class WorldSearchTools
    {
        private const int MaxSearchCells = 20000;

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

        private static IEnumerable<SearchHit> SearchCells(SearchRequest request)
        {
            var hits = new List<SearchHit>();
            int scanned = 0;
            foreach (int cell in request.Cells())
            {
                if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                    continue;
                if (request.VisibleOnly && !Grid.IsVisible(cell))
                    continue;
                scanned++;

                var element = Grid.Element[cell];
                string elementId = element?.id.ToString() ?? "Unknown";
                string elementName = ToolUtil.CleanName(element?.name ?? elementId);
                string state = ToolUtil.GetElementState(element);
                bool solid = Grid.Solid[cell];
                float mass = ToolUtil.SafeFloat(Grid.Mass[cell]);
                float tempC = ToolUtil.SafeFloat(Grid.Temperature[cell]) - 273.15f;

                if (!request.MatchesQuery(elementId, elementName, state))
                    continue;
                if (!request.MatchesState(state))
                    continue;
                if (request.Solid.HasValue && request.Solid.Value != solid)
                    continue;
                if (request.MinMassKg.HasValue && mass < request.MinMassKg.Value)
                    continue;
                if (request.MaxMassKg.HasValue && mass > request.MaxMassKg.Value)
                    continue;
                if (request.MinTempC.HasValue && tempC < request.MinTempC.Value)
                    continue;
                if (request.MaxTempC.HasValue && tempC > request.MaxTempC.Value)
                    continue;

                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                hits.Add(new SearchHit
                {
                    Kind = "cell",
                    Id = cell.ToString(),
                    Name = elementName,
                    PrefabId = elementId,
                    ElementId = elementId,
                    X = x,
                    Y = y,
                    WorldId = Grid.WorldIdx[cell],
                    MassKg = mass,
                    TemperatureC = tempC,
                    State = state,
                    Solid = solid,
                    Visible = Grid.IsVisible(cell),
                    Scanned = scanned
                });
            }
            return hits;
        }

        private static Dictionary<string, object> BuildPatternResult(SearchRequest request)
        {
            var terms = ParsePatternTerms(request.Pattern).ToList();
            var matches = new List<Dictionary<string, object>>();
            int scannedStarts = 0;

            if (terms.Count == 0)
            {
                return new Dictionary<string, object>
                {
                    ["v"] = 1,
                    ["tool"] = "world_search",
                    ["returnMode"] = "pattern",
                    ["pattern"] = request.Pattern,
                    ["error"] = "pattern/sequence did not contain any terms"
                };
            }

            var directions = PatternDirections(request.PatternDirection).ToList();
            for (int y = request.Rect["y1"]; y <= request.Rect["y2"]; y++)
            {
                for (int x = request.Rect["x1"]; x <= request.Rect["x2"]; x++)
                {
                    foreach (var direction in directions)
                    {
                        scannedStarts++;
                        Dictionary<string, object> match;
                        if (TryMatchPatternAt(request, terms, x, y, direction.dx, direction.dy, out match))
                        {
                            matches.Add(match);
                            if (matches.Count >= request.Limit)
                                return PatternResultDictionary(request, terms, scannedStarts, matches, truncated: true);
                        }
                    }
                }
            }

            return PatternResultDictionary(request, terms, scannedStarts, matches, truncated: false);
        }

        private static Dictionary<string, object> PatternResultDictionary(SearchRequest request, List<string> terms, int scannedStarts, List<Dictionary<string, object>> matches, bool truncated)
        {
            return new Dictionary<string, object>
            {
                ["v"] = 1,
                ["tool"] = "world_search",
                ["returnMode"] = "pattern",
                ["pattern"] = request.Pattern,
                ["terms"] = terms,
                ["direction"] = request.PatternDirection,
                ["matchMode"] = request.MatchMode,
                ["worldId"] = request.WorldId,
                ["rect"] = new[] { request.Rect["x1"], request.Rect["y1"], request.Rect["x2"], request.Rect["y2"] },
                ["visibleOnly"] = request.VisibleOnly,
                ["scannedStarts"] = scannedStarts,
                ["matched"] = matches.Count,
                ["returned"] = matches.Count,
                ["truncated"] = truncated,
                ["items"] = matches,
                ["syntax"] = "separators: -, >, ->, comma, slash, whitespace; term: element/state/id/name; wildcard: * or ?; alternatives: A|B; repeat: term{N}; matchMode: exact/smart/fuzzy; direction scans forward and reverse."
            };
        }

        private static bool TryMatchPatternAt(SearchRequest request, List<string> terms, int startX, int startY, int dx, int dy, out Dictionary<string, object> match)
        {
            match = null;
            var cells = new List<Dictionary<string, object>>();
            for (int i = 0; i < terms.Count; i++)
            {
                int x = startX + dx * i;
                int y = startY + dy * i;
                if (x < request.Rect["x1"] || x > request.Rect["x2"] || y < request.Rect["y1"] || y > request.Rect["y2"])
                    return false;
                int cell = Grid.XYToCell(x, y);
                if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                    return false;
                if (request.WorldId >= 0 && Grid.WorldIdx[cell] != request.WorldId)
                    return false;
                if (request.VisibleOnly && !Grid.IsVisible(cell))
                    return false;

                var element = Grid.Element[cell];
                string elementId = element?.id.ToString() ?? "Unknown";
                string elementName = ToolUtil.CleanName(element?.name ?? elementId);
                string state = ToolUtil.GetElementState(element);
                if (!PatternTermMatches(terms[i], request.MatchMode, elementId, elementName, state))
                    return false;

                cells.Add(new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["cell"] = cell,
                    ["elementId"] = elementId,
                    ["elementName"] = elementName,
                    ["state"] = state,
                    ["kg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3)
                });
            }

            int endX = startX + dx * (terms.Count - 1);
            int endY = startY + dy * (terms.Count - 1);
            var rect = new Dictionary<string, int>
            {
                ["x1"] = Math.Min(startX, endX),
                ["y1"] = Math.Min(startY, endY),
                ["x2"] = Math.Max(startX, endX),
                ["y2"] = Math.Max(startY, endY)
            };
            int worldId = Grid.WorldIdx[Grid.XYToCell(startX, startY)];
            var area = AreaHandleRegistry.Define(rect, worldId, "pattern:" + request.Pattern);
            match = new Dictionary<string, object>
            {
                ["areaId"] = area.Id,
                ["worldId"] = worldId,
                ["direction"] = PatternDirectionName(dx, dy),
                ["start"] = new Dictionary<string, int> { ["x"] = startX, ["y"] = startY },
                ["end"] = new Dictionary<string, int> { ["x"] = endX, ["y"] = endY },
                ["rect"] = rect,
                ["length"] = terms.Count,
                ["anchor"] = new Dictionary<string, int> { ["x"] = startX, ["y"] = startY },
                ["cells"] = cells
            };
            return true;
        }

        private static IEnumerable<string> ParsePatternTerms(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                yield break;

            string normalized = pattern
                .Replace("->", "-")
                .Replace(">", "-")
                .Replace("→", "-")
                .Replace("，", ",")
                .Replace("/", "-");

            foreach (string raw in normalized.Split(new[] { '-', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string term = raw.Trim();
                if (string.IsNullOrWhiteSpace(term))
                    continue;

                int repeat = ParseRepeatSuffix(ref term);
                for (int i = 0; i < repeat; i++)
                    yield return term;
            }
        }

        private static int ParseRepeatSuffix(ref string term)
        {
            int open = term.EndsWith("}", StringComparison.Ordinal) ? term.LastIndexOf('{') : -1;
            if (open < 0 || open >= term.Length - 2)
                return 1;

            string countText = term.Substring(open + 1, term.Length - open - 2);
            int count;
            if (!int.TryParse(countText, out count))
                return 1;

            term = term.Substring(0, open).Trim();
            return Math.Max(1, Math.Min(count, 32));
        }

        private static IEnumerable<(int dx, int dy)> PatternDirections(string direction)
        {
            string value = (direction ?? "both").Trim().ToLowerInvariant();
            if (value == "horizontal" || value == "x" || value == "row" || value == "both")
            {
                yield return (1, 0);
                yield return (-1, 0);
            }
            if (value == "vertical" || value == "y" || value == "column" || value == "both")
            {
                yield return (0, 1);
                yield return (0, -1);
            }
        }

        private static string PatternDirectionName(int dx, int dy)
        {
            if (dx > 0) return "right";
            if (dx < 0) return "left";
            if (dy > 0) return "up";
            if (dy < 0) return "down";
            return "none";
        }

        private static bool PatternTermMatches(string term, string matchMode, params string[] values)
        {
            if (IsWildcardPatternTerm(term))
                return true;

            foreach (string alternative in SplitPatternAlternatives(term))
            {
                foreach (string value in values)
                {
                    if (PatternValueMatches(alternative, value, matchMode))
                        return true;
                }
            }
            return false;
        }

        private static bool IsWildcardPatternTerm(string term)
        {
            string value = (term ?? "").Trim();
            return value == "*" || value == "?" || value == "." || value.Equals("any", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SplitPatternAlternatives(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                yield break;

            string value = term.Trim();
            if (value.Length >= 2 && value[0] == '(' && value[value.Length - 1] == ')')
                value = value.Substring(1, value.Length - 2);

            foreach (string part in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string alternative = part.Trim();
                if (!string.IsNullOrWhiteSpace(alternative))
                    yield return alternative;
            }
        }

        private static bool PatternValueMatches(string term, string value, string matchMode)
        {
            if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(value))
                return false;
            string mode = (matchMode ?? "smart").Trim().ToLowerInvariant();
            if (string.Equals(term.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
            string termNorm = NormalizePatternText(term);
            string valueNorm = NormalizePatternText(value);
            if (termNorm.Length == 0 || valueNorm.Length == 0)
                return false;
            if (termNorm == valueNorm)
                return true;
            if (mode == "exact")
                return false;
            if (valueNorm.Contains(termNorm) || termNorm.Contains(valueNorm))
                return true;
            if (mode == "fuzzy")
            {
                int maxDistance = termNorm.Length <= 4 ? 1 : termNorm.Length <= 8 ? 2 : 3;
                return BoundedEditDistance(valueNorm, termNorm, maxDistance) >= 0;
            }
            return false;
        }

        private static string NormalizePatternText(string value)
        {
            var chars = new List<char>(value.Length);
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    chars.Add(char.ToLowerInvariant(ch));
            }
            return new string(chars.ToArray());
        }

        private static int BoundedEditDistance(string left, string right, int maxDistance)
        {
            if (Math.Abs(left.Length - right.Length) > maxDistance)
                return -1;
            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++)
                previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                int rowMin = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                    rowMin = Math.Min(rowMin, current[j]);
                }
                if (rowMin > maxDistance)
                    return -1;
                var temp = previous;
                previous = current;
                current = temp;
            }
            return previous[right.Length] <= maxDistance ? previous[right.Length] : -1;
        }

        private static IEnumerable<SearchHit> SearchBuildings(SearchRequest request)
        {
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.gameObject == null)
                    continue;
                int cell = Grid.PosToCell(building.gameObject);
                if (!request.MatchesCell(cell))
                    continue;
                var def = building.Def;
                var kpid = building.GetComponent<KPrefabID>();
                string prefabId = def?.PrefabID ?? kpid?.PrefabTag.Name ?? building.name;
                string name = ToolUtil.CleanName(building.GetProperName());
                if (!request.MatchesQuery(name, prefabId, building.name))
                    continue;

                int x;
                int y;
                Grid.CellToXY(cell, out x, out y);
                yield return new SearchHit
                {
                    Kind = "building",
                    Id = (kpid?.InstanceID ?? building.gameObject.GetInstanceID()).ToString(),
                    Name = name,
                    PrefabId = prefabId,
                    X = x,
                    Y = y,
                    WorldId = Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : building.GetMyWorldId(),
                    Operational = building.GetComponent<Operational>()?.IsOperational,
                    Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell)
                };
            }
        }

        private static IEnumerable<SearchHit> SearchItems(SearchRequest request)
        {
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                int cell = pickupable.cachedCell;
                if (!request.MatchesCell(cell, pickupable.GetMyWorldId()))
                    continue;
                var kpid = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                string prefabId = kpid?.PrefabTag.Name ?? pickupable.name;
                string name = ToolUtil.CleanName(pickupable.GetProperName());
                string elementId = primary == null ? null : primary.ElementID.ToString();
                if (!request.MatchesQuery(name, prefabId, elementId, pickupable.name))
                    continue;

                int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
                int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
                yield return new SearchHit
                {
                    Kind = "item",
                    Id = (kpid?.InstanceID ?? pickupable.gameObject.GetInstanceID()).ToString(),
                    Name = name,
                    PrefabId = prefabId,
                    ElementId = elementId,
                    X = x,
                    Y = y,
                    WorldId = Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : pickupable.GetMyWorldId(),
                    MassKg = primary == null ? (float?)null : ToolUtil.SafeFloat(primary.Mass),
                    TemperatureC = primary == null ? (float?)null : ToolUtil.SafeFloat(primary.Temperature) - 273.15f,
                    Stored = pickupable.storage != null || kpid != null && kpid.HasTag(GameTags.Stored),
                    Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell)
                };
            }
        }

        private static IEnumerable<SearchHit> SearchDupes(SearchRequest request)
        {
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.gameObject == null)
                    continue;
                int cell = Grid.PosToCell(dupe.gameObject);
                if (!request.MatchesCell(cell, dupe.GetMyWorldId()))
                    continue;
                string name = ToolUtil.CleanName(dupe.GetProperName());
                if (!request.MatchesQuery(name, "dupe", "duplicant"))
                    continue;
                var kpid = dupe.GetComponent<KPrefabID>();
                int x;
                int y;
                Grid.CellToXY(cell, out x, out y);
                yield return new SearchHit
                {
                    Kind = "dupe",
                    Id = (kpid?.InstanceID ?? dupe.gameObject.GetInstanceID()).ToString(),
                    Name = name,
                    PrefabId = "Minion",
                    X = x,
                    Y = y,
                    WorldId = dupe.GetMyWorldId(),
                    Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell)
                };
            }
        }

        private static Dictionary<string, object> BuildResult(string tool, SearchRequest request, IEnumerable<SearchHit> rawHits)
        {
            var hits = rawHits.ToList();
            var result = new Dictionary<string, object>
            {
                ["v"] = 1,
                ["tool"] = tool,
                ["returnMode"] = request.ReturnMode,
                ["query"] = request.Query,
                ["kinds"] = request.Kinds.ToArray(),
                ["worldId"] = request.WorldId,
                ["rect"] = new[] { request.Rect["x1"], request.Rect["y1"], request.Rect["x2"], request.Rect["y2"] },
                ["visibleOnly"] = request.VisibleOnly,
                ["matched"] = hits.Count,
                ["sort"] = request.Sort,
                ["near"] = request.HasNear ? (object)new[] { request.NearX.Value, request.NearY.Value } : null,
                ["summary"] = hits.GroupBy(hit => hit.Kind).ToDictionary(group => group.Key, group => group.Count()),
                ["recommendedFollowUp"] = "Use world_area_snapshot or world_text_map around a cluster bbox when terrain context is needed; use camera_focus_cell for visual inspection."
            };

            if (request.ReturnMode == "summary")
            {
                result["returned"] = 0;
                return result;
            }

            if (request.ReturnMode == "clusters")
            {
                var clusters = BuildClusters(hits, request).Take(request.Limit).ToList();
                result["returned"] = clusters.Count;
                result["clusters"] = clusters;
                return result;
            }

            var sorted = SortHits(hits, request).Take(request.Limit).Select(hit => hit.ToDictionary(request)).ToList();
            result["returned"] = sorted.Count;
            result["items"] = sorted;
            return result;
        }

        private static IEnumerable<Dictionary<string, object>> BuildClusters(List<SearchHit> hits, SearchRequest request)
        {
            foreach (var cluster in BuildCellClusters(hits.Where(hit => hit.Kind == "cell").ToList(), request))
                yield return cluster;
            foreach (var cluster in BuildObjectGroups(hits.Where(hit => hit.Kind != "cell").ToList(), request))
                yield return cluster;
        }

        private static IEnumerable<Dictionary<string, object>> BuildCellClusters(List<SearchHit> cellHits, SearchRequest request)
        {
            var orderedGroups = cellHits
                .GroupBy(hit => $"{hit.WorldId}|{hit.ElementId}|{hit.State}")
                .OrderByDescending(group => group.Count());

            int clusterIndex = 0;
            foreach (var group in orderedGroups)
            {
                var remaining = group.ToDictionary(hit => CellKey(hit.WorldId, hit.X, hit.Y), hit => hit);
                while (remaining.Count > 0)
                {
                    var first = remaining.Values.First();
                    var queue = new Queue<SearchHit>();
                    var members = new List<SearchHit>();
                    remaining.Remove(CellKey(first.WorldId, first.X, first.Y));
                    queue.Enqueue(first);

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        members.Add(current);
                        TryEnqueueNeighbor(current.WorldId, current.X + 1, current.Y, remaining, queue);
                        TryEnqueueNeighbor(current.WorldId, current.X - 1, current.Y, remaining, queue);
                        TryEnqueueNeighbor(current.WorldId, current.X, current.Y + 1, remaining, queue);
                        TryEnqueueNeighbor(current.WorldId, current.X, current.Y - 1, remaining, queue);
                    }

                    yield return CellClusterInfo($"cell_cluster_{clusterIndex++}", members, request);
                }
            }
        }

        private static IEnumerable<Dictionary<string, object>> BuildObjectGroups(List<SearchHit> objectHits, SearchRequest request)
        {
            int groupIndex = 0;
            foreach (var group in objectHits.GroupBy(hit => $"{hit.WorldId}|{hit.Kind}|{hit.PrefabId}").OrderByDescending(group => group.Count()))
            {
                var members = group.ToList();
                yield return new Dictionary<string, object>
                {
                    ["id"] = $"object_group_{groupIndex++}",
                    ["kind"] = members[0].Kind,
                    ["prefabId"] = members[0].PrefabId,
                    ["name"] = members[0].Name,
                    ["worldId"] = members[0].WorldId,
                    ["bbox"] = BBox(members),
                    ["count"] = members.Count,
                    ["nearestDistance"] = request.HasNear ? (object)Math.Round(Math.Sqrt(members.Min(hit => request.DistanceSquared(hit.X, hit.Y))), 2) : null,
                    ["sampleItems"] = SortHits(members, request).Take(8).Select(hit => hit.ToDictionary(request)).ToList()
                };
            }
        }

        private static Dictionary<string, object> CellClusterInfo(string id, List<SearchHit> members, SearchRequest request)
        {
            float mass = members.Sum(hit => hit.MassKg ?? 0f);
            var temps = members.Where(hit => hit.TemperatureC.HasValue).Select(hit => hit.TemperatureC.Value).ToList();
            var sorted = SortHits(members, request).Take(12).ToList();
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["kind"] = "cell_cluster",
                ["elementId"] = members[0].ElementId,
                ["state"] = members[0].State,
                ["worldId"] = members[0].WorldId,
                ["bbox"] = BBox(members),
                ["cells"] = members.Count,
                ["estimatedMassKg"] = Math.Round(mass, 2),
                ["avgMassKg"] = Math.Round(mass / Math.Max(1, members.Count), 3),
                ["avgTemperatureC"] = temps.Count == 0 ? (object)null : Math.Round(temps.Average(), 2),
                ["minTemperatureC"] = temps.Count == 0 ? (object)null : Math.Round(temps.Min(), 2),
                ["maxTemperatureC"] = temps.Count == 0 ? (object)null : Math.Round(temps.Max(), 2),
                ["nearestDistance"] = request.HasNear ? (object)Math.Round(Math.Sqrt(members.Min(hit => request.DistanceSquared(hit.X, hit.Y))), 2) : null,
                ["suggestedCells"] = sorted.Select(hit => new[] { hit.X, hit.Y }).ToList()
            };
        }

        private static int[] BBox(List<SearchHit> members)
        {
            return new[] { members.Min(hit => hit.X), members.Min(hit => hit.Y), members.Max(hit => hit.X), members.Max(hit => hit.Y) };
        }

        private static string CellKey(int worldId, int x, int y)
        {
            return worldId + ":" + x + ":" + y;
        }

        private static void TryEnqueueNeighbor(int worldId, int x, int y, Dictionary<string, SearchHit> remaining, Queue<SearchHit> queue)
        {
            string key = CellKey(worldId, x, y);
            SearchHit hit;
            if (!remaining.TryGetValue(key, out hit))
                return;
            remaining.Remove(key);
            queue.Enqueue(hit);
        }

        private static IEnumerable<SearchHit> SortHits(List<SearchHit> hits, SearchRequest request)
        {
            string sort = request.Sort;
            if (sort == "nearest" && request.HasNear)
                return hits.OrderBy(hit => request.DistanceSquared(hit.X, hit.Y)).ThenBy(hit => hit.Kind).ThenBy(hit => hit.Name);
            if (sort == "mass")
                return hits.OrderByDescending(hit => hit.MassKg ?? 0f).ThenBy(hit => hit.Kind).ThenBy(hit => hit.Name);
            if (sort == "temperature")
                return hits.OrderByDescending(hit => hit.TemperatureC ?? -9999f).ThenBy(hit => hit.Kind).ThenBy(hit => hit.Name);
            return hits.OrderBy(hit => hit.Kind).ThenBy(hit => hit.Name).ThenBy(hit => hit.X).ThenBy(hit => hit.Y);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                   && !string.IsNullOrEmpty(query)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class SearchRequest
        {
            public string Query;
            public HashSet<string> Kinds;
            public Dictionary<string, int> Rect;
            public int WorldId;
            public bool VisibleOnly;
            public int Limit;
            public string Sort;
            public int? NearX;
            public int? NearY;
            public string ReturnMode;
            public string State;
            public bool? Solid;
            public float? MinMassKg;
            public float? MaxMassKg;
            public float? MinTempC;
            public float? MaxTempC;
            public string Pattern;
            public string PatternDirection;
            public string MatchMode;

            public bool HasNear => NearX.HasValue && NearY.HasValue;
            public bool HasPattern => !string.IsNullOrWhiteSpace(Pattern);

            public static SearchRequest From(JObject args, string defaultReturnMode)
            {
                int maxCells = Math.Max(100, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 2500, MaxSearchCells));
                var rect = WorldEditor.ResolveRectOrCamera(args, maxCells);
                var request = new SearchRequest
                {
                    Query = args["query"]?.ToString()?.Trim(),
                    Kinds = ParseKinds(args["kinds"]),
                    Rect = rect,
                    WorldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? 0),
                    VisibleOnly = ToolUtil.GetBool(args, "visibleOnly", true),
                    Limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 50, 300)),
                    Sort = NormalizeSort(args["sort"]?.ToString(), ToolUtil.GetInt(args, "nearX").HasValue && ToolUtil.GetInt(args, "nearY").HasValue),
                    NearX = ToolUtil.GetInt(args, "nearX"),
                    NearY = ToolUtil.GetInt(args, "nearY"),
                    ReturnMode = NormalizeReturnMode(args["returnMode"]?.ToString(), defaultReturnMode),
                    State = NormalizeState(args["state"]?.ToString()),
                Solid = args["solid"] == null ? (bool?)null : ToolUtil.GetBool(args, "solid", false),
                MinMassKg = ToolUtil.GetFloat(args, "minMassKg"),
                MaxMassKg = ToolUtil.GetFloat(args, "maxMassKg"),
                MinTempC = ToolUtil.GetFloat(args, "minTempC"),
                MaxTempC = ToolUtil.GetFloat(args, "maxTempC"),
                Pattern = FirstNonEmpty(args["pattern"], args["sequence"]),
                PatternDirection = NormalizePatternDirection(args["direction"]?.ToString()),
                MatchMode = NormalizeMatchMode(args["matchMode"]?.ToString())
            };
                return request;
            }

            public IEnumerable<int> Cells()
            {
                for (int y = Rect["y1"]; y <= Rect["y2"]; y++)
                {
                    for (int x = Rect["x1"]; x <= Rect["x2"]; x++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        if (MatchesCell(cell))
                            yield return cell;
                    }
                }
            }

            public bool IncludeKind(string kind)
            {
                return Kinds.Contains("all") || Kinds.Contains(kind);
            }

            public bool MatchesQuery(params string[] values)
            {
                if (string.IsNullOrWhiteSpace(Query))
                    return true;
                return values.Any(value => Contains(value, Query));
            }

            public bool MatchesState(string state)
            {
                return State == "any" || string.Equals(State, state, StringComparison.OrdinalIgnoreCase);
            }

            public bool MatchesCell(int cell, int fallbackWorldId = -1)
            {
                int world = fallbackWorldId;
                if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                    world = Grid.WorldIdx[cell];
                if (WorldId >= 0 && world >= 0 && world != WorldId)
                    return false;
                if (Grid.IsValidCell(cell))
                {
                    int x = Grid.CellColumn(cell);
                    int y = Grid.CellRow(cell);
                    if (x < Rect["x1"] || x > Rect["x2"] || y < Rect["y1"] || y > Rect["y2"])
                        return false;
                    if (VisibleOnly && !Grid.IsVisible(cell))
                        return false;
                }
                return true;
            }

            public double DistanceSquared(int x, int y)
            {
                if (!HasNear || x < 0 || y < 0)
                    return double.MaxValue;
                double dx = x - NearX.Value;
                double dy = y - NearY.Value;
                return dx * dx + dy * dy;
            }

            private static HashSet<string> ParseKinds(JToken value)
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (value == null || value.Type == JTokenType.Null)
                {
                    result.Add("all");
                    return result;
                }
                if (value.Type == JTokenType.Array)
                {
                    foreach (var item in value.Children())
                        AddKind(result, item?.ToString());
                }
                else
                {
                    foreach (string item in value.ToString().Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        AddKind(result, item);
                }
                if (result.Count == 0)
                    result.Add("all");
                return result;
            }

            private static void AddKind(HashSet<string> result, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;
                string kind = value.Trim().ToLowerInvariant();
                if (kind == "element")
                    kind = "elements";
                if (kind == "resource")
                    kind = "resources";
                if (kind == "duplicant")
                    kind = "dupes";
                result.Add(kind);
            }

            private static string NormalizeSort(string value, bool hasNear)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return hasNear ? "nearest" : "kind";
                string sort = value.Trim().ToLowerInvariant();
                return sort == "nearest" || sort == "mass" || sort == "temperature" ? sort : "kind";
            }

            private static string NormalizeReturnMode(string value, string defaultReturnMode)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return defaultReturnMode == "hits" || defaultReturnMode == "summary" ? defaultReturnMode : "clusters";
                string mode = value.Trim().ToLowerInvariant();
                return mode == "hits" || mode == "summary" || mode == "clusters" ? mode : "clusters";
            }

            private static string NormalizeState(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "any";
                string state = value.Trim().ToLowerInvariant();
                return state == "gas" || state == "liquid" || state == "solid" || state == "vacuum" ? state : "any";
            }

            private static string FirstNonEmpty(params JToken[] values)
            {
                foreach (var value in values)
                {
                    string text = value?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
                return null;
            }

            private static string NormalizePatternDirection(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "both";
                string direction = value.Trim().ToLowerInvariant();
                if (direction == "horizontal" || direction == "x" || direction == "row")
                    return "horizontal";
                if (direction == "vertical" || direction == "y" || direction == "column")
                    return "vertical";
                return "both";
            }

            private static string NormalizeMatchMode(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "smart";
                string mode = value.Trim().ToLowerInvariant();
                return mode == "exact" || mode == "fuzzy" ? mode : "smart";
            }
        }

        private sealed class SearchHit
        {
            public string Kind;
            public string Id;
            public string Name;
            public string PrefabId;
            public string ElementId;
            public int X;
            public int Y;
            public int WorldId;
            public float? MassKg;
            public float? TemperatureC;
            public string State;
            public bool? Solid;
            public bool? Stored;
            public bool? Operational;
            public bool Visible;
            public int Scanned;

            public Dictionary<string, object> ToDictionary(SearchRequest request)
            {
                var result = new Dictionary<string, object>
                {
                    ["kind"] = Kind,
                    ["id"] = Id,
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["elementId"] = ElementId,
                    ["x"] = X,
                    ["y"] = Y,
                    ["worldId"] = WorldId,
                    ["visible"] = Visible
                };
                if (MassKg.HasValue)
                    result["massKg"] = Math.Round(MassKg.Value, 3);
                if (TemperatureC.HasValue)
                    result["temperatureC"] = Math.Round(TemperatureC.Value, 2);
                if (!string.IsNullOrWhiteSpace(State))
                    result["state"] = State;
                if (Solid.HasValue)
                    result["solid"] = Solid.Value;
                if (Stored.HasValue)
                    result["stored"] = Stored.Value;
                if (Operational.HasValue)
                    result["operational"] = Operational.Value;
                if (request.HasNear)
                    result["distance"] = Math.Round(Math.Sqrt(request.DistanceSquared(X, Y)), 2);
                return result;
            }
        }
    }
}
