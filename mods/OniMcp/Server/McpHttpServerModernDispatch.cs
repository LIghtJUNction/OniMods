using System;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

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
                if (taskToken?.Type == JTokenType.Object)
                {
                    response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                    SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InvalidParams,
                        "2025 task-augmented tool calls are not supported on the stateless 2026 path"),
                        (int)HttpStatusCode.OK);
                    return;
                }
            }

            if (string.Equals(rpcRequest.Method, "resources/read", StringComparison.Ordinal))
            {
                var uriToken = rpcRequest.Params?["uri"];
                if (uriToken?.Type == JTokenType.String)
                {
                    string uri = (string)uriToken;
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

            if (IsModernMetadataOnlyMethod(rpcRequest.Method))
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
