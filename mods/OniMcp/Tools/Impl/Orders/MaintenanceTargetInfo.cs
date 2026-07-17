using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class MaintenanceActionTools
    {
        private static Dictionary<string, object> ActionInfo(string key, string title, string category, string componentType, bool canExecute, Dictionary<string, object> state)
        {
            return new Dictionary<string, object>
            {
                ["actionKey"] = key,
                ["title"] = title,
                ["category"] = category,
                ["componentType"] = componentType,
                ["canExecute"] = canExecute,
                ["state"] = state
            };
        }

        private static Dictionary<string, object> ToiletInfo(Toilet toilet)
        {
            var info = TargetInfo(toilet.gameObject);
            info["flushesUsed"] = toilet.FlushesUsed;
            info["maxFlushes"] = toilet.maxFlushes;
            info["flushesRemaining"] = toilet.smi?.GetFlushesRemaining() ?? 0;
            info["isSoiled"] = toilet.smi?.IsSoiled ?? false;
            info["isCloggedWithGunk"] = toilet.smi?.IsCloggedWithGunk ?? false;
            info["hasCleanChore"] = toilet.smi?.cleanChore != null;
            info["canCleanEarly"] = CanCleanToilet(toilet);
            return info;
        }

        private static bool CanCleanToilet(Toilet toilet)
        {
            return toilet?.smi != null
                   && toilet.smi.GetCurrentState() != toilet.smi.sm.full
                   && toilet.smi.IsSoiled
                   && toilet.smi.cleanChore == null;
        }

        private static Dictionary<string, object> DesalinatorInfo(Desalinator desalinator)
        {
            var info = TargetInfo(desalinator.gameObject);
            info["saltStorageLeft"] = Math.Round(ToolUtil.SafeFloat(desalinator.SaltStorageLeft), 3);
            info["maxSalt"] = Math.Round(ToolUtil.SafeFloat(desalinator.maxSalt), 3);
            info["hasSalt"] = desalinator.smi?.HasSalt ?? false;
            info["isFull"] = desalinator.smi?.IsFull() ?? false;
            info["hasEmptyChore"] = desalinator.smi?.emptyChore != null;
            info["canEmptyEarly"] = CanEmptyDesalinator(desalinator);
            return info;
        }

        private static bool CanEmptyDesalinator(Desalinator desalinator)
        {
            return desalinator?.smi != null
                   && desalinator.smi.GetCurrentState() != desalinator.smi.sm.full
                   && desalinator.smi.HasSalt
                   && desalinator.smi.emptyChore == null;
        }

        private static Dictionary<string, object> TravelTubeInfo(TravelTubeEntrance entrance)
        {
            var info = TargetInfo(entrance.gameObject);
            bool usingWax = TravelTubeUseWaxField != null && (bool)TravelTubeUseWaxField.GetValue(entrance);
            info["usingWax"] = usingWax;
            info["availableJoules"] = Math.Round(ToolUtil.SafeFloat(entrance.AvailableJoules), 3);
            info["totalCapacity"] = Math.Round(ToolUtil.SafeFloat(entrance.TotalCapacity), 3);
            info["usageJoules"] = Math.Round(ToolUtil.SafeFloat(entrance.UsageJoules), 3);
            info["hasLaunchPower"] = entrance.HasLaunchPower;
            info["hasWaxForGreasyLaunch"] = entrance.HasWaxForGreasyLaunch;
            info["waxLaunchesAvailable"] = entrance.WaxLaunchesAvailable;
            return info;
        }

        private static Dictionary<string, object> HiveInfo(HiveHarvestMonitor.Instance smi)
        {
            var info = TargetInfo(smi.gameObject);
            info["shouldHarvest"] = smi.sm.shouldHarvest.Get(smi);
            info["storedProducedOreKg"] = Math.Round(ToolUtil.SafeFloat(smi.storage.GetMassAvailable(smi.def.producedOre)), 3);
            info["harvestThresholdKg"] = Math.Round(ToolUtil.SafeFloat(smi.def.harvestThreshold), 3);
            info["producedOre"] = smi.def.producedOre.Name;
            return info;
        }

        private static Dictionary<string, object> CargoBayInfo(GameObject go)
        {
            var info = TargetInfo(go);
            var cargoBay = go.GetComponent<CargoBay>();
            var cargoBayCluster = go.GetComponent<CargoBayCluster>();
            Storage storage = cargoBay != null ? cargoBay.storage : cargoBayCluster?.storage;
            info["componentType"] = cargoBay != null ? "CargoBay" : "CargoBayCluster";
            info["storageType"] = cargoBay != null ? cargoBay.storageType.ToString() : cargoBayCluster?.storageType.ToString();
            info["massStoredKg"] = Math.Round(ToolUtil.SafeFloat(storage?.MassStored() ?? 0f), 3);
            info["capacityKg"] = Math.Round(ToolUtil.SafeFloat(storage?.Capacity() ?? 0f), 3);
            info["canEmpty"] = storage != null && storage.MassStored() > 0f;
            return info;
        }

        private static float CargoMass(GameObject go)
        {
            var cargoBay = go.GetComponent<CargoBay>();
            if (cargoBay?.storage != null)
                return cargoBay.storage.MassStored();
            var cargoBayCluster = go.GetComponent<CargoBayCluster>();
            return cargoBayCluster?.storage != null ? cargoBayCluster.storage.MassStored() : 0f;
        }

        private static Dictionary<string, object> DupeInfo(MinionIdentity dupe)
        {
            var kpid = dupe.GetComponent<KPrefabID>();
            int cell = Grid.PosToCell(dupe);
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? dupe.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(dupe.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? dupe.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : dupe.GetMyWorldId()
            };
        }

        private static Dictionary<string, object> EquipmentSlotInfo(AssignableSlotInstance slot)
        {
            var equippable = slot.assignable as Equippable;
            return new Dictionary<string, object>
            {
                ["slotId"] = slot.slot.Id,
                ["slotName"] = slot.slot.Name,
                ["assigned"] = slot.IsAssigned(),
                ["isUnassigning"] = slot.IsUnassigning(),
                ["equipment"] = equippable == null ? null : EquipmentItemInfo(equippable),
                ["canUnequip"] = equippable != null && equippable.unequippable
            };
        }

        private static Dictionary<string, object> EquipmentItemInfo(Equippable equippable)
        {
            var go = equippable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["genericName"] = ToolUtil.CleanName(equippable.def.GenericName),
                ["slotId"] = equippable.slotID,
                ["isEquipped"] = equippable.isEquipped,
                ["unequippable"] = equippable.unequippable
            };
        }

        private static AssignableSlotInstance ResolveEquipmentSlot(Equipment equipment, JObject args)
        {
            int? equipmentId = ToolUtil.GetInt(args, "equipmentId");
            string slotId = args["slotId"]?.ToString();
            string prefab = args["equipmentPrefab"]?.ToString();
            string query = args["query"]?.ToString();

            return equipment.Slots.FirstOrDefault(slot =>
            {
                var equippable = slot?.assignable as Equippable;
                if (equippable == null)
                    return false;
                var kpid = equippable.GetComponent<KPrefabID>();
                if (equipmentId.HasValue && kpid != null && kpid.InstanceID == equipmentId.Value)
                    return true;
                if (!string.IsNullOrWhiteSpace(slotId) && string.Equals(slot.slot.Id, slotId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!string.IsNullOrWhiteSpace(prefab) && kpid != null && string.Equals(kpid.PrefabTag.Name, prefab.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    string q = query.Trim();
                    return Contains(slot.slot.Id, q)
                           || Contains(slot.slot.Name, q)
                           || Contains(kpid?.PrefabTag.Name, q)
                           || Contains(ToolUtil.CleanName(equippable.GetProperName()), q)
                           || Contains(ToolUtil.CleanName(equippable.def.GenericName), q);
                }
                return false;
            });
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && !string.IsNullOrWhiteSpace(query)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

    }
}
