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
            return WorldEditor.ResolveWorldId(args, fallbackWorldId);
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
            return WorldEditor.ResolveRect(args);
        }

        public static float SafeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
