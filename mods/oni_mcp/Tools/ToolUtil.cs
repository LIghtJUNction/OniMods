using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class ToolUtil
    {
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            int start = name.IndexOf('>');
            int end = name.LastIndexOf("</", StringComparison.Ordinal);
            if (start >= 0 && end > start)
                return name.Substring(start + 1, end - start - 1);

            return name;
        }

        public static string GetElementState(Element element)
        {
            if (element == null)
                return "unknown";
            if (element.IsVacuum)
                return "vacuum";
            if (element.IsGas)
                return "gas";
            if (element.IsLiquid)
                return "liquid";
            if (element.IsSolid)
                return "solid";
            return "unknown";
        }

        public static int ClampLimit(JObject args, int defaultValue, int max)
        {
            int value;
            if (args["limit"] != null && int.TryParse(args["limit"].ToString(), out value))
                return Math.Max(1, Math.Min(value, max));
            return defaultValue;
        }

        public static bool GetBool(JObject args, string key, bool defaultValue)
        {
            bool value;
            return args[key] != null && bool.TryParse(args[key].ToString(), out value) ? value : defaultValue;
        }

        public static int? GetInt(JObject args, string key)
        {
            int value;
            return args[key] != null && int.TryParse(args[key].ToString(), out value) ? value : (int?)null;
        }

        public static float? GetFloat(JObject args, string key)
        {
            float value;
            return args[key] != null && float.TryParse(args[key].ToString(), out value) ? value : (float?)null;
        }

        public static int ResolveWorldId(JObject args, int fallbackWorldId = -1)
        {
            int? requested = GetInt(args, "worldId");
            if (requested.HasValue)
                return requested.Value;

            string areaId = args["areaId"]?.ToString();
            AreaHandle area;
            if (!string.IsNullOrWhiteSpace(areaId) && AreaHandleRegistry.TryGet(areaId, out area))
                return area.WorldId;

            if (fallbackWorldId >= 0)
                return fallbackWorldId;

            return ClusterManager.Instance?.activeWorldId ?? -1;
        }

        public static bool CellMatchesWorld(int cell, int worldId)
        {
            if (worldId < 0)
                return true;
            return Grid.IsWorldValidCell(cell) && Grid.WorldIdx[cell] == worldId;
        }

        public static bool GameObjectMatchesWorld(GameObject go, int worldId)
        {
            if (go == null || worldId < 0)
                return true;

            int cell = Grid.PosToCell(go);
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell] == worldId;

            var component = go.GetComponent<KMonoBehaviour>();
            return component == null || component.GetMyWorldId() == worldId;
        }

        public static MinionIdentity FindDupe(JObject args)
        {
            int id;
            if (args["id"] != null && int.TryParse(args["id"].ToString(), out id))
            {
                foreach (var minion in Components.LiveMinionIdentities.Items)
                {
                    var kpid = minion?.GetComponent<KPrefabID>();
                    if (kpid != null && kpid.InstanceID == id)
                        return minion;
                }
            }

            string name = args["name"]?.ToString();
            if (!string.IsNullOrEmpty(name))
            {
                foreach (var minion in Components.LiveMinionIdentities.Items)
                {
                    if (minion != null && string.Equals(minion.GetProperName(), name, StringComparison.OrdinalIgnoreCase))
                        return minion;
                }
            }

            return null;
        }

        public static Dictionary<string, int> GetRect(JObject args)
        {
            string areaId = args["areaId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(areaId))
                return AreaHandleRegistry.ResolveRect(areaId);

            int x1 = GetInt(args, "x1") ?? GetInt(args, "x") ?? 0;
            int y1 = GetInt(args, "y1") ?? GetInt(args, "y") ?? 0;
            int x2 = GetInt(args, "x2") ?? x1;
            int y2 = GetInt(args, "y2") ?? y1;
            if (x2 < x1) { int t = x1; x1 = x2; x2 = t; }
            if (y2 < y1) { int t = y1; y1 = y2; y2 = t; }
            return new Dictionary<string, int>
            {
                ["x1"] = Mathf.Clamp(x1, 0, Grid.WidthInCells - 1),
                ["y1"] = Mathf.Clamp(y1, 0, Grid.HeightInCells - 1),
                ["x2"] = Mathf.Clamp(x2, 0, Grid.WidthInCells - 1),
                ["y2"] = Mathf.Clamp(y2, 0, Grid.HeightInCells - 1)
            };
        }

        public static float SafeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
