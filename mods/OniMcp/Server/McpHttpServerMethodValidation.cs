using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool IsTaskMetadataMethod(string method)
        {
            return method == "tasks/list" || method == "tasks/get"
                || method == "tasks/result" || method == "tasks/cancel";
        }

        // Validate protocol envelopes before they consume Unity queue capacity.
        // Domain-specific gameplay arguments remain the tool handler's contract.
        private static JsonRpcResponse ValidateMethodParameters(JsonRpcRequest request)
        {
            var args = request.Params;
            string field = null;
            string expected = null;
            switch (request.Method)
            {
                case "initialize":
                    if (!OptionalType(args, "capabilities", JTokenType.Object))
                    { field = "capabilities"; expected = "an object"; }
                    else if (!OptionalType(args, "clientInfo", JTokenType.Object))
                    { field = "clientInfo"; expected = "an object"; }
                    break;
                case "tools/call":
                    if (!RequiredString(args, "name"))
                    { field = "name"; expected = "a nonempty string"; }
                    else if (!OptionalType(args, "arguments", JTokenType.Object))
                    { field = "arguments"; expected = "an object"; }
                    else if (!OptionalType(args, "task", JTokenType.Object))
                    { field = "task"; expected = "an object"; }
                    else if (args?["task"] is JObject task)
                    {
                        if (!OptionalType(task, "title", JTokenType.String))
                        { field = "task.title"; expected = "a string"; }
                        else if (!OptionalType(task, "metadata", JTokenType.Object))
                        { field = "task.metadata"; expected = "an object"; }
                        else if (task["ttl"] != null && task["ttl"].Type != JTokenType.Null
                            && (task["ttl"].Type != JTokenType.Integer
                                || !int.TryParse(task["ttl"].ToString(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out _)))
                        { field = "task.ttl"; expected = "a 32-bit integer"; }
                    }
                    break;
                case "resources/read":
                    if (!RequiredString(args, "uri"))
                    { field = "uri"; expected = "a nonempty string"; }
                    break;
                case "tasks/get":
                case "tasks/result":
                case "tasks/cancel":
                    if (!RequiredString(args, "taskId"))
                    { field = "taskId"; expected = "a nonempty string"; }
                    break;
            }
            return field == null ? null : JsonRpcResponse.MakeError(
                request.Id, McpErrorCode.InvalidParams,
                request.Method + " requires params." + field + " to be " + expected,
                new JObject { ["reasonCode"] = "invalid_params", ["field"] = "params." + field });
        }

        private static bool RequiredString(JObject args, string name)
        {
            return args?[name]?.Type == JTokenType.String
                && !string.IsNullOrWhiteSpace((string)args[name]);
        }

        private static bool OptionalType(JObject args, string name, JTokenType type)
        {
            var value = args?[name];
            return value == null || value.Type == JTokenType.Null || value.Type == type;
        }
    }
}
