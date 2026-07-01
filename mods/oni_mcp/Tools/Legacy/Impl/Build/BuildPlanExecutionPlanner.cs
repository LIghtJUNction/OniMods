using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> BuildExecutionPlan(
            string intent,
            string prefabId,
            string material,
            string anchorQuery,
            List<PlanSequenceItem> buildingItems,
            List<PlanSequenceItem> materialItems,
            List<PlanSequenceItem> worldItems)
        {
            var plan = new Dictionary<string, object>
            {
                ["intent"] = intent,
                ["oneCall"] = OneCallTemplate(prefabId, material, anchorQuery),
                ["requiresAnchorSearch"] = worldItems != null && worldItems.Count > 0,
                ["requiresMaterialSearch"] = string.IsNullOrWhiteSpace(material) && materialItems != null && materialItems.Count > 0,
                ["anchorSearch"] = WorldSearchTemplate(worldItems),
                ["buildAfterAnchor"] = BuildAfterAnchorTemplate(prefabId, material),
                ["buildSteps"] = BuildExecutionSteps(buildingItems, materialItems),
                ["tokenHint"] = "Use oneCall when query is enough; use anchorSearch then buildAfterAnchor when sequence contains world cells."
            };

            return plan;
        }

        private static Dictionary<string, object> OneCallTemplate(string prefabId, string material, string anchorQuery)
        {
            return new Dictionary<string, object>
            {
                ["tool"] = "building_control",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["domain"] = "planning",
                    ["action"] = "build_area",
                    ["prefabId"] = prefabId,
                    ["material"] = material,
                    ["query"] = anchorQuery,
                    ["dryRun"] = true
                }
            };
        }

        private static Dictionary<string, object> WorldSearchTemplate(List<PlanSequenceItem> worldItems)
        {
            if (worldItems == null || worldItems.Count == 0)
                return null;

            return new Dictionary<string, object>
            {
                ["tool"] = "read_control",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["domain"] = "world",
                    ["action"] = "search",
                    ["pattern"] = string.Join("-", worldItems.Select(item => item.ElementId)),
                    ["sequence"] = string.Join("-", worldItems.Select(item => item.Token)),
                    ["matchMode"] = "smart",
                    ["direction"] = "both",
                    ["limit"] = 5
                }
            };
        }

        private static Dictionary<string, object> BuildAfterAnchorTemplate(string prefabId, string material)
        {
            return new Dictionary<string, object>
            {
                ["tool"] = "building_control",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["domain"] = "planning",
                    ["action"] = "build_area",
                    ["prefabId"] = prefabId,
                    ["material"] = material,
                    ["anchor"] = "use first anchorSearch match cell x,y",
                    ["dryRun"] = true
                }
            };
        }

        private static List<Dictionary<string, object>> BuildExecutionSteps(
            List<PlanSequenceItem> buildingItems,
            List<PlanSequenceItem> materialItems)
        {
            var steps = new List<Dictionary<string, object>>();
            if (buildingItems == null)
                return steps;

            foreach (var item in buildingItems)
            {
                var stepMaterial = item.Material;
                if (string.IsNullOrWhiteSpace(stepMaterial) && materialItems != null && materialItems.Count == 1)
                    stepMaterial = materialItems[0].Material;

                steps.Add(new Dictionary<string, object>
                {
                    ["tool"] = "building_control",
                    ["arguments"] = new Dictionary<string, object>
                    {
                        ["domain"] = "planning",
                        ["action"] = "build_area",
                        ["prefabId"] = item.PrefabId,
                        ["material"] = stepMaterial,
                        ["dryRun"] = true
                    }
                });
            }

            return steps;
        }
    }
}
