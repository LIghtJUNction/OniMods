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
            var meta = rawMessage["params"]?["_meta"] as JObject;
            string metaVersion = meta?["io.modelcontextprotocol/protocolVersion"]?.Type == JTokenType.String
                ? (string)meta["io.modelcontextprotocol/protocolVersion"]
                : null;

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

            var methodToken = rawMessage["method"];
            if (methodToken?.Type != JTokenType.String)
            {
                SendJson(response, JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidRequest,
                    "Missing or invalid JSON-RPC method"), 200);
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

            bool isNotification = rawMessage.Property("id") == null;
            if (isNotification)
            {
                MainThreadBridge.Enqueue(new System.Action(() =>
                {
                    if (_running)
                        ProcessModernMethod(rpcRequest);
                }));
                response.StatusCode = 202;
                response.ContentLength64 = 0;
                response.Close();
                return true;
            }

            DispatchModernPostResponse(response, rpcRequest);
            return true;
        }

        private static bool ValidateModernRequest(HttpListenerRequest httpRequest, JObject rawMessage, string method,
            string protocolVersion, JObject meta, string metaVersion, out JsonRpcResponse error)
        {
            error = null;
            if (!string.Equals(protocolVersion, ModernProtocolVersion, StringComparison.Ordinal))
            {
                error = HeaderMismatch(rawMessage["id"],
                    $"Mcp-Protocol-Version must be {ModernProtocolVersion} for a modern request");
                return false;
            }

            if (string.IsNullOrEmpty(metaVersion))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "Modern requests require params._meta.io.modelcontextprotocol/protocolVersion");
                return false;
            }

            if (!string.Equals(metaVersion, ModernProtocolVersion, StringComparison.Ordinal))
            {
                error = UnsupportedProtocolVersion(rawMessage["id"], metaVersion);
                return false;
            }

            if (meta?["io.modelcontextprotocol/clientCapabilities"]?.Type != JTokenType.Object)
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "Modern requests require params._meta.io.modelcontextprotocol/clientCapabilities");
                return false;
            }

            var clientInfo = meta["io.modelcontextprotocol/clientInfo"];
            if (clientInfo != null && clientInfo.Type != JTokenType.Object)
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "params._meta.io.modelcontextprotocol/clientInfo must be an object when provided");
                return false;
            }

            string methodHeader = httpRequest.Headers["Mcp-Method"];
            if (string.IsNullOrEmpty(methodHeader) || !string.Equals(methodHeader, method, StringComparison.Ordinal))
            {
                error = HeaderMismatch(rawMessage["id"],
                    $"Mcp-Method header must match JSON-RPC method '{method}'");
                return false;
            }

            string expectedName = ModernPrincipalName(method, rawMessage["params"] as JObject);
            string nameHeader = httpRequest.Headers["Mcp-Name"];
            if (expectedName != null)
            {
                if (string.IsNullOrEmpty(nameHeader))
                {
                    error = HeaderMismatch(rawMessage["id"],
                        $"Mcp-Name header must match request principal '{expectedName}'");
                    return false;
                }

                string decodedNameHeader;
                if (!TryDecodeModernHeaderValue(nameHeader, out decodedNameHeader))
                {
                    error = HeaderMismatch(rawMessage["id"], "Mcp-Name contains invalid Base64 or UTF-8 encoding");
                    return false;
                }

                if (!string.Equals(decodedNameHeader, expectedName, StringComparison.Ordinal))
                {
                    error = HeaderMismatch(rawMessage["id"],
                        $"Mcp-Name header must match request principal '{expectedName}'");
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(nameHeader))
            {
                error = HeaderMismatch(rawMessage["id"],
                    $"Mcp-Name is not valid for method '{method}'");
                return false;
            }

            if (string.Equals(method, "tools/call", StringComparison.Ordinal)
                && !ValidateModernToolParameterHeaders(httpRequest, rawMessage, out error))
            {
                return false;
            }

            return true;
        }

        private static bool TryDecodeModernHeaderValue(string headerValue, out string decodedValue)
        {
            decodedValue = headerValue;
            if (string.IsNullOrEmpty(headerValue)
                || !headerValue.StartsWith(Base64HeaderPrefix, StringComparison.Ordinal)
                || !headerValue.EndsWith(Base64HeaderSuffix, StringComparison.Ordinal))
            {
                return true;
            }

            int payloadLength = headerValue.Length - Base64HeaderPrefix.Length - Base64HeaderSuffix.Length;
            if (payloadLength < 0)
                return false;

            string payload = headerValue.Substring(Base64HeaderPrefix.Length, payloadLength);
            try
            {
                decodedValue = StrictUtf8.GetString(Convert.FromBase64String(payload));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static JsonRpcResponse HeaderMismatch(object id, string message)
        {
            return JsonRpcResponse.MakeError(id, HeaderMismatchErrorCode, message);
        }

        private static JsonRpcResponse UnsupportedProtocolVersion(object id, string requestedVersion)
        {
            return JsonRpcResponse.MakeError(id, UnsupportedProtocolVersionErrorCode,
                $"Unsupported protocol version: {requestedVersion}", new JObject
                {
                    ["requested"] = requestedVersion,
                    ["supported"] = BuildSupportedProtocolVersions()
                });
        }

        private static JArray BuildSupportedProtocolVersions()
        {
            return new JArray(ModernProtocolVersion, CurrentProtocolVersion, LegacyProtocolVersion);
        }

        private static string ModernPrincipalName(string method, JObject @params)
        {
            if (@params == null)
                return null;
            switch (method)
            {
                case "resources/read":
                    return @params["uri"]?.Type == JTokenType.String ? (string)@params["uri"] : null;
                case "tools/call":
                case "prompts/get":
                    return @params["name"]?.Type == JTokenType.String ? (string)@params["name"] : null;
                default:
                    return null;
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
                    return CompleteModernResult(new JObject
                    {
                        ["resources"] = JArray.FromObject(OniResourceRegistry.GetResourceInfos()
                            .OrderBy(item => item.Uri, StringComparer.Ordinal))
                    });

                case "resources/templates/list":
                    return CompleteModernResult(new JObject
                    {
                        ["resourceTemplates"] = JArray.FromObject(OniResourceRegistry.GetResourceTemplateInfos()
                            .OrderBy(item => item.UriTemplate, StringComparer.Ordinal))
                    });

                case "resources/read":
                    var @params = request.Params?.ToObject<ReadResourceParams>();
                    if (@params == null || string.IsNullOrEmpty(@params.Uri))
                        return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams, "Missing resource uri");
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
            result.Add(modernToolInfo);
            return result;
        }

        private static object CallModernReadOnlyTool(JsonRpcRequest request)
        {
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

            var toolResult = OniToolRegistry.CallTool(@params.Name, @params.Arguments);
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

        private void DispatchModernPostResponse(HttpListenerResponse response, JsonRpcRequest rpcRequest)
        {
            MainThreadBridge.Enqueue(new System.Action(() =>
            {
                object result = null;
                Exception processEx = null;
                try
                {
                    result = _running
                        ? ProcessModernMethod(rpcRequest)
                        : JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InternalError, "MCP server is stopping");
                }
                catch (Exception ex)
                {
                    processEx = ex;
                }

                ThreadPool.QueueUserWorkItem(_ => SendModernPostResponse(response, rpcRequest.Id, result, processEx));
            }));
        }

        private void SendModernPostResponse(HttpListenerResponse response, object requestId, object result,
            Exception processEx)
        {
            try
            {
                response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                if (processEx != null)
                {
                    SendJson(response, JsonRpcResponse.MakeError(requestId, McpErrorCode.InternalError,
                        processEx.Message), 200);
                    return;
                }

                if (result is JsonRpcResponse rpcResponse)
                {
                    int status = rpcResponse.Error?.Code == McpErrorCode.MethodNotFound ? 404 : 200;
                    SendJson(response, rpcResponse, status);
                }
                else
                    SendJson(response, JsonRpcResponse.Success(requestId, result), 200);
            }
            catch (Exception ex)
            {
                OniMcp.Support.OniMcpLog.Warning($"[OniMcp] Failed to send modern MCP response: {ex.GetType().Name}: {ex.Message}");
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