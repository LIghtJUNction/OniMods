using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private const int MaxDeferredBuilds = 100;
        private static readonly List<DeferredBuild> DeferredBuilds = new List<DeferredBuild>();
        private static DeferredBuildDriver deferredBuildDriver;
        private static int deferredBuildCursor;

        private static bool HasPendingAutoDigOrders(Dictionary<string, object> autoDig)
        {
            return BuildPlanningDeferredBuildPolicy.HasPendingOrders(
                GetInt(autoDig, "marked"), GetInt(autoDig, "alreadyMarked"),
                GetInt(autoDig, "uprootMarked"), GetInt(autoDig, "alreadyUprootMarked"));
        }

        internal sealed class DeferredBuildOrderHandle
        {
            internal string Id { get; private set; }
            internal Func<string> ReadStatus { get; private set; }

            internal DeferredBuildOrderHandle(string id, Func<string> readStatus)
            {
                Id = id;
                ReadStatus = readStatus;
            }
        }

        internal static List<DeferredBuildOrderHandle> CaptureDeferredBuildOrders(
            int worldId, HashSet<int> targetCells)
        {
            var captured = new List<DeferredBuildOrderHandle>();
            if (targetCells == null || targetCells.Count == 0 || Game.Instance == null)
                return captured;

            object rootWorld = DeferredRootWorld();
            object watchedWorld = DeferredWorld(worldId);
            if (rootWorld == null || watchedWorld == null)
                return captured;

            foreach (var item in DeferredBuilds)
            {
                if (!BuildPlanningDeferredBuildPolicy.IsUnfinished(item.Status)
                    || !ReferenceEquals(item.Game, Game.Instance)
                    || !ReferenceEquals(item.RootWorld, rootWorld)
                    || !ReferenceEquals(item.WatchedWorld, watchedWorld)
                    || item.WorldId != worldId)
                    continue;

                bool intersects = false;
                string resolvedPrefabId;
                string resolveError;
                var def = ResolveBuildingDef(item.PrefabId, out resolvedPrefabId, out resolveError);
                if (def != null)
                {
                    var placement = BuildPlacementDetails(def, item.X, item.Y, item.WorldId,
                        ParseOrientation(item.Args?["orientation"]?.ToString()));
                    intersects = placement?.Footprint != null
                        && placement.Footprint.Any(cell => cell.Valid && targetCells.Contains(cell.Cell));
                }
                if (!intersects)
                    continue;

                var capturedItem = item;
                captured.Add(new DeferredBuildOrderHandle(capturedItem.Id, () => capturedItem.Status));
            }
            return captured;
        }

        private static Dictionary<string, object> DeferBuildAfterAutoDig(
            string prefabId, int x, int y, JObject args, PlacementDetails placement,
            Dictionary<string, object> autoDig, Dictionary<string, object> materialSelection,
            string deferredBuildId)
        {
            var targetResults = GetObjectList(autoDig, "targets");
            int targetCount = GetInt(autoDig, "targetCount");
            bool everyTargetMarked = BuildPlanningDeferredBuildPolicy.AllTargetsQueued(targetCount,
                GetInt(autoDig, "marked"), GetInt(autoDig, "alreadyMarked"),
                GetInt(autoDig, "uprootMarked"), GetInt(autoDig, "alreadyUprootMarked"),
                GetInt(autoDig, "failed"), GetInt(autoDig, "skipped"), GetBool(autoDig, "limitReached"));

            if (!string.IsNullOrWhiteSpace(deferredBuildId))
            {
                int worldId = placement?.WorldId ?? ToolUtil.ResolveWorldId(args);
                var existing = DeferredBuilds.FirstOrDefault(item => string.Equals(item.Id, deferredBuildId, StringComparison.Ordinal));
                bool matches = BuildPlanningDeferredBuildPolicy.CanReuseHandle(existing != null,
                    existing != null && ReferenceEquals(existing.Game, Game.Instance),
                    existing != null && ReferenceEquals(existing.RootWorld, DeferredRootWorld()),
                    existing != null && ReferenceEquals(existing.WatchedWorld, DeferredWorld(worldId)),
                    existing != null && existing.WorldId == worldId,
                    existing != null && existing.X == x && existing.Y == y,
                    existing != null && string.Equals(existing.PrefabId, prefabId, StringComparison.OrdinalIgnoreCase),
                    existing != null && SameDeferredBuildOptions(existing.Args, args));
                if (!matches || existing.Status != "pending")
                    return DeferredBuildResult(null, prefabId, x, y, autoDig, materialSelection, "not_queued",
                        "The internal deferred-build handle is missing, stale, or does not match this exact request.",
                        BuildDeferredRemainingAction(args, prefabId, x, y));
                return DeferredPendingResult(existing.Id, prefabId, x, y, args, autoDig, materialSelection,
                    "Waiting for the native dig and uproot errands to clear this footprint.");
            }

            if (!everyTargetMarked)
            {
                return DeferredBuildResult(null, prefabId, x, y, autoDig, materialSelection, "partial",
                    "Some obstruction orders were not placed. Retry remainingAction after inspecting the failed or skipped targets.",
                    BuildDeferredRemainingAction(args, prefabId, x, y));
            }

            if (!TryCreateDeferredBuild(prefabId, x, y, args, placement, targetResults, materialSelection,
                    out string id, out string error))
            {
                return DeferredBuildResult(null, prefabId, x, y, autoDig, materialSelection, "not_queued", error,
                    BuildDeferredRemainingAction(args, prefabId, x, y));
            }

            return DeferredPendingResult(id, prefabId, x, y, args, autoDig, materialSelection,
                "Natural dig and uproot errands are queued. The blueprint will be placed after the recorded footprint clears.");
        }

        private static bool TryCreateDeferredBuild(string prefabId, int x, int y, JObject args,
            PlacementDetails placement, List<Dictionary<string, object>> targetResults,
            Dictionary<string, object> materialSelection,
            out string id, out string error)
        {
            id = null;
            error = null;
            if (Game.Instance == null || MainThreadBridge.Instance == null)
            {
                error = "The game or main-thread bridge is not available; native dig orders remain queued. Retry remainingAction after the colony is loaded.";
                return false;
            }

            object rootWorld = DeferredRootWorld();
            object watchedWorld = DeferredWorld(placement?.WorldId ?? ToolUtil.ResolveWorldId(args));
            if (rootWorld == null || watchedWorld == null)
            {
                error = "Stable loaded-world identity is unavailable; native obstruction orders remain queued. Retry remainingAction after the world is loaded.";
                return false;
            }

            PurgeDeferredBuildHistory();
            var existingRequest = DeferredBuilds.FirstOrDefault(item => item.Status == "pending"
                && ReferenceEquals(item.Game, Game.Instance)
                && ReferenceEquals(item.RootWorld, rootWorld)
                && ReferenceEquals(item.WatchedWorld, watchedWorld)
                && item.WorldId == (placement?.WorldId ?? ToolUtil.ResolveWorldId(args))
                && item.X == x && item.Y == y
                && string.Equals(item.PrefabId, prefabId, StringComparison.OrdinalIgnoreCase)
                && SameDeferredBuildOptions(item.Args, args));
            if (existingRequest != null)
            {
                id = existingRequest.Id;
                return true;
            }
            if (DeferredBuilds.Count(item => item.Status == "pending" || item.Status == "blueprint_placed") >= MaxDeferredBuilds)
            {
                error = "The deferred-build queue is full; native dig orders remain queued. Retry remainingAction after an existing pending build finishes or is cancelled.";
                return false;
            }

            var naturalCells = new HashSet<int>();
            var uprootCells = new HashSet<int>();
            foreach (var target in targetResults ?? new List<Dictionary<string, object>>())
            {
                int cell = GetInt(target, "cell");
                string status = target.ContainsKey("status") ? target["status"]?.ToString() : null;
                if (cell < 0)
                    continue;
                if (EqualsIgnoreCase(status, "marked") || EqualsIgnoreCase(status, "already_marked"))
                    naturalCells.Add(cell);
                else if (EqualsIgnoreCase(status, "uproot_marked") || EqualsIgnoreCase(status, "already_uproot_marked"))
                    uprootCells.Add(cell);
            }

            // The autoDig result is intentionally sample-capped. Derive the full watched set
            // from this one placement footprint so large buildings cannot resume on a partial sample.
            foreach (var cell in placement?.Footprint ?? new List<FootprintCell>())
            {
                if (!cell.Valid)
                    continue;
                if (IsNaturalDiggableSolidCell(cell.Cell, placement.WorldId)
                    && Grid.Objects[cell.Cell, (int)ObjectLayer.DigPlacer] != null)
                    naturalCells.Add(cell.Cell);
                if (UprootablesAtCell(cell.Cell, placement.WorldId).Any(target => target.IsMarkedForUproot))
                    uprootCells.Add(cell.Cell);
            }

            if (naturalCells.Count + uprootCells.Count == 0)
            {
                error = "No native obstruction orders were recorded for this deferred build.";
                return false;
            }

            id = Guid.NewGuid().ToString("N");
            var savedArgs = args == null ? new JObject() : (JObject)args.DeepClone();
            savedArgs["dryRun"] = false;
            savedArgs["confirm"] = true;
            savedArgs.Remove("__deferredBuildId");
            var entry = new DeferredBuild
            {
                Id = id,
                Game = Game.Instance,
                RootWorld = rootWorld,
                WatchedWorld = watchedWorld,
                WorldId = placement?.WorldId ?? ToolUtil.ResolveWorldId(args),
                PrefabId = prefabId,
                X = x,
                Y = y,
                MaterialSelection = materialSelection,
                Args = savedArgs,
                NaturalCells = naturalCells,
                UprootCells = uprootCells,
                CreatedAt = Time.realtimeSinceStartup,
                Status = "pending",
                LastResult = null,
                RemainingAction = BuildDeferredRemainingAction(args, prefabId, x, y)
            };
            DeferredBuilds.Add(entry);
            EnsureDeferredBuildDriver(entry.Game);
            return true;
        }

        private static bool SameDeferredBuildOptions(JObject left, JObject right)
        {
            foreach (string key in new[] { "material", "facade", "facadeId", "orientation", "allowUnsupported", "priority" })
            {
                if (!string.Equals(left?[key]?.ToString() ?? string.Empty,
                    right?[key]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static void EnsureDeferredBuildDriver(Game game)
        {
            if (deferredBuildDriver != null)
                return;
            var bridge = MainThreadBridge.Instance;
            if (bridge == null || game == null)
                return;
            deferredBuildDriver = bridge.gameObject.AddComponent<DeferredBuildDriver>();
            deferredBuildDriver.Initialize(game);
        }

        private static Dictionary<string, object> DeferredPendingResult(string id, string prefabId,
            int x, int y, JObject args, Dictionary<string, object> autoDig,
            Dictionary<string, object> materialSelection, string note)
        {
            var remaining = BuildDeferredRemainingAction(args, prefabId, x, y);
            return DeferredBuildResult(id, prefabId, x, y, autoDig, materialSelection, "pending", note, remaining);
        }

        private static Dictionary<string, object> DeferredBuildResult(string id, string prefabId,
            int x, int y, Dictionary<string, object> autoDig, Dictionary<string, object> materialSelection,
            string status, string note,
            Dictionary<string, object> remainingAction)
        {
            return new Dictionary<string, object>
            {
                ["planned"] = false,
                ["blueprintPlaced"] = false,
                ["buildingCompleted"] = false,
                ["pendingBuild"] = status == "pending",
                ["valid"] = status == "pending" || status == "partial",
                ["status"] = status,
                ["persisted"] = false,
                ["expiresOnLoad"] = true,
                ["prefabId"] = prefabId,
                ["x"] = x,
                ["y"] = y,
                ["pendingBuildId"] = id,
                ["autoDig"] = autoDig,
                ["materialSelection"] = materialSelection,
                ["remainingAction"] = remainingAction,
                ["next"] = string.IsNullOrWhiteSpace(id)
                    ? "Inspect autoDig.targets, then use remainingAction to retry after correcting failed or unreachable work."
                    : "Use building_control domain=planning action=pending_build pendingBuildId=" + id + " to inspect or cancel this deferred build.",
                ["note"] = note + " The pending handle is memory-only and expires when this save/world is unloaded; remainingAction is the recovery path."
            };
        }

        private static Dictionary<string, object> BuildDeferredRemainingAction(JObject source,
            string prefabId, int x, int y)
        {
            var arguments = new Dictionary<string, object>
            {
                ["domain"] = "planning",
                ["action"] = "build_area",
                ["prefabId"] = prefabId,
                ["worldId"] = ToolUtil.ResolveWorldId(source),
                ["anchors"] = new[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y } },
                ["confirm"] = true,
                ["dryRun"] = false,
                ["allowPartial"] = true,
                ["autoDigObstructions"] = true
            };
            foreach (string key in new[] { "material", "facade", "facadeId", "orientation", "priority",
                "allowUnsupported", "autoUprootObstructions", "maxAutoDigCells", "autoConnectPower", "maxAutoConnectRadius" })
            {
                var value = source?[key];
                if (value == null)
                    continue;
                arguments[key] = value.Type == JTokenType.Boolean ? (object)value.Value<bool>()
                    : value.Type == JTokenType.Integer ? (object)value.Value<int>()
                    : value.ToString();
            }
            return new Dictionary<string, object>
            {
                ["tool"] = "building_control",
                ["arguments"] = arguments,
                ["note"] = "Retry this exact anchor after the native dig or uproot errand has cleared the footprint."
            };
        }

        public static CallToolResult DeferredBuildControl(JObject args)
        {
            string action = args?["action"]?.ToString()?.Trim().ToLowerInvariant() ?? "status";
            string id = args?["pendingBuildId"]?.ToString();
            if (action != "status" && action != "cancel")
                return CallToolResult.Error("pending_build action must be status or cancel");
            if (string.IsNullOrWhiteSpace(id))
                return CallToolResult.Error("pendingBuildId is required");

            var item = DeferredBuilds.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
            if (item == null)
                return CallToolResult.Error("Deferred build not found; pending handles are scoped to the current game session.");
            if (action == "cancel")
            {
                bool dryRun = ToolUtil.GetBool(args, "dryRun", true);
                bool confirm = ToolUtil.GetBool(args, "confirm", false);
                bool canCancel = BuildPlanningDeferredBuildPolicy.CanCancel(item.Status == "pending", dryRun, confirm);
                if (BuildPlanningDeferredBuildPolicy.IsCancelPreview(dryRun))
                {
                    var preview = item.ToDictionary();
                    preview["cancelPreview"] = new Dictionary<string, object>
                    {
                        ["wouldCancel"] = item.Status == "pending",
                        ["cancelled"] = false,
                        ["dryRun"] = true,
                        ["note"] = "Preview only; pass dryRun=false and confirm=true to cancel the deferred build."
                    };
                    return CallToolResult.Text(Newtonsoft.Json.JsonConvert.SerializeObject(preview, McpJsonUtil.Settings));
                }
                if (!confirm)
                    return CallToolResult.Error("Cancelling a deferred build requires confirm=true.");
                if (!canCancel)
                    return CallToolResult.Error("Only a pending deferred build can be cancelled.");
                item.Status = "cancelled";
                item.UpdatedAt = Time.realtimeSinceStartup;
                item.LastResult = new Dictionary<string, object>
                {
                    ["cancelled"] = true,
                    ["note"] = "Only the deferred build was cancelled. Existing native dig and uproot orders were left unchanged."
                };
            }
            return CallToolResult.Text(Newtonsoft.Json.JsonConvert.SerializeObject(item.ToDictionary(), McpJsonUtil.Settings));
        }

    }
}
