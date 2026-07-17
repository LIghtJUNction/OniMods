using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Vector3 BuildPlacementPosition(int cell, BuildingDef def)
        {
            return Grid.CellToPosCBC(cell, def.SceneLayer);
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
                ["footprintCells"] = width * height,
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
            return new PlacementDetails
            {
                PrefabId = def.PrefabID,
                AnchorX = x,
                AnchorY = y,
                WorldId = worldId,
                Orientation = orientation,
                Width = Math.Max(1, def.WidthInCells),
                Height = Math.Max(1, def.HeightInCells),
                PlacementPoint = BuildPlacementPosition(cell, def),
                Footprint = FootprintCells(def, x, y, worldId).ToList()
            };
        }

        private static IEnumerable<FootprintCell> FootprintCells(BuildingDef def, int x, int y, int worldId)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int fx = x + dx;
                    int fy = y + dy;
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
        }

        private static FootprintValidation ValidateFootprint(PlacementDetails placement, GameObject ignored = null)
        {
            var invalid = placement.Footprint
                .Where(cell => !cell.Valid || !cell.Visible || !cell.InWorld)
                .Select(cell => cell.ToDictionary())
                .ToList();

            var obstructions = FindFootprintObstructions(placement, ignored);

            if (invalid.Count == 0 && obstructions.Count == 0)
                return FootprintValidation.Success();

            string error = invalid.Count > 0
                ? "Invalid footprint: every occupied cell must be visible, valid, and inside the selected world"
                : "Obstructed footprint: occupied terrain, building, or blueprint overlaps the requested cells";
            return FootprintValidation.Invalid(error, invalid, obstructions);
        }

        private static Dictionary<string, object> ActualPlacementDetails(GameObject go, BuildingDef def, int expectedX, int expectedY)
        {
            int cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
            int originX = x >= 0 ? x : expectedX;
            int originY = y >= 0 ? y : expectedY;

            var building = go.GetComponent<Building>();
            if (building != null)
            {
                int anchorCell = building.GetBottomLeftCell();
                if (Grid.IsValidCell(anchorCell))
                {
                    originX = Grid.CellColumn(anchorCell);
                    originY = Grid.CellRow(anchorCell);
                }
            }

            int worldId = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1;

            return new Dictionary<string, object>
            {
                ["objectCell"] = cell,
                ["objectX"] = x,
                ["objectY"] = y,
                ["derivedAnchorX"] = originX,
                ["derivedAnchorY"] = originY,
                ["worldId"] = worldId,
                ["note"] = "derivedAnchor is Building.GetBottomLeftCell when available; placement uses the requested anchor cell directly"
            };
        }

        private static Dictionary<string, object> ComparePlacement(PlacementDetails expected, Dictionary<string, object> actual)
        {
            int actualX = actual.ContainsKey("derivedAnchorX") ? Convert.ToInt32(actual["derivedAnchorX"]) : -1;
            int actualY = actual.ContainsKey("derivedAnchorY") ? Convert.ToInt32(actual["derivedAnchorY"]) : -1;
            int actualWorld = actual.ContainsKey("worldId") ? Convert.ToInt32(actual["worldId"]) : -1;
            bool anchorMatches = actualX == expected.AnchorX && actualY == expected.AnchorY;
            bool worldMatches = actualWorld < 0 || expected.WorldId < 0 || actualWorld == expected.WorldId;
            bool valid = anchorMatches && worldMatches;
            return new Dictionary<string, object>
            {
                ["valid"] = valid,
                ["anchorMatches"] = anchorMatches,
                ["worldMatches"] = worldMatches,
                ["expectedAnchor"] = new { x = expected.AnchorX, y = expected.AnchorY },
                ["actualDerivedAnchor"] = new { x = actualX, y = actualY },
                ["expectedWorldId"] = expected.WorldId,
                ["actualWorldId"] = actualWorld,
                ["next"] = valid
                    ? "Verify with world_area_snapshot/world_text_map before placing the next footprint batch."
                    : "Cancel the misplaced blueprint before retrying from the expected anchor."
            };
        }

        private         static Dictionary<string, object> BuildPlacementFailureDetails(PlacementDetails placement, MaterialSelection materialResult)
        {
            return new Dictionary<string, object>
            {
                ["placement"] = placement.ToDictionary(),
                ["obstructions"] = FindFootprintObstructions(placement).Take(50).ToList(),
                ["materialSelection"] = materialResult.ToDictionary(),
                ["materials"] = materialResult.ToDictionary(),
                ["reasonHint"] = "TryPlace returned null after preflight; inspect obstructions/support/materialSelection for likely cause."
            };
        }
    }
}
