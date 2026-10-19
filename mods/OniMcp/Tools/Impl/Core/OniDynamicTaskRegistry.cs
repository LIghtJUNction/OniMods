using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Server;
using OniMcp.Support;

namespace OniMcp.Tools
{
    /// <summary>
    /// In-memory dynamic tasks and reports registry.
    /// Supports asynchronous execution, dynamic resource generation,
    /// and active resource notifications (notifications/resources/list_changed).
    /// </summary>
    public static class OniDynamicTaskRegistry
    {
        public sealed class DynamicTaskEntry
        {
            public string TaskId { get; set; }
            public string Title { get; set; }
            public string Category { get; set; }
            public string Status { get; set; } // submitted, running, completed, failed, cancelled
            public string StatusMessage { get; set; }
            public System.DateTime CreatedAt { get; set; }
            public System.DateTime? CompletedAt { get; set; }
            public string ResourceUri { get; set; }
            public float Progress { get; set; }
            public JToken Result { get; set; }
            public string Error { get; set; }
            public JObject Meta { get; set; }

            public JObject ToJson()
            {
                var obj = new JObject
                {
                    ["taskId"] = TaskId,
                    ["title"] = Title,
                    ["category"] = Category,
                    ["status"] = Status,
                    ["statusMessage"] = StatusMessage,
                    ["createdAt"] = CreatedAt.ToString("o"),
                    ["resourceUri"] = ResourceUri,
                    ["progress"] = Progress
                };
                if (CompletedAt.HasValue)
                    obj["completedAt"] = CompletedAt.Value.ToString("o");
                if (!string.IsNullOrEmpty(Error))
                    obj["error"] = Error;
                if (Meta != null)
                    obj["meta"] = Meta.DeepClone();
                if (Result != null)
                    obj["result"] = Result.DeepClone();
                return obj;
            }
        }

        private static readonly object _syncLock = new object();
        private static readonly Dictionary<string, DynamicTaskEntry> _tasks = new Dictionary<string, DynamicTaskEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, JToken> _reports = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
        private static int _taskCounter = 0;

        public static DynamicTaskEntry SubmitTask(string title, string category, Func<JToken> work, JObject meta = null)
        {
            int num = Interlocked.Increment(ref _taskCounter);
            string taskId = $"task_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}_{num:D3}";
            string resourceUri = $"oni://reports/{taskId}";

            var entry = new DynamicTaskEntry
            {
                TaskId = taskId,
                Title = title ?? "Async Task",
                Category = category ?? "general",
                Status = "submitted",
                StatusMessage = "Task queued for execution",
                CreatedAt = System.DateTime.UtcNow,
                ResourceUri = resourceUri,
                Progress = 0f,
                Meta = meta
            };

            lock (_syncLock)
            {
                CleanupOldEntriesLocked();
                _tasks[taskId] = entry;
            }

            // Execute asynchronously via MainThreadBridge
            MainThreadBridge.EnqueueDeferred(new System.Action(() =>
            {
                lock (_syncLock)
                {
                    entry.Status = "running";
                    entry.StatusMessage = "Task is executing";
                    entry.Progress = 0.2f;
                }

                try
                {
                    JToken result = work != null ? work() : new JObject { ["success"] = true };
                    lock (_syncLock)
                    {
                        entry.Status = "completed";
                        entry.StatusMessage = "Task finished successfully";
                        entry.Progress = 1.0f;
                        entry.CompletedAt = System.DateTime.UtcNow;
                        entry.Result = result;
                        _reports[taskId] = result;
                    }
                    NotifyResourceUpdate(taskId, resourceUri);
                }
                catch (Exception ex)
                {
                    lock (_syncLock)
                    {
                        entry.Status = "failed";
                        entry.StatusMessage = "Task failed: " + ex.Message;
                        entry.Error = ex.ToString();
                        entry.CompletedAt = System.DateTime.UtcNow;
                    }
                    NotifyResourceUpdate(taskId, null);
                }
            }));

            return entry;
        }

        public static void RegisterReport(string reportId, string title, JToken content)
        {
            if (string.IsNullOrEmpty(reportId))
                return;

            string resourceUri = $"oni://reports/{reportId}";
            lock (_syncLock)
            {
                _reports[reportId] = content;
                if (!_tasks.ContainsKey(reportId))
                {
                    _tasks[reportId] = new DynamicTaskEntry
                    {
                        TaskId = reportId,
                        Title = title ?? reportId,
                        Category = "report",
                        Status = "completed",
                        StatusMessage = "Report published",
                        CreatedAt = System.DateTime.UtcNow,
                        CompletedAt = System.DateTime.UtcNow,
                        ResourceUri = resourceUri,
                        Progress = 1.0f,
                        Result = content
                    };
                }
            }
            NotifyResourceUpdate(reportId, resourceUri);
        }

        public static JToken GetReport(string reportId)
        {
            if (string.IsNullOrEmpty(reportId))
                return null;

            lock (_syncLock)
            {
                if (_reports.TryGetValue(reportId, out var report))
                    return report.DeepClone();
                if (_tasks.TryGetValue(reportId, out var task) && task.Result != null)
                    return task.Result.DeepClone();
            }
            return null;
        }

        public static DynamicTaskEntry GetTask(string taskId)
        {
            if (string.IsNullOrEmpty(taskId))
                return null;

            lock (_syncLock)
            {
                if (_tasks.TryGetValue(taskId, out var task))
                    return task;
            }
            return null;
        }

        public static JArray ListTasks()
        {
            lock (_syncLock)
            {
                var array = new JArray();
                foreach (var task in _tasks.Values.OrderByDescending(t => t.CreatedAt))
                {
                    array.Add(task.ToJson());
                }
                return array;
            }
        }

        public static JArray ListReports()
        {
            lock (_syncLock)
            {
                var array = new JArray();
                foreach (var kvp in _reports)
                {
                    _tasks.TryGetValue(kvp.Key, out var task);
                    var item = new JObject
                    {
                        ["reportId"] = kvp.Key,
                        ["uri"] = $"oni://reports/{kvp.Key}",
                        ["title"] = task?.Title ?? kvp.Key,
                        ["createdAt"] = task?.CreatedAt.ToString("o") ?? System.DateTime.UtcNow.ToString("o")
                    };
                    array.Add(item);
                }
                return array;
            }
        }

        private static void NotifyResourceUpdate(string taskId, string resourceUri)
        {
            try
            {
                var server = McpHttpServer.Instance;
                if (server == null)
                    return;

                server.EnqueueResourceListChangedNotification();
                if (!string.IsNullOrEmpty(resourceUri))
                    server.EnqueueResourceUpdatedNotification(resourceUri);
                server.EnqueueResourceUpdatedNotification($"oni://tasks/{taskId}");
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to broadcast dynamic resource notification: " + ex.Message);
            }
        }

        private static void CleanupOldEntriesLocked()
        {
            if (_tasks.Count > 150)
            {
                var toRemove = _tasks.Values
                    .OrderBy(t => t.CreatedAt)
                    .Take(_tasks.Count - 100)
                    .Select(t => t.TaskId)
                    .ToList();

                foreach (var id in toRemove)
                {
                    _tasks.Remove(id);
                    _reports.Remove(id);
                }
            }
        }
    }
}
