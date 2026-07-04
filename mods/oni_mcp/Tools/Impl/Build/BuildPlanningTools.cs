using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        public static McpTool ControlBuildPlanning()
        {
            return new McpTool
            {
                Name = "build_planning_control",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "buildings_planning_control", "build_control" },
                Tags = new List<string> { "buildings", "materials", "preview", "placement", "utility", "建造", "材料", "预检", "候选" },
 Description = "建造规划组合工具：action=search_defs/materials/preview/placement_candidates/auto_connect/build_area/room_template",
                Parameters = BuildPlanningControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    var forwardArgs = ForwardArgs(args);
                    switch (action)
                {
                    case "parse_plan":
                    case "parse_sequence":
                    case "parse":
                    case "plan_text":
                        return ParseBuildPlan().Handler(forwardArgs);
                    case "search_defs":
                        case "search":
                        case "defs":
                            return SearchBuildables().Handler(forwardArgs);
                        case "materials":
                        case "list_materials":
                            return ListBuildMaterials().Handler(forwardArgs);
                        case "preview":
                        case "validate":
                            return PreviewBuild().Handler(forwardArgs);
                        case "placement_candidates":
                        case "candidates":
                        case "anchors":
                            return FindPlacementCandidates().Handler(forwardArgs);
                        case "auto_connect":
                        case "utility_auto_connect":
                        case "connect":
                            return AutoConnectUtility().Handler(forwardArgs);
 case "build_area":
 case "area":
 case "batch_build":
 return BuildArea().Handler(forwardArgs);
 case "room_template":
 case "room_plan":
 case "quick_room":
 return RoomTemplatePlan().Handler(forwardArgs);
 default:
 return CallToolResult.Error("action must be parse_plan, search_defs, materials, preview, placement_candidates, auto_connect, build_area, or room_template");
                    }
                }
            };
        }


        private static string DefaultUtilityPrefab(string type)
        {
            switch ((type ?? "wire").Trim().ToLowerInvariant())
            {
                case "liquid":
                case "water":
                case "pipe":
                    return "LiquidConduit";
                case "gas":
                    return "GasConduit";
                case "solid":
                case "conveyor":
                case "shipping":
                    return "SolidConduit";
                case "logic":
                case "automation":
                case "signal":
                    return "LogicWire";
                default:
                    return "Wire";
            }
        }

        private sealed class PlacementDetails
        {
            public string PrefabId;
            public int AnchorX;
            public int AnchorY;
            public int WorldId;
            public int Width;
            public int Height;
            public Vector3 PlacementPoint;
            public List<FootprintCell> Footprint = new List<FootprintCell>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["prefabId"] = PrefabId,
                    ["anchor"] = "lowerLeftCell",
                    ["anchorX"] = AnchorX,
                    ["anchorY"] = AnchorY,
                    ["worldId"] = WorldId,
                    ["width"] = Width,
                    ["height"] = Height,
                    ["footprintCells"] = Width * Height,
                    ["placementPoint"] = new
                    {
                        x = Math.Round(PlacementPoint.x, 3),
                        y = Math.Round(PlacementPoint.y, 3),
                        z = Math.Round(PlacementPoint.z, 3)
                    },
                    ["guidance"] = Width == 1 && Height == 1
                        ? "This is a single-cell footprint and can be line-dragged."
                        : "This is a multi-cell footprint; place each anchor with a separate left click and verify before continuing."
                };
            }
        }

        private sealed class FootprintCell
        {
            public int X;
            public int Y;
            public int Cell;
            public int WorldId;
            public bool Valid;
            public bool Visible;
            public bool InWorld;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["x"] = X,
                    ["y"] = Y,
                    ["cell"] = Cell,
                    ["worldId"] = WorldId,
                    ["valid"] = Valid,
                    ["visible"] = Visible,
                    ["inWorld"] = InWorld,
                    ["reasonCode"] = Valid && Visible && InWorld ? null : (!Valid ? "invalid_cell" : (!Visible ? "unrevealed" : "wrong_world"))
                };
            }
        }

        private sealed class FootprintValidation
        {
            public bool Valid;
            public string Error;
            public List<Dictionary<string, object>> InvalidCells = new List<Dictionary<string, object>>();
            public List<Dictionary<string, object>> Obstructions = new List<Dictionary<string, object>>();

            public static FootprintValidation Success()
            {
                return new FootprintValidation { Valid = true };
            }

            public static FootprintValidation Invalid(string error, List<Dictionary<string, object>> invalidCells, List<Dictionary<string, object>> obstructions = null)
            {
                return new FootprintValidation
                {
                    Valid = false,
                    Error = error,
                    InvalidCells = invalidCells ?? new List<Dictionary<string, object>>(),
                    Obstructions = obstructions ?? new List<Dictionary<string, object>>()
                };
            }

            public Dictionary<string, object> ToDictionary(PlacementDetails placement)
            {
                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["error"] = Error,
                    ["placement"] = placement.ToDictionary(),
                    ["invalidCells"] = InvalidCells,
                    ["obstructions"] = Obstructions
                };
            }
        }

        private sealed class BuildDragPolicyResult
        {
            public bool Allowed;
            public string PrefabId;
            public int Width;
            public int Height;
            public bool SingleCell;
            public bool AllowFootprintDrag;
            public string Reason;

            public static BuildDragPolicyResult Allow(string prefabId, int width, int height, bool singleCell, bool allowFootprintDrag)
            {
                return new BuildDragPolicyResult
                {
                    Allowed = true,
                    PrefabId = prefabId,
                    Width = width,
                    Height = height,
                    SingleCell = singleCell,
                    AllowFootprintDrag = allowFootprintDrag,
                    Reason = singleCell ? "single-cell footprint" : "allowFootprintDrag=true"
                };
            }

            public static BuildDragPolicyResult Reject(string prefabId, int width, int height)
            {
                return new BuildDragPolicyResult
                {
                    Allowed = false,
                    PrefabId = prefabId,
                    Width = width,
                    Height = height,
                    SingleCell = false,
                    AllowFootprintDrag = false,
                    Reason = "Multi-cell buildings must be placed one anchor click at a time to avoid shifted furniture or machines."
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["allowed"] = Allowed,
                    ["prefabId"] = PrefabId,
                    ["width"] = Width,
                    ["height"] = Height,
                    ["singleCell"] = SingleCell,
                    ["allowFootprintDrag"] = AllowFootprintDrag,
                    ["reason"] = Reason,
                    ["next"] = Allowed ? null : "Use navigation_control action=left_click for each lower-left anchor cell, or retry with allowFootprintDrag=true if this repeated footprint is intentional."
                };
            }
        }

        private sealed class AutoDigContext
        {
            private readonly HashSet<int> reservedCells = new HashSet<int>();
            private int distance;

            public int MaxCells;
            public int Marked;
            public bool LimitReached;

            public static AutoDigContext FromArgs(JObject args)
            {
                return new AutoDigContext
                {
                    MaxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxAutoDigCells") ?? 100, 500))
                };
            }

            public bool TryReserve(int cell)
            {
                if (!reservedCells.Add(cell))
                    return false;
                if (Marked >= MaxCells)
                {
                    LimitReached = true;
                    return false;
                }
                return true;
            }

            public int NextDistance()
            {
                return distance++;
            }
        }

    }
}
