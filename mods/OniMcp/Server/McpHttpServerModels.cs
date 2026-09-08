using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

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
        internal const int MaxQueuedOutboundMessages = 256;
        private readonly Queue<JObject> _outboundQueue = new Queue<JObject>();
        private readonly object _outboundLock = new object();
        private bool _closed;

        public string Id { get; set; }
        public System.DateTime CreatedAt { get; set; }
        public string ProtocolVersion { get; set; }
        public Implementation ClientInfo { get; set; }
        public ClientCapabilities Capabilities { get; set; }
        public int SseConnections { get; set; }

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

        public bool EnqueueOutbound(JObject message)
        {
            if (message == null)
                return false;

            lock (_outboundLock)
            {
                if (_closed || _outboundQueue.Count >= MaxQueuedOutboundMessages)
                    return false;
                _outboundQueue.Enqueue(message);
                Monitor.PulseAll(_outboundLock);
                return true;
            }
        }

        public void WaitForOutbound(int timeoutMs)
        {
            lock (_outboundLock)
            {
                if (!_closed && _outboundQueue.Count == 0)
                    Monitor.Wait(_outboundLock, timeoutMs);
            }
        }

        public void Close()
        {
            lock (_outboundLock)
            {
                _closed = true;
                _outboundQueue.Clear();
                Monitor.PulseAll(_outboundLock);
            }
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
        public string SessionId { get; set; }
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
