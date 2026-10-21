using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static IEnumerable<Dictionary<string, object>> ExistingObjectFootprint(GameObject go, BuildingDef def, string kind, HashSet<int> footprintCells)
        {
            if (go == null)
                yield break;
            int objectCell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(objectCell))
                yield break;

            int objectX = Grid.CellColumn(objectCell);
            int objectY = Grid.CellRow(objectCell);
            var building = go.GetComponent<Building>();
            // GetBottomLeftCell returns the native placement origin. For wide
            // or rotated buildings it is not the minimum occupied X/Y cell.
            var occupied = building?.PlacementCells;
            if (occupied == null || occupied.Length == 0)
            {
                var orientation = go.GetComponent<Rotatable>()?.GetOrientation() ?? Orientation.Neutral;
                occupied = def?.PlacementOffsets?.Select(offset => Grid.OffsetCell(objectCell,
                    Rotatable.GetRotatedCellOffset(offset, orientation))).ToArray();
                if (occupied == null || occupied.Length == 0)
                    occupied = new[] { objectCell };
            }
            int anchorX = int.MaxValue;
            int anchorY = int.MaxValue;
            foreach (int cell in occupied)
            {
                if (!Grid.IsValidCell(cell))
                    continue;
                anchorX = Math.Min(anchorX, Grid.CellColumn(cell));
                anchorY = Math.Min(anchorY, Grid.CellRow(cell));
            }
            var kpid = go.GetComponent<KPrefabID>();
            string id = def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;

            foreach (int cell in occupied)
            {
                if (!Grid.IsValidCell(cell) || !footprintCells.Contains(cell))
                    continue;
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                yield return new Dictionary<string, object>
                {
                    ["kind"] = kind,
                    ["id"] = id,
                    ["name"] = ToolUtil.CleanName(go.GetProperName()),
                    ["x"] = x,
                    ["y"] = y,
                    ["cell"] = cell,
                    ["objectX"] = objectX,
                    ["objectY"] = objectY,
                    ["anchorX"] = anchorX,
                    ["anchorY"] = anchorY,
                    ["reasonCode"] = "occupied_by_" + kind,
                    ["reason"] = "requested footprint overlaps an existing " + kind + " footprint"
                };
            }
        }
    }
}
