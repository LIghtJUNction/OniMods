using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ToolCallMiddleware
    {
        private static readonly object Sync = new object();
        private static readonly Queue<PendingNotification> PendingNotifications = new Queue<PendingNotification>();

        public static Dictionary<string, object> QueueNotification(string message, string level = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("message is required", nameof(message));

            var item = new PendingNotification
            {
                Message = message.Trim(),
                Level = string.IsNullOrWhiteSpace(level) ? "info" : level.Trim(),
                CreatedAtUtc = System.DateTime.UtcNow
            };

            lock (Sync)
                PendingNotifications.Enqueue(item);

            return Status();
        }

        public static List<Dictionary<string, object>> DrainNotifications()
        {
            var drained = new List<Dictionary<string, object>>();
            lock (Sync)
            {
                while (PendingNotifications.Count > 0)
                    drained.Add(PendingNotifications.Dequeue().ToDictionary());
            }
            return drained;
        }

        public static Dictionary<string, object> Status()
        {
            lock (Sync)
            {
                return new Dictionary<string, object>
                {
                    ["pendingNotifications"] = PendingNotifications.Count
                };
            }
        }

        public static Dictionary<string, object> Clear()
        {
            lock (Sync)
                PendingNotifications.Clear();
            return Status();
        }

        public static CallToolResult Inject(CallToolResult result, List<Dictionary<string, object>> notifications)
        {
            if (notifications == null || notifications.Count == 0)
                return result;

            if (result == null)
                result = CallToolResult.Text(string.Empty);
            if (result.Content == null)
                result.Content = new List<ToolContent>();

            result.Content.Add(new ToolContent
            {
                Text = JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["middleware"] = "tool_call",
                    ["notifications"] = notifications
                }, McpJsonUtil.Settings)
            });
            return result;
        }

        private sealed class PendingNotification
        {
            public string Message { get; set; }
            public string Level { get; set; }
            public System.DateTime CreatedAtUtc { get; set; }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["level"] = Level,
                    ["message"] = Message,
                    ["createdAtUtc"] = CreatedAtUtc.ToString("o")
                };
            }
        }
    }
}
