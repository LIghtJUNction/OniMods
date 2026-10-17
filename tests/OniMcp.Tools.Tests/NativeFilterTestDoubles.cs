using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Native storage/filter boundaries used by the linked production handlers.
// Source contract: Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636.
// SelectTag deliberately invokes UpdateFilters immediately, matching the game.
internal class Building : KMonoBehaviour { internal BuildingDef Def { get; set; } }
internal sealed class BuildingComplete : Building { }
internal sealed class Storage : KMonoBehaviour
{
    internal List<Tag> storageFilters { get; } = new List<Tag>();
    internal List<GameObject> items { get; } = new List<GameObject>();
    internal float capacityKg { get; set; } = 1000;
    internal float MassStored() => items.Sum(item => item.GetComponent<PrimaryElement>()?.Mass ?? 0);
}
internal sealed class StorageLocker { internal float UserMaxCapacity { get; set; } }

internal sealed class TreeFilterable : KMonoBehaviour
{
    internal Storage FilterStorage { get; set; }
    internal HashSet<Tag> Selected { get; } = new HashSet<Tag>();
    internal HashSet<Tag> ForbiddenTags { get; } = new HashSet<Tag>();
    internal bool dropIncorrectOnFilterChange { get; set; } = true;
    internal int UpdateCalls { get; private set; }
    internal Storage GetFilterStorage() => FilterStorage ?? GetComponent<Storage>();
    internal HashSet<Tag> GetTags() => Selected;

    // Native UpdateFilters is destructive: replacing the set may drop stored
    // contents. Tests assert invalid input never reaches this boundary.
    internal void UpdateFilters(HashSet<Tag> tags)
    {
        UpdateCalls++;
        Selected.Clear();
        Selected.UnionWith(tags);
        Selected.ExceptWith(ForbiddenTags);
        if (dropIncorrectOnFilterChange)
            GetFilterStorage()?.items.RemoveAll(item => !Selected.Contains(item.GetComponent<KPrefabID>().PrefabTag));
    }
}

internal sealed class FlatTagFilterable : KMonoBehaviour
{
    internal List<Tag> tagOptions { get; } = new List<Tag>();
    internal List<Tag> selectedTags { get; } = new List<Tag>();
    internal bool currentlyUserAssignable { get; set; } = true;
    internal int SelectCalls { get; private set; }
    internal string GetHeaderText() => "Fixture";
    internal void SelectTag(Tag tag, bool selected)
    {
        SelectCalls++;
        if (selected)
        {
            if (!selectedTags.Contains(tag)) selectedTags.Add(tag);
        }
        else selectedTags.Remove(tag);
        // Native FlatTagFilterable.SelectTag immediately changes storage filters.
        GetComponent<TreeFilterable>().UpdateFilters(new HashSet<Tag>(selectedTags));
    }
}

internal sealed class Filterable : KMonoBehaviour
{
    internal Tag SelectedTag { get; set; }
    internal string filterElementState { get; set; } = "Solid";
    internal Dictionary<Tag, List<Tag>> GetTagOptions() => throw new NotSupportedException();
}

internal sealed class DiscoveredResources
{
    internal static DiscoveredResources Instance { get; set; }
    internal Dictionary<Tag, HashSet<Tag>> Categories { get; } = new Dictionary<Tag, HashSet<Tag>>();
    internal HashSet<Tag> GetDiscoveredResourcesFromTag(Tag category)
    {
        HashSet<Tag> tags;
        return Categories.TryGetValue(category, out tags) ? tags : new HashSet<Tag>();
    }
}
