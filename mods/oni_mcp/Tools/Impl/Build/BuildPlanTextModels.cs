using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private sealed class BuildPlanResolution
        {
            public string Input;
            public List<string> Tokens = new List<string>();
            public string PrefabId;
            public string BuildingName;
            public string Material;
            public string MaterialName;
            public string AnchorQuery;
            public List<PlanBuildingCandidate> BuildingCandidates = new List<PlanBuildingCandidate>();
            public List<PlanMaterialCandidate> MaterialCandidates = new List<PlanMaterialCandidate>();
            public List<PlanSequenceItem> SequenceItems = new List<PlanSequenceItem>();

            public Dictionary<string, object> ToDictionary()
            {
                var primaryItem = SequenceItems.FirstOrDefault(item => item.IsKind("building"))
                    ?? SequenceItems.FirstOrDefault(item => item.IsKind("material"))
                    ?? SequenceItems.FirstOrDefault(item => item.IsKind("world"))
                    ?? SequenceItems.FirstOrDefault();
                var buildingItems = SequenceItems.Where(item => item.IsKind("building") && !string.IsNullOrWhiteSpace(item.PrefabId)).ToList();
                var materialItems = SequenceItems.Where(item => item.IsKind("material") && !string.IsNullOrWhiteSpace(item.Material)).ToList();
                var worldItems = SequenceItems.Where(item => item.IsKind("world") && !string.IsNullOrWhiteSpace(item.ElementId)).ToList();
                string intent = BuildSequenceIntent(SequenceItems, buildingItems, materialItems, worldItems);

                var result = new Dictionary<string, object>
                {
                    ["input"] = Input,
                    ["tokens"] = Tokens,
                    ["resolved"] = new Dictionary<string, object>
                    {
                        ["prefabId"] = PrefabId,
                        ["buildingName"] = BuildingName,
                        ["material"] = Material,
                        ["materialName"] = MaterialName,
                        ["query"] = AnchorQuery
                    },
                    ["parsedArgs"] = new Dictionary<string, object>
                    {
                        ["prefabId"] = PrefabId,
                        ["material"] = Material,
                        ["query"] = AnchorQuery
                    },
                ["actionTemplate"] = BuildActionTemplate(intent, PrefabId, Material, AnchorQuery, worldItems),
                ["executionPlan"] = BuildExecutionPlan(intent, PrefabId, Material, AnchorQuery, buildingItems, materialItems, worldItems),
                    ["buildingCandidates"] = BuildingCandidates.Select(item => item.ToDictionary()).ToList(),
                    ["materialCandidates"] = MaterialCandidates.Select(item => item.ToDictionary()).ToList(),
                    ["sequenceItems"] = SequenceItems.Select(item => item.ToDictionary()).ToList()
                };

                if (primaryItem != null)
                    result["primaryItem"] = primaryItem.ToDictionary();

                result["sequenceSummary"] = new Dictionary<string, object>
                {
                    ["intent"] = intent,
                    ["count"] = SequenceItems.Count,
                    ["buildingCount"] = buildingItems.Count,
                    ["materialCount"] = materialItems.Count,
                    ["worldCellCount"] = worldItems.Count,
                    ["unknownCount"] = SequenceItems.Count(item => item.IsKind("unknown")),
                    ["kinds"] = SequenceItems.Select(item => item.Kind).ToList(),
                    ["prefabIds"] = buildingItems.Select(item => item.PrefabId).Distinct().ToList(),
                    ["materials"] = materialItems.Select(item => item.Material).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().ToList(),
                    ["elements"] = worldItems.Select(item => item.ElementId).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().ToList(),
                    ["pattern"] = worldItems.Count == SequenceItems.Count && worldItems.Count > 0
                        ? string.Join("-", worldItems.Select(item => item.ElementId))
                        : null,
                    ["buildSteps"] = BuildStepSummaries(buildingItems, materialItems)
                };

                result["next"] = NextHint(intent);
                result["tokenHint"] = "Read resolved, sequenceSummary.intent, actionTemplate, then sequenceItems only if ambiguous.";
                return result;
            }

            private static string BuildSequenceIntent(List<PlanSequenceItem> items, List<PlanSequenceItem> buildingItems, List<PlanSequenceItem> materialItems, List<PlanSequenceItem> worldItems)
            {
                if (items == null || items.Count == 0)
                    return "empty";
                if (worldItems.Count == items.Count)
                    return worldItems.Count > 1 ? "world_pattern" : "world_cell";
                if (buildingItems.Count > 0 && worldItems.Count == 0)
                    return buildingItems.Count > 1 ? "build_chain" : "build_request";
                if (buildingItems.Count > 0 && worldItems.Count > 0)
                    return "build_request_with_anchor_pattern";
                if (materialItems.Count > 0 && worldItems.Count > 0)
                    return "material_near_world";
                if (materialItems.Count == items.Count)
                    return materialItems.Count > 1 ? "material_sequence" : "material";
                return "mixed_sequence";
            }

            private static Dictionary<string, object> BuildActionTemplate(string intent, string prefabId, string material, string anchorQuery, List<PlanSequenceItem> worldItems)
            {
                if (intent == "world_pattern" || intent == "world_cell")
                {
                    return new Dictionary<string, object>
                    {
                        ["tool"] = "read_control",
                        ["domain"] = "world",
                        ["action"] = "search",
                        ["sequence"] = string.Join("-", worldItems.Select(item => item.Token)),
                        ["pattern"] = string.Join("-", worldItems.Select(item => item.ElementId)),
                        ["matchMode"] = "smart",
                        ["direction"] = "both"
                    };
                }

                return new Dictionary<string, object>
                {
                    ["tool"] = "building_control",
                    ["domain"] = "planning",
                    ["action"] = "build_area",
                    ["prefabId"] = prefabId,
                    ["material"] = material,
                    ["query"] = anchorQuery,
                    ["dryRun"] = true
                };
            }

            private static List<Dictionary<string, object>> BuildStepSummaries(List<PlanSequenceItem> buildingItems, List<PlanSequenceItem> materialItems)
            {
                var steps = new List<Dictionary<string, object>>();
                foreach (var item in buildingItems)
                {
                    var material = item.Material;
                    if (string.IsNullOrWhiteSpace(material) && materialItems.Count == 1)
                        material = materialItems[0].Material;
                    steps.Add(new Dictionary<string, object>
                    {
                        ["index"] = item.Index,
                        ["prefabId"] = item.PrefabId,
                        ["buildingName"] = item.BuildingName,
                        ["material"] = material,
                        ["token"] = item.Token
                    });
                }
                return steps;
            }

            private static string NextHint(string intent)
            {
                switch (intent)
                {
                    case "world_pattern":
                    case "world_cell":
                        return "Use actionTemplate with read_control to locate matching cells, then use a returned cell as anchor.";
                    case "build_chain":
                        return "Use sequenceSummary.buildSteps in order; each step can feed building_control domain=planning action=build_area dryRun=true.";
                    case "build_request_with_anchor_pattern":
                        return "Locate the world pattern first if query is empty, then build the parsed prefab/material at the chosen anchor.";
                    default:
                        return "Use parsedArgs directly with building_control domain=planning action=build_area dryRun=true, or inspect sequenceItems if ambiguous.";
                }
            }
        }

        private sealed class PlanSequenceItem
        {
            public int Index;
            public string Token;
            public string Kind;
            public string Hint;
            public string PrefabId;
            public string BuildingName;
            public string Material;
            public string MaterialName;
            public string ElementId;
            public string CandidateElementId;
            public string MatchSummary;
            public string PreferredAction;
            public Dictionary<string, object> ActionArgs = new Dictionary<string, object>();
            public List<PlanBuildingCandidate> BuildingCandidates = new List<PlanBuildingCandidate>();
            public List<PlanMaterialCandidate> MaterialCandidates = new List<PlanMaterialCandidate>();

            public bool IsKind(string kind)
            {
                return string.Equals(Kind, kind, StringComparison.OrdinalIgnoreCase);
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["index"] = Index,
                    ["token"] = Token,
                    ["kind"] = Kind,
                    ["hint"] = Hint,
                    ["prefabId"] = PrefabId,
                    ["buildingName"] = BuildingName,
                    ["material"] = Material,
                    ["materialName"] = MaterialName,
                    ["elementId"] = ElementId,
                    ["candidateElementId"] = CandidateElementId,
                    ["matchSummary"] = MatchSummary,
                    ["preferredAction"] = PreferredAction,
                    ["parsedArgs"] = new Dictionary<string, object>
                    {
                        ["prefabId"] = PrefabId,
                        ["material"] = Material,
                        ["elementId"] = ElementId
                    },
                    ["actionArgs"] = ActionArgs,
                    ["buildingCandidates"] = BuildingCandidates.Take(3).Select(item => item.ToDictionary()).ToList(),
                    ["materialCandidates"] = MaterialCandidates.Take(3).Select(item => item.ToDictionary()).ToList()
                };
            }
        }

        private sealed class PlanBuildingCandidate
        {
            public string PrefabId;
            public string Name;
            public int Score;
            public string Matched;
            public string MatchKind;
            public bool AvailableNow;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["prefabId"] = PrefabId,
                    ["name"] = Name,
                    ["score"] = Score,
                    ["matched"] = Matched,
                    ["matchKind"] = MatchKind,
                    ["availableNow"] = AvailableNow
                };
            }
        }

        private sealed class PlanMaterialCandidate
        {
            public string Tag;
            public string Name;
            public int Score;
            public string Matched;
            public float AvailableKg;
            public bool ValidForBuilding;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["tag"] = Tag,
                    ["name"] = Name,
                    ["score"] = Score,
                    ["matched"] = Matched,
                    ["availableKg"] = Math.Round(ToolUtil.SafeFloat(AvailableKg), 3),
                    ["validForBuilding"] = ValidForBuilding
                };
            }
        }
    }
}
