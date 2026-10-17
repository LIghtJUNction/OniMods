using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool HasValidModernSamplingMessageComposition(string role, JToken content)
        {
            bool isUser = string.Equals(role, "user", StringComparison.Ordinal);

            var block = content as JObject;
            if (block != null)
                return !isUser || !string.Equals((string)block["type"], "tool_use", StringComparison.Ordinal);

            var blocks = content as JArray;
            if (blocks == null)
                return true;

            bool hasToolResult = false;
            bool hasOtherContent = false;
            foreach (var item in blocks)
            {
                string type = (string)(item as JObject)?["type"];
                if (isUser && string.Equals(type, "tool_use", StringComparison.Ordinal))
                    return false;

                if (string.Equals(type, "tool_result", StringComparison.Ordinal))
                    hasToolResult = true;
                else
                    hasOtherContent = true;
            }

            return !isUser || !hasToolResult || !hasOtherContent;
        }
    }
}
