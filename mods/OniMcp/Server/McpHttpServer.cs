using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
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
            GameRestartCoordinator.EnsureIntentConsumerStarted();
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

                var listener = _listener;
                _listenerThread = new Thread(() => ListenLoop(listener))
                {
                    IsBackground = true,
                    Name = "OniMcpHttpListener"
                };
                _listenerThread.Start();

                OniMcpLog.Debug($"[OniMcp] MCP Server started on {_options.EndpointUrl}");
            }
            catch (Exception ex)
            {
                _running = false;
                try { _listener?.Close(); } catch { }
                _listener = null;
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
            ResetHttpFrontDoorAdmission();
            ResetLegacySseAdmission();
            ResetMainThreadHttpAdmission();
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
                foreach (var session in _sessions.Values)
                    session.Close();
                _sessions.Clear();
            }
            lock (_taskLock)
            {
                _tasks.Clear();
            }

            CameraTools.CleanupTemporaryScreenshots();

            OniMcpLog.Debug("[OniMcp] MCP Server stopped.");
        }

        private void ListenLoop(HttpListener listener)
        {
            while (_running && ReferenceEquals(listener, _listener))
            {
                try
                {
                    var context = listener.GetContext();
                    HttpFrontDoorAdmissionLease admission;
                    if (!TryAcquireHttpFrontDoorAdmission(out admission))
                    {
                        RejectHttpFrontDoorBusy(context.Response);
                        continue;
                    }

                    try
                    {
                        // Do not put potentially blocking request-body reads on the CLR thread pool.
                        // A bounded set of dedicated request workers keeps HttpListener's own async
                        // machinery responsive enough for the listener thread to reject overloads.
                        var requestThread = new Thread(() =>
                        {
                            try
                            {
                                if (admission.IsCurrentGeneration() && ReferenceEquals(listener, _listener))
                                    ProcessRequest(context, admission);
                                else
                                    CloseStaleHttpResponse(context.Response);
                            }
                            finally
                            {
                                admission.Release();
                            }
                        })
                        {
                            IsBackground = true,
                            Name = "OniMcpHttpRequest"
                        };
                        requestThread.Start();
                    }
                    catch
                    {
                        admission.Release();
                        CloseStaleHttpResponse(context.Response);
                        throw;
                    }
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

        private void ProcessRequest(HttpListenerContext context, HttpFrontDoorAdmissionLease frontDoorAdmission)
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
                response.Headers.Add("Access-Control-Allow-Headers", BuildModernCorsAllowedHeaders());
                response.Headers.Add("Access-Control-Expose-Headers", "Mcp-Session-Id, Mcp-Protocol-Version");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                if (TryHandleSettingsRequest(request, response))
                    return;

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

                if (string.Equals(protocolVersion, ModernProtocolVersion, StringComparison.Ordinal)
                    && (request.HttpMethod == "GET" || request.HttpMethod == "DELETE"))
                {
                    response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    response.ContentLength64 = 0;
                    response.Close();
                    return;
                }

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
                            if (!AcceptsEventStream(request))
                            {
                                HandleGet(request, response, sessionId);
                                break;
                            }

                            LegacySseAdmissionLease sseAdmission;
                            if (!TryAcquireLegacySseAdmission(response, out sseAdmission))
                                break;

                            // The front-door lease exists to bound finite/pre-body request work.
                            // Once a validated legacy GET is admitted to the separate bounded SSE
                            // pool, release that finite-request slot before entering the stream loop.
                            frontDoorAdmission.Release();
                            try
                            {
                                HandleGet(request, response, sessionId);
                            }
                            finally
                            {
                                sseAdmission.Release();
                            }
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
