using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using Toon.Format;

namespace OniMcp.Support
{
    /// <summary>Encodes JSON-shaped MCP tool content text as TOON when enabled.</summary>
    public static class McpTextContentEncoder
    {
        private static volatile bool _useToon = true;

        /// <summary>Configure structured tool text as "toon" or "json". Defaults to TOON.</summary>
        public static void Configure(string format)
        {
            _useToon = !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Convert a JSON document to TOON while leaving ordinary text and JSON mode unchanged.</summary>
        public static string EncodeIfJson(string text)
        {
            if (!_useToon || !CouldBeJson(text))
                return text;

            try
            {
                using (var stringReader = new StringReader(text))
                using (var jsonReader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    SupportMultipleContent = false
                })
                {
                    if (!jsonReader.Read())
                        return text;

                    JToken token = JToken.ReadFrom(jsonReader);
                    while (jsonReader.Read())
                    {
                        if (jsonReader.TokenType != JsonToken.Comment)
                            return text;
                    }

                    return ToonEncoder.Encode(ToPlainValue(token));
                }
            }
            catch
            {
                // Formatting must not turn a successful tool call into an MCP failure.
                return text;
            }
        }

        /// <summary>
        /// Formats only text blocks inside a JSON-RPC tool result at the HTTP
        /// response boundary. The protocol envelope and structuredContent stay JSON.
        /// </summary>
        public static object FormatJsonRpcResponse(object response)
        {
            if (!_useToon || response == null)
                return response;

            var rpc = response as JsonRpcResponse;
            if (rpc != null)
            {
                // Catalogs, initialization and protocol errors have no tool text.
                // Avoid serializing their full graphs twice just to discover that.
                if (rpc.Error != null || rpc.Result == null)
                    return response;
                var resultObject = rpc.Result as JObject;
                if (resultObject != null && !(resultObject["content"] is JArray))
                    return response;
                var tool = rpc.Result as CallToolResult;
                if (tool != null && !HasJsonText(tool))
                    return response;
            }

            var existingToken = response as JToken;
            JObject message = existingToken as JObject;
            if (message == null)
            {
                try
                {
                    message = JObject.FromObject(response, JsonSerializer.Create(McpJsonUtil.Settings));
                }
                catch
                {
                    return response;
                }
            }
            if (message["jsonrpc"]?.Type != JTokenType.String
                || !string.Equals((string)message["jsonrpc"], "2.0", StringComparison.Ordinal))
                return response;

            var toolResult = message["result"] as JObject;
            var content = toolResult?["content"] as JArray;
            if (content == null)
                return response;

            if (existingToken != null)
            {
                message = (JObject)message.DeepClone();
                content = (JArray)message["result"]["content"];
            }

            foreach (var block in content)
            {
                var contentBlock = block as JObject;
                if (contentBlock == null
                    || contentBlock["type"]?.Type != JTokenType.String
                    || !string.Equals((string)contentBlock["type"], "text", StringComparison.Ordinal)
                    || contentBlock["text"]?.Type != JTokenType.String)
                    continue;

                contentBlock["text"] = EncodeIfJson((string)contentBlock["text"]);
            }

            return message;
        }

        private static bool HasJsonText(CallToolResult result)
        {
            if (result.Content == null)
                return false;
            foreach (var block in result.Content)
                if (block != null && block.Type == "text" && CouldBeJson(block.Text))
                    return true;
            return false;
        }

        private static bool CouldBeJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            int index = 0;
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            char first = text[index];
            return first == '{' || first == '[' || first == '"' || first == '-'
                || first == 't' || first == 'f' || first == 'n'
                || (first >= '0' && first <= '9');
        }

        private static object ToPlainValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return null;

            var obj = token as JObject;
            if (obj != null)
            {
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var property in obj.Properties())
                    values[property.Name] = ToPlainValue(property.Value);
                return values;
            }

            var array = token as JArray;
            if (array != null)
            {
                var values = new List<object>(array.Count);
                foreach (var item in array)
                    values.Add(ToPlainValue(item));
                return values;
            }

            var value = token as JValue;
            return value != null ? value.Value : token.ToObject<object>();
        }
    }
}
