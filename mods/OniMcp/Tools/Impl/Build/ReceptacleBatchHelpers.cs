using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ReceptacleTools
    {
        private static string ApplyReceptacleAction(SingleEntityReceptacle receptacle, JObject args)
        {
            string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
            switch (action)
            {
                case "request":
                {
                    string tagName = args["entityTag"]?.ToString();
                    if (string.IsNullOrWhiteSpace(tagName))
                        return "entityTag is required for action=request";
                    var tag = TagManager.Create(tagName.Trim());
                    var prefab = Assets.GetPrefab(tag);
                    if (prefab == null)
                        return "entityTag prefab not found";
                    if (!IsValidReceptacleOption(receptacle, prefab))
                        return "entityTag is not valid for this receptacle";
                    if (receptacle.Occupant != null)
                        return "Receptacle already has an occupant; use action=remove_occupant first";
                    if (receptacle.GetActiveRequest != null && ToolUtil.GetBool(args, "replaceExistingRequest", true))
                        receptacle.CancelActiveRequest();
                    if (receptacle.GetActiveRequest != null)
                        return "Receptacle already has an active request";
                    var additional = string.IsNullOrWhiteSpace(args["additionalTag"]?.ToString())
                        ? AdditionalTagForPrefab(prefab)
                        : TagManager.Create(args["additionalTag"].ToString().Trim());
                    receptacle.CreateOrder(tag, additional);
                    return null;
                }
                case "cancel_request":
                    receptacle.CancelActiveRequest();
                    return null;
                case "remove_occupant":
                    if (receptacle.Occupant == null)
                        return "Receptacle has no occupant";
                    receptacle.OrderRemoveOccupant();
                    return null;
                case "cancel_remove":
                {
                    var uprootable = receptacle.Occupant == null ? null : receptacle.Occupant.GetComponent<Uprootable>();
                    if (uprootable == null || !uprootable.IsMarkedForUproot)
                        return "Receptacle occupant is not marked for removal";
                    uprootable.ForceCancelUproot();
                    return null;
                }
                default:
                    return "action must be request, cancel_request, remove_occupant, or cancel_remove";
            }
        }

        private static string ApplyStorageTileSelection(StorageTile.Instance tile, JObject args)
        {
            if (ToolUtil.GetBool(args, "clear", false))
            {
                tile.SetTargetItem(StorageTile.INVALID_TAG);
                return null;
            }

            string tagName = args["itemTag"]?.ToString();
            if (string.IsNullOrWhiteSpace(tagName))
                return "itemTag is required unless clear=true";
            var tag = TagManager.Create(tagName.Trim());
            if (!StorageTileOptions(tile).Any(item => item.Tag == tag))
                return "itemTag is not a valid option for this StorageTile";
            tile.SetTargetItem(tag);
            return null;
        }

        private static JObject MergeReceptacleDefaults(JObject item, JObject defaults)
        {
            return MergeBatchDefaults(item, defaults, CopyReceptacleAliases, IsReceptacleAlias);
        }

        private static JObject MergeStorageTileDefaults(JObject item, JObject defaults)
        {
            return MergeBatchDefaults(item, defaults, CopyStorageTileAliases, IsStorageTileAlias);
        }

        private static JObject MergeBatchDefaults(JObject item, JObject defaults, Action<JObject, JObject, bool> copyAliases, Func<string, bool> isAlias)
        {
            var result = new JObject();
            copyAliases(defaults, result, false);
            CopyNonAliases(defaults, result, false, isAlias);
            copyAliases(item, result, true);
            CopyNonAliases(item, result, true, isAlias);
            return result;
        }

        private static void CopyReceptacleAliases(JObject source, JObject target, bool overwrite)
        {
            if (source == null)
                return;

            CopyAlias(source, target, "action", "a", overwrite);
            CopyAlias(source, target, "entityTag", "tag", overwrite);
            CopyAlias(source, target, "worldId", "w", overwrite);
        }

        private static void CopyStorageTileAliases(JObject source, JObject target, bool overwrite)
        {
            if (source == null)
                return;

            CopyAlias(source, target, "itemTag", "i", overwrite);
            CopyAlias(source, target, "clear", "c", overwrite);
            CopyAlias(source, target, "worldId", "w", overwrite);
        }

        private static void CopyAlias(JObject source, JObject target, string longKey, string shortKey, bool overwrite)
        {
            var token = source[longKey] ?? source[shortKey];
            if (token != null && (overwrite || target[longKey] == null))
                target[longKey] = token.DeepClone();
        }

        private static void CopyNonAliases(JObject source, JObject target, bool overwrite, Func<string, bool> isAlias)
        {
            if (source == null)
                return;

            foreach (var property in source.Properties())
            {
                if (isAlias(property.Name))
                    continue;
                if (overwrite || target[property.Name] == null)
                    target[property.Name] = property.Value.DeepClone();
            }
        }

        private static bool IsReceptacleAlias(string name)
        {
            return string.Equals(name, "action", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "entityTag", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "tag", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "worldId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "w", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStorageTileAlias(string name)
        {
            return string.Equals(name, "itemTag", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "i", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "clear", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "c", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "worldId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "w", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<SingleEntityReceptacle> AllReceptacles()
        {
            return Components.BuildingCompletes.Items
                .Select(item => item?.GetComponent<SingleEntityReceptacle>())
                .Where(item => item != null && IsGenericReceptacle(item));
        }

        private static IEnumerable<StorageTile.Instance> AllStorageTiles()
        {
            return Components.BuildingCompletes.Items
                .Select(item => item?.gameObject?.GetSMI<StorageTile.Instance>())
                .Where(item => item != null && item.gameObject.GetComponent<TreeFilterable>() != null);
        }

        private static bool IsGenericReceptacle(SingleEntityReceptacle receptacle)
        {
            if (receptacle == null || !receptacle.enabled)
                return false;
            var go = receptacle.gameObject;
            return go.GetComponent<PlantablePlot>() == null
                && go.GetComponent<EggIncubator>() == null;
        }

        private static SingleEntityReceptacle FindReceptacle(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var receptacle in AllReceptacles())
            {
                var go = receptacle.gameObject;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return receptacle;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return receptacle;
            }
            return null;
        }

        private static StorageTile.Instance FindStorageTile(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var tile in AllStorageTiles())
            {
                var go = tile.gameObject;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return tile;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return tile;
            }
            return null;
        }

    }
}
