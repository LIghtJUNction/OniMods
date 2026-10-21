using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static FootprintValidation ValidateFootprint(PlacementDetails placement, GameObject ignored = null)
        {
            var invalid = placement.Footprint
                .Where(cell => !cell.Valid || !cell.Visible || !cell.InWorld)
                .Select(cell => cell.ToDictionary())
                .ToList();

            var obstructions = FindFootprintObstructions(placement, ignored);
            AddBackwallFoundationFailure(placement, obstructions);

            if (invalid.Count == 0 && obstructions.Count == 0)
                return FootprintValidation.Success();

            var backwallFailure = obstructions.FirstOrDefault(obstruction =>
                obstruction.ContainsKey("reasonCode")
                && string.Equals(obstruction["reasonCode"]?.ToString(), "backwall_required", StringComparison.Ordinal));
            string error = invalid.Count > 0
                ? "Invalid footprint: every occupied cell must be visible, valid, and inside the selected world"
                : backwallFailure != null
                    ? backwallFailure["reason"]?.ToString()
                    : "Obstructed footprint: occupied terrain, building, or blueprint overlaps the requested cells";
            return FootprintValidation.Invalid(error, invalid, obstructions);
        }

        private static void AddBackwallFoundationFailure(PlacementDetails placement, List<Dictionary<string, object>> obstructions)
        {
            var def = ResolveBuildingDefForPlacement(placement);
            if (def == null)
                return;

            string rule = def.BuildLocationRule.ToString();
            if (!string.Equals(rule, "OnBackWall", StringComparison.OrdinalIgnoreCase))
                return;

            int cell = Grid.PosToCell(placement.PlacementPoint);
            bool nativeFoundationValid = Grid.IsValidCell(cell)
                && BuildingDef.CheckFoundation(
                    cell,
                    placement.Orientation,
                    def.BuildLocationRule,
                    def.WidthInCells,
                    def.HeightInCells);
            var decision = BuildPlanningBackwallSupportPolicy.Evaluate(rule, nativeFoundationValid);
            if (decision.Valid)
                return;

            obstructions.Insert(0, new Dictionary<string, object>
            {
                ["kind"] = "missing_backwall",
                ["x"] = placement.AnchorX,
                ["y"] = placement.AnchorY,
                ["cell"] = cell,
                ["buildLocationRule"] = rule,
                ["reasonCode"] = decision.ReasonCode,
                ["reason"] = decision.Error,
                ["nativeFoundationCheck"] = true
            });
        }

        private static Dictionary<string, object> ActualPlacementDetails(GameObject go, BuildingDef def, int expectedX, int expectedY)
        {
            int cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
            int originX = x >= 0 ? x : expectedX;
            int originY = y >= 0 ? y : expectedY;

            int worldId = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1;
            var rotatable = go.GetComponent<Rotatable>();
            bool orientationRelevant = rotatable != null || def?.BuildingComplete?.GetComponent<Rotatable>() != null;
            Orientation orientation = rotatable == null ? Orientation.Neutral : rotatable.GetOrientation();
            var building = go.GetComponent<Building>();
            var occupied = building?.PlacementCells?.Where(Grid.IsValidCell).ToArray();
            if (occupied != null && occupied.Length > 0)
            {
                originX = occupied.Min(Grid.CellColumn);
                originY = occupied.Min(Grid.CellRow);
            }
            else if (def != null && Grid.IsValidCell(cell))
            {
                var offsets = NativePlacementOffsets(def, orientation).ToArray();
                originX = x + offsets.Min(p => p.x);
                originY = y + offsets.Min(p => p.y);
            }

            return new Dictionary<string, object>
            {
                ["objectCell"] = cell,
                ["objectX"] = x,
                ["objectY"] = y,
                ["derivedAnchorX"] = originX,
                ["derivedAnchorY"] = originY,
                ["worldId"] = worldId,
                ["orientation"] = orientation.ToString(),
                ["orientationRelevant"] = orientationRelevant,
                ["note"] = "derivedAnchor is the lower-left bound of actual occupied cells; objectX/objectY retain the native placement origin"
            };
        }

        private static Dictionary<string, object> ComparePlacement(PlacementDetails expected, Dictionary<string, object> actual)
        {
            int actualX = actual.ContainsKey("derivedAnchorX") ? Convert.ToInt32(actual["derivedAnchorX"]) : -1;
            int actualY = actual.ContainsKey("derivedAnchorY") ? Convert.ToInt32(actual["derivedAnchorY"]) : -1;
            int actualWorld = actual.ContainsKey("worldId") ? Convert.ToInt32(actual["worldId"]) : -1;
            string actualOrientation = actual.ContainsKey("orientation")
                ? actual["orientation"]?.ToString()
                : Orientation.Neutral.ToString();
            bool orientationRelevant = actual.ContainsKey("orientationRelevant")
                && Convert.ToBoolean(actual["orientationRelevant"]);
            var identity = BuildPlacementIdentityPolicy.Evaluate(
                expected.AnchorX,
                expected.AnchorY,
                expected.WorldId,
                expected.Orientation.ToString(),
                actualX,
                actualY,
                actualWorld,
                actualOrientation,
                orientationRelevant);
            return new Dictionary<string, object>
            {
                ["valid"] = identity.Valid,
                ["anchorMatches"] = identity.AnchorMatches,
                ["worldMatches"] = identity.WorldMatches,
                ["orientationMatches"] = identity.OrientationMatches,
                ["orientationRelevant"] = orientationRelevant,
                ["expectedAnchor"] = new { x = expected.AnchorX, y = expected.AnchorY },
                ["actualDerivedAnchor"] = new { x = actualX, y = actualY },
                ["expectedWorldId"] = expected.WorldId,
                ["actualWorldId"] = actualWorld,
                ["expectedOrientation"] = expected.Orientation.ToString(),
                ["actualOrientation"] = actualOrientation,
                ["next"] = identity.Valid
                    ? "Verify with world_area_snapshot/world_text_map before placing the next footprint batch."
                    : "Inspect the existing object's anchor and orientation before retrying; do not treat it as the requested placement."
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
