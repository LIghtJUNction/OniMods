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

        private const string CurrentProtocolVersion = "2025-11-25";

        private const string LegacyProtocolVersion = "2025-06-18";

        private static readonly string[] SupportedProtocolVersions = { CurrentProtocolVersion, LegacyProtocolVersion };

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
                    Version = "0.2.0"
                }
            };
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
}
}
