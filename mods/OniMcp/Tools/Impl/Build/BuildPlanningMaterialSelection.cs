using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private sealed class MaterialSelection
        {
            public bool Valid;
            public string Mode;
            public string Requested;
            public List<Tag> Elements = new List<Tag>();
            public BuildMaterialInfo Selected;
            public List<BuildMaterialInfo> Available = new List<BuildMaterialInfo>();
            public List<BuildMaterialInfo> Candidates = new List<BuildMaterialInfo>();
            public float RequiredKg;
            public int NewPlacements = 1;
            public string ReasonCode;
            public string Error;
            public ConstructionRecipe Recipe;
            public List<BuildMaterialInfo> SlotMaterials = new List<BuildMaterialInfo>();

            public void RequireMassForPlacements(int count)
            {
                NewPlacements = count;
                RequiredKg = Recipe != null && Recipe.Known ? Recipe.TotalMassKg * count : 0f;
                if (!Valid && (ReasonCode != "insufficient_material" || Recipe == null
                    || SlotMaterials.Count != Recipe.Slots.Count))
                    return;
                if (!BuildMaterialSufficiencyPolicy.IsSatisfied(Recipe,
                    SlotMaterials.Select(item => item.Tag.Name).ToList(),
                    SlotMaterials.Select(item => item.AvailableKg).ToList(), count,
                    out string reasonCode, out string error))
                {
                    Valid = false;
                    ReasonCode = reasonCode;
                    Error = error;
                }
            }

            public static MaterialSelection Success(List<Tag> elements, string mode, string requested, BuildMaterialInfo selected,
                List<BuildMaterialInfo> available, ConstructionRecipe recipe = null, List<BuildMaterialInfo> slotMaterials = null)
            {
                return new MaterialSelection
                {
                    Valid = true,
                    Mode = mode,
                    Requested = string.IsNullOrWhiteSpace(requested) ? "auto" : requested,
                    Elements = elements ?? new List<Tag>(),
                    Selected = selected,
                    Available = available ?? new List<BuildMaterialInfo>(),
                    RequiredKg = recipe?.TotalMassKg ?? 0f,
                    Recipe = recipe,
                    SlotMaterials = slotMaterials ?? new List<BuildMaterialInfo>()
                };
            }

            public static MaterialSelection Invalid(string error, string requested, List<BuildMaterialInfo> available,
                List<BuildMaterialInfo> candidates, ConstructionRecipe recipe = null,
                List<BuildMaterialInfo> slotMaterials = null, BuildMaterialInfo selected = null,
                string reasonCode = "invalid_material_selection")
            {
                return new MaterialSelection
                {
                    Valid = false,
                    Mode = "invalid",
                    Requested = string.IsNullOrWhiteSpace(requested) ? "auto" : requested,
                    Error = error,
                    ReasonCode = reasonCode,
                    Selected = selected,
                    Available = available ?? new List<BuildMaterialInfo>(),
                    Candidates = candidates ?? new List<BuildMaterialInfo>(),
                    RequiredKg = recipe != null && recipe.Known ? recipe.TotalMassKg : 0f,
                    Recipe = recipe,
                    SlotMaterials = slotMaterials ?? new List<BuildMaterialInfo>()
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                float selectedAvailableKg = Selected != null ? ToolUtil.SafeFloat(Selected.ReachableLooseKg) : 0f;
                bool hasRequiredKg = Recipe != null && Recipe.Known && RequiredKg > 0f
                    && !float.IsNaN(RequiredKg) && !float.IsInfinity(RequiredKg);
                var slotResults = new List<Dictionary<string, object>>();
                var slotReadiness = new List<MaterialReadiness>();
                var readinessByTag = new Dictionary<string, MaterialReadiness>(StringComparer.OrdinalIgnoreCase);
                float localShortage = 0f;
                if (hasRequiredKg && SlotMaterials.Count == Recipe.Slots.Count)
                {
                    var groupedRequirements = Enumerable.Range(0, Recipe.Slots.Count)
                        .GroupBy(index => SlotMaterials[index].Tag.Name, StringComparer.OrdinalIgnoreCase);
                    foreach (var group in groupedRequirements)
                    {
                        float requiredForTag = group.Sum(index => Recipe.Slots[index].RequiredKg) * NewPlacements;
                        var stock = SlotMaterials[group.First()];
                        localShortage += Math.Max(0f, requiredForTag - stock.AvailableKg);
                        readinessByTag[group.Key] = ConstructionReadinessPolicy.Material(
                            requiredForTag, stock.ReachableLooseKg, stock.UnverifiedLocalKg,
                            stock.HasActiveNavigator, stock != null);
                    }
                    for (int i = 0; i < Recipe.Slots.Count; i++)
                    {
                        var slot = Recipe.Slots[i];
                        var stock = SlotMaterials[i];
                        var readinessForSlot = readinessByTag[stock.Tag.Name];
                        slotReadiness.Add(readinessForSlot);
                        slotResults.Add(new Dictionary<string, object>
                        {
                            ["index"] = slot.Index,
                            ["category"] = slot.Category,
                            ["material"] = stock.Tag.Name,
                            ["requiredKg"] = Math.Round(slot.RequiredKg * NewPlacements, 3),
                            ["requirementKnown"] = true,
                            ["reachableAvailableKg"] = Math.Round(stock.ReachableLooseKg, 3),
                            ["localInventoryKg"] = Math.Round(stock.AvailableKg, 3),
                            ["foreignWorldInventoryKgContextOnly"] = Math.Round(stock.RelatedForeignKg, 3),
                            ["unverifiedLocalStockKg"] = Math.Round(stock.UnverifiedLocalKg, 3),
                            ["hasActiveNavigator"] = stock.HasActiveNavigator,
                            ["shortageKg"] = readinessForSlot.ShortageKg.HasValue ? (object)Math.Round(readinessForSlot.ShortageKg.Value, 3) : null,
                            ["localAvailabilityStatus"] = readinessForSlot.Status
                        });
                    }
                }
                string status = !hasRequiredKg || slotReadiness.Count == 0 ? "unknown"
                    : slotReadiness.Any(item => item.Status == "unknown") ? "unknown"
                    : slotReadiness.Any(item => item.Status == "shortage") ? "shortage" : "sufficient";
                if (!Valid)
                    status = ReasonCode == "insufficient_material" ? "shortage" : "invalid";
                float? shortage = status == "unknown" || status == "invalid" ? (float?)null
                    : ReasonCode == "insufficient_material" ? (slotReadiness.Count > 0 ? (float?)localShortage : null)
                    : readinessByTag.Values.Sum(item => item.ShortageKg.GetValueOrDefault());
                object satisfied = !Valid ? (object)false : status == "sufficient" ? (object)true
                    : status == "shortage" ? (object)false : null;

                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["mode"] = Mode,
                    ["requested"] = Requested,
                    ["reasonCode"] = ReasonCode,
                    ["newPlacements"] = NewPlacements,
                    ["requirementKnown"] = hasRequiredKg,
                    ["requiredKg"] = hasRequiredKg ? (object)Math.Round(ToolUtil.SafeFloat(RequiredKg), 3) : null,
                    ["selectedAvailableKg"] = Selected != null ? (object)Math.Round(selectedAvailableKg, 3) : null,
                    ["selectedLocalInventoryKg"] = Selected != null ? (object)Math.Round(ToolUtil.SafeFloat(Selected.AvailableKg), 3) : null,
                    ["selectedForeignWorldInventoryKgContextOnly"] = Selected != null ? (object)Math.Round(ToolUtil.SafeFloat(Selected.RelatedForeignKg), 3) : null,
                    ["localAvailabilityStatus"] = status,
                    ["satisfied"] = satisfied,
                    ["shortageKg"] = shortage.HasValue ? (object)Math.Round(shortage.Value, 3) : null,
                    ["recipeSlots"] = slotResults,
                    ["recipeUnknownReason"] = Recipe?.UnknownReason,
                    ["selected"] = Selected != null ? Selected.ToDictionary() : null,
                    ["elements"] = Elements.Select(tag => tag.Name).ToList(),
                    ["availableMaterials"] = Available.Take(20).Select(item => item.ToDictionary()).ToList(),
                    ["candidateMaterials"] = Candidates.Take(20).Select(item => item.ToDictionary()).ToList(),
                    ["fallbackMaterial"] = Available.Count > 0 ? Available[0].Tag.Name : null,
                    ["next"] = status == "sufficient"
                        ? "The selected material is reachable in sufficient local quantity; batch plans still count this stock once and do not reserve it."
                        : status == "shortage"
                            ? "Move or sweep the reported shortage into a reachable local supply, then retry the build."
                            : status == "unknown"
                                ? "Some local stock or recipe data is not verified; inspect accessible storage and material locations before assuming the blueprint can progress."
                                : Available.Count > 0
                            ? "Retry with material=auto or material=" + Available[0].Tag.Name + " if exact material is not required."
                            : "No usable material is currently available; inspect inventory/material candidates before retrying.",
                    ["suggestion"] = Available.Count > 0 ? "Use material=auto or material=" + Available[0].Tag.Name : "No available material; inspect read_control domain=resources action=inventory/building_control domain=planning action=materials",
                    ["error"] = Error
                };
            }
        }
    }
}
