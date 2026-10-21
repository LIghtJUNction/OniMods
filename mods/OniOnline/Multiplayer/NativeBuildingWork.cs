using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // Deliberately narrower than the game's general build tool: no construction digs,
    // uproots, replacements, utility networks, skins or remote-provided footprints.
    internal static class NativeBuildingWork
    {
        internal static WorkExecution Preflight(WorldContainer world, WorkTaskIntent plan, bool enabled, Func<GameObject, bool> managedPending)
        {
            if (!enabled) return WorkExecution.RejectBeforeMutation("work_native_build_validation_required");
            if (world == null || !WorkPlanGeometry.ValidBuild(plan.Build)) return WorkExecution.RejectBeforeMutation("work_unsupported_build_spec");
            if (DebugHandler.InstantBuildMode || Game.Instance.SandboxModeActive) return WorkExecution.RejectBeforeMutation("work_build_game_mode");
            BuildingDef def = Assets.GetBuildingDef(plan.Build.PrefabId);
            string error = ValidateDefinition(def, plan.Build);
            if (error != null) return WorkExecution.RejectBeforeMutation(error);
            if (Db.Get() == null || !Db.Get().TechItems.IsTechItemComplete(def.PrefabID)) return WorkExecution.RejectBeforeMutation("work_build_research_locked");
            if (DiscoveredResources.Instance == null) return WorkExecution.RejectBeforeMutation("work_world_unavailable");
            Tag material = new Tag(plan.Build.MaterialTags[0]);
            if (!MaterialSelector.GetValidMaterials(new Tag(def.MaterialCategory[0]), true).Contains(material) ||
                ElementLoader.GetElement(material) == null || !DiscoveredResources.Instance.IsDiscovered(material))
                return WorkExecution.RejectBeforeMutation("work_build_material_invalid");
            if (!MaterialSelector.AllowInsufficientMaterialBuild() && !def.MaterialsAvailable(new[] { material }, world))
                return WorkExecution.RejectBeforeMutation("work_build_material_unavailable");
            int anchor;
            error = ResolveCell(world, plan.Cell, out anchor);
            if (error != null) return WorkExecution.RejectBeforeMutation(error);
            var nativeCells = def.PlacementOffsets.Select(offset => Grid.OffsetCell(anchor, Rotatable.GetRotatedCellOffset(offset, Orientation.Neutral))).ToArray();
            var claimedCells = WorkPlanGeometry.Footprint(plan).Select(c => Grid.XYToCell(c.X + world.WorldOffset.x, c.Y + world.WorldOffset.y)).ToArray();
            if (!nativeCells.OrderBy(c => c).SequenceEqual(claimedCells.OrderBy(c => c))) return WorkExecution.RejectBeforeMutation("work_build_geometry_changed");
            bool replacingManaged = false;
            foreach (WorkCell point in WorkPlanGeometry.Footprint(plan))
            {
                int cell; error = ResolveCell(world, point, out cell);
                if (error != null) return WorkExecution.RejectBeforeMutation(error);
                if (Grid.Solid[cell] || Grid.Foundation[cell] || Grid.Element[cell].IsLiquid)
                    return WorkExecution.RejectBeforeMutation("work_build_requires_clear_cell");
                if (Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null) return WorkExecution.RejectBeforeMutation("work_build_native_dig_conflict");
                for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
                {
                    GameObject existing = Grid.Objects[cell, layer];
                    if (existing == null) continue;
                    if (managedPending != null && managedPending(existing)) { replacingManaged = true; continue; }
                    if (existing.GetComponent<Uprootable>() != null || existing.GetComponent<Building>() != null || existing.GetComponent<Constructable>() != null)
                        return WorkExecution.RejectBeforeMutation("work_build_unmanaged_footprint_conflict");
                }
            }
            // Both first-slice definitions are OnFloor. Validate the real explored support row
            // in this world, rather than accepting another player's unbuilt support proposal.
            for (int x = 0; x < def.WidthInCells; x++)
            {
                int support; error = ResolveCell(world, new WorkCell(plan.Cell.X + x, plan.Cell.Y - 1), out support);
                if (error != null) return WorkExecution.RejectBeforeMutation(error);
            }
            string reason;
            if (!def.IsValidBuildLocation(def.BuildingComplete, anchor, Orientation.Neutral, false, out reason))
                return WorkExecution.RejectBeforeMutation("work_build_location_invalid");
            // An existing managed victim cannot be made invisible to a native dry run. It is
            // reported as replacement and checked again after exact native cancellation.
            if (!replacingManaged && !def.IsValidPlaceLocation(null, anchor, Orientation.Neutral, false, out reason, false))
                return WorkExecution.RejectBeforeMutation("work_build_placement_invalid");
            return WorkExecution.Accept(1);
        }

        private static string ValidateDefinition(BuildingDef def, WorkBuildSpec build)
        {
            int width = build.PrefabId == "Outhouse" ? 2 : 1, height = build.PrefabId == "Outhouse" ? 3 : 2;
            if (def == null || !def.IsAvailable() || def.DebugOnly || !Game.IsCorrectDlcActiveForCurrentSave(def) || !def.ShouldShowInBuildMenu() ||
                def.BuildingComplete == null || def.BuildingUnderConstruction == null || def.WidthInCells != width || def.HeightInCells != height ||
                def.BuildLocationRule != BuildLocationRule.OnFloor || def.ObjectLayer != ObjectLayer.Building || def.TileLayer != ObjectLayer.NumLayers ||
                def.ReplacementLayer != ObjectLayer.NumLayers || def.RequiresPowerInput || def.RequiresPowerOutput || def.InputConduitType != ConduitType.None ||
                def.OutputConduitType != ConduitType.None || def.AttachmentSlotTag.IsValid || def.OnePerWorld || !def.Cancellable ||
                def.BuildingUnderConstruction.GetComponent<Cancellable>() == null || def.BuildingUnderConstruction.GetComponent<RemoteWorkBuildProvenance>() == null || def.BuildingComplete.GetComponent<LogicPorts>() != null ||
                def.MaterialCategory == null || def.MaterialCategory.Length != 1 || def.Mass == null || def.Mass.Length != 1 ||
                def.CraftRecipe == null || def.CraftRecipe.Ingredients.Count != 1 || def.PlacementOffsets == null || def.PlacementOffsets.Length != width * height)
                return "work_build_definition_not_supported";
            var id = def.BuildingComplete.GetComponent<KPrefabID>();
            if (id == null || id.HasTag(GameTags.Telepad) || id.HasTag(GameTags.RocketInteriorBuilding) || id.HasTag(GameTags.NotRocketInteriorBuilding) || id.HasTag(GameTags.UniquePerWorld))
                return "work_build_special_world_restriction";
            return null;
        }

        internal static string CanChangePending(WorldContainer world, GameObject order, WorkTaskIntent plan)
        {
            if (order == null || order.GetComponent<RemoteWorkBuildProvenance>() == null || !order.GetComponent<RemoteWorkBuildProvenance>().Managed ||
                !RemoteWorkPersistence.SameIntent(order.GetComponent<RemoteWorkBuildProvenance>().CaptureOrder().Winner, plan) ||
                order.GetComponent<Constructable>() == null || order.GetComponent<Cancellable>() == null || DetailsScreen.Instance == null)
                return "work_build_cancel_unavailable";
            foreach (WorkCell point in WorkPlanGeometry.Footprint(plan))
            {
                int cell;
                string error = ResolveCell(world, point, out cell); if (error != null) return error;
                // Native cleanup deletes ALL digs here, and priority callbacks also retarget
                // digs. Do not let a later native designation become an accidental victim.
                if (Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null) return "work_build_cancel_native_dig_conflict";
                for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
                {
                    GameObject other = Grid.Objects[cell, layer];
                    if (other != null && other != order && other.GetComponent<Uprootable>() != null)
                        return "work_build_cancel_native_uproot_conflict";
                }
            }
            return null;
        }

        internal static GameObject Place(WorldContainer world, WorkTaskIntent plan, out string error)
        {
            error = null;
            int cell;
            error = ResolveCell(world, plan.Cell, out cell);
            if (error != null) return null;
            BuildingDef def = Assets.GetBuildingDef(plan.Build.PrefabId);
            string reason;
            if (!def.IsValidPlaceLocation(null, cell, Orientation.Neutral, false, out reason, false)) { error = "work_build_placement_changed"; return null; }
            return def.TryPlace(null, Grid.CellToPosCBC(cell, Grid.SceneLayer.Building), Orientation.Neutral,
                plan.Build.MaterialTags.Select(t => new Tag(t)).ToArray(), "DEFAULT_FACADE", false, 0);
        }

        internal static bool Matches(GameObject order, WorkPlanet planet, WorkTaskIntent plan, int nativeObjectId)
        {
            if (order == null || !WorkPlanGeometry.ValidBuild(plan.Build)) return false;
            var world = ClusterManager.Instance == null ? null : ClusterManager.Instance.GetWorld(planet.NativeWorldId);
            var building = order.GetComponent<BuildingUnderConstruction>();
            var constructable = order.GetComponent<Constructable>();
            var prefab = order.GetComponent<KPrefabID>();
            var facade = order.GetComponent<BuildingFacade>();
            if (world == null || building == null || constructable == null || prefab == null || prefab.InstanceID != nativeObjectId ||
                building.Def.PrefabID != plan.Build.PrefabId || ValidateDefinition(building.Def, plan.Build) != null || building.Orientation != Orientation.Neutral || facade == null ||
                (!string.IsNullOrWhiteSpace(facade.CurrentFacade) && facade.CurrentFacade != "DEFAULT_FACADE") ||
                !constructable.SelectedElementsTags.Select(t => t.Name).SequenceEqual(plan.Build.MaterialTags)) return false;
            int anchor;
            if (ResolveCell(world, plan.Cell, out anchor) != null || Grid.PosToCell(order) != anchor) return false;
            return WorkPlanGeometry.Footprint(plan).All(c =>
            {
                int cell; return ResolveCell(world, c, out cell) == null && Grid.Objects[cell, (int)ObjectLayer.Building] == order;
            });
        }
        private static string ResolveCell(WorldContainer world, WorkCell point, out int cell)
        {
            cell = -1;
            if (point.X < 0 || point.Y < 0 || point.X >= world.WorldSize.x || point.Y >= world.WorldSize.y) return "work_outside_world";
            cell = Grid.XYToCell(point.X + world.WorldOffset.x, point.Y + world.WorldOffset.y);
            if (!Grid.IsValidBuildingCell(cell) || Grid.WorldIdx[cell] != world.id) return "work_outside_world";
            return Grid.Visible[cell] == 0 ? "work_unexplored" : null;
        }
    }
}
