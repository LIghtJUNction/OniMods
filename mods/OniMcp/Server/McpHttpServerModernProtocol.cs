using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;
using UnityEngine;

namespace OniMcp.Server
{
    /// <summary>
    /// MCP 2026-07-28 stateless compatibility path.
    /// Kept separate from the initialize/session transport so legacy behavior stays unchanged.
    /// </summary>
    public partial class McpHttpServer : MonoBehaviour
    {
        private const string ModernProtocolVersion = "2026-07-28";
        private const string ModernReadOnlyToolName = "benchmark";
        private const int HeaderMismatchErrorCode = -32020;
        private const int UnsupportedProtocolVersionErrorCode = -32022;
        private const string Base64HeaderPrefix = "=?base64?";
        private const string Base64HeaderSuffix = "?=";
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private bool TryHandleModernPost(HttpListenerRequest httpRequest, HttpListenerResponse response,
            JObject rawMessage, string protocolVersion)
        {
            bool explicitModern = string.Equals(protocolVersion, ModernProtocolVersion, StringComparison.Ordinal);
            if (IsSupportedProtocolVersion(protocolVersion))
                return false;

            if (!string.IsNullOrEmpty(protocolVersion) && !explicitModern)
            {
                SendJson(response, UnsupportedProtocolVersion(rawMessage["id"], protocolVersion), 400);
                return true;
            }

            string sessionId = httpRequest.Headers["Mcp-Session-Id"];
            if (!explicitModern && IsSessionActive(sessionId))
                return false;

            if (explicitModern && TryHandleModernCancellationNotification(response, rawMessage))
                return true;

            var paramsToken = rawMessage["params"];
            var paramsObject = paramsToken as JObject;
            if (paramsToken != null && paramsObject == null)
            {
                if (explicitModern)
                {
                    SendJson(response, JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                        "Modern request params must be an object"), 400);
                    return true;
                }

                return false;
            }

            var meta = paramsObject?["_meta"] as JObject;
            string metaVersion = meta?["io.modelcontextprotocol/protocolVersion"]?.Type == JTokenType.String
                ? (string)meta["io.modelcontextprotocol/protocolVersion"]
                : null;

            if (!string.IsNullOrEmpty(metaVersion)
                && !string.Equals(metaVersion, ModernProtocolVersion, StringComparison.Ordinal)
                && !IsSupportedProtocolVersion(metaVersion))
            {
                SendJson(response, UnsupportedProtocolVersion(rawMessage["id"], metaVersion), 400);
                return true;
            }

            bool modernSignal = explicitModern
                || string.Equals(metaVersion, ModernProtocolVersion, StringComparison.Ordinal);
            if (!modernSignal)
                return false;

            // 2026-07-28 does not define routing-header requirements for notification POSTs.
            // Some current clients still send notifications/cancelled over HTTP, so accept the
            // body-level modern signal and preserve the existing acknowledge-and-drop behavior.
            if (!explicitModern && TryHandleModernCancellationNotification(response, rawMessage))
                return true;

            var methodToken = rawMessage["method"];
            if (methodToken?.Type != JTokenType.String)
            {
                SendJson(response, JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidRequest,
                    "Missing or invalid JSON-RPC method"), 400);
                return true;
            }

            string method = (string)methodToken;
            JsonRpcResponse validationError;
            if (!ValidateModernRequest(httpRequest, rawMessage, method, protocolVersion, meta, metaVersion,
                    out validationError))
            {
                SendJson(response, validationError, 400);
                return true;
            }

