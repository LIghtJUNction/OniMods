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
        private bool IsSessionActive(string sessionId)
        {
            lock (_sessionLock)
            {
                return _sessions.ContainsKey(sessionId) && !_terminatedSessions.Contains(sessionId);
            }
        }

        private bool ValidateInitializeTransport(HttpListenerResponse response, string sessionId, string protocolVersion)
        {
            if (!string.IsNullOrEmpty(protocolVersion) && !IsSupportedProtocolVersion(protocolVersion))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, $"Unsupported protocol version header: {protocolVersion}. Supported: {string.Join(", ", SupportedProtocolVersions)}"), 400);
                return false;
            }

            if (string.IsNullOrEmpty(sessionId))
                return true;

            lock (_sessionLock)
            {
                if (_terminatedSessions.Contains(sessionId))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Session not found or terminated"), 404);
                    return false;
                }
            }
            return true;
        }

        private bool ValidateNonInitRequest(HttpListenerResponse response, string sessionId, string protocolVersion)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Missing Mcp-Session-Id header"), 400);
                return false;
            }
            lock (_sessionLock)
            {
                McpSession session;
                if (!_sessions.TryGetValue(sessionId, out session))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Session not found or terminated"), 404);
                    return false;
                }
                if (string.IsNullOrEmpty(protocolVersion))
                    return true;

                if (!IsSupportedProtocolVersion(protocolVersion))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, $"Unsupported protocol version: {protocolVersion}. Supported: {string.Join(", ", SupportedProtocolVersions)}"), 400);
                    return false;
                }
                if (!string.Equals(session.ProtocolVersion, protocolVersion, StringComparison.Ordinal))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, $"Protocol version mismatch for session. Expected {session.ProtocolVersion}, got {protocolVersion}"), 400);
                    return false;
                }
            }
            return true;
        }

        private void SetResponseProtocolVersion(HttpListenerResponse response, string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return;

            lock (_sessionLock)
            {
                McpSession session;
                if (_sessions.TryGetValue(sessionId, out session) && !string.IsNullOrEmpty(session.ProtocolVersion))
                    response.Headers["Mcp-Protocol-Version"] = session.ProtocolVersion;
            }
        }

        private static void SetResponseSessionId(HttpListenerResponse response, string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
                response.Headers["Mcp-Session-Id"] = sessionId;
        }

        private string EnsureSession(HttpListenerResponse response, string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                sessionId = Guid.NewGuid().ToString("N");

            lock (_sessionLock)
            {
                if (!_sessions.ContainsKey(sessionId))
                {
                    _sessions[sessionId] = new McpSession
                    {
                        Id = sessionId,
                        CreatedAt = System.DateTime.UtcNow,
                        ProtocolVersion = CurrentProtocolVersion
                    };
                }
            }

            response.Headers["Mcp-Session-Id"] = sessionId;
            return sessionId;
        }

        private int SessionCount
        {
            get
            {
                lock (_sessionLock)
                {
                    return _sessions.Count;
                }
            }
        }

        public List<Dictionary<string, object>> GetSessionSummaries()
        {
            lock (_sessionLock)
            {
                return _sessions.Values
                    .OrderBy(session => session.CreatedAt)
                    .Select(session =>
                    {
                        var capabilities = session.Capabilities;
                        return new Dictionary<string, object>
                        {
                            ["id"] = session.Id,
                            ["createdAt"] = session.CreatedAt.ToString("o"),
                            ["protocolVersion"] = session.ProtocolVersion,
                            ["clientInfo"] = session.ClientInfo,
                            ["supportsSampling"] = capabilities?.Sampling != null,
                            ["supportsElicitation"] = capabilities?.Elicitation != null,
                            ["supportsTasks"] = capabilities?.Tasks != null,
                            ["sseConnections"] = session.SseConnections,
                            ["queuedClientMessages"] = session.QueuedOutboundCount,
                            ["capabilities"] = capabilities
                        };
                    })
                    .ToList();
            }
        }

        public Implementation GetSessionClientInfo(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return null;

            lock (_sessionLock)
            {
                McpSession session;
                return _sessions.TryGetValue(sessionId, out session) ? session.ClientInfo : null;
            }
        }

        private int TaskCount
        {
            get
            {
                lock (_taskLock)
                {
                    CleanupExpiredTasks();
                    return _tasks.Count;
                }
            }
        }

        public static string CurrentSessionId
        {
            get { return CurrentSessionContext.Value; }
        }

        internal static IDisposable PushSessionContext(string sessionId)
        {
            var previous = CurrentSessionContext.Value;
            CurrentSessionContext.Value = string.IsNullOrWhiteSpace(sessionId) ? previous : sessionId;
            return new SessionContextScope(previous);
        }

        private sealed class SessionContextScope : IDisposable
        {
            private readonly string previous;

            public SessionContextScope(string previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                CurrentSessionContext.Value = previous;
            }
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return result;
            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    string key = Uri.UnescapeDataString(parts[0]).Replace("+", " ");
                    string val = Uri.UnescapeDataString(parts[1]).Replace("+", " ");
                    result[key] = val;
                }
                else if (parts.Length == 1)
                {
                    string key = Uri.UnescapeDataString(parts[0]).Replace("+", " ");
                    result[key] = "";
                }
            }
            return result;
        }
}
}
