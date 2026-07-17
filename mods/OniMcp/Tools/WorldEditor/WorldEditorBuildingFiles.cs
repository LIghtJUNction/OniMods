using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private const string BuildingInstancePrefix = "buildings/instances/";

        private static IEnumerable<GameObject> LiveCompletedBuildings()
        {
            return Components.BuildingCompletes?.Items
                ?.Where(item => item != null && item.gameObject != null)
                .Select(item => item.gameObject)
                ?? Enumerable.Empty<GameObject>();
        }

        private static bool IsBuildingDetailMarkdown(string relative)
        {
            return relative.StartsWith(BuildingInstancePrefix, StringComparison.Ordinal)
                && relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetBuildingDetailFileName(GameObject go)
        {
            var config = BuildingConfigTools.SnapshotConfig(go);
            string prefab = config.TryGetValue("prefabId", out object value) ? value?.ToString() : go.name;
            string safe = Regex.Replace(prefab ?? "building", @"[^A-Za-z0-9_.-]+", "_").Trim('_');
            int id = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID();
            return (string.IsNullOrWhiteSpace(safe) ? "building" : safe) + "-" + id + ".md";
        }

        private static GameObject ResolveBuildingDetailFile(string relative, out string error)
        {
            error = null;
            string stem = System.IO.Path.GetFileNameWithoutExtension(relative) ?? string.Empty;
            int dash = stem.LastIndexOf('-');
            if (dash < 0 || !int.TryParse(stem.Substring(dash + 1), out int id))
            {
                error = "Building detail filename must end with a stable InstanceID: " + relative;
                return null;
            }
            var match = LiveCompletedBuildings().FirstOrDefault(go =>
                (go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID()) == id);
            if (match == null)
                error = "Completed building detail file not found: " + relative;
            return match;
        }

        private static JObject BuildingConfigSnapshot(GameObject go)
        {
            return JObject.FromObject(BuildingConfigTools.SnapshotConfig(go));
        }

        private static JObject BuildingStateSnapshot(GameObject go)
        {
            return JObject.FromObject(StateControlTools.SnapshotState(go));
        }

        private static string ReadBuildingIndexMarkdown()
        {
            var sb = new StringBuilder("# Completed Buildings\n\n");
            sb.AppendLine("Each linked instance file exposes supported editable side-screen parameters. Files are stable by InstanceID.");
            sb.AppendLine();
            sb.AppendLine("| Building | Prefab | ID | Position | Parameters |");
            sb.AppendLine("| --- | --- | ---: | --- | --- |");
            foreach (var go in LiveCompletedBuildings().OrderBy(go => go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID()))
            {
                JObject config = BuildingConfigSnapshot(go);
                string file = GetBuildingDetailFileName(go);
                sb.AppendLine("| " + config["name"] + " | " + config["prefabId"] + " | " + config["id"]
                    + " | (" + config["x"] + "," + config["y"] + ") | [" + file + "](/active/"
                    + BuildingInstancePrefix + file + ") |");
            }
            return sb.ToString();
        }

        private static string ReadBuildingDetailMarkdown(string relative)
        {
            GameObject go = ResolveBuildingDetailFile(relative, out string error);
            if (go == null)
                return "# Error\n\n" + error + "\n";
            JObject config = BuildingConfigSnapshot(go);
            JObject state = BuildingStateSnapshot(go);
            var editable = EditableBuildingLines(config, state);
            var capabilities = ((JArray)config["capabilities"] ?? new JArray()).Select(item => item.ToString())
                .Concat(((JArray)state["controlKinds"] ?? new JArray()).Select(item => item.ToString()))
                .Distinct().OrderBy(item => item).ToList();
            var sb = new StringBuilder("# Building Parameters\n\n");
            sb.AppendLine("Path: /active/" + BuildingInstancePrefix + GetBuildingDetailFileName(go));
            sb.AppendLine("Identity: " + config["name"] + " | Prefab=" + config["prefabId"] + " | ID=" + config["id"]);
            sb.AppendLine("Position: (" + config["x"] + "," + config["y"] + ") | World=" + config["worldId"]);
            sb.AppendLine("Capabilities: " + (capabilities.Count == 0 ? "none" : string.Join(", ", capabilities)));
            sb.AppendLine();
            sb.AppendLine("## Editable");
            sb.AppendLine("SEARCH/REPLACE exactly one canonical line. Unknown and readonly lines are rejected.");
            foreach (var line in editable)
                sb.AppendLine(line.Key + ": " + line.Value);
            if (editable.Count == 0)
                sb.AppendLine("(no supported editable parameters)");
            sb.AppendLine();
            sb.AppendLine("## Readonly JSON Snapshot");
            sb.AppendLine("```json");
            sb.AppendLine(JsonConvert.SerializeObject(new JObject { ["config"] = config, ["state"] = state }, Formatting.Indented));
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## Related Files");
            sb.AppendLine("- Production and other building actions: /active/ops/build.md");
            sb.AppendLine("- Storage and filters: /active/ops/storage.md");
            sb.AppendLine("- Map cell details: /active/map/cell_" + config["x"] + "_" + config["y"] + ".md");
            return sb.ToString();
        }

        private static SortedDictionary<string, string> EditableBuildingLines(JObject config, JObject state)
        {
            var lines = new SortedDictionary<string, string>(StringComparer.Ordinal);
            AddValue(lines, "Enabled", config["enabled"]);
            AddValue(lines, "Toggle", config["toggle"]);
            foreach (JObject item in config["thresholds"] as JArray ?? new JArray())
            {
                string component = item["component"]?.ToString();
                AddValue(lines, "Threshold." + component + ".Value", item["threshold"]);
                AddValue(lines, "Threshold." + component + ".ActivateAbove", item["activateAbove"]);
            }
            foreach (JObject item in config["sliders"] as JArray ?? new JArray())
                AddValue(lines, "Slider." + item["component"] + "." + item["index"] + ".Value", item["value"]);
            AddValue(lines, "Valve.Flow", config["valve"]?["desiredFlowKgPerSecond"]);
            AddValue(lines, "LimitValve.Limit", config["limitValve"]?["limit"]);
            AddValue(lines, "LogicTimer.OnSeconds", config["timer"]?["onSeconds"]);
            AddValue(lines, "LogicTimer.OffSeconds", config["timer"]?["offSeconds"]);
            AddValue(lines, "LogicTimer.DisplayCycles", config["timer"]?["displayCyclesMode"]);
            AddValue(lines, "Ribbon.SelectedBit", config["ribbonBit"]?["selectedBit"]);
            AddValue(lines, "Door.State", NormalizeDoorState(config["door"]?["requested"]?.ToString()));
            AddValue(lines, "Capacity", state["capacity"]?["userMaxCapacity"]);
            AddValue(lines, "Checkbox", state["checkbox"]?["value"]);
            AddValue(lines, "Counter.Max", state["counter"]?["maxCount"]);
            AddValue(lines, "Counter.Advanced", state["counter"]?["advancedMode"]);
            AddValue(lines, "TimeRange.Start", state["timeRange"]?["start"]);
            AddValue(lines, "TimeRange.Duration", state["timeRange"]?["duration"]);
            return lines;
        }

        private static void AddValue(IDictionary<string, string> lines, string key, JToken value)
        {
            if (value == null || value.Type == JTokenType.Null || string.IsNullOrWhiteSpace(key))
                return;
            lines[key] = value.Type == JTokenType.Boolean
                ? value.Value<bool>().ToString().ToLowerInvariant()
                : Convert.ToString((value as JValue)?.Value ?? value.ToString(), CultureInfo.InvariantCulture);
        }

        private static string NormalizeDoorState(string value)
        {
            string state = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (state == "open") state = "opened";
            return state;
        }

        private static CallToolResult PreflightBuildingDetailEdit(string relative, string search, string replacement)
        {
            if (!TryBuildBuildingSetter(relative, search, replacement, out JObject request, out string error))
                return CallToolResult.Error(error);
            return JsonResult(new JObject { ["ok"] = true, ["phase"] = "preflight", ["request"] = request });
        }

        private static CallToolResult ApplyBuildingDetailEdit(JObject args, string relative, string search, string replacement)
        {
            if (!TryBuildBuildingSetter(relative, search, replacement, out JObject request, out string error))
                return CallToolResult.Error(error);
            var routed = InheritWorldEditorExecutionPolicy(args, request);
            return BuildingControlTools.ControlBuilding().Handler(routed);
        }

        private static bool TryBuildBuildingSetter(string relative, string search, string replacement,
            out JObject request, out string error)
        {
            request = null;
            GameObject go = ResolveBuildingDetailFile(relative, out error);
            if (go == null)
                return false;
            if (!TrySingleCanonicalLine(search, out string key, out string before)
                || !TrySingleCanonicalLine(replacement, out string replacementKey, out string after)
                || !string.Equals(key, replacementKey, StringComparison.Ordinal))
            {
                error = "Building parameter edits must replace exactly one canonical line without changing its key.";
                return false;
            }
            JObject config = BuildingConfigSnapshot(go);
            JObject state = BuildingStateSnapshot(go);
            var current = EditableBuildingLines(config, state);
            if (!current.TryGetValue(key, out string currentValue))
            {
                error = "readonly or unknown building parameter: " + key;
                return false;
            }
            if (!string.Equals(before, currentValue, StringComparison.OrdinalIgnoreCase))
            {
                error = "Building parameter SEARCH value is stale for " + key;
                return false;
            }
            request = new JObject { ["domain"] = "config", ["id"] = config["id"]?.DeepClone() };
            return PopulateBuildingSetter(request, key, after, config, state, out error);
        }

        private static bool TrySingleCanonicalLine(string text, out string key, out string value)
        {
            key = value = null;
            var lines = NormalizeSearchText(text).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            if (lines.Count != 1)
                return false;
            int colon = lines[0].IndexOf(':');
            if (colon <= 0)
                return false;
            key = lines[0].Substring(0, colon).Trim();
            value = lines[0].Substring(colon + 1).Trim();
            return value.Length > 0;
        }

        private static bool PopulateBuildingSetter(JObject request, string key, string value,
            JObject config, JObject state, out string error)
        {
            error = null;
            if (key == "Enabled") return SetBoolRequest(request, "set_enabled", "enabled", value, out error);
            if (key == "Toggle") return SetBoolRequest(request, "set_toggle", "on", value, out error);
            if (key == "Valve.Flow") return SetFloatRequest(request, "set_valve_flow", "flowKgPerSecond", value, out error);
            if (key == "LimitValve.Limit") return SetFloatRequest(request, "set_limit_valve", "limit", value, out error);
            if (key == "Ribbon.SelectedBit") return SetIntRequest(request, "set_logic_ribbon_bit", "bitIndex", value, out error);
            if (key == "Door.State")
            {
                string stateValue = NormalizeDoorState(value);
                if (stateValue != "auto" && stateValue != "opened" && stateValue != "locked")
                {
                    error = "Door.State must be auto, opened, or locked";
                    return false;
                }
                request["action"] = "set_door_state"; request["state"] = stateValue; return true;
            }
            if (key == "Capacity") return SetStateFloatRequest(request, "capacity", "capacity", value, out error);
            if (key == "Checkbox") return SetStateBoolRequest(request, "checkbox", "value", value, out error);
            if (key == "Counter.Max" || key == "Counter.Advanced")
                return PopulateCounterRequest(request, key, value, state, out error);
            if (key == "TimeRange.Start" || key == "TimeRange.Duration")
                return PopulateTimeRangeRequest(request, key, value, state, out error);
            if (key.StartsWith("Threshold.", StringComparison.Ordinal))
                return PopulateThresholdRequest(request, key, value, config, out error);
            if (key.StartsWith("Slider.", StringComparison.Ordinal))
                return PopulateSliderRequest(request, key, value, out error);
            if (key.StartsWith("LogicTimer.", StringComparison.Ordinal))
                return PopulateTimerRequest(request, key, value, config, out error);
            error = "readonly or unknown building parameter: " + key;
            return false;
        }

        private static bool SetBoolRequest(JObject request, string action, string field, string value, out string error)
        {
            error = null;
            if (!bool.TryParse(value, out bool parsed)) { error = field + " must be true or false"; return false; }
            request["action"] = action; request[field] = parsed; return true;
        }

        private static bool SetFloatRequest(JObject request, string action, string field, string value, out string error)
        {
            error = null;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) { error = field + " must be numeric"; return false; }
            request["action"] = action; request[field] = parsed; return true;
        }

        private static bool SetIntRequest(JObject request, string action, string field, string value, out string error)
        {
            error = null;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) { error = field + " must be an integer"; return false; }
            request["action"] = action; request[field] = parsed; return true;
        }

        private static bool SetStateFloatRequest(JObject request, string kind, string field, string value, out string error)
        {
            if (!SetFloatRequest(request, "state_set", field, value, out error)) return false;
            request["kind"] = kind; return true;
        }

        private static bool SetStateBoolRequest(JObject request, string kind, string field, string value, out string error)
        {
            if (!SetBoolRequest(request, "state_set", field, value, out error)) return false;
            request["kind"] = kind; return true;
        }

        private static bool PopulateThresholdRequest(JObject request, string key, string value, JObject config, out string error)
        {
            string component = key.Substring("Threshold.".Length).Replace(".Value", "").Replace(".ActivateAbove", "");
            JObject current = (config["thresholds"] as JArray)?.OfType<JObject>().FirstOrDefault(item => item["component"]?.ToString() == component);
            if (current == null) { error = "threshold component not found: " + component; return false; }
            request["action"] = "set_threshold"; request["component"] = component;
            request["threshold"] = current["threshold"]?.DeepClone(); request["activateAbove"] = current["activateAbove"]?.DeepClone();
            if (key.EndsWith(".Value", StringComparison.Ordinal)) return SetFloatRequest(request, "set_threshold", "threshold", value, out error);
            return SetBoolRequest(request, "set_threshold", "activateAbove", value, out error);
        }

        private static bool PopulateSliderRequest(JObject request, string key, string value, out string error)
        {
            string[] parts = key.Split('.');
            if (parts.Length != 4 || !int.TryParse(parts[2], out int index)) { error = "invalid slider canonical key"; return false; }
            request["component"] = parts[1]; request["index"] = index;
            return SetFloatRequest(request, "set_slider", "value", value, out error);
        }

        private static bool PopulateTimerRequest(JObject request, string key, string value, JObject config, out string error)
        {
            JObject timer = config["timer"] as JObject;
            request["action"] = "set_logic_timer";
            request["onSeconds"] = timer?["onSeconds"]?.DeepClone(); request["offSeconds"] = timer?["offSeconds"]?.DeepClone();
            request["displayCyclesMode"] = timer?["displayCyclesMode"]?.DeepClone();
            if (key == "LogicTimer.OnSeconds") return SetFloatRequest(request, "set_logic_timer", "onSeconds", value, out error);
            if (key == "LogicTimer.OffSeconds") return SetFloatRequest(request, "set_logic_timer", "offSeconds", value, out error);
            return SetBoolRequest(request, "set_logic_timer", "displayCyclesMode", value, out error);
        }

        private static bool PopulateCounterRequest(JObject request, string key, string value, JObject state, out string error)
        {
            JObject counter = state["counter"] as JObject; request["action"] = "state_set"; request["kind"] = "counter";
            request["maxCount"] = counter?["maxCount"]?.DeepClone(); request["advancedMode"] = counter?["advancedMode"]?.DeepClone();
            return key == "Counter.Max" ? SetIntRequest(request, "state_set", "maxCount", value, out error)
                : SetBoolRequest(request, "state_set", "advancedMode", value, out error);
        }

        private static bool PopulateTimeRangeRequest(JObject request, string key, string value, JObject state, out string error)
        {
            JObject range = state["timeRange"] as JObject; request["action"] = "state_set"; request["kind"] = "time_range";
            request["start"] = range?["start"]?.DeepClone(); request["duration"] = range?["duration"]?.DeepClone();
            return SetFloatRequest(request, "state_set", key == "TimeRange.Start" ? "start" : "duration", value, out error);
        }

        private static void AppendBuildingParameterReferences(StringBuilder sb, int xMin, int xMax, int yMin, int yMax)
        {
            var buildings = LiveCompletedBuildings().Select(go => new { Go = go, Cell = Grid.PosToCell(go) })
                .Where(item => Grid.IsValidCell(item.Cell))
                .Where(item => Grid.CellColumn(item.Cell) >= xMin && Grid.CellColumn(item.Cell) <= xMax
                    && Grid.CellRow(item.Cell) >= yMin && Grid.CellRow(item.Cell) <= yMax)
                .OrderBy(item => item.Go.GetComponent<KPrefabID>()?.InstanceID ?? item.Go.GetInstanceID()).ToList();
            if (buildings.Count == 0) return;
            sb.AppendLine("## Building Parameter Files");
            foreach (var item in buildings)
            {
                JObject info = BuildingConfigSnapshot(item.Go); string file = GetBuildingDetailFileName(item.Go);
                sb.AppendLine("- " + info["name"] + " @(" + info["x"] + "," + info["y"] + ") -> [/active/"
                    + BuildingInstancePrefix + file + "](/active/" + BuildingInstancePrefix + file + ")");
            }
            sb.AppendLine();
        }
    }
}
