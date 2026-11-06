using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

internal partial class BuildingDef
{
    internal bool RequiresPowerInput { get; set; }
}

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static CallToolResult PowerConnectionResponseForTest;

        internal static Dictionary<string, object> TestPowerConnectionFailure(
            BuildingDef def, Dictionary<string, object> materialError)
        {
            PowerConnectionResponseForTest = CallToolResult.Error(JsonConvert.SerializeObject(materialError));
            def.RequiresPowerInput = true;
            try
            {
                return TryAutoConnectPower(def, 2, 1, Orientation.Neutral,
                    new JObject { ["worldId"] = 0 }, new HashSet<int>(), new AutoDigContext());
            }
            finally
            {
                def.RequiresPowerInput = false;
                PowerConnectionResponseForTest = null;
            }
        }

        // Only the unrelated native path discovery and tool dispatch are fixtures;
        // TryAutoConnectPower's error/count/status handling is production code.
        private sealed class AutoDigContext { }
        private static bool TryFindNearestWireOrPowerOutput(int input, int world, int radius, out int source)
        { source = Grid.XYToCell(1, 1); return true; }
        private static CellCoord CellCoordFromCell(int cell) => new CellCoord(Grid.CellColumn(cell), Grid.CellRow(cell));
        private static bool AddManhattanSegment(List<CellCoord> path, CellCoord source, CellCoord target, int maxCells, out string error)
        { path.Add(source); path.Add(target); error = null; return true; }
        private static bool IsDryRun(JObject args) => ToolUtil.GetBool(args, "dryRun", false);
        private static McpTool AutoConnectUtility() => new McpTool { Handler = args => PowerConnectionResponseForTest };
        private static Dictionary<string, object> ParseToolJsonPayload(CallToolResult result)
            => JsonConvert.DeserializeObject<Dictionary<string, object>>(result.Content.First().Text);
        private static int GetInt(Dictionary<string, object> result, string name)
            => result.TryGetValue(name, out object value) ? Convert.ToInt32(value) : 0;
        private static List<object> BuildPathSegments(List<CellCoord> path) => new List<object>();
    }
}
