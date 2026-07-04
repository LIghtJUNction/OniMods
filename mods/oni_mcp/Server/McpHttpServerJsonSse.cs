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
    public partial class McpHttpServer : MonoBehaviour
{
        private void SendJson(HttpListenerResponse response, object data, int statusCode)
        {
            string json = JsonConvert.SerializeObject(data, McpJsonUtil.Settings);
            byte[] buffer = Encoding.UTF8.GetBytes(json);

            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            if (string.IsNullOrEmpty(response.Headers["Mcp-Protocol-Version"]))
                response.Headers["Mcp-Protocol-Version"] = CurrentProtocolVersion;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        private static void WriteSseComment(HttpListenerResponse response, string comment)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(": " + comment + "\n\n");
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Flush();
        }

        private static void WriteSseMessage(HttpListenerResponse response, JObject message)
        {
            string json = JsonConvert.SerializeObject(message, McpJsonUtil.Settings);
            byte[] buffer = Encoding.UTF8.GetBytes("event: message\ndata: " + json + "\n\n");
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Flush();
        }

        private void HandleClientResponse(JObject message, string sessionId)
        {
            string id = message["id"]?.ToString() ?? "";
            if (message["error"] != null)
                OniMcpLog.Warning($"[OniMcp] Client response error for server request {id} session={sessionId}: {message["error"]}");
            else
                OniMcpLog.Debug($"[OniMcp] Client response received for server request {id} session={sessionId}");
        }

        public int EnqueueClientRequest(JObject request, bool requireSampling)
        {
            if (request == null)
                return 0;

            List<McpSession> sessions;
            lock (_sessionLock)
            {
                sessions = _sessions.Values
                    .Where(session => !requireSampling || session.Capabilities?.Sampling != null)
                    .ToList();
            }

            foreach (var session in sessions)
                session.EnqueueOutbound(CloneOutboundMessage(request));

            if (sessions.Count > 0)
                OniMcpLog.Debug($"[OniMcp] Queued client request {request["method"]} for {sessions.Count} session(s).");

            return sessions.Count;
        }

        public int EnqueueClientNotification(string level, string logger, object data)
        {
            var notification = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/message",
                ["params"] = new JObject
                {
                    ["level"] = string.IsNullOrWhiteSpace(level) ? "info" : level,
                    ["logger"] = string.IsNullOrWhiteSpace(logger) ? "OniMcp" : logger,
                    ["data"] = data == null ? JValue.CreateNull() : JToken.FromObject(data, JsonSerializer.Create(McpJsonUtil.Settings))
                }
            };
            return EnqueueClientRequest(notification, requireSampling: false);
        }

        private static JObject CloneOutboundMessage(JObject message)
        {
            var clone = (JObject)message.DeepClone();
            if (clone["id"] != null)
                clone["id"] = Guid.NewGuid().ToString("N");
            return clone;
        }
}
}
