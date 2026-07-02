using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class SurvivalPlanTools
    {
        private static Dictionary<string, object> ExtractConstructionPlan(Dictionary<string, object> accessPlan)
        {
            object plan;
            return accessPlan != null && accessPlan.TryGetValue("constructionPlan", out plan)
                ? plan as Dictionary<string, object>
                : null;
        }

        private static List<Dictionary<string, object>> BuildNextActions(
            Dictionary<string, object> accessPlan,
            Dictionary<string, object> constructionPlan)
        {
            var actions = new List<Dictionary<string, object>>();
            if (constructionPlan == null)
            {
                AddAccessMapProbe(actions, accessPlan);
                return actions;
            }

            string status = constructionPlan.ContainsKey("status") ? constructionPlan["status"]?.ToString() : null;
            string materialStatus = constructionPlan.ContainsKey("materialStatus")
                ? constructionPlan["materialStatus"]?.ToString()
                : null;

            if (TryAddMaterialAction(
                actions,
                materialStatus,
                "reachable_loose_material_found",
                "materialPrepAction",
                "material_prep",
                "wait_for_delivery_or_storage_then_rerun_survival_plan",
                constructionPlan))
                return actions;

            if (TryAddMaterialAction(
                actions,
                materialStatus,
                "reachable_natural_material_found",
                "materialDigAction",
                "material_dig",
                "wait_for_debris_then_rerun_survival_plan",
                constructionPlan))
                return actions;

            if (TryAddMaterialAction(
                actions,
                materialStatus,
                "reachable_deconstruct_material_found",
                "materialDeconstructAction",
                "material_deconstruct",
                "wait_for_debris_then_rerun_survival_plan",
                constructionPlan))
                return actions;

            if (string.Equals(status, "material_search_required", StringComparison.OrdinalIgnoreCase))
            {
                actions.Add(MaterialSearchAction("SiltStone"));
                actions.Add(MaterialSearchAction("SandStone"));
                AddBlockedActionHints(actions, constructionPlan);
                return actions;
            }

            if (constructionPlan.ContainsKey("buildAction"))
                actions.Add(new Dictionary<string, object>
                {
                    ["kind"] = "build_access",
                    ["action"] = constructionPlan["buildAction"]
                });

            if (actions.Count == 0)
                AddAccessMapProbe(actions, accessPlan);

            return actions;
        }

        private static bool TryAddMaterialAction(
            List<Dictionary<string, object>> actions,
            string materialStatus,
            string expectedStatus,
            string actionKey,
            string kind,
            string then,
            Dictionary<string, object> constructionPlan)
        {
            if (!string.Equals(materialStatus, expectedStatus, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!constructionPlan.ContainsKey(actionKey))
                return false;

            actions.Add(new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["action"] = constructionPlan[actionKey],
                ["then"] = then
            });
            return true;
        }

        private static void AddAccessMapProbe(
            List<Dictionary<string, object>> actions,
            Dictionary<string, object> accessPlan)
        {
            if (accessPlan == null || !accessPlan.TryGetValue("target", out var targetValue))
                return;
            if (!(targetValue is Dictionary<string, object> target))
                return;
            if (!target.ContainsKey("x") || !target.ContainsKey("y"))
                return;

            int x = Convert.ToInt32(target["x"]);
            int y = Convert.ToInt32(target["y"]);
            int worldId = target.ContainsKey("worldId") ? Convert.ToInt32(target["worldId"]) : 0;
            string status = accessPlan.ContainsKey("status") ? accessPlan["status"]?.ToString() : "no_construction_plan";

            actions.Add(new Dictionary<string, object>
            {
                ["kind"] = "access_map_probe",
                ["reason"] = status,
                ["action"] = new Dictionary<string, object>
                {
                    ["tool"] = "read_control",
                    ["arguments"] = new Dictionary<string, object>
                    {
                        ["domain"] = "world",
                        ["action"] = "area_snapshot",
                        ["x1"] = x - 6,
                        ["y1"] = y - 12,
                        ["x2"] = x + 6,
                        ["y2"] = y + 4,
                        ["worldId"] = worldId,
                        ["maxCells"] = 260
                    }
                },
                ["then"] = "choose_safe_ladder_or_dig_route_then_rerun_survival_plan"
            });
        }

        private static void AddBlockedActionHints(
            List<Dictionary<string, object>> actions,
            Dictionary<string, object> constructionPlan)
        {
            if (!constructionPlan.TryGetValue("blockedActions", out var value))
                return;
            if (!(value is IEnumerable<object> blockedActions))
                return;

            foreach (var item in blockedActions)
            {
                if (!(item is Dictionary<string, object> blocked))
                    continue;
                if (!blocked.TryGetValue("action", out var action))
                    continue;
                actions.Add(new Dictionary<string, object>
                {
                    ["kind"] = "blocked_action_probe",
                    ["disabledReason"] = blocked.ContainsKey("disabledReason") ? blocked["disabledReason"] : null,
                    ["action"] = action,
                    ["then"] = "Run as dryRun only; execute only if the result reports actionable normal-game work."
                });
            }
        }

        private static Dictionary<string, object> MaterialSearchAction(string query)
        {
            return new Dictionary<string, object>
            {
                ["kind"] = "material_search",
                ["action"] = new Dictionary<string, object>
                {
                    ["tool"] = "read_control",
                    ["arguments"] = new Dictionary<string, object>
                    {
                        ["domain"] = "resources",
                        ["action"] = "search_items",
                        ["query"] = query,
                        ["includeStored"] = false,
                        ["limit"] = 20
                    }
                }
            };
        }
    }
}
