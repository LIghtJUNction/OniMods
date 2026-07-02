using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> BuildPlacementCandidate(Dictionary<string, object> preview, int x, int y, JObject args, int score, string status)
        {
            var candidate = new Dictionary<string, object>
            {
                ["score"] = score,
                ["status"] = status,
                ["anchor"] = new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y
                },
                ["preview"] = preview,
                ["placement"] = GetObject(preview, "placement"),
                ["footprint"] = GetObjectList(preview, "footprint"),
                ["support"] = GetObject(preview, "support"),
                ["materialSelection"] = GetObject(preview, "materialSelection"),
                ["facade"] = preview.ContainsKey("facade") ? preview["facade"] : null,
                ["error"] = preview.ContainsKey("error") ? preview["error"] : null
            };

            WorldEditor.AddRelativeInfo(candidate["anchor"] as Dictionary<string, object>, args, x, y);
            return candidate;
        }

        private static int ScorePlacementCandidate(Dictionary<string, object> preview, bool valid, bool warningOnly)
        {
            if (!valid)
            {
                int invalidPenalty = 200;
                if (preview != null && preview.ContainsKey("failureReason"))
                {
                    string reason = preview["failureReason"]?.ToString() ?? "";
                    if (reason == "unsupported")
                        invalidPenalty = 140;
                    else if (reason == "unavailableMaterial")
                        invalidPenalty = 160;
                    else if (reason == "obstructed")
                        invalidPenalty = 180;
                }
                return -invalidPenalty;
            }

            int score = 100;
            var support = GetObject(preview, "support");
            var footprint = GetObjectList(preview, "footprint");
            var placement = GetObject(preview, "placement");
            int width = GetInt(placement, "width");
            int height = GetInt(placement, "height");

            score -= Math.Max(0, footprint.Count - Math.Max(1, width * height)) * 2;

            if (warningOnly)
                score -= 10;

            var missingSupport = GetObjectList(support, "missingSupportCells");
            score -= missingSupport.Count * 12;

            var obstructions = GetObjectList(preview, "obstructions");
            score -= obstructions.Count * 25;

            if (GetBool(support, "valid"))
                score += 10;
            if (!GetBool(support, "warningOnly"))
                score += 5;

            return score;
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static List<Dictionary<string, object>> GetObjectList(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value as List<Dictionary<string, object>> : null ?? new List<Dictionary<string, object>>();
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return false;
            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static bool SameAnchor(Dictionary<string, object> result, CellCoord anchor)
        {
            return GetInt(result, "x") == anchor.x && GetInt(result, "y") == anchor.y;
        }

        private static Dictionary<string, object> BuildRemainingBuildAreaAction(JObject args, string prefabId, List<CellCoord> anchors)
        {
            if (anchors == null || anchors.Count == 0)
                return null;

            var arguments = new Dictionary<string, object>
            {
                ["domain"] = "planning",
                ["action"] = "build_area",
                ["prefabId"] = prefabId,
                ["worldId"] = ToolUtil.ResolveWorldId(args),
                ["anchors"] = anchors
                    .Select(anchor => new Dictionary<string, object> { ["x"] = anchor.x, ["y"] = anchor.y })
                    .ToList(),
                ["confirm"] = true,
                ["dryRun"] = false,
                ["allowPartial"] = true
            };

            CopyIfPresent(args, arguments, "material");
            CopyIfPresent(args, arguments, "facade");
            CopyIfPresent(args, arguments, "facadeId");
            CopyIfPresent(args, arguments, "orientation");
            CopyIfPresent(args, arguments, "priority");
            CopyIfPresent(args, arguments, "allowUnsupported");
            CopyIfPresent(args, arguments, "autoConnectPower");
            CopyIfPresent(args, arguments, "maxAutoConnectRadius");

            return new Dictionary<string, object>
            {
                ["tool"] = "building_control",
                ["arguments"] = arguments,
                ["note"] = "Retry only anchors that are not yet built/blueprinted/connected. If autoDigQueued > 0, run after dig chores finish."
            };
        }

        private static void CopyIfPresent(JObject source, Dictionary<string, object> target, string key)
        {
            if (source == null || target == null || source[key] == null)
                return;
            if (source[key].Type == JTokenType.Boolean)
                target[key] = source[key].Value<bool>();
            else if (source[key].Type == JTokenType.Integer)
                target[key] = source[key].Value<int>();
            else
                target[key] = source[key].ToString();
        }

        private static bool GetDictionaryBool(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return false;
            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static bool GetNestedBool(Dictionary<string, object> dict, string parentKey, string childKey)
        {
            var parent = GetObject(dict, parentKey);
            return GetBool(parent, childKey);
        }

        private static int GetInt(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return 0;
            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static Dictionary<string, object> ParseToolJsonPayload(CallToolResult result)
        {
            string text = result?.Content?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(text);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsAutoDiggableFailure(Dictionary<string, object> result)
        {
            var autoDig = GetObject(result, "autoDig");
            return GetBool(autoDig, "available") && GetInt(autoDig, "targetCount") > 0;
        }

        private static bool IsAutoDigResult(Dictionary<string, object> result)
        {
            var autoDig = GetObject(result, "autoDig");
            return GetInt(autoDig, "marked") > 0
                || GetInt(autoDig, "alreadyMarked") > 0
                || GetInt(autoDig, "uprootMarked") > 0
                || GetInt(autoDig, "alreadyUprootMarked") > 0;
        }

        private static int GetAutoDigInt(Dictionary<string, object> result, string key)
        {
            return GetInt(GetObject(result, "autoDig"), key);
        }

        private static void AddAnchorInfo(Dictionary<string, object> anchor, JObject args, int x, int y)
        {
            var area = WorldEditor.ResolveRelativeArea(args);
            if (area == null)
                return;

            anchor["areaId"] = area.Id;
            anchor["rx"] = x - area.X1;
            anchor["ry"] = y - area.Y1;
            anchor["origin"] = new[] { area.X1, area.Y1 };
            anchor["coordMode"] = "relative";
        }

    }
}
