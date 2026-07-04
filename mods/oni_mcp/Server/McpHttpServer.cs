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
        public static McpHttpServer Instance { get; private set; }

        private static readonly AsyncLocal<string> CurrentSessionContext = new AsyncLocal<string>();

        private HttpListener _listener;

        private Thread _listenerThread;

        private volatile bool _running;

        private readonly Dictionary<string, McpSession> _sessions = new Dictionary<string, McpSession>();

        private readonly HashSet<string> _terminatedSessions = new HashSet<string>();

        private readonly object _sessionLock = new object();

        private readonly Dictionary<string, McpTaskEntry> _tasks = new Dictionary<string, McpTaskEntry>();

        private readonly object _taskLock = new object();

        private OniMcpOptions _options;

        internal const int TaskTtlMilliseconds = 600000;

        internal const int TaskPollIntervalMilliseconds = 1000;

        public int Port => _options?.Port ?? OniMcpOptions.Current.Port;

        public string EndpointUrl => (_options ?? OniMcpOptions.Current).EndpointUrl;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            StartServer();
        }

        private void OnDestroy()
        {
            StopServer();
            if (Instance == this)
                Instance = null;
        }

        public void StartServer()
        {
            if (_running) return;

            try
            {
                _options = OniMcpOptions.Current;
                CameraTools.CleanupTemporaryScreenshots();

                _listener = new HttpListener();
                foreach (var prefix in _options.ListenPrefixes)
                    _listener.Prefixes.Add(prefix);
                _listener.Start();
                _running = true;

                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "OniMcpHttpListener"
                };
                _listenerThread.Start();

                OniMcpLog.Debug($"[OniMcp] MCP Server started on {_options.EndpointUrl}");
            }
            catch (Exception ex)
            {
                OniMcpLog.Error($"[OniMcp] Failed to start MCP Server: {ex.Message}");
            }
        }

        public void RestartServer()
        {
            StopServer();
            StartServer();
        }

        public void StopServer()
        {
            _running = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }
            _listener = null;

            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                _listenerThread.Join(1000);
            }

            lock (_sessionLock)
            {
                _sessions.Clear();
                _terminatedSessions.Clear();
            }
            lock (_taskLock)
            {
                _tasks.Clear();
            }

            CameraTools.CleanupTemporaryScreenshots();

            OniMcpLog.Debug("[OniMcp] MCP Server stopped.");
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => ProcessRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OniMcpLog.Error($"[OniMcp] Listener error: {ex.Message}");
                }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string corsOrigin;
                if (!TryValidateCors(request, out corsOrigin))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Forbidden CORS origin"), 403);
                    return;
                }

                ApplyCorsHeaders(response, corsOrigin);

                response.Headers.Add("Access-Control-Allow-Methods", "GET, HEAD, POST, DELETE, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Mcp-Session-Id, Mcp-Protocol-Version, Accept, Authorization, X-Oni-Mcp-Token");
                response.Headers.Add("Access-Control-Expose-Headers", "Mcp-Session-Id, Mcp-Protocol-Version");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                if (!ValidateAuth(request, response))
                    return;

                if (request.Url != null
                    && request.Url.AbsolutePath.StartsWith("/screenshots/", StringComparison.OrdinalIgnoreCase))
                {
                    HandleScreenshotRequest(request, response);
                    return;
                }

                if (request.Url != null
                    && request.HttpMethod == "GET"
                    && IsVirtualWorldPath(request.Url.AbsolutePath))
                {
                    HandleVirtualWorldRequest(request, response);
                    return;
                }

                string sessionId = request.Headers["Mcp-Session-Id"];
                string protocolVersion = request.Headers["Mcp-Protocol-Version"];

                if (request.HttpMethod == "HEAD")
                {
                    SetResponseProtocolVersion(response, sessionId);
                    if (string.IsNullOrEmpty(response.Headers["Mcp-Protocol-Version"]))
                        response.Headers["Mcp-Protocol-Version"] = CurrentProtocolVersion;
                    response.StatusCode = 200;
                    response.ContentLength64 = 0;
                    response.Close();
                    return;
                }

                if (request.HttpMethod == "GET"
                    && string.IsNullOrEmpty(sessionId)
                    && !AcceptsEventStream(request))
                {
                    SendJson(response, new
                    {
                        status = "ok",
                        server = "OniMcp",
                        protocolVersion = CurrentProtocolVersion,
                        endpoint = EndpointUrl
                    }, 200);
                    return;
                }

                switch (request.HttpMethod)
                {
                    case "POST":
                        HandlePost(request, response, sessionId, protocolVersion);
                        break;
                    case "GET":
                        if (ValidateNonInitRequest(response, sessionId, protocolVersion))
                        {
                            SetResponseSessionId(response, sessionId);
                            SetResponseProtocolVersion(response, sessionId);
                            HandleGet(request, response, sessionId);
                        }
                        break;
                    case "DELETE":
                        if (ValidateNonInitRequest(response, sessionId, protocolVersion))
                        {
                            SetResponseSessionId(response, sessionId);
                            SetResponseProtocolVersion(response, sessionId);
                            HandleDelete(response, sessionId);
                        }
                        break;
                    default:
                        response.StatusCode = 405;
                        response.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                OniMcpLog.Error($"[OniMcp] Request error: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch { }
            }
        }
}
}
