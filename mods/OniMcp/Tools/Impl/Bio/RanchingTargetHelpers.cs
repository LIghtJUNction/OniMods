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
    public static partial class RanchingTools
    {
        private static CreatureDeliveryPoint FindDropOff(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                var point = go == null ? null : go.GetComponent<CreatureDeliveryPoint>();
                if (point == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return point;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return point;
            }
            return null;
        }

        private static string ApplyDropOffConfig(CreatureDeliveryPoint point, JObject args)
        {
            int? capacity = ToolUtil.GetInt(args, "capacity");
            if (capacity.HasValue)
            {
                if (capacity.Value == 0 && !ToolUtil.GetBool(args, "confirm", false))
                    return "confirm=true is required when setting capacity to 0";
                var tracker = point.GetComponent<BaggableCritterCapacityTracker>();
                if (tracker == null)
                    return "Target has no BaggableCritterCapacityTracker";
                float clamped = Mathf.Clamp(capacity.Value, 0, tracker.maximumCreatures);
                ((IUserControlledCapacity)tracker).UserMaxCapacity = clamped;
                tracker.RefreshCreatureCount();
            }

            string mode = (args["mode"]?.ToString() ?? "replace").Trim().ToLowerInvariant();
            var filter = point.GetComponent<TreeFilterable>();
            if (filter != null && (args["critterTags"] != null || mode == "clear"))
            {
                var tags = ParseTags(args["critterTags"]);
                if (mode == "clear")
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return "confirm=true is required when clearing critter filters";
                    filter.UpdateFilters(new HashSet<Tag>());
                }
                else
                {
                    var current = new HashSet<Tag>(filter.GetTags());
                    if (mode == "add")
                        current.UnionWith(tags);
                    else if (mode == "remove")
                        current.ExceptWith(tags);
                    else if (mode == "replace")
                        current = tags;
                    else
                        return "mode must be replace, add, remove, or clear";
                    filter.UpdateFilters(current);
                }
            }

            point.critterCapacity.RefreshCreatureCount();
            return null;
        }

        private static EggIncubator FindIncubator(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                var incubator = go == null ? null : go.GetComponent<EggIncubator>();
                if (incubator == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return incubator;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return incubator;
            }
            return null;
        }

        private static Dictionary<string, object> CritterInfo(Capturable capturable)
        {
            var go = capturable.gameObject;
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var baggable = go.GetComponent<Baggable>();
            var pickupable = go.GetComponent<Pickupable>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1,
                ["isCapturable"] = capturable.IsCapturable(),
                ["markedForCapture"] = capturable.IsMarkedForCapture,
                ["wrangled"] = baggable?.wrangled ?? false,
                ["bagged"] = go.HasTag(GameTags.Creatures.Bagged),
                ["stored"] = go.HasTag(GameTags.Stored),
                ["storageId"] = pickupable?.storage?.GetComponent<KPrefabID>()?.InstanceID ?? -1
            };
        }

        private static Dictionary<string, object> DropOffInfo(CreatureDeliveryPoint point)
        {
            var go = point.gameObject;
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var filter = go.GetComponent<TreeFilterable>();
            var tracker = go.GetComponent<BaggableCritterCapacityTracker>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1,
                ["logicEnabled"] = point.LogicEnabled(),
                ["capacity"] = tracker?.creatureLimit ?? 0,
                ["maximumCreatures"] = tracker?.maximumCreatures ?? 0,
                ["storedCreatureCount"] = tracker?.storedCreatureCount ?? 0,
                ["acceptedTags"] = filter == null ? new List<string>() : filter.GetTags().Select(tag => tag.Name).OrderBy(name => name).ToList()
            };
        }

        private static Dictionary<string, object> IncubatorInfo(EggIncubator incubator)
        {
            var go = incubator.gameObject;
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1,
                ["autoReplace"] = incubator.autoReplaceEntity,
                ["requestedEgg"] = incubator.requestedEntityTag.IsValid ? incubator.requestedEntityTag.Name : null,
                ["hasActiveRequest"] = incubator.GetActiveRequest != null,
                ["progress"] = Math.Round(incubator.GetProgress(), 3),
                ["occupant"] = incubator.Occupant == null ? null : ObjectInfo(incubator.Occupant),
                ["acceptedEggTags"] = incubator.possibleDepositObjectTags.Select(tag => tag.Name).OrderBy(name => name).ToList()
            };
        }

        private static Dictionary<string, object> ObjectInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static HashSet<Tag> ParseTags(JToken token)
        {
            var tags = new HashSet<Tag>();
            if (token == null)
                return tags;
            if (token.Type == JTokenType.Array)
            {
                foreach (var item in token)
                {
                    string value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        tags.Add(TagManager.Create(value.Trim()));
                }
            }
            else
            {
                foreach (string value in token.ToString().Split(new[] { ',', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    tags.Add(TagManager.Create(value.Trim()));
            }
            return tags;
        }

        private static bool Matches(CreatureDeliveryPoint point, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var go = point.gameObject;
            var filter = go.GetComponent<TreeFilterable>();
            return Contains(TargetName(go), q)
                || Contains(go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name, q)
                || (filter != null && filter.GetTags().Any(tag => Contains(tag.Name, q)));
        }

        private static bool CritterMatches(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(TargetName(go), q)
                || Contains(go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name, q);
        }

        private static bool IncubatorMatches(EggIncubator incubator, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var go = incubator.gameObject;
            return Contains(TargetName(go), q)
                || Contains(go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name, q)
                || Contains(incubator.requestedEntityTag.Name, q)
                || Contains(incubator.Occupant?.GetProperName(), q)
                || Contains(incubator.Occupant?.GetComponent<KPrefabID>()?.PrefabTag.Name, q);
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell)) return false;
            if (!ToolUtil.CellMatchesWorld(cell, worldId)) return false;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static string TargetName(GameObject go)
        {
            return ToolUtil.CleanName(go.GetProperName());
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
