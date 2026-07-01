using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static IEnumerable<PlanSequenceItem> ResolvePlanSequenceItemsV2(List<string> tokens, int worldId, int limit)
        {
            if (tokens == null)
                yield break;

            var prepared = tokens
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(token => BuildSequenceCandidate(token, worldId, limit))
                .Where(item => item != null)
                .ToList();
            bool sequenceHasExplicitBuilding = prepared.Any(item =>
                string.Equals(item.Hint, "building", StringComparison.OrdinalIgnoreCase)
                || (item.BestBuilding != null && string.IsNullOrWhiteSpace(item.ElementId)));
            bool sequenceHasWorldPattern = !sequenceHasExplicitBuilding
                && prepared.Count > 1
                && prepared.Count(item => !string.IsNullOrWhiteSpace(item.ElementId)) >= Math.Max(2, prepared.Count - 1);

            int index = 0;
            foreach (var candidate in prepared)
            {
                string kind = ChooseSequenceKind(candidate, sequenceHasExplicitBuilding, sequenceHasWorldPattern);
                string prefabId = kind == "building" ? candidate.BestBuilding?.PrefabId : null;
                string material = kind == "building" || kind == "material" ? candidate.BestMaterial?.Tag : null;
                string elementId = kind == "world" ? candidate.ElementId : null;

                yield return new PlanSequenceItem
                {
                    Index = index,
                    Token = candidate.RawToken,
                    Kind = kind,
                    Hint = candidate.Hint,
                    PrefabId = prefabId,
                    BuildingName = kind == "building" ? candidate.BestBuilding?.Name : null,
                    Material = material,
                    MaterialName = kind == "building" || kind == "material" ? candidate.BestMaterial?.Name : null,
                    ElementId = elementId,
                    CandidateElementId = candidate.ElementId,
                    MatchSummary = BuildSequenceMatchSummary(kind, candidate.BestBuilding, candidate.BestMaterial, candidate.ElementId),
                    PreferredAction = PreferredSequenceAction(kind),
                    ActionArgs = BuildSequenceActionArgs(kind, prefabId, material, elementId, candidate.RawToken),
                    BuildingCandidates = candidate.Buildings,
                    MaterialCandidates = candidate.Materials
                };
                index++;
            }
        }

        private sealed class SequenceCandidate
        {
            public string RawToken;
            public string Token;
            public string Hint;
            public List<PlanBuildingCandidate> Buildings;
            public PlanBuildingCandidate BestBuilding;
            public List<PlanMaterialCandidate> Materials;
            public PlanMaterialCandidate BestMaterial;
            public string ElementId;
        }

        private static SequenceCandidate BuildSequenceCandidate(string rawToken, int worldId, int limit)
        {
            string hint;
            string token = StripSequenceKindHint(rawToken, out hint);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var terms = SplitPlanTerms(token).ToList();
            if (string.Equals(hint, "building", StringComparison.OrdinalIgnoreCase))
                terms.Insert(0, token);

            var buildings = ResolveBuildingCandidates(null, token, terms, includeUnavailable: true)
                .Take(limit)
                .ToList();
            var bestBuilding = buildings.FirstOrDefault();
            var materialDef = bestBuilding != null ? Assets.GetBuildingDef(bestBuilding.PrefabId) : Assets.GetBuildingDef("Tile");
            var materials = ResolveMaterialCandidates(materialDef, null, token, terms, worldId)
                .Take(limit)
                .ToList();

            return new SequenceCandidate
            {
                RawToken = rawToken,
                Token = token,
                Hint = hint,
                Buildings = buildings,
                BestBuilding = bestBuilding,
                Materials = materials,
                BestMaterial = materials.FirstOrDefault(),
                ElementId = ResolvePlanElementId(token)
            };
        }

        private static string StripSequenceKindHint(string rawToken, out string hint)
        {
            hint = null;
            string token = rawToken.Trim();
            int split = token.IndexOf(':');
            if (split < 0)
                split = token.IndexOf('：');
            if (split <= 0 || split + 1 >= token.Length)
                return token;

            string prefix = NormalizePlanText(token.Substring(0, split));
            switch (prefix)
            {
                case "building":
                case "build":
                case "prefab":
                case "建筑":
                case "建筑物":
                    hint = "building";
                    break;
                case "material":
                case "mat":
                case "材料":
                case "建材":
                    hint = "material";
                    break;
                case "element":
                case "cell":
                case "world":
                case "元素":
                case "格子":
                case "地形":
                    hint = "world";
                    break;
                default:
                    return token;
            }
            return token.Substring(split + 1).Trim();
        }

        private static string ChooseSequenceKind(SequenceCandidate candidate, bool sequenceHasExplicitBuilding, bool sequenceHasWorldPattern)
        {
            string hint = candidate.Hint;
            if (!string.IsNullOrWhiteSpace(hint))
                return hint;
            if (sequenceHasWorldPattern && !string.IsNullOrWhiteSpace(candidate.ElementId))
                return "world";
            if (sequenceHasExplicitBuilding && candidate.BestMaterial != null && !IsStrongBuildingMatch(candidate.BestBuilding))
                return "material";
            if (IsStrongBuildingMatch(candidate.BestBuilding)
                && (candidate.BestMaterial == null || candidate.BestBuilding.Score >= candidate.BestMaterial.Score))
                return "building";
            if (candidate.BestMaterial != null)
                return "material";
            if (!string.IsNullOrWhiteSpace(candidate.ElementId))
                return "world";
            return "unknown";
        }

        private static bool IsStrongBuildingMatch(PlanBuildingCandidate building)
        {
            return building != null && building.Score >= 700;
        }

        private static string BuildSequenceMatchSummary(string kind, PlanBuildingCandidate building, PlanMaterialCandidate material, string elementId)
        {
            switch (kind)
            {
                case "building":
                    return building == null ? "building:unresolved" : "building:" + building.PrefabId;
                case "material":
                    return material == null ? "material:unresolved" : "material:" + material.Tag;
                case "world":
                    return string.IsNullOrWhiteSpace(elementId) ? "world:unresolved" : "world:" + elementId;
                default:
                    return "unknown";
            }
        }

        private static string PreferredSequenceAction(string kind)
        {
            switch (kind)
            {
                case "building":
                    return "build";
                case "material":
                    return "use_as_material_or_search_item";
                case "world":
                    return "search_world_cell";
                default:
                    return "inspect_candidates";
            }
        }

        private static Dictionary<string, object> BuildSequenceActionArgs(string kind, string prefabId, string material, string elementId, string token)
        {
            switch (kind)
            {
                case "building":
                    return new Dictionary<string, object>
                    {
                        ["tool"] = "building_control",
                        ["domain"] = "planning",
                        ["action"] = "build_area",
                        ["prefabId"] = prefabId,
                        ["material"] = material,
                        ["dryRun"] = true
                    };
                case "material":
                    return new Dictionary<string, object>
                    {
                        ["tool"] = "read_control",
                        ["domain"] = "resources",
                        ["action"] = "search_items",
                        ["query"] = material ?? token
                    };
                case "world":
                    return new Dictionary<string, object>
                    {
                        ["tool"] = "read_control",
                        ["domain"] = "world",
                        ["action"] = "search",
                        ["pattern"] = elementId ?? token,
                        ["matchMode"] = "smart"
                    };
                default:
                    return new Dictionary<string, object>
                    {
                        ["token"] = token
                    };
            }
        }

        private static void ApplySequenceResolution(
            List<PlanSequenceItem> sequenceItems,
            int worldId,
            int limit,
            ref PlanBuildingCandidate bestBuilding,
            ref BuildingDef def,
            ref List<PlanBuildingCandidate> buildingCandidates,
            ref PlanMaterialCandidate bestMaterial,
            ref List<PlanMaterialCandidate> materialCandidates)
        {
            if (sequenceItems == null || sequenceItems.Count == 0)
                return;

            var sequenceBuilding = sequenceItems.FirstOrDefault(item =>
                item.IsKind("building")
                && !string.IsNullOrWhiteSpace(item.PrefabId));
            if (sequenceBuilding == null && sequenceItems.Count > 1)
            {
                bestBuilding = null;
                def = null;
                buildingCandidates = new List<PlanBuildingCandidate>();
            }
            if (sequenceBuilding != null)
            {
                var sequenceDef = Assets.GetBuildingDef(sequenceBuilding.PrefabId);
                bestBuilding = new PlanBuildingCandidate
                {
                    PrefabId = sequenceBuilding.PrefabId,
                    Name = sequenceBuilding.BuildingName,
                    Score = 10050,
                    Matched = sequenceBuilding.Token,
                    MatchKind = "sequence",
                    AvailableNow = IsUnlockedAndAvailable(sequenceDef)
                };
                def = sequenceDef;
                string sequencePrefabId = bestBuilding.PrefabId;
                buildingCandidates = new[] { bestBuilding }
                    .Concat(buildingCandidates.Where(item => !string.Equals(item.PrefabId, sequencePrefabId, StringComparison.OrdinalIgnoreCase)))
                    .Take(limit)
                    .ToList();
            }

            var sequenceMaterial = sequenceItems.FirstOrDefault(item =>
                (item.IsKind("material") || item.IsKind("building"))
                && !string.IsNullOrWhiteSpace(item.Material));
            if (sequenceMaterial == null)
            {
                if (sequenceBuilding == null && sequenceItems.Any(item => item.IsKind("world")))
                {
                    bestMaterial = null;
                    materialCandidates = new List<PlanMaterialCandidate>();
                }
                return;
            }

            if (def == null)
            {
                materialCandidates = new List<PlanMaterialCandidate>
                {
                    new PlanMaterialCandidate
                    {
                        Tag = sequenceMaterial.Material,
                        Name = sequenceMaterial.MaterialName,
                        Score = 990,
                        Matched = sequenceMaterial.Token,
                        AvailableKg = 0,
                        ValidForBuilding = false
                    }
                };
                bestMaterial = materialCandidates.FirstOrDefault();
                return;
            }

            materialCandidates = ResolveMaterialCandidates(def, sequenceMaterial.Material, sequenceMaterial.Token, SplitPlanTerms(sequenceMaterial.Token).ToList(), worldId)
                .Take(limit)
                .ToList();
            if (materialCandidates.Count == 0)
            {
                materialCandidates.Add(new PlanMaterialCandidate
                {
                    Tag = sequenceMaterial.Material,
                    Name = sequenceMaterial.MaterialName,
                    Score = 990,
                    Matched = sequenceMaterial.Token,
                    AvailableKg = 0,
                    ValidForBuilding = false
                });
            }
            bestMaterial = materialCandidates.FirstOrDefault();
        }
    }
}
