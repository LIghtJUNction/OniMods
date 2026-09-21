using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool ValidateModernInputResponseRequestParams(string method, JObject parameters,
            out string errorMessage)
        {
            errorMessage = null;
            if (!string.Equals(method, "tools/call", StringComparison.Ordinal)
                && !string.Equals(method, "resources/read", StringComparison.Ordinal))
            {
                return true;
            }

            var requestState = parameters?.Property("requestState");
            if (requestState != null && requestState.Value.Type != JTokenType.String)
            {
                errorMessage = "params.requestState must be a string when provided";
                return false;
            }

            var inputResponses = parameters?.Property("inputResponses");
            if (inputResponses != null && inputResponses.Value.Type != JTokenType.Object)
            {
                errorMessage = "params.inputResponses must be an object when provided";
                return false;
            }

            var inputResponsesObject = inputResponses?.Value as JObject;
            if (inputResponsesObject != null)
            {
                foreach (var response in inputResponsesObject.Properties())
                {
                    var responseObject = response.Value as JObject;
                    if (responseObject == null)
                    {
                        errorMessage = "params.inputResponses values must be objects";
                        return false;
                    }

                    if (!IsModernInputResponseShape(responseObject))
                    {
                        errorMessage = "params.inputResponses values must match an MCP InputResponse shape";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsModernInputResponseShape(JObject response)
        {
            return IsModernElicitResponse(response)
                || IsModernListRootsResponse(response)
                || IsModernCreateMessageResponse(response);
        }

        private static bool IsModernElicitResponse(JObject response)
        {
            var action = response["action"];
            if (action?.Type != JTokenType.String)
                return false;

            string value = (string)action;
            bool validAction = string.Equals(value, "accept", StringComparison.Ordinal)
                || string.Equals(value, "decline", StringComparison.Ordinal)
                || string.Equals(value, "cancel", StringComparison.Ordinal);
            if (!validAction)
                return false;

            var content = response["content"];
            if (content == null)
                return true;

            var contentObject = content as JObject;
            if (contentObject == null)
                return false;

            foreach (var field in contentObject.Properties())
            {
                if (field.Value.Type == JTokenType.String
                    || field.Value.Type == JTokenType.Integer
                    || field.Value.Type == JTokenType.Boolean)
                {
                    continue;
                }

                var selections = field.Value as JArray;
                if (selections == null)
                    return false;

                foreach (var selection in selections)
                {
                    if (selection.Type != JTokenType.String)
                        return false;
                }
            }

            return true;
        }

        private static bool IsModernListRootsResponse(JObject response)
        {
            var roots = response["roots"] as JArray;
            if (roots == null)
                return false;

            foreach (var rootToken in roots)
            {
                var root = rootToken as JObject;
                if (root == null)
                    return false;

                var uriToken = root["uri"];
                if (uriToken?.Type != JTokenType.String)
                    return false;

                string uri = (string)uriToken;
                Uri parsedUri;
                if (string.IsNullOrEmpty(uri)
                    || !uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    || !Uri.TryCreate(uri, UriKind.Absolute, out parsedUri)
                    || !string.Equals(parsedUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var name = root["name"];
                if (name != null && name.Type != JTokenType.String)
                    return false;
            }

            return true;
        }

        private static bool IsModernCreateMessageResponse(JObject response)
        {
            var role = response["role"];
            var content = response["content"];
            var model = response["model"];
            if (role?.Type != JTokenType.String || model?.Type != JTokenType.String || content == null)
                return false;

            string roleValue = (string)role;
            bool validRole = string.Equals(roleValue, "user", StringComparison.Ordinal)
                || string.Equals(roleValue, "assistant", StringComparison.Ordinal);
            return validRole
                && IsModernSamplingContent(content)
                && IsOptionalString(response, "stopReason")
                && IsOptionalObject(response, "_meta");
        }

        private static bool IsModernSamplingContent(JToken content)
        {
            var block = content as JObject;
            if (block != null)
                return IsModernSamplingContentBlock(block);

            var blocks = content as JArray;
            if (blocks == null)
                return false;

            foreach (var item in blocks)
            {
                var itemBlock = item as JObject;
                if (itemBlock == null || !IsModernSamplingContentBlock(itemBlock))
                    return false;
            }

            return true;
        }

        private static bool IsModernSamplingContentBlock(JObject block)
        {
            var type = block["type"];
            if (type?.Type != JTokenType.String)
                return false;

            string value = (string)type;
            if (string.Equals(value, "text", StringComparison.Ordinal))
            {
                return block["text"]?.Type == JTokenType.String
                    && HasValidModernContentMetadata(block);
            }

            if (string.Equals(value, "image", StringComparison.Ordinal)
                || string.Equals(value, "audio", StringComparison.Ordinal))
            {
                return block["data"]?.Type == JTokenType.String
                    && block["mimeType"]?.Type == JTokenType.String
                    && HasValidModernContentMetadata(block);
            }

            if (string.Equals(value, "tool_use", StringComparison.Ordinal))
            {
                return block["id"]?.Type == JTokenType.String
                    && block["name"]?.Type == JTokenType.String
                    && block["input"]?.Type == JTokenType.Object
                    && IsOptionalObject(block, "_meta");
            }

            if (!string.Equals(value, "tool_result", StringComparison.Ordinal))
                return false;

            var toolUseId = block["toolUseId"];
            var resultContent = block["content"] as JArray;
            if (toolUseId?.Type != JTokenType.String || resultContent == null)
                return false;

            var isError = block.Property("isError");
            if (isError != null && isError.Value.Type != JTokenType.Boolean)
                return false;
            if (!IsOptionalObject(block, "_meta"))
                return false;

            foreach (var item in resultContent)
            {
                var contentBlock = item as JObject;
                if (contentBlock == null || !IsModernToolResultContentBlock(contentBlock))
                    return false;
            }

            return true;
        }

        private static bool IsModernToolResultContentBlock(JObject block)
        {
            var type = block["type"];
            if (type?.Type != JTokenType.String)
                return false;

            string value = (string)type;
            if (string.Equals(value, "text", StringComparison.Ordinal))
            {
                return block["text"]?.Type == JTokenType.String
                    && HasValidModernContentMetadata(block);
            }

            if (string.Equals(value, "image", StringComparison.Ordinal)
                || string.Equals(value, "audio", StringComparison.Ordinal))
            {
                return block["data"]?.Type == JTokenType.String
                    && block["mimeType"]?.Type == JTokenType.String
                    && HasValidModernContentMetadata(block);
            }

            if (string.Equals(value, "resource_link", StringComparison.Ordinal))
            {
                return block["name"]?.Type == JTokenType.String
                    && block["uri"]?.Type == JTokenType.String
                    && IsOptionalString(block, "title")
                    && IsOptionalString(block, "description")
                    && IsOptionalString(block, "mimeType")
                    && IsOptionalNumber(block, "size")
                    && IsOptionalModernAnnotations(block, "annotations")
                    && IsOptionalModernIcons(block, "icons")
                    && IsOptionalObject(block, "_meta");
            }

            if (!string.Equals(value, "resource", StringComparison.Ordinal))
                return false;

            if (!IsOptionalModernAnnotations(block, "annotations") || !IsOptionalObject(block, "_meta"))
                return false;

            var resource = block["resource"] as JObject;
            if (resource == null
                || resource["uri"]?.Type != JTokenType.String
                || !IsOptionalString(resource, "mimeType")
                || !IsOptionalObject(resource, "_meta"))
            {
                return false;
            }

            var text = resource["text"];
            var blob = resource["blob"];
            bool hasText = text?.Type == JTokenType.String;
            bool hasBlob = blob?.Type == JTokenType.String;
            return hasText != hasBlob;
        }

        private static bool HasValidModernContentMetadata(JObject block)
        {
            return IsOptionalModernAnnotations(block, "annotations")
                && IsOptionalObject(block, "_meta");
        }

        private static bool IsOptionalString(JObject value, string propertyName)
        {
            var property = value.Property(propertyName);
            return property == null || property.Value.Type == JTokenType.String;
        }

        private static bool IsOptionalNumber(JObject value, string propertyName)
        {
            var property = value.Property(propertyName);
            return property == null
                || property.Value.Type == JTokenType.Integer
                || property.Value.Type == JTokenType.Float;
        }

        private static bool IsOptionalObject(JObject value, string propertyName)
        {
            var property = value.Property(propertyName);
            return property == null || property.Value.Type == JTokenType.Object;
        }

        private static bool IsOptionalModernAnnotations(JObject value, string propertyName)
        {
            var property = value.Property(propertyName);
            if (property == null)
                return true;

            var annotations = property.Value as JObject;
            if (annotations == null)
                return false;

            var audienceProperty = annotations.Property("audience");
            if (audienceProperty != null)
            {
                var audience = audienceProperty.Value as JArray;
                if (audience == null)
                    return false;

                foreach (var roleToken in audience)
                {
                    if (roleToken.Type != JTokenType.String)
                        return false;

                    string role = (string)roleToken;
                    if (!string.Equals(role, "user", StringComparison.Ordinal)
                        && !string.Equals(role, "assistant", StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            var priorityProperty = annotations.Property("priority");
            if (priorityProperty != null)
            {
                if (priorityProperty.Value.Type != JTokenType.Integer
                    && priorityProperty.Value.Type != JTokenType.Float)
                {
                    return false;
                }

                double priority = priorityProperty.Value.Value<double>();
                if (double.IsNaN(priority) || double.IsInfinity(priority)
                    || priority < 0d || priority > 1d)
                {
                    return false;
                }
            }

            var lastModifiedProperty = annotations.Property("lastModified");
            return lastModifiedProperty == null
                || lastModifiedProperty.Value.Type == JTokenType.String
                || lastModifiedProperty.Value.Type == JTokenType.Date;
        }

        private static bool IsOptionalModernIcons(JObject value, string propertyName)
        {
            var property = value.Property(propertyName);
            if (property == null)
                return true;

            var icons = property.Value as JArray;
            if (icons == null)
                return false;

            foreach (var iconToken in icons)
            {
                var icon = iconToken as JObject;
                if (icon == null
                    || icon["src"]?.Type != JTokenType.String
                    || !IsOptionalString(icon, "mimeType")
                    || !IsOptionalStringArray(icon, "sizes")
                    || !IsOptionalIconTheme(icon, "theme"))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsOptionalStringArray(JObject value, string propertyName)
        {
            var property = value.Property(propertyName);
            if (property == null)
                return true;

            var items = property.Value as JArray;
            if (items == null)
                return false;

            foreach (var item in items)
            {
                if (item.Type != JTokenType.String)
                    return false;
            }

            return true;
        }

        private static bool IsOptionalIconTheme(JObject value, string propertyName)
        {
            var property = value.Property(propertyName);
            if (property == null)
                return true;
            if (property.Value.Type != JTokenType.String)
                return false;

            string theme = (string)property.Value;
            return string.Equals(theme, "light", StringComparison.Ordinal)
                || string.Equals(theme, "dark", StringComparison.Ordinal);
        }
    }
}
