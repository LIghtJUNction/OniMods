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
    }
}
