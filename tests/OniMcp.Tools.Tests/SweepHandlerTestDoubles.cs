using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Game objects and unrelated tools are fixtures. SweepArea, its ordering,
// counters, response assembly, and area routing are compiled from production.
internal sealed class SweepTestPickupable
{
    internal GameObject gameObject { get; } = new GameObject();
    internal int Cell { get; set; }
    internal int WorldId { get; set; }
    internal object storage { get; set; }
    internal KPrefabID KPrefabID => GetComponent<KPrefabID>();
    internal PrimaryElement PrimaryElement => GetComponent<PrimaryElement>();
    internal T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
}

internal sealed class Clearable
{
    internal bool isClearable { get; set; }
    internal bool IsMarked { get; private set; }
    internal int MarkCalls { get; private set; }

    internal void MarkForClear()
    {
        MarkCalls++;
        if (isClearable)
            IsMarked = true;
    }
}

internal sealed class SweepTestPriority
{
    internal int Writes { get; set; }
    internal int Value { get; set; } = 3;
}

internal sealed class KPrefabID
{
    internal HashSet<string> Tags { get; } = new HashSet<string>();
    internal bool HasTag(string tag) => Tags.Contains(tag);
}

internal static class GameTags
{
    internal const string Equipped = "Equipped";
    internal const string Stored = "Stored";
}

internal static partial class Components
{
    internal static class Pickupables
    {
        internal static readonly List<SweepTestPickupable> Items = new List<SweepTestPickupable>();
    }
}

internal static partial class Grid
{
    internal static bool IsValidCell(int cell) => cell >= 0;
}

namespace OniMcp.Tools
{
    public static partial class ToolUtil
    {
        internal static float SafeFloat(float value) => value;
    }

    internal static class PreviewTokenRegistry
    {
        private static readonly Dictionary<string, JObject> Cached = new Dictionary<string, JObject>();

        internal static string Register(JObject args)
        {
            string token = Guid.NewGuid().ToString("N");
            Cached[token] = (JObject)args.DeepClone();
            return token;
        }

        internal static JObject Get(string token)
        {
            JObject args;
            return Cached.TryGetValue(token, out args) ? (JObject)args.DeepClone() : null;
        }

        internal static void Reset() => Cached.Clear();
    }

    public static partial class OrdersTools
    {
        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> parameters)
        {
            return parameters;
        }

        // Share the existing Tools fixture's rectangle representation.
        private static int RectCellCount(Tuple<int, int, int, int> rect)
        {
            return (rect.Item3 - rect.Item1 + 1) * (rect.Item4 - rect.Item2 + 1);
        }

        private static int SweepCell(SweepTestPickupable pickupable) => pickupable.Cell;

        private static bool CellInRect(int cell, Tuple<int, int, int, int> rect, int worldId)
        {
            int x = cell % 1000;
            int y = cell / 1000;
            return x >= rect.Item1 && x <= rect.Item3 && y >= rect.Item2 && y <= rect.Item4
                && Components.Pickupables.Items.Any(item => item.Cell == cell
                    && (worldId < 0 || item.WorldId == worldId));
        }

        private static Dictionary<string, object> ScanLiquidCells(Tuple<int, int, int, int> rect, int worldId, int limit)
        {
            return new Dictionary<string, object>
            {
                ["count"] = 0,
                ["samples"] = new List<object>()
            };
        }

        private static List<string> SweepPreviewRisks(Dictionary<string, object> scan)
        {
            return new List<string>();
        }

        private static void IncrementSkip(Dictionary<string, int> skipped, string reason)
        {
            int count;
            skipped.TryGetValue(reason, out count);
            skipped[reason] = count + 1;
        }

        private static void AddSweepTarget(List<Dictionary<string, object>> targets, bool detail, int limit,
            SweepTestPickupable pickupable, int cell, string status)
        {
            if (!detail || targets.Count >= limit)
                return;
            targets.Add(new Dictionary<string, object> { ["cell"] = cell, ["status"] = status });
        }

        private static Dictionary<string, object> CellExecutionMetadata(string action, int worldId,
            List<int> cells, Dictionary<string, int> skipped, bool detail, int limit)
        {
            return new Dictionary<string, object>
            {
                ["targetCount"] = cells.Count,
                ["skipped"] = new Dictionary<string, int>(skipped)
            };
        }

        private static void ApplyPriority(GameObject target, JObject args)
        {
            var priority = target.GetComponent<SweepTestPriority>();
            priority.Writes++;
            priority.Value = (int?)args["priority"] ?? 5;
        }

        // These routes are outside this test. Fail rather than invent success.
        private static McpTool DeconstructBuilding() => throw new NotSupportedException();
        private static McpTool Attack() => throw new NotSupportedException();
        private static McpTool CaptureCritters() => throw new NotSupportedException();
        private static McpTool EmptyConduits() => throw new NotSupportedException();
        private static McpTool CutConduits() => throw new NotSupportedException();
        private static McpTool ConfigureManualDelivery() => throw new NotSupportedException();
        private static McpTool DigArea() => throw new NotSupportedException();
        private static McpTool MopArea() => throw new NotSupportedException();
        private static McpTool DisinfectArea() => throw new NotSupportedException();
        private static McpTool CancelArea() => throw new NotSupportedException();
        private static McpTool HarvestArea() => throw new NotSupportedException();
        private static JObject WithRoutedMode(JObject args) => throw new NotSupportedException();
    }
}
