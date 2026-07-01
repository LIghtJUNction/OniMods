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

        private static List<Dictionary<string, object>> BuildNextActions(Dictionary<string, object> constructionPlan)
        {
            var actions = new List<Dictionary<string, object>>();
            if (constructionPlan == null)
                return actions;

            string status = constructionPlan.ContainsKey("status") ? constructionPlan["status"]?.ToString() : null;
            string materialStatus = constructionPlan.ContainsKey("materialStatus") ? constructionPlan["materialStatus"]?.ToString() : null;

            if (string.Equals(materialStatus, "reachable_loose_material_found", StringComparison.OrdinalIgnoreCase)
                && constructionPlan.ContainsKey("materialPrepAction"))
            {
                actions.Add(new Dictionary<string, object>
                {
                    ["kind"] = "material_prep",
                    ["action"] = constructionPlan["materialPrepAction"],
                    ["then"] = "wait_for_delivery_or_storage_then_rerun_survival_plan"
                });
                return actions;
            }

            if (string.Equals(materialStatus, "reachable_natural_material_found", StringComparison.OrdinalIgnoreCase)
                && constructionPlan.ContainsKey("materialDigAction"))
            {
                actions.Add(new Dictionary<string, object>
                {
                    ["kind"] = "material_dig",
                    ["action"] = constructionPlan["materialDigAction"],
                    ["then"] = "wait_for_debris_then_rerun_survival_plan"
                });
                return actions;
            }

            if (string.Equals(status, "material_search_required", StringComparison.OrdinalIgnoreCase))
            {
                actions.Add(MaterialSearchAction("SiltStone"));
                actions.Add(MaterialSearchAction("SandStone"));
                return actions;
            }

            if (constructionPlan.ContainsKey("buildAction"))
                actions.Add(new Dictionary<string, object> { ["kind"] = "build_access", ["action"] = constructionPlan["buildAction"] });
            return actions;
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
