using System;
using System.Collections.Generic;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private sealed class PlacementDetails
        {
            public string PrefabId;
            public int AnchorX;
            public int AnchorY;
            public int WorldId;
            public Orientation Orientation;
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
                    ["orientation"] = Orientation.ToString(),
                    ["width"] = Width,
                    ["height"] = Height,
                    ["footprintCells"] = Footprint.Count,
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
    }
}
