using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
        private static CallToolResult DiagnoseTask(JObject args)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");
            int targetId = args["id"]?.Value<int?>() ?? args["targetId"]?.Value<int?>() ?? 0;
            if (targetId <= 0)
                return CallToolResult.Error("task diagnose requires id (the target InstanceID)");

            string requestedType = (args["taskType"]?.ToString() ?? string.Empty).Trim();
            int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 20, 100));
            var observations = new List<TaskObservation>();
            var active = new List<Dictionary<string, object>>();
            int consumersSeen = 0;

            foreach (MinionIdentity minion in Components.LiveMinionIdentities)
            {
                if (minion == null)
                    continue;
                var consumer = minion.GetComponent<ChoreConsumer>();
                if (consumer == null)
                    continue;
                consumersSeen++;
                var current = consumer.choreDriver?.GetCurrentChore();
                if (MatchesTask(current, targetId, requestedType))
                    active.Add(new Dictionary<string, object>
                    {
                        ["dupeId"] = minion.GetComponent<KPrefabID>()?.InstanceID,
                        ["dupe"] = ToolUtil.CleanName(minion.GetProperName()),
                        ["choreType"] = current.choreType?.Id ?? current.GetType().Name
                    });

                // This is the consumer's most recent precondition-evaluation snapshot, not a live queue.
                var snapshot = consumer.GetLastPreconditionSnapshot();
                var rankedMatches = ObservedTaskRanking.Locate(
                    snapshot.succeededContexts,
                    context => MatchesTask(context.chore, targetId, requestedType),
                    (left, right) => right.CompareTo(left));
                foreach (var match in rankedMatches)
                    observations.Add(new TaskObservation(minion, match.Value, eligible: true, match.ObservedQueueRank, match.ObservedQueueLength));
                foreach (var context in snapshot.failedContexts)
                    if (MatchesTask(context.chore, targetId, requestedType))
                        observations.Add(new TaskObservation(minion, context, eligible: false, queueRank: null, queueLength: snapshot.succeededContexts.Count));
            }

            var groups = observations
                .GroupBy(item => item.Context.chore.choreType?.Id ?? item.Context.chore.GetType().Name)
                .Select(group => TaskGroupInfo(group.Key, group.ToList(), limit))
                .ToList();

            var payload = new Dictionary<string, object>
            {
                ["targetId"] = targetId,
                ["requestedTaskType"] = string.IsNullOrEmpty(requestedType) ? null : requestedType,
                ["activeChores"] = active.Take(limit).ToList(),
                ["currentChoreCount"] = active.Count,
                ["activeTruncated"] = active.Count > limit,
                ["observedTaskGroups"] = groups.Take(limit).ToList(),
                ["observedGroupCount"] = groups.Count,
                ["groupsTruncated"] = groups.Count > limit,
                ["coverage"] = new Dictionary<string, object>
                {
                    ["liveMinionIdentities"] = Components.LiveMinionIdentities.Count,
                    ["consumersInspected"] = consumersSeen,
                    ["snapshotSource"] = "ChoreConsumer.GetLastPreconditionSnapshot",
                    ["snapshotAge"] = "unknown",
                    ["snapshotCoverage"] = "partial/unknown",
                    ["unobservedEligibility"] = "unknown",
                    ["ranking"] = "per_dupe_cached_queue",
                    ["rankTieRule"] = "competition",
                    ["eligibility"] = "cached_native_preconditions"
                }
            };
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }

        private static bool MatchesTask(Chore chore, int targetId, string requestedType)
        {
            if (chore == null)
                return false;
            string type = chore.choreType?.Id ?? chore.GetType().Name;
            if (!string.IsNullOrEmpty(requestedType) && !string.Equals(type, requestedType, StringComparison.OrdinalIgnoreCase))
                return false;
            int choreTargetId = chore.target?.gameObject?.GetComponent<KPrefabID>()?.InstanceID ?? 0;
            return choreTargetId == targetId;
        }

        private static Dictionary<string, object> TaskGroupInfo(string choreType, List<TaskObservation> items, int limit)
        {
            var eligible = items.Where(item => item.Eligible)
                .GroupBy(item => item.Minion.GetComponent<KPrefabID>()?.InstanceID ?? item.Minion.gameObject.GetInstanceID())
                .Select(group => group.OrderBy(item => item.QueueRank).First())
                .ToList();
            var blocked = items.Where(item => !item.Eligible)
                .GroupBy(item => item.Minion.GetComponent<KPrefabID>()?.InstanceID ?? item.Minion.gameObject.GetInstanceID())
                .Select(group => group.First())
                .ToList();
            return new Dictionary<string, object>
            {
                ["choreType"] = choreType,
                ["eligibleObserved"] = eligible.Take(limit).Select(item => new Dictionary<string, object>
                {
                    ["dupeId"] = item.Minion.GetComponent<KPrefabID>()?.InstanceID,
                    ["dupe"] = ToolUtil.CleanName(item.Minion.GetProperName()),
                    ["observedQueueRank"] = item.QueueRank,
                    ["observedQueueLength"] = item.QueueLength
                }).ToList(),
                ["blockedObserved"] = blocked.Take(limit).Select(item => new Dictionary<string, object>
                {
                    ["dupeId"] = item.Minion.GetComponent<KPrefabID>()?.InstanceID,
                    ["dupe"] = ToolUtil.CleanName(item.Minion.GetProperName()),
                    ["failedPreconditionId"] = item.Context.failedPreconditionId
                }).ToList(),
                ["observedEligibleCount"] = eligible.Count,
                ["candidateCount"] = eligible.Count,
                ["eligibleTruncated"] = eligible.Count > limit,
                ["observedBlockedCount"] = blocked.Count,
                ["blockedCount"] = blocked.Count,
                ["blockedTruncated"] = blocked.Count > limit
            };
        }

        private sealed class TaskObservation
        {
            public readonly MinionIdentity Minion;
            public readonly Chore.Precondition.Context Context;
            public readonly bool Eligible;
            public readonly int? QueueRank;
            public readonly int QueueLength;

            public TaskObservation(MinionIdentity minion, Chore.Precondition.Context context, bool eligible, int? queueRank, int queueLength)
            {
                Minion = minion;
                Context = context;
                Eligible = eligible;
                QueueRank = queueRank;
                QueueLength = queueLength;
            }
        }
    }
}
