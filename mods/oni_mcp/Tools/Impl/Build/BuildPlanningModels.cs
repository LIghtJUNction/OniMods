using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private struct CellCoord
        {
            public readonly int x;
            public readonly int y;

            public CellCoord(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        private struct SupportCell
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Cell;

            public SupportCell(int x, int y, int cell)
            {
                X = x;
                Y = y;
                Cell = cell;
            }
        }
        private sealed class SupportValidation
        {
            public bool Valid;
            public bool WarningOnly;
            public string Rule;
            public List<Dictionary<string, object>> MissingSupportCells = new List<Dictionary<string, object>>();
            public string Error;

            public static SupportValidation Success(string rule, List<Dictionary<string, object>> missing)
            {
                return new SupportValidation
                {
                    Valid = true,
                    WarningOnly = false,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>()
                };
            }

            public static SupportValidation Warning(string rule, List<Dictionary<string, object>> missing, string error)
            {
                return new SupportValidation
                {
                    Valid = true,
                    WarningOnly = true,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>(),
                    Error = error
                };
            }

            public static SupportValidation Invalid(string rule, List<Dictionary<string, object>> missing, string error)
            {
                return new SupportValidation
                {
                    Valid = false,
                    WarningOnly = false,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>(),
                    Error = error
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["warningOnly"] = WarningOnly,
                    ["buildLocationRule"] = Rule,
                    ["missingSupportCells"] = MissingSupportCells,
                    ["error"] = Error
                };
            }
        }

        private sealed class PlacementCandidate
        {
            public int Score;
            public string Status;
            public Dictionary<string, object> Anchor = new Dictionary<string, object>();
            public Dictionary<string, object> Preview = new Dictionary<string, object>();

            public int AnchorX => GetInt(Anchor, "x");
            public int AnchorY => GetInt(Anchor, "y");

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["score"] = Score,
                    ["status"] = Status,
                    ["anchor"] = Anchor,
                    ["preview"] = Preview,
                    ["placement"] = GetObject(Preview, "placement"),
                    ["footprint"] = GetObjectList(Preview, "footprint"),
                    ["support"] = GetObject(Preview, "support"),
                    ["materialSelection"] = GetObject(Preview, "materialSelection"),
                    ["facade"] = Preview != null && Preview.ContainsKey("facade") ? Preview["facade"] : null,
                    ["error"] = Preview != null && Preview.ContainsKey("error") ? Preview["error"] : null
                };
            }
        }
    }
}
