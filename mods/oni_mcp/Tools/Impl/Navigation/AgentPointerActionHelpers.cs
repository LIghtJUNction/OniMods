using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class AgentPointerTools
    {
        private sealed class PointerLookup
        {
            public AgentPointerState State;
            public string Error;
        }

        private static JObject CloneWithoutControlAction(JObject args)
        {
            var clone = args != null ? (JObject)args.DeepClone() : new JObject();
            clone.Remove("action");
            return clone;
        }

        private static PointerLookup RequirePointer(string agentId)
        {
            var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, agentId);
            if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                return new PointerLookup { Error = "Pointer is not aimed at a valid cell; call navigation_control action=aim_cell first" };
            return new PointerLookup { State = pointer };
        }

        private static string NormalizeActionTool(string tool)
        {
            tool = (tool ?? "inspect").Trim().ToLowerInvariant();
            switch (tool)
            {
                case "build":
                case "dig":
                case "cancel":
                case "sweep":
                case "mop":
                case "disinfect":
                case "harvest":
                case "deconstruct":
                case "inspect":
                    return tool;
                case "mine":
                case "excavate":
                    return "dig";
                case "erase":
                case "remove":
                    return "cancel";
                case "clean":
                    return "sweep";
                case "destruct":
                case "拆除":
                    return "deconstruct";
                default:
                    return "inspect";
            }
        }

        private static CallToolResult ExecuteSelectedTool(AgentPointerState pointer, int x1, int y1, int x2, int y2, JObject sourceArgs, bool isDrag)
        {
            string tool = NormalizeActionTool(pointer.CurrentTool);
            if (tool == "inspect")
            {
                return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["action"] = isDrag ? "hold_left" : "left_click",
                    ["tool"] = "inspect",
                    ["message"] = "No modifying tool selected"
                }, McpJsonUtil.Settings));
            }

            var args = CloneActionArgs(sourceArgs);
            int worldId = pointer.WorldId >= 0 ? pointer.WorldId : ToolUtil.ResolveWorldId(args);
            args["worldId"] = worldId;
            args["confirm"] = true;
            if (args["priority"] == null)
                args["priority"] = pointer.Priority;

            if (tool == "build")
            {
                if (string.IsNullOrWhiteSpace(pointer.BuildPrefabId))
                    return CallToolResult.Error("Current pointer tool is build but no prefabId is selected; call navigation_control action=select_tool first");

                args["agentId"] = pointer.AgentId;
                args["prefabId"] = pointer.BuildPrefabId;
                if (args["material"] == null && !string.IsNullOrWhiteSpace(pointer.BuildMaterial))
                    args["material"] = pointer.BuildMaterial;
                if (args["facade"] == null && !string.IsNullOrWhiteSpace(pointer.BuildFacade))
                    args["facade"] = pointer.BuildFacade;

                return isDrag
                    ? BuildPlanningTools.DragLineFromPointer(args)
                    : BuildPlanningTools.PlanAtPointer(args);
            }

            SetRect(args, x1, y1, x2, y2);
            switch (tool)
            {
                case "dig":
                    return OrdersTools.DigArea().Handler(args);
                case "cancel":
                    return OrdersTools.CancelArea().Handler(args);
                case "sweep":
                    return OrdersTools.SweepArea().Handler(args);
                case "mop":
                    return OrdersTools.MopArea().Handler(args);
                case "disinfect":
                    return OrdersTools.DisinfectArea().Handler(args);
                case "harvest":
                    return OrdersTools.HarvestArea().Handler(args);
                case "deconstruct":
                    return ExecuteDeconstructLine(x1, y1, x2, y2, args, isDrag);
                default:
                    return CallToolResult.Error("Unsupported pointer tool: " + tool);
            }
        }

        private static JObject CloneActionArgs(JObject sourceArgs)
        {
            var args = sourceArgs == null ? new JObject() : (JObject)sourceArgs.DeepClone();
            args.Remove("agentId");
            return args;
        }

        private static void SetRect(JObject args, int x1, int y1, int x2, int y2)
        {
            args["x1"] = Math.Min(x1, x2);
            args["y1"] = Math.Min(y1, y2);
            args["x2"] = Math.Max(x1, x2);
            args["y2"] = Math.Max(y1, y2);
        }

        private static CallToolResult ExecuteDeconstructLine(int x1, int y1, int x2, int y2, JObject sourceArgs, bool isDrag)
        {
            if (!isDrag)
            {
                var single = CloneActionArgs(sourceArgs);
                single["x"] = x1;
                single["y"] = y1;
                single["confirm"] = true;
                return OrdersTools.DeconstructBuilding().Handler(single);
            }

            int count = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) + 1;
            if (count > 200)
                return CallToolResult.Error("Refusing to deconstruct more than 200 cells");

            int queued = 0;
            int failed = 0;
            var results = new List<Dictionary<string, object>>();
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : (float)i / (count - 1);
                int x = Mathf.RoundToInt(Mathf.Lerp(x1, x2, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(y1, y2, t));
                var args = CloneActionArgs(sourceArgs);
                args["x"] = x;
                args["y"] = y;
                args["confirm"] = true;
                var result = OrdersTools.DeconstructBuilding().Handler(args);
                if (result.IsError)
                    failed++;
                else
                    queued++;
                results.Add(new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["ok"] = !result.IsError,
                    ["text"] = ResultText(result)
                });
            }

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["tool"] = "deconstruct",
                ["queued"] = queued,
                ["failed"] = failed,
                ["results"] = results
            }, McpJsonUtil.Settings));
        }

        private static CallToolResult WrapActionResult(AgentPointerState pointer, CallToolResult result)
        {
            var parsed = ParseResultPayload(result);
            bool isError = result == null || result.IsError;
            bool ok = !isError && ActionSucceeded(parsed);
            var payload = new Dictionary<string, object>
            {
                ["ok"] = ok,
                ["actionSucceeded"] = ok,
                ["pointer"] = pointer != null ? pointer.ToDictionary() : null,
                ["result"] = new Dictionary<string, object>
                {
                    ["isError"] = isError,
                    ["json"] = parsed,
                    ["text"] = parsed == null ? ResultText(result) : null
                }
            };
            AddPlacementOutcome(payload, parsed);
            string text = JsonConvert.SerializeObject(payload, McpJsonUtil.Settings);
            return isError || !ok ? CallToolResult.Error(text) : CallToolResult.Text(text);
        }

        private static JObject ParseResultPayload(CallToolResult result)
        {
            string text = ResultText(result);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                return JObject.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        private static bool ActionSucceeded(JObject payload)
        {
            if (payload == null)
                return true;
            if (payload["planned"] != null)
                return ToolUtil.GetBool(payload, "dryRun", false)
                    ? GetTruthyResultCount(payload, "valid") > 0
                    : (GetTruthyResultCount(payload, "planned") > 0 || GetTruthyResultCount(payload, "valid") > 0);
            if (payload["valid"] != null && payload["error"] != null)
                return GetTruthyResultCount(payload, "valid") > 0;
            return true;
        }

        private static void AddPlacementOutcome(Dictionary<string, object> payload, JObject resultPayload)
        {
            if (resultPayload == null || resultPayload["planned"] == null)
                return;

            bool dryRun = ToolUtil.GetBool(resultPayload, "dryRun", false);
            bool planned = GetTruthyResultCount(resultPayload, "planned") > 0;
            payload["blueprintPlaced"] = !dryRun && planned;
            payload["actualAnchor"] = ExtractActualAnchor(resultPayload);
            payload["expectedAnchor"] = new[] { ToolUtil.GetInt(resultPayload, "x") ?? -1, ToolUtil.GetInt(resultPayload, "y") ?? -1 };
            payload["placementReason"] = planned
                ? "placed"
                : ClassifyPlacementFailure(resultPayload);
        }

        private static int GetTruthyResultCount(JObject payload, string key)
        {
            if (payload == null || payload[key] == null)
                return 0;

            bool boolValue;
            if (bool.TryParse(payload[key].ToString(), out boolValue))
                return boolValue ? 1 : 0;

            int intValue;
            if (int.TryParse(payload[key].ToString(), out intValue))
                return intValue;

            return 0;
        }

        private static object ExtractActualAnchor(JObject resultPayload)
        {
            var actual = resultPayload["actualPlacement"] as JObject;
            if (actual == null)
                return null;
            int x = ToolUtil.GetInt(actual, "derivedAnchorX") ?? -1;
            int y = ToolUtil.GetInt(actual, "derivedAnchorY") ?? -1;
            return new[] { x, y };
        }

        private static string ClassifyPlacementFailure(JObject resultPayload)
        {
            string error = resultPayload["error"]?.ToString() ?? "";
            string detailText = resultPayload["details"]?.ToString(Formatting.None) ?? "";
            if (error.IndexOf("Unsupported", StringComparison.OrdinalIgnoreCase) >= 0)
                return "unsupported";
            if (error.IndexOf("Invalid footprint", StringComparison.OrdinalIgnoreCase) >= 0)
                return "invalidFloor";
            if (error.IndexOf("Obstructed", StringComparison.OrdinalIgnoreCase) >= 0
                || detailText.IndexOf("obstructions", StringComparison.OrdinalIgnoreCase) >= 0)
                return "obstructed";
            if (error.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0)
                return "unavailableMaterial";
            if (error.IndexOf("not visible", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("not in worldId", StringComparison.OrdinalIgnoreCase) >= 0)
                return "invalidCell";
            return string.IsNullOrWhiteSpace(error) ? "unknown" : "failed";
        }

        private static string ResultText(CallToolResult result)
        {
            if (result == null || result.Content == null || result.Content.Count == 0 || result.Content[0] == null)
                return "";
            return result.Content[0].Text ?? "";
        }

    }
}
