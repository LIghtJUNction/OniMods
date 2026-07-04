using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
{
        private static MinionIdentity FindDupeForAssignableSlot(JObject args)
        {
            var lookup = new JObject();
            if (args["dupeId"] != null)
                lookup["id"] = args["dupeId"];
            else if (args["id"] != null)
                lookup["id"] = args["id"];

            if (args["dupeName"] != null)
                lookup["name"] = args["dupeName"];
            else if (args["name"] != null)
                lookup["name"] = args["name"];

            return ToolUtil.FindDupe(lookup);
        }

        private static AssignableSlotInstance ResolveAssignableSlot(MinionIdentity dupe, JObject args, out string error)
        {
            error = null;
            var allSlots = GetAssignableSlots(dupe).ToList();
            if (allSlots.Count == 0)
            {
                error = "Duplicant has no assignable slots";
                return null;
            }

            string slotInstanceId = (args["slotInstanceId"] ?? args["assignableSlotId"])?.ToString();
            string slotId = args["slotId"]?.ToString();
            int? slotIndex = ToolUtil.GetInt(args, "slotIndex");

            var matches = allSlots
                .Where(slot => string.IsNullOrWhiteSpace(slotInstanceId) || string.Equals(slot.ID, slotInstanceId.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(slot => string.IsNullOrWhiteSpace(slotId) || string.Equals(slot.slot.Id, slotId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (slotIndex.HasValue)
            {
                if (slotIndex.Value < 0 || slotIndex.Value >= matches.Count)
                {
                    error = $"slotIndex is out of range for {matches.Count} matching slots";
                    return null;
                }
                return matches[slotIndex.Value];
            }

            if (matches.Count == 1)
                return matches[0];
            if (matches.Count == 0)
            {
                error = "Assignable slot not found; use dupes_control domain=side_screen action=equipment or action=bionic_upgrades first";
                return null;
            }

            error = "Multiple slots matched; provide slotInstanceId/assignableSlotId or slotIndex";
            return null;
        }

        private static IEnumerable<AssignableSlotInstance> GetAssignableSlots(MinionIdentity dupe)
        {
            var proxy = dupe.assignableProxy.Get();
            var ownables = proxy?.GetComponent<Ownables>();
            if (ownables != null)
            {
                foreach (var slot in ownables.Slots)
                    if (slot != null)
                        yield return slot;
            }

            var equipment = proxy?.GetComponent<Equipment>();
            if (equipment != null)
            {
                foreach (var slot in equipment.Slots)
                    if (slot != null)
                        yield return slot;
            }
        }

        private static Assignable FindAssignableForSlot(AssignableSlotInstance slot, IAssignableIdentity ownerIdentity, JObject args, out string error)
        {
            error = null;
            int? itemId = ToolUtil.GetInt(args, "assignableId") ?? ToolUtil.GetInt(args, "itemId");
            string prefabId = args["prefabId"]?.ToString();
            string query = (args["itemName"] ?? args["query"])?.ToString();

            var candidates = Components.AssignableItems.Items
                .Where(item => IsCandidateForSlot(item, slot, ownerIdentity))
                .Where(item => !itemId.HasValue || AssignableInstanceId(item) == itemId.Value)
                .Where(item => string.IsNullOrWhiteSpace(prefabId) || string.Equals(AssignablePrefabId(item), prefabId.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(item => AssignableItemMatches(item, query))
                .OrderBy(item => item.IsAssigned() ? 1 : 0)
                .ThenBy(item => ToolUtil.CleanName(item.GetProperName()))
                .ToList();

            if (candidates.Count == 1)
                return candidates[0];
            if (candidates.Count == 0)
            {
                error = "Assignable item not found for slot; use dupes_control domain=assignable action=list or dupes_control domain=side_screen action=equipment includeAvailable first";
                return null;
            }

            error = "Multiple assignable items matched; provide assignableId/itemId or prefabId";
            return null;
        }

        private static bool IsCandidateForSlot(Assignable item, AssignableSlotInstance slot, IAssignableIdentity ownerIdentity)
        {
            if (item == null || slot?.slot == null || ownerIdentity == null)
                return false;
            if (!string.Equals(item.slotID, slot.slot.Id, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!item.CanAssignTo(ownerIdentity))
                return false;

            bool assignedToOwner = item.assignee != null && item.assignee.GetSoleOwner() == ownerIdentity.GetSoleOwner();
            bool currentSlotItem = assignedToOwner && slot.assignable == item;
            bool restrictAssignedToOthers = slot is EquipmentSlotInstance || slot.ID.IndexOf("BionicUpgrade", StringComparison.OrdinalIgnoreCase) >= 0;
            if (restrictAssignedToOthers)
            {
                if (item.assignee != null && !assignedToOwner)
                    return false;
                if (assignedToOwner && !currentSlotItem)
                    return false;
            }

            var equippable = item as Equippable;
            if (equippable != null)
            {
                var ownerGo = OwnerTargetGameObject(ownerIdentity);
                if (ownerGo == null)
                    return false;
                var itemGo = equippable.isEquipped && equippable.assignee != null
                    ? OwnerTargetGameObject(equippable.assignee)
                    : equippable.gameObject;
                if (itemGo == null || itemGo.GetMyWorldId() != ownerGo.GetMyWorldId())
                    return false;
            }

            return true;
        }

        private static GameObject OwnerTargetGameObject(IAssignableIdentity identity)
        {
            var owner = identity?.GetOwners()?.FirstOrDefault();
            return owner?.GetComponent<MinionAssignablesProxy>()?.GetTargetGameObject();
        }

        private static bool AssignableItemMatches(Assignable item, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(ToolUtil.CleanName(item.GetProperName()), q)
                   || Contains(AssignablePrefabId(item), q)
                   || Contains(item.slotID, q);
        }

        private static int AssignableInstanceId(Assignable assignable)
        {
            return assignable.GetComponent<KPrefabID>()?.InstanceID ?? assignable.gameObject.GetInstanceID();
        }

        private static string AssignablePrefabId(Assignable assignable)
        {
            return assignable.GetComponent<KPrefabID>()?.PrefabTag.Name ?? assignable.gameObject.name;
        }

        private static Dictionary<string, object> AssignableSlotToDictionary(AssignableSlotInstance slot)
        {
            var assignables = slot.assignables;
            return new Dictionary<string, object>
            {
                ["slotInstanceId"] = slot.ID,
                ["slotId"] = slot.slot.Id,
                ["slotName"] = slot.slot.Name,
                ["ownerType"] = assignables == null ? null : assignables.GetType().Name,
                ["assigned"] = slot.IsAssigned(),
                ["isUnassigning"] = slot.IsUnassigning(),
                ["assignable"] = slot.assignable == null ? null : AssignableToDictionary(slot.assignable)
            };
        }

        private static bool AssignableMatches(Assignable assignable, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var go = assignable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            return Contains(assignable.slotID, q)
                || Contains(ToolUtil.CleanName(go.GetProperName()), q)
                || Contains(kpid?.PrefabTag.Name ?? go.name, q)
                || Contains(assignable.assignee?.GetProperName(), q);
        }

        private static Assignable FindAssignable(Newtonsoft.Json.Linq.JObject args)
        {
            int? id = ToolUtil.GetInt(args, "targetId") ?? ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            string slotId = args["slotId"]?.ToString();

            foreach (var assignable in Components.AssignableItems.Items)
            {
                var go = assignable?.gameObject;
                if (go == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(slotId) && !string.Equals(assignable.slotID, slotId, StringComparison.OrdinalIgnoreCase))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return assignable;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return assignable;
            }

            return null;
        }

        private static Dictionary<string, object> AssignableToDictionary(Assignable assignable)
        {
            var go = assignable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            int cell = Grid.PosToCell(go);
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["slotId"] = assignable.slotID,
                ["slotName"] = assignable.slot?.Name,
                ["canBeAssigned"] = assignable.CanBeAssigned,
                ["canBePublic"] = assignable.canBePublic,
                ["assigned"] = assignable.assignee != null && !assignable.assignee.IsNull(),
                ["assignee"] = assignable.assignee == null || assignable.assignee.IsNull() ? null : new Dictionary<string, object>
                {
                    ["name"] = assignable.assignee.GetProperName(),
                    ["ownerCount"] = assignable.assignee.NumOwners()
                },
                ["position"] = new
                {
                    x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                    y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
                },
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : go.GetComponent<KMonoBehaviour>()?.GetMyWorldId() ?? -1
            };
        }
        }
}
