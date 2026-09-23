using System;
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
            if (string.Equals(protocolVersion, ModernProtocolVersion, StringComparison.Ordinal)
                && !IsJsonRequestMediaType(request.ContentType))
            {
                response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest,
                    "MCP 2026-07-28 POST requests require Content-Type: application/json"),
                    (int)HttpStatusCode.UnsupportedMediaType);
                return;
            }

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

            JToken parsedMessage;
            try
            {
                parsedMessage = JToken.Parse(body);
            }
            catch (JsonException ex)
            {
                int statusCode = string.Equals(protocolVersion, ModernProtocolVersion, StringComparison.Ordinal) ? 400 : 200;
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.ParseError, $"Parse error: {ex.Message}"), statusCode);
                return;
            }

            var rawMessage = parsedMessage as JObject;
            if (rawMessage == null)
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest,
                    "JSON-RPC request must be a single object"), 400);
                return;
            }

            var requestId = rawMessage["id"];
            if (rawMessage["jsonrpc"]?.Type != JTokenType.String || (string)rawMessage["jsonrpc"] != "2.0"
                || (requestId != null && requestId.Type != JTokenType.Null && requestId.Type != JTokenType.String
                    && requestId.Type != JTokenType.Integer && requestId.Type != JTokenType.Float))
            {
                int statusCode = string.Equals(protocolVersion, ModernProtocolVersion, StringComparison.Ordinal) ? 400 : 200;
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Invalid JSON-RPC request"), statusCode);
                return;
            }

            // MCP 2026-07-28 is a stateless protocol era. Route it before the
            // initialize/session code so legacy transport semantics remain untouched.
            if (TryHandleModernPost(request, response, rawMessage, protocolVersion))
                return;

            // Both supported 2025 Streamable HTTP revisions require clients to
            // advertise both legal response media types for every POST. Enforce
            // this after modern routing is ruled out but before any legacy session
            // state or Unity/main-thread admission can be consumed.
            if (!AcceptsModernResponseMediaTypes(request))
            {
                SendJson(response, JsonRpcResponse.MakeError(requestId, McpErrorCode.InvalidRequest,
                    "MCP 2025 Streamable HTTP POST requests require Accept to list both application/json and text/event-stream"),
                    (int)HttpStatusCode.NotAcceptable);
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
            bool isPingRequest = rpcRequest.Method == "ping" && !isNotification;
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
                bool transportValid = isPingRequest && string.IsNullOrEmpty(sessionId)
                    ? ValidateInitializeTransport(response, sessionId, protocolVersion)
                    : ValidateNonInitRequest(response, sessionId, protocolVersion);
                if (!transportValid)
                    return;
            }
            else if (!ValidateInitializeTransport(response, sessionId, protocolVersion))
            {
                return;
            }

            if (isPingRequest)
            {
                SetResponseSessionId(response, sessionId);
                SetResponseProtocolVersion(response, sessionId);
                SendJson(response, JsonRpcResponse.Success(rpcRequest.Id, new JObject()), 200);
                return;
            }

            MainThreadHttpAdmissionLease admission;
            if (!TryAcquireMainThreadHttpAdmission(response, rpcRequest.Id, sessionId, false, out admission))
                return;

            if (isInitialize)
            {
                sessionId = EnsureSession(response, sessionId);
                if (sessionId == null)
                {
                    admission.Release();
                    return;
                }
            }

            // 通知（无 id）：返回 202 Accepted
            if (isNotification)
            {
                // 在后台处理通知
                EnqueueAdmittedMainThread(admission, new System.Action(() =>
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

            DispatchPostResponse(response, rpcRequest, sessionId, admission);
        }

        private static bool IsJsonRequestMediaType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            int parameterSeparator = contentType.IndexOf(';');
            string mediaType = parameterSeparator >= 0
                ? contentType.Substring(0, parameterSeparator)
                : contentType;
            return string.Equals(mediaType.Trim(), "application/json", StringComparison.OrdinalIgnoreCase);
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

        private void DispatchPostResponse(HttpListenerResponse response, JsonRpcRequest rpcRequest, string sessionId,
            MainThreadHttpAdmissionLease admission)
        {
            EnqueueAdmittedMainThread(admission, new System.Action(() =>
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
            }), () => CloseStaleHttpResponse(response));
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