            bool isNotification = rawMessage.Property("id") == null;
            if (isNotification)
            {
                if (IsModernRequestMethod(method))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest,
                        $"Modern request method '{method}' requires a request id"), 400);
                }
                else
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.MethodNotFound,
                        $"Notification method is not available on the {ModernProtocolVersion} compatibility path: {method}"), 404);
                }
                return true;
            }

            JsonRpcRequest rpcRequest;
            try
            {
                rpcRequest = rawMessage.ToObject<JsonRpcRequest>();
            }
            catch (Exception ex)
            {
                SendJson(response, JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidRequest,
                    $"Invalid modern MCP request: {ex.Message}"), 200);
                return true;
            }

            DispatchModernPostResponse(response, rpcRequest);
            return true;
        }

        private bool TryHandleModernCancellationNotification(HttpListenerResponse response, JObject rawMessage)
        {
            var method = rawMessage["method"];
            if (rawMessage.Property("id") != null
                || method?.Type != JTokenType.String
                || !string.Equals((string)method, "notifications/cancelled", StringComparison.Ordinal))
            {
                return false;
            }

            string cancellationError;
            if (!ValidateModernCancellationNotification(rawMessage["params"], out cancellationError))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest,
                    cancellationError), 400);
                return true;
            }

            // This stateless compatibility path has no request-owned work to cancel.
            // Acknowledge and drop the notification rather than turning it into a 4xx.
            response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
            response.StatusCode = (int)HttpStatusCode.Accepted;
            response.ContentLength64 = 0;
            response.Close();
            return true;
        }

        private static bool ValidateModernCancellationNotification(JToken paramsToken, out string errorMessage)
        {
            var parameters = paramsToken as JObject;
            if (parameters == null)
            {
                errorMessage = "Modern cancellation notification requires object params";
                return false;
            }

            var requestId = parameters["requestId"];
            if (!IsValidModernRequestId(requestId))
            {
                errorMessage = "Modern cancellation notification requires a string or integer requestId";
                return false;
            }

            var reason = parameters["reason"];
            if (reason != null && reason.Type != JTokenType.String)
            {
                errorMessage = "Modern cancellation notification reason must be a string when provided";
                return false;
            }

            var meta = parameters["_meta"];
            if (meta != null && meta.Type != JTokenType.Object)
            {
                errorMessage = "Modern cancellation notification _meta must be an object when provided";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private static bool IsModernRequestMethod(string method)
        {
            switch (method)
            {
                case "server/discover":
                case "tools/list":
                case "tools/call":
                case "resources/list":
                case "resources/templates/list":
                case "resources/read":
                    return true;
                default:
                    return false;
            }
        }

        private object ProcessModernMethod(JsonRpcRequest request)
        {
            switch (request.Method)
            {
                case "server/discover":
                    return BuildModernDiscoveryResult();

                case "tools/list":
                {
                    var toolInfos = BuildModernToolInfos();
                    if (toolInfos.Count == 0)
                        return ModernToolMethodUnavailable(request);
                    return CompleteModernListResult(new JObject { ["tools"] = toolInfos });
                }

                case "tools/call":
                    if (!IsModernReadOnlyToolAvailable())
                        return ModernToolMethodUnavailable(request);
                    return CallModernReadOnlyTool(request);

                case "resources/list":
                    return CompleteModernListResult(new JObject
                    {
                        ["resources"] = JArray.FromObject(OniResourceRegistry.GetResourceInfos()
                            .Where(item => IsModernReadOnlyResourceUri(item.Uri))
                            .OrderBy(item => item.Uri, StringComparer.Ordinal))
                    });

                case "resources/templates/list":
                    return CompleteModernListResult(new JObject
                    {
                        ["resourceTemplates"] = JArray.FromObject(OniResourceRegistry.GetResourceTemplateInfos()
                            .Where(item => IsModernReadOnlyResourceTemplate(item.UriTemplate))
                            .OrderBy(item => item.UriTemplate, StringComparer.Ordinal))
                    });

                case "resources/read":
                    var @params = request.Params?.ToObject<ReadResourceParams>();
                    if (@params == null || string.IsNullOrEmpty(@params.Uri))
                        return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, "Missing resource uri");
                    if (!IsModernReadOnlyResourceUri(@params.Uri))
                    {
                        return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams,
                            $"Resource is not available on the {ModernProtocolVersion} read-only path: {@params.Uri}",
                            new JObject { ["uri"] = @params.Uri });
                    }
                    var readResult = OniResourceRegistry.ReadResource(@params.Uri);
                    if (readResult == null)
                        return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams,
                            $"Resource not found: {@params.Uri}", new JObject { ["uri"] = @params.Uri });
                    return CompleteModernResult(JObject.FromObject(readResult));

                default:
                    return JsonRpcResponse.MakeError(request.Id, McpErrorCode.MethodNotFound,
                        $"Method is not available on the {ModernProtocolVersion} compatibility path: {request.Method}");
            }
        }

        private static bool IsModernReadOnlyResourceUri(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                return true;

            string canonicalPath = parsed.AbsolutePath.TrimEnd('/');
            if (string.Equals(parsed.Scheme, "oni", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parsed.Host, "tools", StringComparison.OrdinalIgnoreCase)
                && canonicalPath.StartsWith("/read/", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(parsed.Scheme, "oni", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parsed.Host, "mcp", StringComparison.OrdinalIgnoreCase)
                && string.Equals(canonicalPath, "/sessions", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(parsed.Scheme, "oni", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parsed.Host, "game", StringComparison.OrdinalIgnoreCase)
                && string.Equals(canonicalPath, "/saves", StringComparison.Ordinal))
            {
                return false;
            }

            return !string.Equals(parsed.Scheme, "oni", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(parsed.Host, "world", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(canonicalPath, "/coordinate-screenshot", StringComparison.Ordinal);
        }

        private static bool IsModernReadOnlyResourceTemplate(string uriTemplate)
        {
            return string.IsNullOrEmpty(uriTemplate)
                || (!uriTemplate.StartsWith("oni://world/coordinate-screenshot", StringComparison.Ordinal)
                    && !uriTemplate.StartsWith("oni://tools/read/", StringComparison.Ordinal)
                    && !uriTemplate.StartsWith("oni://game/saves{", StringComparison.Ordinal));
        }
        private static JsonRpcResponse ModernToolMethodUnavailable(JsonRpcRequest request)
        {
            return JsonRpcResponse.MakeError(request.Id, McpErrorCode.MethodNotFound,
                $"Method is not available until a safe modern tool is registered: {request.Method}");
        }

        private static bool IsModernReadOnlyToolAvailable()
        {
            McpToolInfo toolInfo;
            List<ModernToolHeaderBinding> bindings;
            return TryGetModernReadOnlyToolInfo(out toolInfo, out bindings);
        }

        private static JArray BuildModernToolInfos()
        {
            var result = new JArray();
            McpToolInfo toolInfo;
            List<ModernToolHeaderBinding> bindings;
            if (!TryGetModernReadOnlyToolInfo(out toolInfo, out bindings))
                return result;

            var modernToolInfo = JObject.FromObject(toolInfo);
            // `execution.taskSupport` belonged to the 2025 core task model. Tasks moved
            // out of core in 2026, so do not advertise that legacy field here.
            modernToolInfo.Remove("execution");

            var inputSchema = modernToolInfo["inputSchema"] as JObject;
            var properties = inputSchema?["properties"] as JObject;
            var taskProperty = properties?[ToolCallMiddleware.TaskDescriptionParameter] as JObject;
            if (taskProperty != null)
            {
                taskProperty["description"] =
                    "Required for this benchmark call: briefly describe what you are doing. The stateless 2026 path does not display this text in ONI.";
            }

            result.Add(modernToolInfo);
            return result;
        }

        private static object CallModernReadOnlyTool(JsonRpcRequest request)
        {
            var argumentsToken = request.Params?["arguments"];
            if (argumentsToken != null && argumentsToken.Type != JTokenType.Object)
            {
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams,
                    "Tool arguments must be an object when provided");
            }

            var @params = request.Params?.ToObject<CallToolParams>();
            if (@params == null || string.IsNullOrEmpty(@params.Name))
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, "Missing tool name");

            if (!string.Equals(@params.Name, ModernReadOnlyToolName, StringComparison.Ordinal))
            {
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams,
                    $"Tool is not available on the {ModernProtocolVersion} read-only path: {@params.Name}",
                    new JObject { ["name"] = @params.Name });
            }

            if (@params.Task != null)
            {
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams,
                    "2025 task-augmented tool calls are not supported on the stateless 2026 path");
            }

            if (!ToolCallMiddleware.TryGetTaskDescription(@params.Arguments, out _))
            {
                return CompleteModernToolResult(JObject.FromObject(CallToolResult.Error(
                    "task is required: describe what you are doing before every tool call.")));
            }

            if (!OniToolRegistry.IsCoordinateTool(ModernReadOnlyToolName)
                && OniToolRegistry.HasCoordinateArguments(@params.Arguments))
            {
                return CompleteModernToolResult(JObject.FromObject(CallToolResult.Error(
                    "Coordinate arguments are only supported by coordinate_control; use semantic query/target/areaId inputs for this tool.")));
            }

            McpTool tool;
            if (!OniToolRegistry.TryGetTool(ModernReadOnlyToolName, out tool)
                || tool == null || tool.Handler == null
                || !string.Equals(tool.Name, ModernReadOnlyToolName, StringComparison.Ordinal))
            {
                return ModernToolMethodUnavailable(request);
            }

            CallToolResult toolResult;
            try
            {
                toolResult = tool.Handler(@params.Arguments ?? new JObject());
            }
            catch (Exception ex)
            {
                toolResult = CallToolResult.Error($"Tool execution error: {ex.Message}");
            }
            return CompleteModernToolResult(JObject.FromObject(toolResult));
        }

        private static JObject BuildModernDiscoveryResult()
        {
            var capabilities = new JObject
            {
                ["resources"] = new JObject
                {
                    ["subscribe"] = false,
                    ["listChanged"] = false
                }
            };
            if (IsModernReadOnlyToolAvailable())
            {
                capabilities["tools"] = new JObject
                {
                    ["listChanged"] = false
                };
            }

            return new JObject
            {
                ["resultType"] = "complete",
                ["supportedVersions"] = BuildSupportedProtocolVersions(),
                ["capabilities"] = capabilities,
                ["instructions"] = IsModernReadOnlyToolAvailable()
                    ? "This compatibility path exposes stateless ONI resources plus the read-only benchmark tool. Stateful and game-mutating tool calls remain on the 2025 initialize/session path until their request-scoped state and modern header contracts are migrated."
                    : "This compatibility path exposes stateless ONI resource discovery and reads. Tool calls remain on the 2025 initialize/session path until a safe modern tool is registered.",
                ["ttlMs"] = 3600000,
                ["cacheScope"] = "public",
                ["_meta"] = BuildModernServerMeta()
            };
        }

        private static JObject CompleteModernListResult(JObject result)
        {
            result["resultType"] = "complete";
            result["ttlMs"] = 300000;
            result["cacheScope"] = "public";
            result["_meta"] = BuildModernServerMeta();
            return result;
        }

        private static JObject CompleteModernToolResult(JObject result)
        {
            result["resultType"] = "complete";
            result["_meta"] = BuildModernServerMeta();
            return result;
        }

        private static JObject CompleteModernResult(JObject result)
        {
            result["resultType"] = "complete";
            result["ttlMs"] = 0;
            result["cacheScope"] = "private";
            result["_meta"] = BuildModernServerMeta();
            return result;
        }

        private static JObject BuildModernServerMeta()
        {
            return new JObject
            {
                ["io.modelcontextprotocol/serverInfo"] = new JObject
                {
                    ["name"] = "OniMcp",
                    ["version"] = "0.2.3"
                }
            };
        }
    }
}
