using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
        private const int CancelEvent = 2127324410;

        public static McpTool ControlOrders()
        {
            return new McpTool
            {
                Name = "orders_control",
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "orders", "orders_unified_control", "orders_action_control", "map_orders_control" },
Tags = new List<string> { "orders", "priority", "area", "designation", "dig", "sweep", "mop", "disinfect", "deconstruct", "capture" },
Description = "Unified orders entrypoint. Supports priority plus work orders: area dig/sweep/mop/disinfect/cancel/harvest and designation deconstruct/attack/capture(wrangle)/empty_conduits/cut_conduits/manual_delivery. Chinese map-edit verbs are supported in operation files: 挖/扫/擦/毒/消/收/拆/杀/捕.",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "priority, area, or designation. Omit to infer from action.", Required = false, EnumValues = new List<string> { "priority", "area", "designation" } },
["action"] = new McpToolParameter { Type = "string", Description = "priority: list/set_building/set_area; area: dig/sweep(扫)/mop(擦)/disinfect(毒|消毒)/cancel(消)/harvest(收); designation: deconstruct(拆)/attack(杀)/capture|wrangle(捕)/empty_conduits|empty_pipe/cut_conduits|cut/manual_delivery.", Required = true },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "Single target InstanceID.", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "Single target X for designation actions.", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "Single target Y for designation actions.", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "empty_conduits/cut_conduits layer type when needed.", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "Priority 1-9.", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "Set top priority where supported.", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "mark/cancel for harvest, attack, capture, or manual delivery actions.", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "Reusable area handle for area/priority actions.", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "priority list/set_area: include inactive objects.", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "sweep: include already stored items.", Required = false },
                    ["includeAttack"] = new McpToolParameter { Type = "boolean", Description = "cancel: also cancel attack markers.", Required = false },
                    ["includeCapture"] = new McpToolParameter { Type = "boolean", Description = "cancel: also cancel capture markers.", Required = false },
                    ["readyOnly"] = new McpToolParameter { Type = "boolean", Description = "harvest mark: only ready harvestables.", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Preview without execution where supported.", Required = false },
                    ["previewToken"] = new McpToolParameter { Type = "string", Description = "Preview token returned by dryRun.", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "attack: allow friendly/assistant targets.", Required = false },
                    ["attackAreaConfirm"] = new McpToolParameter { Type = "string", Description = "attack area confirmation, must be exactly attack area.", Required = false },
                    ["paused"] = new McpToolParameter { Type = "boolean", Description = "manual_delivery pause/resume.", Required = false },
                    ["capacityKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery target capacity kg.", Required = false },
                    ["refillMassKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery refill threshold kg.", Required = false },
                    ["minimumMassKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery minimum delivery mass kg.", Required = false },
                    ["requestNow"] = new McpToolParameter { Type = "boolean", Description = "manual_delivery request one delivery now.", Required = false },
                    ["detail"] = new McpToolParameter { Type = "boolean", Description = "Return per-cell samples where supported; prefer summaries by default.", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "Maximum returned diagnostics/items.", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "Required for dangerous or large-area actions by child tool rules.", Required = false }
                }),
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(domain))
                        domain = InferOrdersDomain((args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant());

                    if (domain == "priority" || domain == "priorities")
                        return ControlPriority().Handler(args);
                    if (domain == "area" || domain == "area_action")
                        return AreaAction().Handler(args);
                    if (domain == "designation" || domain == "designate")
                        return DesignationControl().Handler(args);
                    return CallToolResult.Error("domain must be priority, area, or designation");
                }
            };
        }

        private static string InferOrdersDomain(string action)
        {
            switch (action)
            {
                case "dig":
                case "sweep":
                case "mop":
                case "disinfect":
                case "cancel":
                case "harvest":
                    return "area";
                case "list":
                case "set":
                case "set_building":
                case "set_area":
                case "area":
                    return "priority";
                case "deconstruct":
                case "deconstruct_building":
                case "attack":
                case "capture":
                case "wrangle":
                case "empty_conduits":
                case "empty_pipe":
                case "cut_conduits":
                case "cut":
                case "manual_delivery":
                case "delivery":
                    return "designation";
                default:
                    return "";
            }
        }
    }
}
