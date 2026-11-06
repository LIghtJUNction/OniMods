using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

// Only the game and shared lookup boundaries are replaced. UprootArea itself
// is linked from production. These fixtures are not Unity/ONI runtime evidence.
internal sealed class UprootTestObject
{
    internal const int CancelEvent = 2127324410;
    internal int X { get; set; }
    internal int Y { get; set; }
    internal int WorldId { get; set; }
    internal bool HarvestWhenReady { get; set; }
    internal bool MarkedForHarvest { get; set; }
    internal bool HasHarvestChore { get; set; }
    internal int CancelEvents { get; private set; }
    internal int PriorityWrites { get; set; }

    // Pinned source contract: Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636,
    // Assembly-CSharp/Harvestable.cs OnSpawn/OnCancel. A generic Cancel reaches
    // harvest too; a pending harvest chore also clears HarvestWhenReady.
    internal void Trigger(int eventId)
    {
        if (eventId != CancelEvent)
            throw new InvalidOperationException("Unexpected test game event: " + eventId);
        CancelEvents++;
        if (HasHarvestChore)
            HarvestWhenReady = false;
        HasHarvestChore = false;
        MarkedForHarvest = false;
    }
}

internal sealed class UprootTestComponent
{
    internal UprootTestObject gameObject { get; set; }
    internal bool IsMarkedForUproot { get; set; }
    internal bool CanBeUprooted { get; set; } = true;
    internal int CancelCalls { get; private set; }
    internal int MarkCalls { get; private set; }

    internal bool CanUproot() => CanBeUprooted;
    internal void MarkForUproot()
    {
        MarkCalls++;
        IsMarkedForUproot = true;
    }

    // Uprootable.ForceCancelUproot calls its component-scoped OnCancel(null).
    // It does not send GameHashes.Cancel to other components on the plant.
    internal void ForceCancelUproot()
    {
        CancelCalls++;
        IsMarkedForUproot = false;
    }
}

internal static partial class Components
{
    internal static class Uprootables
    {
        internal static readonly List<UprootTestComponent> Items = new List<UprootTestComponent>();
    }
}

internal static partial class Grid
{
    internal static int PosToCell(UprootTestObject target) => target.X + 1000 * target.Y;
}

namespace OniMcp.Tools
{
    public static partial class ToolUtil
    {
        internal static Dictionary<string, int> GetRect(JObject args)
        {
            return new Dictionary<string, int>
            {
                ["x1"] = (int)args["x1"], ["y1"] = (int)args["y1"],
                ["x2"] = (int)args["x2"], ["y2"] = (int)args["y2"]
            };
        }

        internal static int ResolveWorldId(JObject args) => (int?)args["worldId"] ?? 0;
        internal static bool GameObjectMatchesWorld(UprootTestObject target, int worldId)
        {
            return worldId < 0 || target.WorldId == worldId;
        }
    }

    public static partial class FarmingTools
    {
        private const int CancelEvent = UprootTestObject.CancelEvent;
        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> parameters)
        {
            return parameters;
        }

        private static bool HasRectInput(JObject args)
        {
            return args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null;
        }

        private static int RectCellCount(Dictionary<string, int> rect)
        {
            return (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            int x = cell % 1000;
            int y = cell / 1000;
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static void ApplyPriority(UprootTestObject target, JObject args)
        {
            target.PriorityWrites++;
        }

        private static Dictionary<string, object> TargetInfo(UprootTestObject target, string status)
        {
            return new Dictionary<string, object>
            {
                ["x"] = target.X,
                ["y"] = target.Y,
                ["status"] = status
            };
        }
    }
}
