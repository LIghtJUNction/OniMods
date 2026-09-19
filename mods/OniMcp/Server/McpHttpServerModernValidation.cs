using System;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool ValidateModernRequest(HttpListenerRequest httpRequest, JObject rawMessage, string method,
            string protocolVersion, JObject meta, string metaVersion, out JsonRpcResponse error)
        {
            error = null;
            if (rawMessage.Property("id") != null && !IsValidModernRequestId(rawMessage["id"]))
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest,
                    "Modern request id must be a string or integer");
                return false;
            }

            // Notifications use NotificationParams / NotificationMetaObject, not RequestParams.
            // The 2026 HTTP standard-header presence requirements are request-only; a notification
            // can be routed by either the protocol header or an explicit body protocol claim.
            if (rawMessage.Property("id") == null)
                return ValidateModernNotification(httpRequest, rawMessage, method, protocolVersion, meta, metaVersion,
                    out error);

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

            if (!string.Equals(metaVersion, protocolVersion, StringComparison.Ordinal))
            {
                error = HeaderMismatch(rawMessage["id"],
                    $"Mcp-Protocol-Version '{protocolVersion}' must match params._meta.io.modelcontextprotocol/protocolVersion '{metaVersion}'");
                return false;
            }

            if (meta?["io.modelcontextprotocol/clientCapabilities"]?.Type != JTokenType.Object)
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "Modern requests require params._meta.io.modelcontextprotocol/clientCapabilities");
                return false;
            }

            var clientInfo = meta["io.modelcontextprotocol/clientInfo"];
            if (clientInfo != null)
            {
                var clientInfoObject = clientInfo as JObject;
                if (clientInfoObject == null
                    || clientInfoObject["name"]?.Type != JTokenType.String
                    || clientInfoObject["version"]?.Type != JTokenType.String)
                {
                    error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                        "params._meta.io.modelcontextprotocol/clientInfo must contain string name and version when provided");
                    return false;
                }
            }

            string methodHeader = httpRequest.Headers["Mcp-Method"];
            if (string.IsNullOrEmpty(methodHeader) || !string.Equals(methodHeader, method, StringComparison.Ordinal))
            {
                error = HeaderMismatch(rawMessage["id"],
                    $"Mcp-Method header must match JSON-RPC method '{method}'");
                return false;
            }

            bool nameHeaderRequired = RequiresModernNameHeader(method);
            string expectedName = ModernPrincipalName(method, rawMessage["params"] as JObject);
            string nameHeader = httpRequest.Headers["Mcp-Name"];
            if (nameHeaderRequired && string.IsNullOrEmpty(nameHeader))
            {
                error = HeaderMismatch(rawMessage["id"], $"Mcp-Name header is required for method '{method}'");
                return false;
            }

            if (expectedName != null)
            {
                string decodedNameHeader;
                if (!TryDecodeModernNameHeaderValue(nameHeader, out decodedNameHeader))
                {
                    error = HeaderMismatch(rawMessage["id"],
                        "Mcp-Name must use safe plain ASCII or valid MCP Base64 UTF-8 encoding");
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
                error = HeaderMismatch(rawMessage["id"], nameHeaderRequired
                    ? $"Mcp-Name header has no matching string request principal for method '{method}'"
                    : $"Mcp-Name is not valid for method '{method}'");
                return false;
            }

            if (string.Equals(method, "tools/call", StringComparison.Ordinal)
                && !ValidateModernToolParameterHeaders(httpRequest, rawMessage, out error))
            {
                return false;
            }

            return true;
        }

        private static bool ValidateModernNotification(HttpListenerRequest httpRequest, JObject rawMessage,
            string method, string protocolVersion, JObject meta, string metaVersion, out JsonRpcResponse error)
        {
            error = null;

            var metaVersionToken = meta?["io.modelcontextprotocol/protocolVersion"];
            if (metaVersionToken != null && metaVersionToken.Type != JTokenType.String)
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidParams,
                    "Notification protocol version claim must be a string when provided");
                return false;
            }

            if (!string.IsNullOrEmpty(protocolVersion)
                && !string.IsNullOrEmpty(metaVersion)
                && !string.Equals(protocolVersion, metaVersion, StringComparison.Ordinal))
            {
                error = HeaderMismatch(null,
                    $"Mcp-Protocol-Version '{protocolVersion}' must match notification protocol version claim '{metaVersion}'");
                return false;
            }

            string methodHeader = httpRequest.Headers["Mcp-Method"];
            if (!string.IsNullOrEmpty(methodHeader)
                && !string.Equals(methodHeader, method, StringComparison.Ordinal))
            {
                error = HeaderMismatch(null,
                    $"Mcp-Method header must match JSON-RPC notification method '{method}'");
                return false;
            }

            return true;
        }

        private static bool IsValidModernRequestId(JToken requestId)
        {
            return requestId != null
                && (requestId.Type == JTokenType.String || requestId.Type == JTokenType.Integer);
        }

        private static bool RequiresModernNameHeader(string method)
        {
            return string.Equals(method, "tools/call", StringComparison.Ordinal)
                || string.Equals(method, "resources/read", StringComparison.Ordinal)
                || string.Equals(method, "prompts/get", StringComparison.Ordinal);
        }

        private static bool TryDecodeModernNameHeaderValue(string headerValue, out string decodedValue)
        {
            decodedValue = headerValue;
            if (string.IsNullOrEmpty(headerValue))
                return false;

            bool encoded = headerValue.StartsWith(Base64HeaderPrefix, StringComparison.Ordinal)
                && headerValue.EndsWith(Base64HeaderSuffix, StringComparison.Ordinal);
            if (!encoded && !IsSafePlainModernHeaderValue(headerValue))
                return false;

            return TryDecodeModernHeaderValue(headerValue, out decodedValue);
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
            return new JArray(ModernProtocolVersion);
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
    }
}
