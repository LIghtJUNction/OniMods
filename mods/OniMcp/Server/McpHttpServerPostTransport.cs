using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Server
{
    /// <summary>
    /// MCP Streamable HTTP 服务器实现
    /// 基于 System.Net.HttpListener（.NET Framework 内置）
    /// </summary>
    public partial class McpHttpServer : MonoBehaviour
{
        private void HandlePost(HttpListenerRequest request, HttpListenerResponse response, string sessionId, string protocolVersion)
        {
            string body;
            try
            {
                body = HttpRequestBody.Read(request.InputStream, Encoding.UTF8, request.ContentLength64, HttpRequestBody.MaxMcpBytes);
            }
            catch (RequestBodyTooLargeException ex)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, ex.Message), 413);
                return;
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
            catch (JsonException ex)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.ParseError, $"Parse error: {ex.Message}"), 200);
                return;
            }

            var requestId = rawMessage["id"];
            if (rawMessage["jsonrpc"]?.Type != JTokenType.String || (string)rawMessage["jsonrpc"] != "2.0"
                || (requestId != null && requestId.Type != JTokenType.Null && requestId.Type != JTokenType.String
                    && requestId.Type != JTokenType.Integer && requestId.Type != JTokenType.Float))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Invalid JSON-RPC request"), 200);
                return;
            }

            if (rawMessage["method"] == null && (rawMessage["result"] != null || rawMessage["error"] != null))
            {
                if (!ValidateNonInitRequest(response, sessionId, protocolVersion))
                    return;

                HandleClientResponse(rawMessage, sessionId);
                SetResponseSessionId(response, sessionId);
                SetResponseProtocolVersion(response, sessionId);
                response.StatusCode = 202;
                response.ContentLength64 = 0;
                response.Close();
                return;
            }

            if (rawMessage["method"]?.Type != JTokenType.String)
            {
                SendJson(response, JsonRpcResponse.MakeError(requestId, McpErrorCode.InvalidRequest, "Missing or invalid JSON-RPC method"), 200);
                return;
            }

            JsonRpcRequest rpcRequest;
            try
            {
                rpcRequest = rawMessage.ToObject<JsonRpcRequest>();
            }
            catch (JsonException ex)
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
            bool isNotification = rawMessage.Property("id") == null;
            var initializeVersion = rpcRequest.Params?["protocolVersion"];
            if (isInitialize && (isNotification || initializeVersion?.Type != JTokenType.String
                || !IsSupportedProtocolVersion((string)initializeVersion)))
            {
                SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InvalidParams,
                    "initialize requires a request id and a supported protocolVersion"), 200);
                return;
            }
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
            {
                sessionId = EnsureSession(response, sessionId);
                if (sessionId == null)
                    return;
            }

            // 通知（无 id）：返回 202 Accepted
            if (isNotification)
            {
                // 在后台处理通知
                MainThreadBridge.Enqueue(new System.Action(() =>
                {
                    if (_running && IsSessionActive(sessionId))
                        ProcessMethod(rpcRequest, sessionId);
                }));
                SetResponseSessionId(response, sessionId);
                SetResponseProtocolVersion(response, sessionId);
                response.StatusCode = 202;
                response.ContentLength64 = 0;
                response.Close();
                return;
            }

            DispatchPostResponse(response, rpcRequest, sessionId);
        }

        private bool TryValidateCors(HttpListenerRequest request, out string origin)
        {
            origin = request.Headers["Origin"];
            if (string.IsNullOrWhiteSpace(origin))
                return true;

            Uri originUri;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out originUri) || !originUri.IsLoopback)
                return false;

            return true;
        }

        private static void ApplyCorsHeaders(HttpListenerResponse response, string origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return;

            response.Headers["Access-Control-Allow-Origin"] = origin;
            AppendVaryOrigin(response);
        }

        private static void AppendVaryOrigin(HttpListenerResponse response)
        {
            string existingVary = response.Headers["Vary"];
            if (string.IsNullOrEmpty(existingVary))
            {
                response.Headers["Vary"] = "Origin";
                return;
            }

            foreach (var value in existingVary.Split(','))
            {
                if (string.Equals(value.Trim(), "Origin", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            response.Headers["Vary"] = existingVary + ", Origin";
        }

        private bool ValidateAuth(HttpListenerRequest request, HttpListenerResponse response)
        {
            var options = _options ?? OniMcpOptions.Current;
            if (options == null || !options.AuthEnabled)
                return true;

            string expected = options.AuthToken ?? "";
            if (string.IsNullOrWhiteSpace(expected))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest,
                    "Token authentication is enabled but no token is configured"), 401);
                return false;
            }

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
                    result = _running && IsSessionActive(sessionId)
                        ? ProcessMethod(rpcRequest, sessionId)
                        : JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InvalidRequest, "Session not found or terminated");
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

                SetResponseSessionId(response, sessionId);
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
}
}
