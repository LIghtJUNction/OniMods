using System;
using OniMcp.Tools;
using UnityEngine;

internal static class ExistingMaterialReaderRegression
{
    internal static void Run()
    {
        int assertions = 0;
        Expect(null, null, "missing target", ref assertions);
        Expect(null, new GameObject(), "missing construction components", ref assertions);

        var completed = new GameObject().Add(new PrimaryElement { ElementID = SimHashes.TestElement });
        Expect("TestElement", completed, "completed building uses its physical material", ref assertions);
        completed.Add(new Reconstructable { PrimarySelectedElementTag = new Tag("Granite") });
        Expect("TestElement", completed, "a future reconstruction request is not the current material", ref assertions);

        var selected = new[] { new Tag("Granite"), new Tag("Iron") };
        var construction = new Constructable { SelectedElementsTags = selected };
        var blueprint = new GameObject().Add(construction)
            .Add(new PrimaryElement { ElementID = SimHashes.TestElement });
        Expect("Granite", blueprint, "blueprint uses its first selected material", ref assertions);

        selected[0] = Tag.Invalid;
        Expect(null, blueprint, "invalid primary material must not borrow a secondary material (#293)", ref assertions);
        construction.SelectedElementsTags = null;
        Expect(null, blueprint, "missing blueprint selection must not borrow PrimaryElement", ref assertions);
        construction.SelectedElementsTags = new Tag[0];
        Expect(null, blueprint, "empty blueprint selection must not borrow PrimaryElement", ref assertions);
        construction.SelectedElementsTags = new[] { Tag.Invalid };
        Expect(null, blueprint, "invalid blueprint selection must remain unknown", ref assertions);

        selected[0] = new Tag("Granite");
        construction.SelectedElementsTags = selected;
        Expect("Granite", blueprint, "restored primary selection", ref assertions);
        Expect("Granite", blueprint, "repeated read is stable", ref assertions);
        if (!ReferenceEquals(selected, construction.SelectedElementsTags)
            || selected.Length != 2 || selected[0].Name != "Granite" || selected[1].Name != "Iron")
            throw new InvalidOperationException("Material inspection changed the blueprint selection");
        Console.WriteLine("Existing material reader regressions passed: " + assertions);
    }

    private static void Expect(string expected, GameObject target, string scenario, ref int assertions)
    {
        assertions++;
        string actual = BuildPlanningTools.TestExistingConstructionMaterial(target);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(scenario + ": expected " + (expected ?? "<unknown>")
                + ", got " + (actual ?? "<unknown>"));
    }
}

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal static string TestExistingConstructionMaterial(GameObject target)
        {
            return ExistingConstructionMaterialTag(target);
        }
    }
}
