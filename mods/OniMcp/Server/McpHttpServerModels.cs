using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Support;
using OniMcp.Tools;
using UnityEngine;

namespace OniMcp.Server
{
    /// <summary>
    /// MCP Streamable HTTP 服务器实现
    /// 基于 System.Net.HttpListener（.NET Framework 内置）
    /// </summary>
    /// <summary>
    /// MCP 会话
    /// </summary>
    public class McpSession
    {
        private readonly Queue<JObject> _outboundQueue = new Queue<JObject>();
        private readonly object _outboundLock = new object();

        public string Id { get; set; }
        public System.DateTime CreatedAt { get; set; }
        public string ProtocolVersion { get; set; }
        public Implementation ClientInfo { get; set; }
        public ClientCapabilities Capabilities { get; set; }
        public int SseConnections { get; set; }
        public AutoResetEvent OutboundSignal { get; } = new AutoResetEvent(false);

        public int QueuedOutboundCount
        {
            get
            {
                lock (_outboundLock)
                {
                    return _outboundQueue.Count;
                }
            }
        }

        public void EnqueueOutbound(JObject message)
        {
            if (message == null)
                return;

            lock (_outboundLock)
            {
                _outboundQueue.Enqueue(message);
            }
            OutboundSignal.Set();
        }

        public JObject TryDequeueOutbound()
        {
            lock (_outboundLock)
            {
                return _outboundQueue.Count == 0 ? null : _outboundQueue.Dequeue();
            }
        }
    }

    public class McpTaskEntry
    {
        public string TaskId { get; set; }
        public string Status { get; set; }
        public string StatusMessage { get; set; }
        public System.DateTime CreatedAt { get; set; }
        public System.DateTime LastUpdatedAt { get; set; }
        public string Method { get; set; }
        public string Target { get; set; }
        public JObject Metadata { get; set; }
        public int TtlMilliseconds { get; set; }
        public object Result { get; set; }
        public string Error { get; set; }
        public bool CancelRequested { get; set; }

        public McpTaskInfo ToInfo()
        {
            return new McpTaskInfo
            {
                TaskId = TaskId,
                Status = Status,
                StatusMessage = StatusMessage,
                CreatedAt = CreatedAt.ToString("o"),
                LastUpdatedAt = LastUpdatedAt.ToString("o"),
                Ttl = TtlMilliseconds,
                PollInterval = McpHttpServer.TaskPollIntervalMilliseconds
            };
}

}
}
