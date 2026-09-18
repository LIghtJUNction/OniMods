using System;
using System.Globalization;
using System.Net;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private void DispatchModernPostResponse(HttpListenerResponse response, JsonRpcRequest rpcRequest)
        {
            if (!IsModernRequestMethod(rpcRequest.Method))
            {
                response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.MethodNotFound,
                    $"Method is not available on the {ModernProtocolVersion} compatibility path: {rpcRequest.Method}"),
                    (int)HttpStatusCode.NotFound);
                return;
            }

            if (string.Equals(rpcRequest.Method, "tools/call", StringComparison.Ordinal))
            {
                var argumentsToken = rpcRequest.Params?["arguments"];
                if (argumentsToken != null && argumentsToken.Type != JTokenType.Object)
                {
                    response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                    SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InvalidParams,
                        "Tool arguments must be an object when provided"), (int)HttpStatusCode.OK);
                    return;
                }

                var toolNameToken = rpcRequest.Params?["name"];
                if (toolNameToken?.Type == JTokenType.String)
                {
                    string toolName = (string)toolNameToken;
                    if (!string.Equals(toolName, ModernReadOnlyToolName, StringComparison.Ordinal))
                    {
                        response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                        SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InvalidParams,
                            $"Tool is not available on the {ModernProtocolVersion} read-only path: {toolName}",
                            new JObject { ["name"] = toolName }), (int)HttpStatusCode.OK);
                        return;
                    }
                }

                var taskToken = rpcRequest.Params?["task"];
                if (taskToken != null && taskToken.Type != JTokenType.Null)
                {
                    response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                    SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InvalidParams,
                        "2025 task-augmented tool calls are not supported on the stateless 2026 path"),
                        (int)HttpStatusCode.OK);
                    return;
                }

                var argumentsObject = argumentsToken as JObject;
                bool modernToolAvailable = IsModernReadOnlyToolAvailable();
                string taskDescription;
                bool hasTaskDescription = ToolCallMiddleware.TryGetTaskDescription(argumentsObject, out taskDescription);
                if ((taskToken == null || taskToken.Type == JTokenType.Null)
                    && modernToolAvailable
                    && !hasTaskDescription)
                {
                    response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                    var result = CompleteModernToolResult(JObject.FromObject(CallToolResult.Error(
                        "task is required: describe what you are doing before every tool call.")));
                    SendJson(response, JsonRpcResponse.Success(rpcRequest.Id, result), (int)HttpStatusCode.OK);
                    return;
                }

                string benchmarkArgumentError;
                if ((taskToken == null || taskToken.Type == JTokenType.Null)
                    && argumentsObject != null
                    && modernToolAvailable
                    && hasTaskDescription
                    && !TryValidateModernBenchmarkArguments(argumentsObject, out benchmarkArgumentError))
                {
                    response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                    var result = CompleteModernToolResult(JObject.FromObject(CallToolResult.Error(
                        benchmarkArgumentError)));
                    SendJson(response, JsonRpcResponse.Success(rpcRequest.Id, result), (int)HttpStatusCode.OK);
                    return;
                }

                if ((taskToken == null || taskToken.Type == JTokenType.Null)
                    && argumentsObject != null
                    && modernToolAvailable
                    && hasTaskDescription
                    && !OniToolRegistry.IsCoordinateTool(ModernReadOnlyToolName)
                    && OniToolRegistry.HasCoordinateArguments(argumentsObject))
                {
                    response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                    var result = CompleteModernToolResult(JObject.FromObject(CallToolResult.Error(
                        "Coordinate arguments are only supported by coordinate_control; use semantic query/target/areaId inputs for this tool.")));
                    SendJson(response, JsonRpcResponse.Success(rpcRequest.Id, result), (int)HttpStatusCode.OK);
                    return;
                }
            }

            if (string.Equals(rpcRequest.Method, "resources/read", StringComparison.Ordinal))
            {
                var uriToken = rpcRequest.Params?["uri"];
                if (uriToken?.Type == JTokenType.String)
                {
                    string uri = (string)uriToken;
                    Uri parsedUri;
                    if (!Uri.TryCreate(uri, UriKind.Absolute, out parsedUri)
                        || !string.Equals(parsedUri.Scheme, "oni", StringComparison.Ordinal))
                    {
                        response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                        SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InvalidParams,
                            $"Resource not found: {uri}", new JObject { ["uri"] = uri }),
                            (int)HttpStatusCode.OK);
                        return;
                    }

                    if (!IsModernReadOnlyResourceUri(uri))
                    {
                        response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                        SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InvalidParams,
                            $"Resource is not available on the {ModernProtocolVersion} read-only path: {uri}",
                            new JObject { ["uri"] = uri }), (int)HttpStatusCode.OK);
                        return;
                    }
                }
            }

            if (IsModernWorkerSafeRequest(rpcRequest))
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

                SendModernPostResponse(response, rpcRequest.Id, result, processEx);
                return;
            }

            MainThreadHttpAdmissionLease admission;
            if (!TryAcquireMainThreadHttpAdmission(response, rpcRequest.Id, null, true, out admission))
                return;

            EnqueueAdmittedMainThread(admission, new System.Action(() =>
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
            }), () => CloseStaleHttpResponse(response));
        }

        private static bool TryValidateModernBenchmarkArguments(JObject arguments, out string error)
        {
            error = null;

            JToken cases = arguments?["cases"];
            if (cases != null && cases.Type != JTokenType.String)
            {
                error = "cases must be a string when provided";
                return false;
            }
            if (cases?.Type == JTokenType.String && !HasRecognizedModernBenchmarkCase((string)cases))
            {
                error = "cases must contain one or more of: all, toolList, toolLookup, jsonSerialize";
                return false;
            }

            JToken tool = arguments?["tool"];
            if (tool != null && tool.Type != JTokenType.String)
            {
                error = "tool must be a string when provided";
                return false;
            }

            JToken includeDetails = arguments?["includeDetails"];
            if (includeDetails != null && includeDetails.Type != JTokenType.Boolean)
            {
                error = "includeDetails must be a boolean when provided";
                return false;
            }

            if (!TryNormalizeModernBenchmarkIterations(arguments))
            {
                error = "iterations must be an integer from 1 to 5000";
                return false;
            }

            return true;
        }

        private static bool HasRecognizedModernBenchmarkCase(string cases)
        {
            if (string.IsNullOrWhiteSpace(cases))
                return true;

            foreach (string item in cases.Split(','))
            {
                string normalized = item.Trim().ToLowerInvariant();
                if (normalized == "all"
                    || normalized == "toollist"
                    || normalized == "toollookup"
                    || normalized == "lookup"
                    || normalized == "jsonserialize")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryNormalizeModernBenchmarkIterations(JObject arguments)
        {
            JToken token = arguments?["iterations"];
            if (token == null)
                return true;

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                return false;

            decimal value;
            if (!decimal.TryParse(token.ToString(Formatting.None), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value)
                || decimal.Truncate(value) != value
                || value < 1
                || value > 5000)
                return false;

            arguments["iterations"] = (int)value;
            return true;
        }

        private static bool IsModernWorkerSafeRequest(JsonRpcRequest request)
        {
            if (request == null)
                return false;

            if (IsModernMetadataOnlyMethod(request.Method))
                return true;

            // Worker safety is an explicit audit decision. Do not infer it from
            // a tool's Mode/Risk metadata or from membership in the modern allowlist.
            if (!string.Equals(request.Method, "tools/call", StringComparison.Ordinal))
                return false;

            var name = request.Params?["name"];
            return name?.Type == JTokenType.String
                && string.Equals((string)name, "benchmark", StringComparison.Ordinal)
                && string.Equals(ModernReadOnlyToolName, "benchmark", StringComparison.Ordinal);
        }

        private static bool IsModernMetadataOnlyMethod(string method)
        {
            return string.Equals(method, "server/discover", StringComparison.Ordinal)
                || string.Equals(method, "tools/list", StringComparison.Ordinal)
                || string.Equals(method, "resources/list", StringComparison.Ordinal)
                || string.Equals(method, "resources/templates/list", StringComparison.Ordinal);
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
