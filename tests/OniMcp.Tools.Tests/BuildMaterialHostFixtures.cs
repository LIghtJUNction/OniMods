using System.Collections.Generic;
using UnityEngine;

// Only native inventory, element definitions and navigation are fixtures. Selection,
// local/related stock handling and response serialization use production source.
internal sealed class WorldInventory
{
    internal readonly Dictionary<string, float> Local = new Dictionary<string, float>();
    internal readonly Dictionary<string, float> Related = new Dictionary<string, float>();
    internal float GetTotalAmount(Tag tag, bool includeRelatedWorlds)
    {
        var source = includeRelatedWorlds ? Related : Local;
        return source.TryGetValue(tag.Name, out float amount) ? amount : 0f;
    }
}

internal sealed class Element
{
    internal readonly HashSet<Tag> Categories = new HashSet<Tag>();
    internal Tag GetMaterialCategoryTag() => Tag.Invalid;
    internal bool HasTag(Tag tag) => Categories.Contains(tag);
}

internal static class ElementLoader
{
    internal static readonly Dictionary<string, Element> Elements = new Dictionary<string, Element>();
    internal static Element GetElement(Tag tag) => tag.IsValid && Elements.TryGetValue(tag.Name, out var value) ? value : null;
}

internal sealed class DiscoveredResources
{
    internal static DiscoveredResources Instance => null;
    internal IEnumerable<Tag> GetDiscoveredResourcesFromTag(Tag tag) => new Tag[0];
}

internal sealed class Navigator
{
    internal bool CanReach(int cell) => true;
}

internal sealed class MinionIdentity
{
    internal int WorldId { get; set; }
    internal GameObject gameObject { get; } = new GameObject();
    internal int GetMyWorldId() => WorldId;
    internal T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
}

internal static partial class Components
{
    internal static class LiveMinionIdentities
    {
        internal static readonly List<MinionIdentity> Items = new List<MinionIdentity>();
    }
}

internal static partial class Grid
{
    internal static readonly int[] WorldIdx = new int[1000];
    internal static bool IsWorldValidCell(int cell) => cell >= 0 && cell < WorldIdx.Length;
}

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal static Dictionary<string, object> TestMaterialSelection(BuildingDef def, string material, int worldId)
            => SelectElements(def, material, worldId).ToDictionary();

        internal static void ResetMaterialStockForTest()
        {
            BuildStockSnapshots.Clear();
            BuildStockSnapshotFrame = -1;
        }
    }
}
