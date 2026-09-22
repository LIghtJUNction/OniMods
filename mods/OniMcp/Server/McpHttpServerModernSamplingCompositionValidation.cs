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
                return IsModernSamplingBlockAllowedForRole(isUser, (string)block["type"]);

            var blocks = content as JArray;
            if (blocks == null)
                return true;

            bool hasToolResult = false;
            bool hasOtherContent = false;
            foreach (var item in blocks)
            {
                string type = (string)(item as JObject)?["type"];
                if (!IsModernSamplingBlockAllowedForRole(isUser, type))
                    return false;

                if (string.Equals(type, "tool_result", StringComparison.Ordinal))
                    hasToolResult = true;
                else
                    hasOtherContent = true;
            }

            return !isUser || !hasToolResult || !hasOtherContent;
        }

        private static bool IsModernSamplingBlockAllowedForRole(bool isUser, string type)
        {
            if (string.Equals(type, "tool_use", StringComparison.Ordinal))
                return !isUser;
            if (string.Equals(type, "tool_result", StringComparison.Ordinal))
                return isUser;
            return true;
        }
    }
}
