using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Support;
namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static BuildPlanResolution ResolveBuildPlan(JObject args)
        {
            string text = FirstPlanText(args);
            int worldId = ToolUtil.ResolveWorldId(args);
            int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 5, 20));
            var tokens = SplitPlanTerms(text).ToList();
            string requestedPrefab = args["prefabId"]?.ToString();
            string requestedMaterial = args["material"]?.ToString();
            string requestedAnchor = FirstNonEmptyString(args["query"], args["target"], args["search"], args["name"]);
            string anchorQuery = string.IsNullOrWhiteSpace(requestedAnchor) ? ExtractAnchorQuery(text) : requestedAnchor;
            string buildText = RemoveAnchorClause(text);
            var buildTokens = SplitPlanTerms(buildText).ToList();
            var sequenceItems = ResolvePlanSequenceItemsV2(SplitPlanTerms(buildText, expandCompounds: false).ToList(), worldId, limit).ToList();
            var buildingCandidates = ResolveBuildingCandidates(requestedPrefab, buildText, buildTokens, includeUnavailable: true)
                .Take(limit)
                .ToList();
            var bestBuilding = buildingCandidates.FirstOrDefault();
            var def = bestBuilding != null ? Assets.GetBuildingDef(bestBuilding.PrefabId) : null;

            var materialCandidates = ResolveMaterialCandidates(def, requestedMaterial, buildText, buildTokens, worldId)
                .Take(limit)
                .ToList();
            var bestMaterial = materialCandidates.FirstOrDefault();
            ApplySequenceResolution(sequenceItems, worldId, limit, ref bestBuilding, ref def, ref buildingCandidates, ref bestMaterial, ref materialCandidates);
            if (string.IsNullOrWhiteSpace(requestedPrefab) && sequenceItems.Count > 1 && !string.IsNullOrWhiteSpace(sequenceItems[0].PrefabId))
            {
                var firstBuilding = sequenceItems[0];
                var firstDef = Assets.GetBuildingDef(firstBuilding.PrefabId);
                bestBuilding = new PlanBuildingCandidate
                {
                    PrefabId = firstBuilding.PrefabId,
                    Name = firstBuilding.BuildingName,
                    Score = 10050,
                    Matched = firstBuilding.Token,
                    MatchKind = "sequence_first",
                    AvailableNow = firstDef == null || IsUnlockedAndAvailable(firstDef)
                };
                def = Assets.GetBuildingDef(bestBuilding.PrefabId);
                buildingCandidates = new[] { bestBuilding }
                    .Concat(buildingCandidates.Where(item => !string.Equals(item.PrefabId, bestBuilding.PrefabId, StringComparison.OrdinalIgnoreCase)))
                    .Take(limit)
                    .ToList();
                bool hasSequenceMaterial = sequenceItems.Any(item => item.IsKind("material") && !string.IsNullOrWhiteSpace(item.Material));
                if (!hasSequenceMaterial)
                {
                    materialCandidates = ResolveMaterialCandidates(def, requestedMaterial, firstBuilding.Token, SplitPlanTerms(firstBuilding.Token).ToList(), worldId)
                        .Take(limit)
                        .ToList();
                    bestMaterial = materialCandidates.FirstOrDefault();
                }
            }

            return new BuildPlanResolution
            {
                Input = text,
                Tokens = tokens,
                PrefabId = bestBuilding?.PrefabId,
                BuildingName = bestBuilding?.Name,
                Material = bestMaterial?.Tag,
                MaterialName = bestMaterial?.Name,
                AnchorQuery = anchorQuery,
                BuildingCandidates = buildingCandidates,
                MaterialCandidates = materialCandidates,
                SequenceItems = sequenceItems
            };
        }

        private static string FirstPlanText(JObject args)
        {
            return FirstNonEmptyString(args["plan"], args["blueprint"], args["sequence"], args["text"], args["build"], args["building"]);
        }

        private static string FirstNonEmptyString(params JToken[] values)
        {
            foreach (var value in values)
            {
                string text = value?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
            return null;
        }

        private static IEnumerable<string> SplitPlanTerms(string text, bool expandCompounds = true)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            string normalized = text
                .Replace("->", "-")
                .Replace("=>", "-")
                .Replace("→", "-")
                .Replace("，", ",")
                .Replace("、", ",")
                .Replace("/", "-")
                .Replace("@", "-")
                .Replace("建造", "-")
                .Replace("在", "-")
                .Replace("用", "-")
                .Replace("造", "-");

            foreach (string raw in normalized.Split(new[] { '-', ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim();
                if (IsPlanStopWord(token))
                    continue;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    yield return token;
                if (expandCompounds)
                {
                    foreach (string expanded in ExpandCompoundPlanTerm(token))
                        yield return expanded;
                }
            }
        }
        }

        private static bool IsPlanStopWord(string token)
        {
            string normalized = NormalizePlanText(token);
            switch (normalized)
            {
                case "a":
                case "an":
                case "the":
                case "to":
                case "of":
                case "for":
                case "with":
                case "and":
                case "or":
                case "build":
                case "make":
                case "place":
                case "put":
                case "near":
                case "by":
                case "at":
                case "base":
                case "two":
                case "three":
                    return true;
                default:
                    return false;
            }
        }

        private static IEnumerable<string> ExpandCompoundPlanTerm(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                yield break;

            string[] suffixes =
            {
                "tiles", "tile",
                "网格砖", "透气砖", "砖块", "砖", "梯子", "电线", "导线", "电缆",
                "液管", "水管", "气管", "运输轨道", "轨道"
            };
            foreach (string suffix in suffixes)
            {
                if (!token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || token.Length <= suffix.Length)
                    continue;
                string prefix = token.Substring(0, token.Length - suffix.Length).Trim();
                if (!string.IsNullOrWhiteSpace(prefix))
                    yield return prefix;
                yield return suffix;
                yield break;
            }
        }

        private static IEnumerable<PlanBuildingCandidate> ResolveBuildingCandidates(string requestedPrefab, string text, List<string> tokens, bool includeUnavailable)
        {
            if (!string.IsNullOrWhiteSpace(requestedPrefab))
            {
                var exact = Assets.GetBuildingDef(requestedPrefab);
                if (exact != null)
                {
                    yield return new PlanBuildingCandidate
                    {
                        PrefabId = exact.PrefabID,
                        Name = ToolUtil.CleanName(exact.Name),
                        Score = 10000,
                        Matched = requestedPrefab,
                        MatchKind = "prefabId"
                    };
                    yield break;
                }
            }

            if (Assets.BuildingDefs == null)
                yield break;

            var aliases = PlanBuildingAliases();
            var terms = new List<string>();
            if (!string.IsNullOrWhiteSpace(text))
                terms.Add(text);
            terms.AddRange(tokens);

            var candidates = new List<PlanBuildingCandidate>();
            foreach (var def in Assets.BuildingDefs)
            {
                if (def == null || (!includeUnavailable && !IsUnlockedAndAvailable(def)))
                    continue;

                int bestScore = 0;
                string bestTerm = null;
                string bestKind = null;
                for (int termIndex = 0; termIndex < terms.Count; termIndex++)
                {
                    string term = terms[termIndex];
                    if (string.IsNullOrWhiteSpace(term))
                        continue;
                    int orderBoost = Math.Max(0, 50 - termIndex);

                    string aliasPrefab = ResolveBuildingAlias(term, aliases);
                    if (!string.IsNullOrWhiteSpace(aliasPrefab)
                        && string.Equals(aliasPrefab, def.PrefabID, StringComparison.OrdinalIgnoreCase))
                    {
                        bestScore = Math.Max(bestScore, 950 + orderBoost);
                        bestTerm = term;
                        bestKind = "alias";
                    }

                    int score = ScorePlanBuilding(def, term);
                    if (score > 0)
                        score += orderBoost;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTerm = term;
                        bestKind = "text";
                    }
                }

                if (bestScore > 0)
                {
                    candidates.Add(new PlanBuildingCandidate
                    {
                        PrefabId = def.PrefabID,
                        Name = ToolUtil.CleanName(def.Name),
                        Score = bestScore,
                        Matched = bestTerm,
                        MatchKind = bestKind,
                        AvailableNow = IsUnlockedAndAvailable(def)
                    });
                }
            }

            foreach (var candidate in candidates
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.AvailableNow)
                .ThenBy(item => item.PrefabId))
                yield return candidate;
        }

        private static int ScorePlanBuilding(BuildingDef def, string term)
        {
            string query = NormalizePlanText(term);
            if (query.Length == 0)
                return 0;

            int score = Math.Max(
                ScorePlanValue(def.PrefabID, query, 900),
                ScorePlanValue(ToolUtil.CleanName(def.Name), query, 850));
            score = Math.Max(score, ScorePlanValue(def.Desc, query, 350));
            if (def.SearchTerms != null)
            {
                foreach (string searchTerm in def.SearchTerms)
                    score = Math.Max(score, ScorePlanValue(searchTerm, query, 650));
            }
            foreach (string category in BuildingCategories(def) ?? Enumerable.Empty<string>())
                score = Math.Max(score, ScorePlanValue(category, query, 250));
            return score;
        }

        private static IEnumerable<PlanMaterialCandidate> ResolveMaterialCandidates(BuildingDef def, string requestedMaterial, string text, List<string> tokens, int worldId)
        {
            if (def == null)
                yield break;

            var materials = AvailableMaterials(def, worldId, includeUnavailable: true).ToList();
            var aliases = PlanMaterialAliases();
            var terms = new List<string>();
            if (!string.IsNullOrWhiteSpace(requestedMaterial))
                terms.Add(requestedMaterial);
            terms.AddRange(tokens);

            var candidates = new List<PlanMaterialCandidate>();
            foreach (var material in materials)
            {
                if (material == null || material.Tag == null)
                    continue;
                int bestScore = 0;
                string bestTerm = null;
                foreach (string term in terms)
                {
                    string query = NormalizePlanText(term);
                    if (query.Length == 0)
                        continue;

                int score = Math.Max(
                    ScorePlanValue(material.Tag.Name, query, 800),
                    ScorePlanValue(material.Name, query, 760));
                score = Math.Max(score, ScorePlanMaterialAlias(material.Tag.Name, query, aliases));
                    if (material.Categories != null)
                    {
                        foreach (var category in material.Categories)
                        {
                            if (category != null)
                                score = Math.Max(score, ScorePlanValue(category.Name, query, 200));
                        }
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTerm = term;
                    }
                }

                if (bestScore > 0)
                {
                    candidates.Add(new PlanMaterialCandidate
                    {
                        Tag = material.Tag.Name,
                        Name = material.Name,
                        Score = bestScore,
                        Matched = bestTerm,
                        AvailableKg = material.AvailableKg,
                        ValidForBuilding = material.ValidForBuilding
                    });
                }
            }

            if (candidates.Count == 0)
                candidates.AddRange(FallbackMaterialCandidates(terms, aliases));

            foreach (var candidate in candidates
                .OrderByDescending(item => item.ValidForBuilding)
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.AvailableKg)
                .ThenBy(item => item.Tag))
                yield return candidate;
        }

        private static IEnumerable<PlanSequenceItem> ResolvePlanSequenceItems(List<string> tokens, int worldId, int limit)
        {
            if (tokens == null)
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (string token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token) || !seen.Add(index + ":" + token))
                    continue;

                var buildings = ResolveBuildingCandidates(null, token, new List<string> { token }, includeUnavailable: true)
                    .Take(limit)
                    .ToList();
                var bestBuilding = buildings.FirstOrDefault();
                var materialDef = bestBuilding != null ? Assets.GetBuildingDef(bestBuilding.PrefabId) : Assets.GetBuildingDef("Tile");
                var materials = ResolveMaterialCandidates(materialDef, null, token, SplitPlanTerms(token).ToList(), worldId)
                    .Take(limit)
                    .ToList();
                var bestMaterial = materials.FirstOrDefault();
                string elementId = ResolvePlanElementId(token);

                string kind = "unknown";
                if (bestBuilding != null && (bestMaterial == null || bestBuilding.Score >= bestMaterial.Score))
                    kind = "building";
                else if (bestMaterial != null)
                    kind = "material";
                else if (!string.IsNullOrWhiteSpace(elementId))
                    kind = "world";

                yield return new PlanSequenceItem
                {
                    Index = index,
                    Token = token,
                    Kind = kind,
                    PrefabId = bestBuilding?.PrefabId,
                    BuildingName = bestBuilding?.Name,
                    Material = bestMaterial?.Tag,
                    MaterialName = bestMaterial?.Name,
                    ElementId = elementId,
                    BuildingCandidates = buildings,
                    MaterialCandidates = materials
                };
                index++;
            }
        }

        private static string ResolvePlanElementId(string token)
        {
            string id;
            if (PlanElementAliases().TryGetValue(NormalizePlanText(token), out id))
                return id;

            var normalized = NormalizePlanText(token);
            foreach (var element in ElementLoader.elements ?? new List<Element>())
            {
                if (element == null)
                    continue;
                if (NormalizePlanText(element.id.ToString()) == normalized || NormalizePlanText(element.name) == normalized)
                    return element.id.ToString();
            }
            return null;
        }

        private static int ScorePlanMaterialAlias(string tagName, string normalizedQuery, Dictionary<string, string> aliases)
        {
            if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(normalizedQuery))
                return 0;
            string materialTag;
            if (aliases.TryGetValue(normalizedQuery, out materialTag)
                && string.Equals(materialTag, tagName, StringComparison.OrdinalIgnoreCase))
                return 980;
            return 0;
        }



        private static string ExtractAnchorQuery(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string[] markers = { "@", " near ", " anchor ", " target ", "near:", "anchor:", "target:", "锚点", "目标", "靠近", "旁边", "附近", "在", "旁" };
            foreach (string marker in markers)
            {
                int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && index + marker.Length < text.Length)
                {
                    string anchor = CleanAnchorQuery(text.Substring(index + marker.Length).Trim(' ', ',', '，', '-', '>', ':', '：'));
                    return string.IsNullOrWhiteSpace(anchor) ? null : anchor;
                }
            }
            return null;
        }

        private static string CleanAnchorQuery(string anchor)
        {
            if (string.IsNullOrWhiteSpace(anchor))
                return anchor;
            string trimmed = anchor.Trim();
            foreach (string prefix in new[] { "the ", "a ", "an " })
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && trimmed.Length > prefix.Length)
                    return trimmed.Substring(prefix.Length).Trim();
            }
            return trimmed;
        }

        private static string RemoveAnchorClause(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            string[] markers = { "@", " near ", " anchor ", " target ", "near:", "anchor:", "target:", "锚点", "目标", "靠近", "旁边", "附近", "在", "旁" };
            int best = -1;
            foreach (string marker in markers)
            {
                int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && (best < 0 || index < best))
                    best = index;
            }
            return best < 0 ? text : text.Substring(0, best);
        }

        private static string ResolveBuildingAlias(string term, Dictionary<string, string> aliases)
        {
            string normalized = NormalizePlanText(term);
            string prefabId;
            if (aliases.TryGetValue(normalized, out prefabId))
                return prefabId;

            foreach (var item in aliases.OrderByDescending(item => item.Key.Length))
            {
                if (normalized.EndsWith(item.Key, StringComparison.Ordinal)
                    && normalized.Length > item.Key.Length)
                    return item.Value;
            }
            return null;
        }




    }
}
