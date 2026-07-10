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
    public static partial class StoryFacilityTools
    {
        private static Dictionary<string, object> PrinterceptorInfo(HijackedHeadquarters.Instance printer)
        {
            var go = printer.gameObject;
            var storage = go.GetComponent<Storage>();
            var result = TargetInfo(go);
            result["passcodeUnlocked"] = printer.sm.passcodeUnlocked.Get(printer);
            result["interceptCharges"] = printer.sm.interceptCharges.Get(printer);
            result["maxInterceptCharges"] = 3;
            result["immigrantsAvailable"] = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable;
            result["canIntercept"] = CanIntercept(printer);
            result["canOpenPrintInterface"] = CanOpenPrintInterface(printer);
            result["databanks"] = Math.Round(ToolUtil.SafeFloat(storage?.GetAmountAvailable(DatabankHelper.ID) ?? 0f), 3);
            result["printCounts"] = printer.printCounts.ToDictionary(pair => pair.Key.Name, pair => pair.Value);
            return result;
        }

        private static bool CanIntercept(HijackedHeadquarters.Instance printer)
        {
            return printer.sm.passcodeUnlocked.Get(printer)
                   && Immigration.Instance != null
                   && Immigration.Instance.ImmigrantsAvailable
                   && printer.sm.interceptCharges.Get(printer) < 3;
        }

        private static bool CanOpenPrintInterface(HijackedHeadquarters.Instance printer)
        {
            return printer.IsInsideState(printer.sm.operational.readyToPrint.pre)
                   || printer.IsInsideState(printer.sm.operational.readyToPrint.loop);
        }

        private static Dictionary<string, object> PoiTechUnlockInfo(POITechItemUnlocks.Instance portal)
        {
            var result = TargetInfo(portal.gameObject);
            var workable = portal.GetComponent<POITechItemUnlockWorkable>();
            float percent = workable == null ? -1f : workable.GetPercentComplete();
            result["isUnlocked"] = portal.sm.isUnlocked.Get(portal);
            result["pendingChore"] = portal.sm.pendingChore.Get(portal);
            result["seenNotification"] = portal.sm.seenNotification.Get(portal);
            result["sideScreenEnabled"] = portal.SidescreenEnabled();
            result["interactable"] = portal.SidescreenButtonInteractable();
            result["buttonText"] = portal.SidescreenButtonText;
            result["buttonTooltip"] = portal.SidescreenButtonTooltip;
            result["workPercent"] = percent < 0f ? null : (object)Math.Round(ToolUtil.SafeFloat(percent), 4);
            result["workTimeSeconds"] = workable == null ? null : (object)Math.Round(ToolUtil.SafeFloat(workable.GetWorkTime()), 2);
            result["workTimeRemainingSeconds"] = workable == null ? null : (object)Math.Round(ToolUtil.SafeFloat(workable.WorkTimeRemaining), 2);
            result["popupName"] = portal.def.PopUpName.ToString();
            result["loreUnlockId"] = portal.def.loreUnlockId;
            result["unlockTechItems"] = portal.unlockTechItems.Select(PoiTechItemInfo).ToList();
            result["canStart"] = !portal.sm.isUnlocked.Get(portal) && !portal.sm.pendingChore.Get(portal);
            result["canCancel"] = !portal.sm.isUnlocked.Get(portal) && portal.sm.pendingChore.Get(portal);
            return result;
        }

        private static Dictionary<string, object> PoiTechItemInfo(TechItem item)
        {
            return new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["name"] = item.Name,
                ["description"] = item.description,
                ["parentTechId"] = item.parentTechId,
                ["isPOIUnlock"] = item.isPOIUnlock,
                ["complete"] = item.IsComplete()
            };
        }

        private static Dictionary<string, object> RemoteWorkTerminalInfo(RemoteWorkTerminal terminal, bool includeDocks)
        {
            var result = TargetInfo(terminal.gameObject);
            result["currentDock"] = terminal.CurrentDock == null ? null : DockInfo(terminal.CurrentDock);
            result["futureDock"] = terminal.FutureDock == null ? null : DockInfo(terminal.FutureDock);
            result["availableDocks"] = includeDocks
                ? Components.RemoteWorkerDocks.GetItems(terminal.GetMyWorldId()).Where(dock => dock != null).Select(DockInfo).ToList()
                : new List<Dictionary<string, object>>();
            return result;
        }

        private static Dictionary<string, object> DockInfo(RemoteWorkerDock dock)
        {
            var result = TargetInfo(dock.gameObject);
            result["worldId"] = dock.GetMyWorldId();
            return result;
        }

        private static RemoteWorkerDock FindDockForTerminal(RemoteWorkTerminal terminal, JObject args)
        {
            int? id = ToolUtil.GetInt(args, "dockId");
            string name = args["dockName"]?.ToString();
            foreach (var dock in Components.RemoteWorkerDocks.GetItems(terminal.GetMyWorldId()))
            {
                if (dock == null)
                    continue;
                var kpid = dock.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return dock;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(ToolUtil.CleanName(dock.GetProperName()), name, StringComparison.OrdinalIgnoreCase))
                    return dock;
            }
            return null;
        }

        private static Dictionary<string, object> GeneticStationInfo(GeneticAnalysisStation.StatesInstance station)
        {
            var result = TargetInfo(station.gameObject);
            result["unidentifiedSeedMassKg"] = Math.Round(ToolUtil.SafeFloat(station.storage.GetMassAvailable(GameTags.UnidentifiedSeed)), 3);
            result["options"] = GetGeneticSeedOptions(station);
            return result;
        }

        private static List<Dictionary<string, object>> GetGeneticSeedOptions(GeneticAnalysisStation.StatesInstance station)
        {
            var options = new List<Dictionary<string, object>>();
            if (PlantSubSpeciesCatalog.Instance == null)
                return options;
            foreach (Tag species in PlantSubSpeciesCatalog.Instance.GetAllDiscoveredSpecies())
            {
                var subspecies = PlantSubSpeciesCatalog.Instance.GetAllSubSpeciesForSpecies(species);
                if (subspecies.Count <= 1)
                    continue;
                Tag seed = GetSeedIDFromPlantID(species);
                if (!seed.IsValid || !DiscoveredResources.Instance.IsDiscovered(seed))
                    continue;
                bool forbidden = station.GetSeedForbidden(seed);
                options.Add(new Dictionary<string, object>
                {
                    ["speciesId"] = species.Name,
                    ["seedId"] = seed.Name,
                    ["name"] = seed.ProperName(),
                    ["allowed"] = !forbidden,
                    ["forbidden"] = forbidden,
                    ["subSpeciesCount"] = subspecies.Count
                });
            }
            return options.OrderBy(option => option["name"].ToString()).ToList();
        }

        private static Tag ResolveSeedTag(JObject args)
        {
            string seedId = args["seedId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(seedId))
                return new Tag(seedId.Trim());
            string speciesId = args["speciesId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(speciesId))
                return GetSeedIDFromPlantID(new Tag(speciesId.Trim()));
            return Tag.Invalid;
        }

        private static Tag GetSeedIDFromPlantID(Tag speciesID)
        {
            GameObject prefab = Assets.GetPrefab(speciesID);
            SeedProducer component = prefab?.GetComponent<SeedProducer>();
            if (component == null)
                return Tag.Invalid;
            return component.seedInfo.seedId;
        }

    }
}
