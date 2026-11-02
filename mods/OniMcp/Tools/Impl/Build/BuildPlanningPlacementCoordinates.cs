using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Vector3 BuildPlacementPosition(int cell, BuildingDef def,
            Orientation orientation = Orientation.Neutral)
        {
            return Grid.CellToPosCBC(NativePlacementOriginCell(cell, def, orientation), def.SceneLayer);
        }

        private static int NativePlacementOriginCell(int lowerLeftCell, BuildingDef def, Orientation orientation)
        {
            var offsets = NativePlacementOffsets(def, orientation).ToArray();
            return Grid.XYToCell(Grid.CellColumn(lowerLeftCell) - offsets.Min(p => p.x),
                Grid.CellRow(lowerLeftCell) - offsets.Min(p => p.y));
        }

        private static IEnumerable<CellOffset> NativePlacementOffsets(BuildingDef def, Orientation orientation)
        {
            var offsets = def.PlacementOffsets;
            if (offsets == null || offsets.Length == 0)
            {
                // Same default origin as BuildingDef.GenerateOffsets, without mutating the definition.
                int width = Math.Max(1, def.WidthInCells);
                int height = Math.Max(1, def.HeightInCells);
                offsets = Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width)
                    .Select(x => new CellOffset { x = x + width / 2 - width + 1, y = y })).ToArray();
            }
            foreach (var offset in offsets)
                yield return Rotatable.GetRotatedCellOffset(offset, orientation);
        }

        private static GameObject TryPlaceRequestedBuilding(BuildingDef def, PlacementDetails placement,
            IList<Tag> elements, string facadeId)
        {
            // PlacementPoint was converted once from the public lower-left anchor.
            return def.TryPlace(null, placement.PlacementPoint, placement.Orientation, elements, facadeId);
        }

        private static Dictionary<string, object> BuildDefPlacementToDictionary(BuildingDef def)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            return new Dictionary<string, object>
            {
                ["anchor"] = "lowerLeftCell",
                ["anchorDescription"] = "building_control planning treats each anchor as the lower-left footprint cell, not the visual center",
                ["width"] = width,
                ["height"] = height,
                ["footprintCells"] = NativePlacementOffsets(def, Orientation.Neutral).Count(),
                ["singleCellDragSafe"] = width == 1 && height == 1,
                ["dragGuidance"] = width == 1 && height == 1
                    ? "Use building_control domain=planning action=build_area with anchors for repeated tiles, ladders, or buildings; points are only for wire, conduit, or rail utility routes."
                    : "Use building_control domain=planning action=build_area with one lower-left anchor per multi-cell footprint."
            };
        }

        private static PlacementDetails BuildPlacementDetails(BuildingDef def, int x, int y, int worldId,
            Orientation orientation = Orientation.Neutral)
        {
            int cell = Grid.XYToCell(x, y);
            var footprint = FootprintCells(def, x, y, worldId, orientation).ToList();
            return new PlacementDetails
            {
                PrefabId = def.PrefabID,
                AnchorX = x,
                AnchorY = y,
                WorldId = worldId,
                Orientation = orientation,
                Width = footprint.Max(p => p.X) - footprint.Min(p => p.X) + 1,
                Height = footprint.Max(p => p.Y) - footprint.Min(p => p.Y) + 1,
                PlacementPoint = BuildPlacementPosition(cell, def, orientation),
                Footprint = footprint
            };
        }

        private static IEnumerable<FootprintCell> FootprintCells(BuildingDef def, int x, int y, int worldId,
            Orientation orientation = Orientation.Neutral)
        {
            var offsets = NativePlacementOffsets(def, orientation).ToArray();
            int originX = x - offsets.Min(p => p.x);
            int originY = y - offsets.Min(p => p.y);
            foreach (var offset in offsets)
            {
                int fx = originX + offset.x;
                int fy = originY + offset.y;
                int cell = Grid.XYToCell(fx, fy);
                yield return new FootprintCell
                {
                    X = fx,
                    Y = fy,
                    Cell = cell,
                    WorldId = worldId,
                    Valid = Grid.IsValidCell(cell),
                    Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell),
                    InWorld = Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId)
                };
            }
        }

        private static IEnumerable<SupportCell> FloorSupportCells(BuildingDef def, int x, int y, Orientation orientation)
        {
            int width = Math.Max(1, def.WidthInCells);
            int origin = NativePlacementOriginCell(Grid.XYToCell(x, y), def, orientation);
            for (int dx = 0; dx < width; dx++)
            {
                var offset = Rotatable.GetRotatedCellOffset(new CellOffset { x = dx - (width - 1) / 2, y = -1 }, orientation);
                int sx = Grid.CellColumn(origin) + offset.x;
                int sy = Grid.CellRow(origin) + offset.y;
                yield return new SupportCell(sx, sy, Grid.XYToCell(sx, sy));
            }
        }

        private static int PowerInputCell(BuildingDef def, int anchorX, int anchorY, Orientation orientation)
        {
            int anchorCell = def == null ? Grid.InvalidCell
                : NativePlacementOriginCell(Grid.XYToCell(anchorX, anchorY), def, orientation);
            if (!Grid.IsValidCell(anchorCell) || def == null)
                return Grid.InvalidCell;
            CellOffset rotated = Rotatable.GetRotatedCellOffset(def.PowerInputOffset, orientation);
            return Grid.OffsetCell(anchorCell, rotated);
        }
    }
}
