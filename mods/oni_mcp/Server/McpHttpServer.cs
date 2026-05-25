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
    public class McpHttpServer : MonoBehaviour
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
                // CORS
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
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

                string sessionId = request.Headers["Mcp-Session-Id"];
                string protocolVersion = request.Headers["Mcp-Protocol-Version"];

                switch (request.HttpMethod)
                {
                    case "POST":
                        HandlePost(request, response, sessionId, protocolVersion);
                        break;
                    case "GET":
                        if (ValidateNonInitRequest(response, sessionId, protocolVersion))
                        {
                            SetResponseProtocolVersion(response, sessionId);
                            HandleGet(request, response, sessionId);
                        }
                        break;
                    case "DELETE":
                        if (ValidateNonInitRequest(response, sessionId, protocolVersion))
                        {
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

        private void HandlePost(HttpListenerRequest request, HttpListenerResponse response, string sessionId, string protocolVersion)
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Empty request body"), 400);
                return;
            }

            JObject rawMessage;
            try
            {
                rawMessage = JObject.Parse(body);
            }
            catch (Exception ex)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.ParseError, $"Parse error: {ex.Message}"), 200);
                return;
            }

            if (rawMessage["method"] == null && (rawMessage["result"] != null || rawMessage["error"] != null))
            {
                if (!ValidateNonInitRequest(response, sessionId, protocolVersion))
                    return;

                HandleClientResponse(rawMessage, sessionId);
                response.StatusCode = 202;
                response.Close();
                return;
            }

            JsonRpcRequest rpcRequest;
            try
            {
                rpcRequest = rawMessage.ToObject<JsonRpcRequest>();
            }
            catch (Exception ex)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.ParseError, $"Parse error: {ex.Message}"), 200);
                return;
            }

            if (rpcRequest == null || rpcRequest.JsonRpc != "2.0")
            {
                SendJson(response, JsonRpcResponse.MakeError(rpcRequest?.Id, McpErrorCode.InvalidRequest, "Invalid JSON-RPC request"), 200);
                return;
            }

            bool isInitialize = rpcRequest.Method == "initialize";
            if (!isInitialize)
            {
                if (!ValidateNonInitRequest(response, sessionId, protocolVersion))
                    return;
            }
            else if (!ValidateInitializeTransport(response, sessionId, protocolVersion))
            {
                return;
            }

            if (isInitialize)
                sessionId = EnsureSession(response, sessionId);

            // 通知（无 id）：返回 202 Accepted
            if (rpcRequest.IsNotification)
            {
                // 在后台处理通知
                MainThreadBridge.Enqueue(new System.Action(() => ProcessMethod(rpcRequest, sessionId)));
                response.StatusCode = 202;
                response.Close();
                return;
            }

            DispatchPostResponse(response, rpcRequest, sessionId);
        }

        private bool ValidateAuth(HttpListenerRequest request, HttpListenerResponse response)
        {
            var options = _options ?? OniMcpOptions.Current;
            if (options == null || !options.AuthEnabled)
                return true;

            string expected = options.AuthToken ?? "";
            if (string.IsNullOrWhiteSpace(expected))
                return true;

            string provided = request.Headers["X-Oni-Mcp-Token"];
            string authorization = request.Headers["Authorization"];
            if (!string.IsNullOrWhiteSpace(authorization)
                && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                provided = authorization.Substring("Bearer ".Length).Trim();
            }

            if (SlowEquals(provided ?? "", expected))
                return true;

            response.Headers["WWW-Authenticate"] = "Bearer realm=\"OniMcp\"";
            SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Unauthorized MCP request"), 401);
            return false;
        }

        private static bool SlowEquals(string left, string right)
        {
            if (left == null)
                left = "";
            if (right == null)
                right = "";

            int diff = left.Length ^ right.Length;
            int max = Math.Max(left.Length, right.Length);
            for (int i = 0; i < max; i++)
            {
                char a = i < left.Length ? left[i] : '\0';
                char b = i < right.Length ? right[i] : '\0';
                diff |= a ^ b;
            }
            return diff == 0;
        }

        private void DispatchPostResponse(HttpListenerResponse response, JsonRpcRequest rpcRequest, string sessionId)
        {
            MainThreadBridge.Enqueue(new System.Action(() =>
            {
                object result = null;
                Exception processEx = null;
                try
                {
                    result = ProcessMethod(rpcRequest, sessionId);
                }
                catch (Exception ex)
                {
                    processEx = ex;
                }

                ThreadPool.QueueUserWorkItem(_ => SendPostResponse(response, rpcRequest.Id, result, processEx, sessionId));
            }));
        }

        private void SendPostResponse(HttpListenerResponse response, object requestId, object result, Exception processEx, string sessionId)
        {
            try
            {
                if (processEx != null)
                {
                    SendJson(response, JsonRpcResponse.MakeError(requestId, McpErrorCode.InternalError, processEx.Message), 200);
                    return;
                }

                SetResponseProtocolVersion(response, sessionId);
                if (result is JsonRpcResponse rpcResponse)
                    SendJson(response, rpcResponse, 200);
                else
                    SendJson(response, JsonRpcResponse.Success(requestId, result), 200);
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning($"[OniMcp] Failed to send MCP response: {ex.GetType().Name}: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch { }
            }
        }

        private void HandleGet(HttpListenerRequest request, HttpListenerResponse response, string sessionId)
        {
            string accept = request.Headers["Accept"] ?? "";
            if (accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) < 0)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "GET requires Accept: text/event-stream for server-initiated MCP messages."), 406);
                return;
            }

            McpSession session;
            lock (_sessionLock)
            {
                if (!_sessions.TryGetValue(sessionId, out session))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Session not found or terminated"), 404);
                    return;
                }
                session.SseConnections++;
            }

            try
            {
                response.StatusCode = 200;
                response.ContentType = "text/event-stream";
                response.SendChunked = true;
                response.KeepAlive = true;
                response.Headers["Cache-Control"] = "no-cache";

                WriteSseComment(response, "connected");

                while (_running && IsSessionActive(sessionId))
                {
                    JObject message;
                    while ((message = session.TryDequeueOutbound()) != null)
                        WriteSseMessage(response, message);

                    session.OutboundSignal.WaitOne(15000);
                    if (_running && IsSessionActive(sessionId))
                        WriteSseComment(response, "keepalive");
                }
            }
            catch (Exception ex)
            {
                OniMcpLog.Debug($"[OniMcp] SSE stream closed for session {sessionId}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                lock (_sessionLock)
                {
                    McpSession current;
                    if (_sessions.TryGetValue(sessionId, out current) && current.SseConnections > 0)
                        current.SseConnections--;
                }
                try { response.Close(); } catch { }
            }
        }

        private void HandleDelete(HttpListenerResponse response, string sessionId)
        {
            lock (_sessionLock)
            {
                if (_sessions.Remove(sessionId))
                {
                    sessionId = sessionId ?? "";
                    _terminatedSessions.Add(sessionId);
                }
            }
            response.StatusCode = 204;
            response.Close();
        }

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

        /// <summary>
        /// 校验非 initialize 请求必须携带有效的 Mcp-Protocol-Version 和 Mcp-Session-Id
        /// </summary>
        private bool ValidateNonInitRequest(HttpListenerResponse response, string sessionId, string protocolVersion)
        {
            if (string.IsNullOrEmpty(protocolVersion))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Missing Mcp-Protocol-Version header"), 400);
                return false;
            }
            if (!IsSupportedProtocolVersion(protocolVersion))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, $"Unsupported protocol version: {protocolVersion}. Supported: {string.Join(", ", SupportedProtocolVersions)}"), 400);
                return false;
            }
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
                if (!string.Equals(session.ProtocolVersion, protocolVersion, StringComparison.Ordinal))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, $"Protocol version mismatch for session. Expected {session.ProtocolVersion}, got {protocolVersion}"), 400);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 在主线程上处理 MCP 方法调用
        /// </summary>
        private object ProcessMethod(JsonRpcRequest request, string sessionId = null)
        {
            using (PushSessionContext(sessionId))
            {
            switch (request.Method)
            {
                case "initialize":
                    return HandleInitialize(request.Id, request.Params?.ToObject<InitializeParams>(), sessionId);

                case "notifications/initialized":
                    return null;

                case "tools/list":
                    return new ListToolsResult { Tools = OniToolRegistry.GetToolInfos() };

                case "tools/call":
                    var callParams = request.Params?.ToObject<CallToolParams>();
                    if (callParams == null || string.IsNullOrEmpty(callParams.Name))
                        return CallToolResult.Error("Missing tool name");
                    if (callParams.Task != null)
                        return CreateToolTask(callParams, sessionId);
                    return OniToolRegistry.CallTool(callParams.Name, callParams.Arguments);

                case "prompts/list":
                    return new ListPromptsResult { Prompts = OniPromptRegistry.GetPromptInfos() };

                case "prompts/get":
                    return HandleGetPrompt(request);

                case "resources/list":
                    return new ListResourcesResult { Resources = OniResourceRegistry.GetResourceInfos() };

                case "resources/templates/list":
                    return new ListResourceTemplatesResult { ResourceTemplates = OniResourceRegistry.GetResourceTemplateInfos() };

                case "resources/read":
                    return HandleReadResource(request);

                case "tasks/list":
                    return HandleListTasks();

                case "tasks/get":
                    return HandleGetTask(request);

                case "tasks/result":
                    return HandleGetTaskResult(request);

                case "tasks/cancel":
                    return HandleCancelTask(request);

                default:
                    return JsonRpcResponse.MakeError(request.Id, McpErrorCode.MethodNotFound, $"Unknown method: {request.Method}");
            }
            }
        }

        private static readonly string[] SupportedProtocolVersions = { CurrentProtocolVersion };
        private const string CurrentProtocolVersion = "2025-11-25";

        private static bool IsSupportedProtocolVersion(string protocolVersion)
        {
            return SupportedProtocolVersions.Any(version => string.Equals(version, protocolVersion, StringComparison.Ordinal));
        }

        private object HandleInitialize(object id, InitializeParams @params, string sessionId)
        {
            string clientVersion = @params?.ProtocolVersion;
            if (string.IsNullOrEmpty(clientVersion))
            {
                return JsonRpcResponse.MakeError(id, McpErrorCode.InvalidParams, "Missing initialize.protocolVersion");
            }

            if (!IsSupportedProtocolVersion(clientVersion))
            {
                return JsonRpcResponse.MakeError(id, McpErrorCode.InvalidRequest,
                    $"Unsupported protocol version: {clientVersion}. Supported: {string.Join(", ", SupportedProtocolVersions)}");
            }

            string negotiatedVersion = clientVersion;
            if (!string.IsNullOrEmpty(sessionId))
            {
                lock (_sessionLock)
                {
                    McpSession session;
                    if (_sessions.TryGetValue(sessionId, out session))
                    {
                        session.ProtocolVersion = negotiatedVersion;
                        session.ClientInfo = @params?.ClientInfo;
                        session.Capabilities = @params?.Capabilities;
                    }
                }
            }

            return new InitializeResult
            {
                ProtocolVersion = negotiatedVersion,
                Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = false },
                    Prompts = new PromptsCapability { ListChanged = false },
                    Resources = new ResourcesCapability { Subscribe = false, ListChanged = false },
                    Tasks = new TasksCapability
                    {
                        Requests = new JObject
                        {
                            ["tools"] = new JObject
                            {
                                ["call"] = new JObject()
                            }
                        }
                    },
                    Experimental = new JObject
                    {
                        ["streamableHttpSse"] = new JObject(),
                        ["serverInitiatedRequests"] = new JObject
                        {
                            ["transport"] = "sse",
                            ["methods"] = new JArray("sampling/createMessage", "notifications/message")
                        }
                    }
                },
                ServerInfo = new Implementation
                {
                    Name = "OniMcp",
                    Version = "0.1.3"
                }
            };
        }

        private object HandleGetPrompt(JsonRpcRequest request)
        {
            var @params = request.Params?.ToObject<GetPromptParams>();
            if (@params == null || string.IsNullOrEmpty(@params.Name))
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, "Missing prompt name");

            var prompt = OniPromptRegistry.GetPrompt(@params.Name, @params.Arguments);
            if (prompt == null)
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, $"Prompt not found: {@params.Name}");

            return prompt;
        }

        private object HandleReadResource(JsonRpcRequest request)
        {
            var @params = request.Params?.ToObject<ReadResourceParams>();
            if (@params == null || string.IsNullOrEmpty(@params.Uri))
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, "Missing resource uri");

            var result = OniResourceRegistry.ReadResource(@params.Uri);
            if (result == null)
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, $"Resource not found: {@params.Uri}");

            return result;
        }

        private object HandleListTasks()
        {
            lock (_taskLock)
            {
                CleanupExpiredTasks();
                return new ListTasksResult
                {
                    Tasks = _tasks.Values
                        .OrderByDescending(task => task.CreatedAt)
                        .Select(task => task.ToInfo())
                        .ToList()
                };
            }
        }

        private object HandleGetTask(JsonRpcRequest request)
        {
            JsonRpcResponse error;
            var task = FindTask(request, out error);
            if (error != null)
                return error;
            return task.ToInfo();
        }

        private object HandleGetTaskResult(JsonRpcRequest request)
        {
            JsonRpcResponse error;
            var task = FindTask(request, out error);
            if (error != null)
                return error;

            return new TaskResult
            {
                Result = task.Result,
                Error = task.Error,
                Meta = RelatedTaskMeta(task.TaskId)
            };
        }

        private object HandleCancelTask(JsonRpcRequest request)
        {
            JsonRpcResponse error;
            var task = FindTask(request, out error);
            if (error != null)
                return error;

            lock (_taskLock)
            {
                if (task.Status != "completed" && task.Status != "failed" && task.Status != "cancelled")
                {
                    task.CancelRequested = true;
                    task.Status = "cancelled";
                    task.StatusMessage = "Task cancelled";
                    task.LastUpdatedAt = System.DateTime.UtcNow;
                }
                return task.ToInfo();
            }
        }

        private McpTaskEntry FindTask(JsonRpcRequest request, out JsonRpcResponse error)
        {
            error = null;
            var @params = request.Params?.ToObject<TaskIdParams>();
            if (@params == null || string.IsNullOrEmpty(@params.TaskId))
            {
                error = JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, "Missing taskId");
                return null;
            }

            lock (_taskLock)
            {
                CleanupExpiredTasks();
                McpTaskEntry task;
                if (!_tasks.TryGetValue(@params.TaskId, out task))
                {
                    error = JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, $"Task not found: {@params.TaskId}");
                    return null;
                }
                return task;
            }
        }

        private CreateTaskResult CreateToolTask(CallToolParams callParams, string sessionId)
        {
            var task = new McpTaskEntry
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = "working",
                StatusMessage = string.IsNullOrEmpty(callParams.Task.Title) ? $"Calling tool {callParams.Name}" : callParams.Task.Title,
                CreatedAt = System.DateTime.UtcNow,
                LastUpdatedAt = System.DateTime.UtcNow,
                Method = "tools/call",
                Target = callParams.Name,
                Metadata = callParams.Task.Metadata,
                TtlMilliseconds = NormalizeTaskTtl(callParams.Task.Ttl)
            };

            lock (_taskLock)
            {
                CleanupExpiredTasks();
                _tasks[task.TaskId] = task;
            }

            ExecuteToolTask(task.TaskId, callParams.Name, callParams.Arguments, sessionId);
            return new CreateTaskResult { Task = task.ToInfo(), Meta = RelatedTaskMeta(task.TaskId) };
        }

        private void ExecuteToolTask(string taskId, string toolName, JObject arguments, string sessionId)
        {
            MainThreadBridge.EnqueueDeferred(new System.Action(() =>
            {
                McpTaskEntry task;
                lock (_taskLock)
                {
                    if (!_tasks.TryGetValue(taskId, out task) || task.CancelRequested)
                        return;
                    task.Status = "working";
                    task.StatusMessage = $"Calling tool {toolName}";
                    task.LastUpdatedAt = System.DateTime.UtcNow;
                }

                try
                {
                    CallToolResult result;
                    using (PushSessionContext(sessionId))
                    {
                        result = OniToolRegistry.CallTool(toolName, arguments);
                    }
                    lock (_taskLock)
                    {
                        if (task.CancelRequested)
                        {
                            task.Status = "cancelled";
                            task.StatusMessage = "Task cancelled";
                        }
                        else if (result != null && result.IsError)
                        {
                            task.Status = "failed";
                            task.StatusMessage = "Tool returned an error";
                            task.Error = ExtractToolText(result);
                            task.Result = result;
                        }
                        else
                        {
                            task.Status = "completed";
                            task.StatusMessage = "Task completed";
                            task.Result = result;
                        }
                        task.LastUpdatedAt = System.DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    lock (_taskLock)
                    {
                        task.Status = "failed";
                        task.StatusMessage = "Task failed";
                        task.Error = ex.Message;
                        task.LastUpdatedAt = System.DateTime.UtcNow;
                    }
                }
            }));
        }

        private static string ExtractToolText(CallToolResult result)
        {
            if (result == null || result.Content == null)
                return "";
            return string.Join("\n", result.Content.Where(content => content != null).Select(content => content.Text ?? "").ToArray());
        }

        private void CleanupExpiredTasks()
        {
            var expired = _tasks.Values
                .Where(task => task.LastUpdatedAt.AddMilliseconds(task.TtlMilliseconds) < System.DateTime.UtcNow && (task.Status == "completed" || task.Status == "failed" || task.Status == "cancelled"))
                .Select(task => task.TaskId)
                .ToList();
            foreach (var taskId in expired)
                _tasks.Remove(taskId);
        }

        private static int NormalizeTaskTtl(int? ttl)
        {
            if (!ttl.HasValue)
                return TaskTtlMilliseconds;
            return Math.Max(TaskPollIntervalMilliseconds, Math.Min(ttl.Value, TaskTtlMilliseconds));
        }

        private static JObject RelatedTaskMeta(string taskId)
        {
            return new JObject
            {
                ["related-task"] = new JObject
                {
                    ["taskId"] = taskId
                }
            };
        }

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
    }

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
