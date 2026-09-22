using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
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
            if (string.IsNullOrEmpty(sessionId))
                return false;
            lock (_sessionLock)
            {
                return _sessions.ContainsKey(sessionId);
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

            McpSession expiredSession = null;
            bool active = false;
            DateTime now = _legacySessionPolicy.UtcNow();
            lock (_sessionLock)
            {
                McpSession session;
                if (_sessions.TryGetValue(sessionId, out session))
                {
                    if (_legacySessionPolicy.IsExpired(session, now))
                    {
                        _sessions.Remove(sessionId);
                        expiredSession = session;
                    }
                    else
                    {
                        session.LastActivityAt = now;
                        active = true;
                    }
                }
            }
            expiredSession?.Close();

            if (!active)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Session not found or terminated"), 404);
                return false;
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

            McpSession expiredSession = null;
            string errorMessage = null;
            int errorStatus = 0;
            DateTime now = _legacySessionPolicy.UtcNow();
            lock (_sessionLock)
            {
                McpSession session;
                if (!_sessions.TryGetValue(sessionId, out session))
                {
                    errorMessage = "Session not found or terminated";
                    errorStatus = 404;
                }
                else if (_legacySessionPolicy.IsExpired(session, now))
                {
                    _sessions.Remove(sessionId);
                    expiredSession = session;
                    errorMessage = "Session not found or terminated";
                    errorStatus = 404;
                }
                else if (!string.IsNullOrEmpty(protocolVersion) && !IsSupportedProtocolVersion(protocolVersion))
                {
                    errorMessage = $"Unsupported protocol version: {protocolVersion}. Supported: {string.Join(", ", SupportedProtocolVersions)}";
                    errorStatus = 400;
                }
                else if (!string.IsNullOrEmpty(protocolVersion)
                    && !string.Equals(session.ProtocolVersion, protocolVersion, StringComparison.Ordinal))
                {
                    errorMessage = $"Protocol version mismatch for session. Expected {session.ProtocolVersion}, got {protocolVersion}";
                    errorStatus = 400;
                }
                else
                {
                    session.LastActivityAt = now;
                    return true;
                }
            }
            expiredSession?.Close();

            SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, errorMessage), errorStatus);
            return false;
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
            List<McpSession> prunedSessions = null;
            bool capacityExceeded = false;
            bool missingSession = false;
            DateTime now = _legacySessionPolicy.UtcNow();

            lock (_sessionLock)
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    prunedSessions = PruneExpiredLegacySessionsLocked(now);
                    if (_sessions.Count >= _legacySessionPolicy.MaxRetainedSessions)
                    {
                        capacityExceeded = true;
                    }
                    else
                    {
                        sessionId = Guid.NewGuid().ToString("N");
                        _sessions[sessionId] = new McpSession
                        {
                            Id = sessionId,
                            CreatedAt = now,
                            LastActivityAt = now,
                            ProtocolVersion = CurrentProtocolVersion
                        };
                    }
                }
                else if (!_sessions.ContainsKey(sessionId))
                {
                    missingSession = true;
                }
            }

            ClosePrunedLegacySessions(prunedSessions);

            if (capacityExceeded)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, MainThreadBusyErrorCode,
                    "Legacy MCP session capacity is exhausted; retry after an idle session expires or is deleted",
                    new JObject
                    {
                        ["reasonCode"] = "legacy_session_capacity",
                        ["retryable"] = true,
                        ["maxRetainedSessions"] = _legacySessionPolicy.MaxRetainedSessions
                    }), (int)HttpStatusCode.ServiceUnavailable);
                return null;
            }

            if (missingSession)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Session not found or terminated"), 404);
                return null;
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
                            ["lastActivityAt"] = session.LastActivityAt.ToString("o"),
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
            var pairs = query.TrimStart('?').Split('&');
            foreach (var pair in pairs)
            {
                if (pair.Length == 0)
                    continue;
                int separator = pair.IndexOf('=');
                string key = separator < 0 ? pair : pair.Substring(0, separator);
                string value = separator < 0 ? "" : pair.Substring(separator + 1);
                // Form '+' is a space; percent-encoded '+' and '=' are literal token characters.
                result[Uri.UnescapeDataString(key.Replace("+", " "))] =
                    Uri.UnescapeDataString(value.Replace("+", " "));
            }
            return result;
        }
}
}
