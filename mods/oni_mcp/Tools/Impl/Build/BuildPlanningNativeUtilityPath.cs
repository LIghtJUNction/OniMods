using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> TryPlaceUtilityPathNative(BuildingDef def, List<CellCoord> path, JObject args)
        {
            var result = new Dictionary<string, object>
            {
                ["attempted"] = true,
                ["placementMode"] = "native_drag_path",
                ["success"] = false,
                ["shouldFallback"] = true
            };

            if (def == null || path == null || path.Count < 2)
            {
                result["attempted"] = false;
                result["reason"] = "native path placement requires a linear utility prefab and at least two path cells";
                return result;
            }

            if (IsDryRun(args))
            {
                result["attempted"] = false;
                result["reason"] = "dryRun uses validation only";
                return result;
            }

            int worldId = ToolUtil.ResolveWorldId(args);
            var materialResult = SelectElements(def, args["material"]?.ToString(), worldId);
            materialResult.RequiredKg = RequiredMaterialKg(def) * path.Count;
            if (!materialResult.Valid)
            {
                result["reason"] = "material selection failed";
                result["materialSelection"] = materialResult.ToDictionary();
                return result;
            }

            var facadeResult = ResolveFacade(def, args["facade"]?.ToString() ?? args["facadeId"]?.ToString());
            if (!facadeResult.Valid)
            {
                result["reason"] = "facade selection failed";
                result["facadeError"] = facadeResult.Error;
                return result;
            }

            var before = CountUtilityPathCells(def, path, worldId);
            result["beforeConnectedCells"] = before;

            object tool = SelectUtilityBuildTool(def.PrefabID);
            if (tool == null)
            {
                result["reason"] = "utility build tool instance is not initialized";
                return result;
            }

            try
            {
                InvokeBest(tool, "Activate", new object[] { def, materialResult.Elements, facadeResult.ResponseId });

                var start = path[0];
                var end = path[path.Count - 1];
                Vector3 startPos = BuildPlacementPosition(Grid.XYToCell(start.x, start.y), def);
                Vector3 endPos = BuildPlacementPosition(Grid.XYToCell(end.x, end.y), def);

                InvokeBest(tool, "OnLeftClickDown", new object[] { startPos });
                for (int i = 0; i < path.Count; i++)
                {
                    int cell = Grid.XYToCell(path[i].x, path[i].y);
                    if (!Grid.IsValidCell(cell))
                        continue;

                    InvokeBest(tool, "OnDragTool", new object[] { cell, i });
                    InvokeBest(tool, "OnMouseMove", new object[] { BuildPlacementPosition(cell, def) });
                }
                InvokeBest(tool, "OnLeftClickUp", new object[] { endPos });

                var after = CountUtilityPathCells(def, path, worldId);
                result["afterConnectedCells"] = after;
                result["newConnectedCells"] = Math.Max(0, after - before);
                result["pathCells"] = path.Count;
                result["materialSelection"] = materialResult.ToDictionary();
                result["facade"] = facadeResult.ResponseId;
                result["path"] = path.Select(p => new { x = p.x, y = p.y }).ToList();
                result["segments"] = BuildPathSegments(path);

                bool allConnected = after >= path.Count;
                result["success"] = allConnected || after > before;
                result["complete"] = allConnected;
                result["shouldFallback"] = after <= before;
                if (!allConnected)
                    result["reason"] = after > before
                        ? "native drag placed part of the path; fallback can fill missing cells"
                        : "native drag did not place any path cells";

                return result;
            }
            catch (Exception ex)
            {
                result["reason"] = "native drag path failed";
                result["error"] = ex.GetType().Name + ": " + ex.Message;
                return result;
            }
        }

        private static object SelectUtilityBuildTool(string prefabId)
        {
            if (!string.IsNullOrWhiteSpace(prefabId) && prefabId.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0)
                return WireBuildTool.Instance;
            return UtilityBuildTool.Instance;
        }

        private static int CountUtilityPathCells(BuildingDef def, List<CellCoord> path, int worldId)
        {
            if (def == null || path == null)
                return 0;

            int count = 0;
            var layers = UtilityLayersForPrefab(def.PrefabID);
            foreach (var point in path)
            {
                int cell = Grid.XYToCell(point.x, point.y);
                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                    continue;

                bool found = false;
                foreach (var layer in layers)
                {
                    var go = Grid.Objects[cell, (int)layer];
                    if (go == null)
                        continue;

                    var building = go.GetComponent<Building>();
                    string existingPrefabId = building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
                    if (SameUtilityFamily(def.PrefabID, existingPrefabId))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    count++;
            }

            return count;
        }

        private static bool SameUtilityFamily(string expected, string actual)
        {
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
                return false;

            return UtilityFamily(expected) == UtilityFamily(actual);
        }

        private static string UtilityFamily(string prefabId)
        {
            string id = prefabId ?? string.Empty;
            if (id.IndexOf("GasConduit", StringComparison.OrdinalIgnoreCase) >= 0)
                return "gas";
            if (id.IndexOf("LiquidConduit", StringComparison.OrdinalIgnoreCase) >= 0)
                return "liquid";
            if (id.IndexOf("SolidConduit", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Conveyor", StringComparison.OrdinalIgnoreCase) >= 0)
                return "solid";
            if (id.IndexOf("Logic", StringComparison.OrdinalIgnoreCase) >= 0)
                return "logic";
            if (id.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0)
                return "wire";
            return id.Trim().ToLowerInvariant();
        }

        private static object InvokeBest(object target, string methodName, object[] args)
        {
            if (target == null)
                throw new InvalidOperationException(methodName + " target is null");

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();
            while (type != null)
            {
                foreach (var method in type.GetMethods(flags).Where(item => item.Name == methodName))
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != args.Length)
                        continue;
                    return method.Invoke(target, args);
                }
                type = type.BaseType;
            }

            throw new MissingMethodException(target.GetType().Name, methodName);
        }
    }
}
