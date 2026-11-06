using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static partial class NativeControlRegression
{
    private static void CheckTreeFilters()
    {
        var tree = AddTree(40);
        foreach (string domain in new[] { "storage", "filter" })
        {
            foreach (string mode in new[] { "replace", "add" })
            {
                foreach (string invalid in new[] { "TypoTag", "UndiscoveredOre", "Water", "ForbiddenOre" })
                {
                    var before = Snapshot(tree);
                    var request = FilterArgs(40, domain, mode, "Dirt", invalid);
                    Check(StorageTools.ControlStorageSystem().Handler(request).IsError,
                        domain + " " + mode + " rejects non-selectable tag " + invalid);
                    CheckUnchanged(tree, before, domain + " invalid tag " + invalid);
                }
            }
            var badModeBefore = Snapshot(tree);
            Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, domain, "typo", "Dirt")).IsError,
                domain + " rejects unknown mode");
            CheckUnchanged(tree, badModeBefore, domain + " unknown mode");
            JObject response = Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, domain, "replace", "Dirt", "SandStone")));
            Check(tree.GetTags().SetEquals(new[] { new Tag("Dirt"), new Tag("SandStone") }),
                domain + " accepts discovered leaves in the target storage category");
            Check(tree.GetFilterStorage().items.Count == 1, domain + " accepted existing content stays stored");
            if (domain == "storage")
                Check(((JArray)response["acceptedTags"]).Values<string>().Contains("SandStone"),
                    "storage response reflects the successfully updated filter");
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, domain, "add", "Solid")));
            Check(tree.Selected.Contains(new Tag("Solid")) && tree.GetFilterStorage().MassStored() == 42,
                domain + " permits a native discovered category row without removing existing leaves");
            tree.Selected.Add(new Tag("OldInvalidSavedTag"));
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, domain, "remove", "OldInvalidSavedTag")));
            Check(!tree.Selected.Contains(new Tag("OldInvalidSavedTag")) && tree.Selected.Contains(new Tag("Dirt")),
                domain + " can remove an old invalid selection without losing valid selections");
        }

        JObject options = Success(FilterTools.ControlFilter().Handler(new JObject
        {
            ["action"] = "list", ["includeOptions"] = true, ["kind"] = "tree"
        }));
        var offered = options["filters"][0]["tree"]["options"].Select(tag => (string)tag["tag"]).ToList();
        Check(new HashSet<string>(offered).SetEquals(new[] { "Solid", "Dirt", "SandStone" }),
            "read-side options use the same discovered/category/forbidden eligibility as writes");

        var firstStorage = tree.GetComponent<Storage>();
        var alternate = new Storage();
        alternate.storageFilters.Add(new Tag("Liquid"));
        tree.FilterStorage = alternate;
        Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, "storage", "replace", "Dirt")).IsError,
            "validation uses native GetFilterStorage rather than first component");
        Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, "filter", "replace", "Water")));
        Check(tree.Selected.SetEquals(new[] { new Tag("Water") }) && firstStorage.items.Count == 1,
            "alternate native filter storage supplies its own selectable category");
        DiscoveredResources.Instance = null;
        var beforeMissing = Snapshot(tree);
        Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, "filter", "add", "Water")).IsError,
            "unavailable discovery state cannot authorize adding a tag");
        CheckUnchanged(tree, beforeMissing, "missing discovery state");
        Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, "filter", "clear")));
        Check(tree.Selected.Count == 0, "clear remains available with unavailable discovery state");
    }

    private static void CheckTreeCategories()
    {
        var tree = AddTree(45);
        tree.GetFilterStorage().storageFilters.Add(new Tag("UndiscoveredCategory"));
        DiscoveredResources.Instance.Categories[new Tag("UndiscoveredCategory")] = new HashSet<Tag>
        { new Tag("DiscoveredChild") };
        DiscoveredResources.Instance.Discovered.Add(new Tag("DiscoveredChild"));
        foreach (string domain in new[] { "storage", "filter" })
        {
            foreach (string requested in new[] { "UndiscoveredCategory", "DiscoveredChild" })
            {
                var before = Snapshot(tree);
                Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(45, domain, "add", requested)).IsError,
                    domain + " rejects rows hidden by the category discovery rule");
                CheckUnchanged(tree, before, domain + " undiscovered category row");
            }
        }
        tree.gameObject.Add(new CreatureDeliveryPoint());
        foreach (string domain in new[] { "storage", "filter" })
        {
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(45, domain, "add", "UndiscoveredCategory", "DiscoveredChild")));
            Check(tree.Selected.Contains(new Tag("UndiscoveredCategory"))
                && tree.Selected.Contains(new Tag("DiscoveredChild")) && tree.GetFilterStorage().MassStored() == 42,
                domain + " preserves native CreatureDeliveryPoint category exception");
        }
    }

    private static void CheckFlatFilters()
    {
        var tree = AddTree(50);
        var flat = new FlatTagFilterable();
        flat.tagOptions.AddRange(new[] { new Tag("Dirt"), new Tag("Algae") });
        flat.selectedTags.Add(new Tag("Dirt"));
        tree.gameObject.Add(flat);
        foreach (string domain in new[] { "storage", "filter" })
        {
            var before = Snapshot(tree);
            int selectCalls = flat.SelectCalls;
            Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, domain, "replace", "NoOption")).IsError,
                domain + " flat rejects tags outside tagOptions");
            CheckUnchanged(tree, before, domain + " invalid flat option");
            Check(flat.SelectCalls == selectCalls && flat.selectedTags.SequenceEqual(new[] { new Tag("Dirt") }),
                domain + " invalid flat option causes no SelectTag calls");
        }
        flat.currentlyUserAssignable = false;
        foreach (string domain in new[] { "storage", "filter" })
        {
            foreach (string mode in new[] { "replace", "add", "remove" })
                CheckFlatWriteRejected(tree, flat, FilterArgs(50, domain, mode, "Dirt"),
                    domain + " non-user-assignable flat " + mode);
        }
        CheckFlatWriteRejected(tree, flat, FilterArgs(50, "filter", "clear"),
            "non-user-assignable flat clear");
        flat.currentlyUserAssignable = true;
        foreach (string domain in new[] { "storage", "filter" })
        {
            foreach (string mode in new[] { "replace", "add" })
                CheckFlatWriteRejected(tree, flat, FilterArgs(50, domain, mode, "Dirt", "Algae"),
                    domain + " undiscovered flat option " + mode);
        }
        Check(TreeFilterSelection.GetOptions(tree).SetEquals(new[] { new Tag("Dirt") }),
            "flat read options honor the native discovery visibility setting");
        Check(FlatReadOptions().SetEquals(new[] { "Dirt" }),
            "read handler omits undiscovered native flat options");
        var discovered = DiscoveredResources.Instance;
        DiscoveredResources.Instance = null;
        foreach (string domain in new[] { "storage", "filter" })
            CheckFlatWriteRejected(tree, flat, FilterArgs(50, domain, "add", "Dirt"),
                domain + " missing flat discovery state");
        // A native filter may explicitly offer undiscovered options; it does not
        // need the discovery singleton in that mode.
        flat.displayOnlyDiscoveredTags = false;
        Check(FlatReadOptions().SetEquals(new[] { "Dirt", "Algae" }),
            "read handler offers undiscovered options when the native filter explicitly allows them");
        foreach (string domain in new[] { "storage", "filter" })
        {
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, domain, "add", "Algae")));
            Check(tree.GetFilterStorage().MassStored() == 42 && tree.GetFilterStorage().items.Count == 1,
                domain + " flat add never temporarily removes accepted Dirt or ejects its contents");
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, domain, "replace", "Dirt", "Algae")));
            Check(tree.GetFilterStorage().MassStored() == 42 && tree.GetFilterStorage().items.Count == 1,
                domain + " identical flat replace preserves existing contents");
        }
        DiscoveredResources.Instance = discovered;
        discovered.Discovered.Add(new Tag("Algae"));
        flat.displayOnlyDiscoveredTags = true;
        Check(TreeFilterSelection.GetOptions(tree).SetEquals(new[] { new Tag("Dirt"), new Tag("Algae") }),
            "newly discovered flat options become selectable without a stale cache");
        // A replacement must add the new selection before removing the old one.
        // This also preserves a newly accepted item already present in storage.
        flat.selectedTags.Remove(new Tag("Algae"));
        tree.Selected.Remove(new Tag("Algae"));
        tree.GetFilterStorage().items.Add(AddObject(151, "Algae").Add(new PrimaryElement { Mass = 43 }));
        Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, "filter", "replace", "Algae")));
        Check(flat.selectedTags.SequenceEqual(new[] { new Tag("Algae") })
            && tree.Selected.SetEquals(flat.selectedTags), "valid flat options still use native SelectTag and synchronize tree selection");
        Check(tree.GetFilterStorage().MassStored() == 43
            && tree.GetFilterStorage().items.Single().GetComponent<KPrefabID>().PrefabTag == new Tag("Algae"),
            "flat replace preserves accepted Algae while dropping only deselected Dirt");
        Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, "storage", "add", "Dirt")));
        Check(tree.Selected.SetEquals(new[] { new Tag("Dirt"), new Tag("Algae") })
            && new HashSet<Tag>(flat.selectedTags).SetEquals(tree.Selected), "storage route also keeps flat and tree selections synchronized");
        Check(tree.GetFilterStorage().MassStored() == 43, "flat add preserves existing accepted Algae");
        // Old save data can retain tags removed from tagOptions. The native
        // SelectTag assertion is modeled by the fixture and must never be hit.
        flat.selectedTags.Add(new Tag("RemovedOption"));
        tree.Selected.Add(new Tag("RemovedOption"));
        foreach (string domain in new[] { "storage", "filter" })
        {
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, domain, "replace", "Dirt", "Algae")));
            Check(flat.selectedTags.Contains(new Tag("RemovedOption"))
                && tree.Selected.SetEquals(flat.selectedTags) && tree.GetFilterStorage().MassStored() == 43,
                domain + " does not send removed saved options to native SelectTag");
        }
    }

    private static HashSet<string> FlatReadOptions()
    {
        JObject response = Success(FilterTools.ControlFilter().Handler(new JObject
        { ["action"] = "list", ["includeOptions"] = true, ["kind"] = "flat" }));
        return new HashSet<string>(response["filters"][0]["flat"]["options"].Select(tag => (string)tag["tag"]));
    }

    private static void CheckFlatWriteRejected(TreeFilterable tree, FlatTagFilterable flat, JObject args, string description)
    {
        var before = Snapshot(tree);
        var beforeFlat = flat.selectedTags.ToList();
        int calls = flat.SelectCalls;
        Check(StorageTools.ControlStorageSystem().Handler(args).IsError, description + " rejects the write");
        CheckUnchanged(tree, before, description);
        Check(flat.SelectCalls == calls && flat.selectedTags.SequenceEqual(beforeFlat),
            description + " leaves native flat selection untouched");
    }

}
