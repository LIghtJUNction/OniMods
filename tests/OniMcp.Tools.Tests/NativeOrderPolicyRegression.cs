using System;
using System.Collections.Generic;
using OniMcp.Tools;

internal static class NativeOrderPolicyRegression
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        Check(AutomatableControlPolicy.RejectionReason(new Automatable()) == null, "vanilla manual control");
        Check(AutomatableControlPolicy.RejectionReason(new CustomAutomatable()) == "custom_automatable_semantics", "derived manual control rejected");
        Check(AutomatableControlPolicy.RejectionReason(null) == "automatable_not_found", "missing manual control rejected");
        QueueCases();
        MaterialCases();
        Console.WriteLine("Native order policy regressions passed: " + assertions + " assertions");
    }

    private static void QueueCases()
    {
        var locked = new ComplexRecipe();
        int next;
        string error;
        foreach (string mode in new[] { "set", "add", "infinite" })
        {
            Check(!ProductionQueuePolicy.TryCompute(locked, 3, mode, 4, out next, out error)
                && error.StartsWith("recipe_locked:"), "locked queue " + mode);
        }
        locked.FailUnlockRead = true;
        Check(ProductionQueuePolicy.TryCompute(locked, 3, "clear", 1, out next, out error) && next == 0, "clear obsolete queue");
        Check(ProductionQueuePolicy.TryCompute(locked, 3, "set", 0, out next, out error) && next == 0, "set zero obsolete queue");
        Check(ProductionQueuePolicy.TryCompute(locked, 3, "remove", 1, out next, out error) && next == 2, "reduce obsolete queue");
        Check(ProductionQueuePolicy.TryCompute(locked, -1, "remove", 1, out next, out error) && next == 98, "reduce infinite obsolete queue");
        var unlocked = new ComplexRecipe { Unlocked = true };
        Check(ProductionQueuePolicy.TryCompute(unlocked, 3, "add", int.MaxValue, out next, out error) && next == 99, "add cannot overflow");
        Check(ProductionQueuePolicy.TryCompute(unlocked, 3, "remove", int.MaxValue, out next, out error) && next == 0, "large removal clamps");
        Check(ProductionQueuePolicy.TryCompute(unlocked, 3, "set", 7, out next, out error) && next == 7, "unlocked research or POI recipe");
        Check(ProductionQueuePolicy.TryCompute(unlocked, -1, "add", 2, out next, out error) && next == -1, "infinite queue remains infinite");
        Check(!ProductionQueuePolicy.TryCompute(unlocked, 3, "unknown", 2, out next, out error) && next == 3, "invalid mode rejected");
    }

    private static void MaterialCases()
    {
        var steel = new Tag("Steel");
        var iron = new Tag("Iron");
        var glass = new Tag("Glass");
        var metal = new Tag("Metal");
        var transparent = new Tag("Transparent");
        MaterialSelector.Categories.Clear();
        MaterialSelector.Categories[metal] = new List<Tag> { steel, iron };
        MaterialSelector.Categories[transparent] = new List<Tag> { glass };
        var def = new BuildingDef { CraftRecipe = new Recipe(), Defaults = new List<Tag> { steel, glass } };
        def.CraftRecipe.Ingredients.Add(new Recipe.Ingredient { tag = metal, amount = 200 });
        def.CraftRecipe.Ingredients.Add(new Recipe.Ingredient { tag = transparent, amount = 100 });
        IList<Tag> materials;
        Check(RocketModuleMaterialPolicy.Resolve(def, "Steel,Glass", tag => true, tag => 200, false, out materials) == null, "valid slots and exact mass");
        Check(RocketModuleMaterialPolicy.Resolve(def, null, tag => true, tag => 200, false, out materials) == null
            && materials.Count == 2, "default materials use same checks");
        Check(Code(def, "Steel", tag => 200) == "invalid_material_count", "too few materials");
        Check(Code(def, "Steel,Glass,Iron", tag => 200) == "invalid_material_count", "too many materials");
        Check(Code(def, "Steel,", tag => 200) == "invalid_material_for_recipe", "blank slot is not dropped");
        Check(Code(def, "Glass,Steel", tag => 200) == "invalid_material_for_recipe", "slot category mismatch");
        Check(Code(def, "Metal,Glass", tag => 200) == "invalid_material_for_recipe", "category tag is not construction material");
        Check(Code(def, "Steel,Glass", tag => 199) == "insufficient_material", "insufficient first material");
        Check(Code(def, "Steel,Glass", tag => tag.Equals(glass) ? 99 : 200) == "insufficient_material", "insufficient second material");
        Check(Code(def, "Steel,Glass", tag => float.NaN) == "insufficient_material", "NaN inventory rejected");
        Check(Code(def, "Steel,Glass", tag => float.PositiveInfinity) == "insufficient_material", "infinite inventory rejected");
        Check(RocketModuleMaterialPolicy.Resolve(def, "Steel,Glass", tag => true, tag => 0, true, out materials) == null, "discovered materials allow zero inventory under the native insufficient-material setting");
        Check(RocketModuleMaterialPolicy.Resolve(def, "Glass,Steel", tag => true, tag => 0, true, out materials) != null, "setting cannot bypass slot checks");
        int inventoryReads = 0;
        Func<Tag, float> inventory = tag => { inventoryReads++; return 1000; };
        foreach (bool allowInsufficient in new[] { false, true })
        {
            var undiscovered = RocketModuleMaterialPolicy.Resolve(def, "Steel,Glass",
                tag => tag.Equals(steel), inventory, allowInsufficient, out materials);
            Check(undiscovered != null && (string)undiscovered["reasonCode"] == "undiscovered_material"
                && (int)undiscovered["slot"] == 1 && (string)undiscovered["material"] == "Glass",
                "undiscovered second material rejected with allowInsufficient=" + allowInsufficient);
            Check(inventoryReads == 0, "all discovery checks precede inventory queries");
            var missingDiscovery = RocketModuleMaterialPolicy.Resolve(def, "Steel,Glass",
                null, inventory, allowInsufficient, out materials);
            Check(missingDiscovery != null && (string)missingDiscovery["reasonCode"] == "material_discovery_unavailable",
                "missing discovery predicate rejected with allowInsufficient=" + allowInsufficient);
            Check(inventoryReads == 0, "missing discovery data never queries inventory");
        }
        var discoveredMaterials = new List<Tag>();
        Check(RocketModuleMaterialPolicy.Resolve(def, "Steel,Glass",
            tag => { discoveredMaterials.Add(tag); return true; },
            tag => { inventoryReads++; return 0; }, true, out materials) == null,
            "stock relaxation retains discovery validation");
        Check(discoveredMaterials.Count == 2 && discoveredMaterials[0].Equals(steel)
            && discoveredMaterials[1].Equals(glass) && inventoryReads == 0,
            "both recipe materials are checked even when inventory is not required");
        var defaultDiscovery = RocketModuleMaterialPolicy.Resolve(def, null,
            tag => tag.Equals(steel), inventory, true, out materials);
        Check(defaultDiscovery != null && (string)defaultDiscovery["reasonCode"] == "undiscovered_material"
            && inventoryReads == 0, "default materials cannot bypass discovery or query inventory on rejection");
        def.CraftRecipe.Ingredients[1].tag = metal;
        Check(Code(def, "Steel,Steel", tag => 250) == "insufficient_material", "same material sums across slots");
        Check(RocketModuleMaterialPolicy.Resolve(def, "Steel,Steel", tag => true, tag => 300, false, out materials) == null, "summed material exact balance");
        def.CraftRecipe.Ingredients[0].amount = float.NaN;
        Check(Code(def, "Steel,Steel", tag => 1000) == "invalid_material_requirement", "invalid recipe amount rejected");
    }

    private static string Code(BuildingDef def, string requested, Func<Tag, float> available)
    {
        IList<Tag> materials;
        var error = RocketModuleMaterialPolicy.Resolve(def, requested, tag => true, available, false, out materials);
        return error == null ? null : (string)error["reasonCode"];
    }

    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("Native order policy regression: " + label);
    }
}
