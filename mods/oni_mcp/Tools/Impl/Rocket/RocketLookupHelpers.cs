using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class RocketTools
    {
        private static Dictionary<string, McpToolParameter> RocketLookupParams(Dictionary<string, McpToolParameter> extra = null)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "火箭 InstanceID", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "火箭名称", Required = false }
            };
            if (extra != null)
            {
                foreach (var item in extra)
                    parameters[item.Key] = item.Value;
            }
            return parameters;
        }

        private static Clustercraft FindClustercraft(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            string name = args["name"]?.ToString();
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null) continue;
                var kpid = craft.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return craft;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(craft.Name, name, StringComparison.OrdinalIgnoreCase))
                    return craft;
            }
            return null;
        }

        private static Dictionary<string, object> ClustercraftToDictionary(Clustercraft craft, bool includeDetail = false)
        {
            var kpid = craft.GetComponent<KPrefabID>();
            var traveler = craft.GetComponent<ClusterTraveler>();
            var moduleInterface = craft.ModuleInterface;
            WorldContainer interior = moduleInterface?.GetInteriorWorld();

            var result = new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? -1,
                ["name"] = craft.Name,
                ["status"] = craft.Status.ToString(),
                ["isFlightInProgress"] = Safe(() => craft.IsFlightInProgress(), false),
                ["launchRequested"] = craft.LaunchRequested,
                ["readyToLaunch"] = Safe(() => craft.CheckReadyToLaunch(), false),
                ["preppedForLaunch"] = Safe(() => craft.CheckPreppedForLaunch(), false),
                ["location"] = AxialToDictionary(craft.Location),
                ["destination"] = AxialToDictionary(craft.Destination),
                ["destinationWorldId"] = traveler != null ? Safe(() => traveler.GetDestinationWorldID(), -1) : -1,
                ["interiorWorldId"] = interior?.id ?? -1,
                ["interiorWorldName"] = interior != null ? interior.GetProperName() : null,
                ["speed"] = Math.Round(Safe(() => craft.Speed, 0f), 2),
                ["range"] = moduleInterface != null ? moduleInterface.RangeInTiles : 0,
                ["maxRange"] = moduleInterface != null ? moduleInterface.MaxRange : 0,
                ["fuelKg"] = Math.Round(moduleInterface != null ? moduleInterface.FuelRemaining : 0f, 2),
                ["fuelCapacityKg"] = Math.Round(moduleInterface != null ? moduleInterface.FuelCapacity : 0f, 2),
                ["oxidizerKg"] = Math.Round(moduleInterface != null ? moduleInterface.OxidizerPowerRemaining : 0f, 2),
                ["oxidizerCapacityKg"] = Math.Round(moduleInterface != null ? moduleInterface.OxidizerCapacity : 0f, 2),
                ["burdenKg"] = Math.Round(moduleInterface != null ? moduleInterface.TotalBurden : 0f, 2),
                ["rocketHeight"] = moduleInterface != null ? moduleInterface.RocketHeight : 0,
                ["hasCargoModule"] = moduleInterface != null && moduleInterface.HasCargoModule
            };

            var rocketSelector = moduleInterface?.GetClusterDestinationSelector() as RocketClusterDestinationSelector;
            if (rocketSelector != null)
            {
                var destinationPad = rocketSelector.GetDestinationPad();
                result["roundTrip"] = rocketSelector.Repeat;
                result["previousDestination"] = AxialToDictionary(rocketSelector.PreviousDestination);
                result["destinationPad"] = destinationPad == null ? null : LaunchPadSummary(destinationPad);
            }

            if (traveler != null)
            {
                result["isTraveling"] = Safe(() => traveler.IsTraveling(), false);
                result["etaSeconds"] = Math.Round(Safe(() => traveler.TravelETA(), 0f), 1);
                result["remainingTravelDistance"] = Math.Round(Safe(() => traveler.RemainingTravelDistance(), 0f), 2);
                result["remainingTravelNodes"] = Safe(() => traveler.RemainingTravelNodes(), 0);
                result["moveProgress"] = Math.Round(Safe(() => traveler.GetMoveProgress(), 0f), 3);
            }

            if (includeDetail)
            {
                result["currentLocation"] = DescribeLocation(craft.Location);
                result["destinationLocation"] = DescribeLocation(craft.Destination);
                result["modules"] = moduleInterface?.GetParts()
                    .Where(part => part != null)
                    .Select(part => new Dictionary<string, object>
                    {
                        ["id"] = part.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                        ["name"] = ToolUtil.CleanName(part.GetProperName()),
                        ["prefabId"] = part.GetComponent<KPrefabID>()?.PrefabTag.Name ?? part.name,
                        ["worldId"] = part.GetMyWorldId()
                    })
                    .ToList();
            }

            return result;
        }

        private static Dictionary<string, object> SpacecraftToDictionary(Spacecraft craft)
        {
            var manager = SpacecraftManager.instance;
            var destination = manager != null ? Safe(() => manager.GetSpacecraftDestination(craft.id), null) : null;
            return new Dictionary<string, object>
            {
                ["id"] = craft.id,
                ["name"] = craft.GetRocketName(),
                ["state"] = craft.state.ToString(),
                ["timeLeftSeconds"] = Math.Round(Safe(() => craft.GetTimeLeft(), 0f), 1),
                ["durationSeconds"] = Math.Round(Safe(() => craft.GetDuration(), 0f), 1),
                ["controlStationBuffTimeRemaining"] = Math.Round(craft.controlStationBuffTimeRemaining, 1),
                ["destination"] = destination != null ? SpaceDestinationToDictionary(destination) : null
            };
        }

        private static Dictionary<string, object> SpaceDestinationToDictionary(SpaceDestination destination)
        {
            return new Dictionary<string, object>
            {
                ["id"] = destination.id,
                ["type"] = destination.type,
                ["distance"] = destination.distance,
                ["analysisState"] = SpacecraftManager.instance != null ? Safe(() => SpacecraftManager.instance.GetDestinationAnalysisState(destination).ToString(), "unknown") : "unknown",
                ["analysisScore"] = SpacecraftManager.instance != null ? Math.Round(Safe(() => SpacecraftManager.instance.GetDestinationAnalysisScore(destination), 0f), 2) : 0
            };
        }

        private static Dictionary<string, object> EntityToDictionary(ClusterGridEntity entity)
        {
            var asteroid = entity.GetComponent<AsteroidGridEntity>();
            var world = GetAsteroidWorld(asteroid);
            return new Dictionary<string, object>
            {
                ["name"] = entity.Name,
                ["layer"] = entity.Layer.ToString(),
                ["location"] = AxialToDictionary(entity.Location),
                ["visible"] = ClusterGrid.Instance == null || ClusterGrid.Instance.IsVisible(entity),
                ["isWorldEntity"] = entity.isWorldEntity,
                ["worldId"] = world?.id ?? -1,
                ["worldName"] = world != null ? world.GetProperName() : null
            };
        }

        private static List<ClusterGridEntity> GetClusterEntities(bool visibleOnly, string layer, int limit)
        {
            var grid = ClusterGrid.Instance;
            if (grid == null || grid.cellContents == null)
                return new List<ClusterGridEntity>();

            var results = new List<ClusterGridEntity>();
            foreach (var bucket in grid.cellContents.Values)
            {
                if (bucket == null) continue;
                foreach (var entity in bucket)
                {
                    if (entity == null) continue;
                    if (visibleOnly && !grid.IsVisible(entity)) continue;
                    if (!string.IsNullOrWhiteSpace(layer) && !string.Equals(entity.Layer.ToString(), layer, StringComparison.OrdinalIgnoreCase)) continue;
                    results.Add(entity);
                    if (results.Count >= limit)
                        return results;
                }
            }
            return results;
        }

        private static List<Spacecraft> GetBaseGameSpacecraft()
        {
            var manager = SpacecraftManager.instance;
            return manager != null ? manager.GetSpacecraft() : new List<Spacecraft>();
        }

        private static List<SpaceDestination> GetSpaceDestinations()
        {
            var manager = SpacecraftManager.instance;
            return manager?.destinations ?? new List<SpaceDestination>();
        }

        private static LaunchPad FindLaunchPad(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "padId");
            int? x = ToolUtil.GetInt(args, "padX");
            int? y = ToolUtil.GetInt(args, "padY");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = ToolUtil.GetInt(args, "padWorldId") ?? -1;
            foreach (var pad in Components.LaunchPads.Items)
            {
                if (pad == null)
                    continue;
                var go = pad.gameObject;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return pad;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return pad;
            }
            return null;
        }

        private static Dictionary<string, object> LaunchPadToDictionary(LaunchPad pad)
        {
            var result = LaunchPadSummary(pad);
            var landed = pad.LandedRocket;
            result["operational"] = pad.GetComponent<Operational>()?.IsOperational ?? false;
            result["logicInputConnected"] = pad.IsLogicInputConnected();
            result["landedRocket"] = landed == null || landed.CraftInterface == null ? null : ClustercraftToDictionary(landed.CraftInterface.GetComponent<Clustercraft>());
            result["waitingToLand"] = WaitingCraftsForPad(pad);
            return result;
        }

        private static Dictionary<string, object> LaunchPadSummary(LaunchPad pad)
        {
            int cell = Grid.PosToCell(pad.gameObject);
            var kpid = pad.GetComponent<KPrefabID>();
            var world = pad.GetMyWorld();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? pad.GetInstanceID(),
                ["prefabId"] = pad.GetComponent<Building>()?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? pad.name,
                ["name"] = ToolUtil.CleanName(pad.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = world?.id ?? -1,
                ["worldName"] = world != null ? ToolUtil.CleanName(world.GetProperName()) : null,
                ["clusterLocation"] = AxialToDictionary(pad.GetMyWorldLocation())
            };
        }

        private static List<Dictionary<string, object>> WaitingCraftsForPad(LaunchPad pad)
        {
            var results = new List<Dictionary<string, object>>();
            if (ClusterGrid.Instance == null)
                return results;
            AxialI location = pad.GetMyWorldLocation();
            foreach (ClusterGridEntity entity in ClusterGrid.Instance.GetEntitiesInRange(location))
            {
                var craft = entity as Clustercraft;
                if (craft == null || craft.Status != Clustercraft.CraftStatus.InFlight || (craft.IsFlightInProgress() && craft.Destination != location))
                    continue;
                string failReason;
                var status = craft.CanLandAtPad(pad, out failReason);
                results.Add(new Dictionary<string, object>
                {
                    ["rocket"] = ClustercraftToDictionary(craft),
                    ["landingStatus"] = status.ToString(),
                    ["failReason"] = failReason,
                    ["selected"] = (craft.ModuleInterface?.GetClusterDestinationSelector() as RocketClusterDestinationSelector)?.GetDestinationPad() == pad
                });
            }
            return results;
        }

        private static bool TryResolveDestination(JObject args, out AxialI destination, out string source)
        {
            int? worldId = ToolUtil.GetInt(args, "worldId");
            if (worldId.HasValue)
            {
                if (TryFindAsteroidLocationForWorld(worldId.Value, out destination))
                {
                    source = "worldId";
                    return true;
                }
                source = null;
                destination = AxialI.INVALID;
                return false;
            }

            int? q = ToolUtil.GetInt(args, "q");
            int? r = ToolUtil.GetInt(args, "r");
            if (q.HasValue && r.HasValue)
            {
                destination = new AxialI(q.Value, r.Value);
                source = "q/r";
                return true;
            }

            source = null;
            destination = AxialI.INVALID;
            return false;
        }

        private static bool TryFindAsteroidLocationForWorld(int worldId, out AxialI location)
        {
            var grid = ClusterGrid.Instance;
            if (grid != null && grid.cellContents != null)
            {
                foreach (var bucket in grid.cellContents.Values)
                {
                    if (bucket == null) continue;
                    foreach (var entity in bucket)
                    {
                        var asteroid = entity != null ? entity.GetComponent<AsteroidGridEntity>() : null;
                        var world = GetAsteroidWorld(asteroid);
                        if (world != null && world.id == worldId)
                        {
                            location = entity.Location;
                            return true;
                        }
                    }
                }
            }

            location = AxialI.INVALID;
            return false;
        }

        private static Dictionary<string, object> DescribeLocation(AxialI location)
        {
            var result = AxialToDictionary(location);
            var grid = ClusterGrid.Instance;
            if (grid == null)
                return result;

            UnityEngine.Sprite sprite;
            string label;
            string sublabel;
            EntityLayer layer;
            grid.GetLocationDescription(location, out sprite, out label, out sublabel, out layer);
            result["label"] = ToolUtil.CleanName(label);
            result["sublabel"] = ToolUtil.CleanName(sublabel);
            result["layer"] = layer.ToString();
            return result;
        }

        private static Dictionary<string, object> AxialToDictionary(AxialI location)
        {
            return new Dictionary<string, object>
            {
                ["q"] = location.Q,
                ["r"] = location.R
            };
        }

        private static WorldContainer GetAsteroidWorld(AsteroidGridEntity asteroid)
        {
            return asteroid != null && AsteroidWorldContainerField != null
                ? AsteroidWorldContainerField.GetValue(asteroid) as WorldContainer
                : null;
        }

        private static bool CanTravelToCell(Clustercraft craft, AxialI destination)
        {
            if (craft == null || CanTravelToCellMethod == null)
                return true;
            return Safe(() => (bool)CanTravelToCellMethod.Invoke(craft, new object[] { destination }), true);
        }

        private static void MarkPathDirty(ClusterTraveler traveler)
        {
            if (traveler == null || MarkPathDirtyMethod == null)
                return;
            Safe(() =>
            {
                MarkPathDirtyMethod.Invoke(traveler, null);
                return true;
            }, false);
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static T Safe<T>(Func<T> func, T fallback)
        {
            try
            {
                return func();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
