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
        private static string Execute(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            string actionKey = args["actionKey"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(actionKey))
                return "actionKey is required";

            switch (actionKey.ToLowerInvariant())
            {
                case "clean_toilet":
                    return ExecuteToiletClean(args, out target);
                case "empty_desalinator":
                    return ExecuteDesalinatorEmpty(args, out target);
                case "set_transit_tube_wax":
                    return ExecuteTransitTubeWax(args, out target);
                case "set_hive_harvest":
                    return ExecuteHiveHarvest(args, out target);
                case "empty_cargo_bay":
                    return ExecuteCargoBayEmpty(args, out target);
                case "unequip_dupe_equipment":
                    return ExecuteDupeUnequip(args, out target);
                default:
                    return "Unsupported actionKey";
            }
        }

        private static string ExecuteToiletClean(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            var toilet = go?.GetComponent<Toilet>();
            if (toilet == null)
                return "Target toilet not found";
            if (toilet.smi == null)
                return "Toilet state machine is not ready";
            target = ToiletInfo(toilet);
            if (toilet.smi.GetCurrentState() == toilet.smi.sm.full)
                return "Toilet is full; full clean chore is managed by the building state";
            if (!toilet.smi.IsSoiled)
                return "Toilet is not soiled";
            if (toilet.smi.cleanChore != null)
                return "Toilet already has a clean chore";
            toilet.smi.GoTo(toilet.smi.sm.earlyclean);
            return null;
        }

        private static string ExecuteDesalinatorEmpty(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            var desalinator = go?.GetComponent<Desalinator>();
            if (desalinator == null)
                return "Target desalinator not found";
            if (desalinator.smi == null)
                return "Desalinator state machine is not ready";
            target = DesalinatorInfo(desalinator);
            if (desalinator.smi.GetCurrentState() == desalinator.smi.sm.full)
                return "Desalinator is full; full empty chore is managed by the building state";
            if (!desalinator.smi.HasSalt)
                return "Desalinator has no salt to empty";
            if (desalinator.smi.emptyChore != null)
                return "Desalinator already has an empty chore";
            desalinator.smi.GoTo(desalinator.smi.sm.earlyEmpty);
            return null;
        }

        private static string ExecuteTransitTubeWax(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            var entrance = go?.GetComponent<TravelTubeEntrance>();
            if (entrance == null)
                return "Target travel tube entrance not found";
            bool enabled = ToolUtil.GetBool(args, "enabled", true);
            target = TravelTubeInfo(entrance);
            entrance.SetWaxUse(enabled);
            return null;
        }

        private static string ExecuteHiveHarvest(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            var smi = go?.GetSMI<HiveHarvestMonitor.Instance>();
            if (smi == null)
                return "Target hive harvest monitor not found";
            bool enabled = ToolUtil.GetBool(args, "enabled", true);
            target = HiveInfo(smi);
            smi.sm.shouldHarvest.Set(enabled, smi);
            return null;
        }

        private static string ExecuteCargoBayEmpty(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            if (go == null)
                return "Target cargo bay not found";
            var cargoBay = go.GetComponent<CargoBay>();
            if (cargoBay != null)
            {
                target = CargoBayInfo(go);
                cargoBay.storage.DropAll();
                return null;
            }
            var cargoBayCluster = go.GetComponent<CargoBayCluster>();
            if (cargoBayCluster != null)
            {
                target = CargoBayInfo(go);
                cargoBayCluster.storage.DropAll();
                return null;
            }
            return "Target cargo bay not found";
        }

        private static string ExecuteDupeUnequip(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var dupe = FindDupeTarget(args);
            if (dupe == null)
                return "Target duplicant not found";
            var equipment = dupe.GetEquipment();
            if (equipment == null)
                return "Duplicant has no equipment component";
            var slot = ResolveEquipmentSlot(equipment, args);
            if (slot == null)
                return "Matching equipped slot not found";
            var equippable = slot.assignable as Equippable;
            if (equippable == null)
                return "Matching slot has no equippable item";
            if (!equippable.unequippable)
                return "Equippable item is not unequippable";
            target = DupeEquipmentInfo(dupe);
            equippable.Unassign();
            return null;
        }

        private static Dictionary<string, object> SnapshotForArgs(JObject args)
        {
            string actionKey = args["actionKey"]?.ToString()?.Trim()?.ToLowerInvariant();
            if (actionKey == "unequip_dupe_equipment")
            {
                var dupe = FindDupeTarget(args);
                return dupe == null ? null : DupeEquipmentInfo(dupe);
            }
            var go = FindTarget(args);
            return go == null ? null : TargetActionsInfo(go);
        }

        private static Dictionary<string, object> TargetActionsInfo(GameObject go)
        {
            var result = TargetInfo(go);
            var actions = new List<Dictionary<string, object>>();

            var toilet = go.GetComponent<Toilet>();
            if (toilet != null)
                actions.Add(ActionInfo("clean_toilet", "Request early toilet cleaning", "maintenance", "Toilet", CanCleanToilet(toilet), ToiletInfo(toilet)));

            var desalinator = go.GetComponent<Desalinator>();
            if (desalinator != null)
                actions.Add(ActionInfo("empty_desalinator", "Request early desalinator emptying", "maintenance", "Desalinator", CanEmptyDesalinator(desalinator), DesalinatorInfo(desalinator)));

            var entrance = go.GetComponent<TravelTubeEntrance>();
            if (entrance != null)
                actions.Add(ActionInfo("set_transit_tube_wax", "Enable/cancel transit tube wax delivery", "buildings", "TravelTubeEntrance", true, TravelTubeInfo(entrance)));

            var hive = go.GetSMI<HiveHarvestMonitor.Instance>();
            if (hive != null)
                actions.Add(ActionInfo("set_hive_harvest", "Enable/cancel hive emptying", "ranching", "HiveHarvestMonitor", true, HiveInfo(hive)));

            if (go.GetComponent<CargoBay>() != null || go.GetComponent<CargoBayCluster>() != null)
                actions.Add(ActionInfo("empty_cargo_bay", "Drop all cargo bay contents", "rockets", "CargoBay/CargoBayCluster", CargoMass(go) > 0f, CargoBayInfo(go)));

            result["actions"] = actions;
            return result;
        }

        private static Dictionary<string, object> DupeEquipmentInfo(MinionIdentity dupe)
        {
            var result = DupeInfo(dupe);
            var equipment = dupe.GetEquipment();
            var actions = new List<Dictionary<string, object>>();
            if (equipment != null)
            {
                var slots = equipment.Slots
                    .Where(slot => slot != null)
                    .Select(EquipmentSlotInfo)
                    .ToList();
                result["slots"] = slots;
                foreach (var slot in equipment.Slots)
                {
                    var equippable = slot?.assignable as Equippable;
                    if (equippable != null && equippable.unequippable)
                    {
                        actions.Add(new Dictionary<string, object>
                        {
                            ["actionKey"] = "unequip_dupe_equipment",
                            ["title"] = "Unequip " + ToolUtil.CleanName(equippable.def.GenericName),
                            ["category"] = "dupes",
                            ["componentType"] = "SuitEquipper",
                            ["canExecute"] = true,
                            ["slotId"] = slot.slot.Id,
                            ["equipment"] = EquipmentItemInfo(equippable)
                        });
                    }
                }
            }
            else
            {
                result["slots"] = new List<Dictionary<string, object>>();
            }
            result["actions"] = actions;
            return result;
        }

    }
}
