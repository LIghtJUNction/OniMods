using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private const string ConnectionGlyphs = "*─│┌┐└┘┬┴├┤┼←→↑↓●";
        private const string TemperatureGlyphs = "零寒冰和暖炎灼熔";
        private const string OxygenGlyphs = "■液不易可难";
        private const string LightGlyphs = "晒明普弱暗";
        private const string DecorGlyphs = "美好平差丑";
        private const string DiseaseGlyphs = "微菌疫";
        private const string RadiationGlyphs = "低辐危";
        private const string CropGlyphs = "枯收植";
        private static readonly HashSet<string> OverlayGlyphViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "temperature", "oxygen", "light", "decor", "disease", "radiation", "crop"
        };
        private static readonly HashSet<string> InfrastructureGlyphViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "power", "gas_conduits", "liquid_conduits", "solid_conveyor", "logic"
        };

        internal static CallToolResult SearchGlyphs(JObject args)
        {
            var queries = ParseGlyphQueries(args, out bool legacySingle, out string queryError);
            if (queryError != null)
                return CallToolResult.Error(queryError);

            string direction = NormalizeGlyphDirection(args?["direction"]?.ToString());
            if (direction == null)
                return CallToolResult.Error("direction must be auto, code_to_meaning, or meaning_to_code");
            string matchMode = NormalizeGlyphMatchMode(args?["matchMode"]?.ToString());
            if (matchMode == null)
                return CallToolResult.Error("matchMode must be auto, exact, or contains");

            string requestedView = args?["view"]?.ToString();
            if (!TryNormalizeGlyphView(requestedView, out string view))
                return CallToolResult.Error("unknown glyph view: " + requestedView);
            int requestedLimit = ToolUtil.GetInt(args, "perQueryLimit")
                ?? ToolUtil.GetInt(args, "limit")
                ?? (legacySingle ? 200 : 20);
            int perQueryLimit = Math.Max(1, Math.Min(requestedLimit, legacySingle ? 1000 : 100));
            var rows = BuildSymbolRows();
            var results = new JArray();
            foreach (string input in queries)
                results.Add(LookupGlyphQuery(rows, input, direction, matchMode, view, perQueryLimit));

            var result = new JObject
            {
                ["queries"] = new JArray(queries),
                ["direction"] = direction,
                ["matchMode"] = matchMode,
                ["view"] = string.IsNullOrWhiteSpace(view) ? null : view,
                ["perQueryLimit"] = perQueryLimit,
                ["results"] = results,
                ["source"] = "authoritative generated ONI glyph rows plus contextual map glyph rows",
                ["resultContract"] = new JObject
                {
                    ["style"] = "authoritative_glyph_mapping",
                    ["rule"] = "Use returned rows as the authoritative runtime mapping. Do not guess glyph meanings or codes.",
                    ["context"] = "A symbol may have multiple meanings; pass view to filter contextual overlay rows."
                },
                ["nextActions"] = new JArray(new JObject
                {
                    ["label"] = "reuse_authoritative_mapping",
                    ["rule"] = "Reuse these mappings in the same runtime turn. Do not guess unknown entries."
                })
            };

            if (legacySingle)
            {
                JObject first = results.Count > 0 ? (JObject)results[0] : EmptyGlyphQueryResult(string.Empty, direction);
                result["query"] = queries.Count > 0 ? queries[0] : string.Empty;
                result["count"] = first["count"] != null ? first["count"].DeepClone() : new JValue(0);
                result["symbols"] = first["matches"] != null ? first["matches"].DeepClone() : new JArray();
            }

            return JsonResult(result);
        }

        private static List<string> ParseGlyphQueries(JObject args, out bool legacySingle, out string error)
        {
            error = null;
            var raw = args?["queries"] as JArray;
            legacySingle = raw == null;
            if (raw != null)
            {
                if (raw.Count == 0)
                {
                    error = "queries must contain at least one non-blank string";
                    return new List<string>();
                }
                if (raw.Count > 100)
                {
                    error = "queries supports at most 100 strings";
                    return new List<string>();
                }
                if (raw.Any(token => token == null
                    || token.Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(token.ToString())))
                {
                    error = "queries must contain only non-blank strings";
                    return new List<string>();
                }
                var queries = raw
                    .Select(token => token.ToString().Trim())
                    .ToList();
                return queries;
            }

            string query = Text(args, "query", "target", "search")?.Trim();
            return new List<string> { query ?? string.Empty };
        }

        private static JObject LookupGlyphQuery(List<JObject> rows, string input, string direction,
            string matchMode, string view, int limit)
        {
            var candidates = FilterGlyphRowsByView(rows, view);
            string resolvedDirection = direction == "auto"
                ? candidates.Any(row => GlyphText(row, "symbol").Equals(input, StringComparison.Ordinal))
                    ? "code_to_meaning"
                    : "meaning_to_code"
                : direction;
            Func<JObject, IEnumerable<string>> values = resolvedDirection == "code_to_meaning"
                ? (Func<JObject, IEnumerable<string>>)(row => new[] { GlyphText(row, "symbol") })
                : row => new[]
                {
                    GlyphText(row, "id"), GlyphText(row, "name"), GlyphText(row, "kind"),
                    GlyphText(row, "meaning"), GlyphText(row, "dirs")
                };

            var exactMatches = candidates.Where(row => values(row).Any(value =>
                value.Equals(input, StringComparison.OrdinalIgnoreCase))).ToList();
            bool useExact = matchMode == "exact" || (matchMode == "auto" && exactMatches.Count > 0);
            var matches = useExact
                ? exactMatches
                : candidates.Where(row => values(row).Any(value =>
                    value.IndexOf(input ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            return new JObject
            {
                ["input"] = input,
                ["resolvedDirection"] = resolvedDirection,
                ["exact"] = useExact && exactMatches.Count > 0,
                ["count"] = matches.Count,
                ["matches"] = new JArray(matches.Take(limit))
            };
        }

        private static JObject EmptyGlyphQueryResult(string input, string direction)
        {
            return new JObject
            {
                ["input"] = input,
                ["resolvedDirection"] = direction,
                ["exact"] = false,
                ["count"] = 0,
                ["matches"] = new JArray()
            };
        }

        private static List<JObject> FilterGlyphRowsByView(List<JObject> rows, string view)
        {
            if (string.IsNullOrWhiteSpace(view))
                return rows;
            if (OverlayGlyphViews.Contains(view))
                return FilterOverlayGlyphRows(rows, view);
            if (view == "rooms")
                return FilterRoomGlyphRows(rows);
            if (InfrastructureGlyphViews.Contains(view))
                return FilterInfrastructureGlyphRows(rows, view);
            return FilterDefaultGlyphRows(rows);
        }

        private static List<JObject> FilterOverlayGlyphRows(List<JObject> rows, string view)
        {
            return rows.Where(row =>
            {
                string kind = GlyphText(row, "kind");
                return kind == "Special" || IsGeneratedGlyphKind(kind) || (kind == "Overlay"
                    && GlyphText(row, "view").Equals(view, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }

        private static List<JObject> FilterRoomGlyphRows(List<JObject> rows)
        {
            return rows.Where(row =>
            {
                string kind = GlyphText(row, "kind");
                return kind == "Special" || IsGeneratedGlyphKind(kind) || kind == "Room";
            }).ToList();
        }

        private static List<JObject> FilterInfrastructureGlyphRows(List<JObject> rows, string view)
        {
            return rows.Where(row =>
            {
                string kind = GlyphText(row, "kind");
                return kind == "Special" || IsGeneratedGlyphKind(kind) || kind == "Connection";
            }).ToList();
        }

        private static List<JObject> FilterDefaultGlyphRows(List<JObject> rows)
        {
            return rows.Where(row =>
            {
                string kind = GlyphText(row, "kind");
                return kind == "Special" || IsGeneratedGlyphKind(kind);
            }).ToList();
        }

        private static bool IsGeneratedGlyphKind(string kind)
        {
            return kind == "Building" || kind == "Element" || kind == "Entity";
        }

        private static string NormalizeGlyphDirection(string value)
        {
            string normalized = (value ?? "auto").Trim().ToLowerInvariant();
            return normalized == "auto" || normalized == "code_to_meaning" || normalized == "meaning_to_code"
                ? normalized
                : null;
        }

        private static string NormalizeGlyphMatchMode(string value)
        {
            string normalized = (value ?? "auto").Trim().ToLowerInvariant();
            return normalized == "auto" || normalized == "exact" || normalized == "contains"
                ? normalized
                : null;
        }

        private static bool TryNormalizeGlyphView(string value, out string normalized)
        {
            normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
            switch (normalized)
            {
                case "": return true;
                case "all": normalized = string.Empty; return true;
                case "default": case "none": case "normal": normalized = "default"; return true;
                case "oxygen": case "gas": normalized = "oxygen"; return true;
                case "power": case "electric": case "electrical": normalized = "power"; return true;
                case "liquid": case "liquid_pipe": case "liquid_pipes": case "liquid_conduits": case "plumbing":
                    normalized = "liquid_conduits"; return true;
                case "gas_pipe": case "gas_pipes": case "gas_conduits": normalized = "gas_conduits"; return true;
                case "logic": case "automation": normalized = "logic"; return true;
                case "shipping": case "conveyor": case "solid_conveyor": normalized = "solid_conveyor"; return true;
                case "temperature": case "temp": normalized = "temperature"; return true;
                case "heat_flow": case "heatflow": normalized = "heat_flow"; return true;
                case "thermal_conductivity": case "conductivity": normalized = "thermal_conductivity"; return true;
                case "materials": case "material": case "tile": case "tiles": normalized = "materials"; return true;
                case "light": case "decor": case "radiation": return true;
                case "rad": normalized = "radiation"; return true;
                case "disease": case "germs": normalized = "disease"; return true;
                case "crop": case "farming": case "harvest": normalized = "crop"; return true;
                case "rooms": case "room": normalized = "rooms"; return true;
                case "priorities": case "priority": normalized = "priorities"; return true;
                case "sound": case "noise": normalized = "sound"; return true;
                case "suit": case "exosuit": case "atmo_suit": normalized = "suit"; return true;
                default: return false;
            }
        }

        private static string GlyphText(JObject row, string key)
        {
            return row?[key]?.ToString() ?? string.Empty;
        }

        private static List<JObject> BuildSymbolRows()
        {
            EnsureRuntimeRoomGlyphs();
            var rows = GeneratedGlyphEntries.Select(entry => GlyphRow(
                entry.Kind,
                entry.Id,
                entry.Name,
                GetUniqueChar(entry.Id, entry.Name).ToString(),
                entry.Name)).ToList();

            rows.Add(GlyphRow("Special", "empty", "空/无视图", ".", "当前格为空，或当前视图无覆盖内容"));
            rows.Add(GlyphRow("Special", "unknown", "未知", "?", "运行时对象或格子状态未知；必须查询，不得猜测"));
            foreach (var entry in RuntimeRoomGlyphEntries())
                rows.Add(GlyphRow("Room", entry.Id, entry.Name,
                    GetUniqueChar(entry.Id, entry.Name).ToString(), entry.Name, "rooms"));
            AddConnectionRows(rows);
            AddOverlayRows(rows, "temperature", TemperatureGlyphs, new[]
            {
                "低于 -260°C", "-260°C 至 -18°C", "-18°C 至 0°C", "0°C 至 20°C",
                "20°C 至 35°C", "35°C 至 100°C", "100°C 至 1000°C", "不低于 1000°C"
            });
            AddOverlayRows(rows, "oxygen", OxygenGlyphs, new[]
            {
                "固体格", "非气体液体格", "不可呼吸气体", "易呼吸，质量不少于 0.6kg",
                "可呼吸，质量不少于 0.1kg", "可呼吸但质量低于 0.1kg"
            });
            AddOverlayRows(rows, "light", LightGlyphs, new[]
            {
                "至少 72500 lux", "至少 1000 lux", "至少 200 lux", "大于 0 lux", "0 lux"
            });
            rows.Add(GlyphRow("Overlay", "light_solid", "固体格", "■", "固体格", "light"));
            AddOverlayRows(rows, "decor", DecorGlyphs, new[]
            {
                "装饰度至少 50", "装饰度大于 0", "装饰度等于 0", "装饰度大于 -50", "装饰度不高于 -50"
            });
            AddOverlayRows(rows, "disease", DiseaseGlyphs, new[]
            {
                "病菌数 1 至 99", "病菌数 100 至 9999", "病菌数至少 10000"
            });
            AddOverlayRows(rows, "radiation", RadiationGlyphs, new[]
            {
                "辐射大于 0 且低于 100", "辐射 100 至 999", "辐射至少 1000"
            });
            AddOverlayRows(rows, "crop", CropGlyphs, new[]
            {
                "植物枯萎", "作物可收获", "作物生长中或不可收获"
            });

            return rows
                .OrderBy(row => GlyphText(row, "symbol"))
                .ThenBy(row => GlyphText(row, "view"))
                .ThenBy(row => GlyphText(row, "kind"))
                .ThenBy(row => GlyphText(row, "id"))
                .ToList();
        }

        private static void AddConnectionRows(List<JObject> rows)
        {
            string[] dirs = { "none", "LR", "UD", "RD", "LD", "RU", "LU", "LRD", "LRU", "RUD", "LUD", "LRUD", "L", "R", "U", "D", "endpoint" };
            string[] meanings = { "孤立连接", "水平连接", "垂直连接", "右下转角", "左下转角", "右上转角", "左上转角", "左右下三通", "左右上三通", "右上下三通", "左上下三通", "四向连接", "指向左", "指向右", "指向上", "指向下", "端点" };
            for (int i = 0; i < ConnectionGlyphs.Length; i++)
                rows.Add(GlyphRow("Connection", "connection_" + dirs[i], meanings[i], ConnectionGlyphs[i].ToString(), meanings[i], null, dirs[i]));
        }

        private static void AddOverlayRows(List<JObject> rows, string view, string symbols, string[] meanings)
        {
            for (int i = 0; i < symbols.Length && i < meanings.Length; i++)
                rows.Add(GlyphRow("Overlay", view + "_" + i, meanings[i], symbols[i].ToString(), meanings[i], view));
        }

        private static JObject GlyphRow(string kind, string id, string name, string symbol,
            string meaning, string view = null, string dirs = null)
        {
            var row = new JObject
            {
                ["kind"] = kind,
                ["id"] = id,
                ["name"] = name,
                ["symbol"] = symbol,
                ["meaning"] = meaning
            };
            if (!string.IsNullOrWhiteSpace(view))
                row["view"] = view;
            if (!string.IsNullOrWhiteSpace(dirs))
                row["dirs"] = dirs;
            return row;
        }
    }
}
