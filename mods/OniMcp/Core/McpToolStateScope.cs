using System;

namespace OniMcp.Core
{
    /// <summary>
    /// Resolves application state scope independently from MCP transport sessions.
    /// Legacy callers stay scoped by Mcp-Session-Id. Stateless callers receive an
    /// explicit opaque handle that must be passed back on later requests.
    /// </summary>
    public sealed class McpToolStateScope
    {
        private const int HandleLength = 32;

        private McpToolStateScope(string cacheScopeId, string handle)
        {
            CacheScopeId = cacheScopeId;
            Handle = handle;
        }

        public string CacheScopeId { get; private set; }

        public string Handle { get; private set; }

        public bool IsStateless
        {
            get { return !string.IsNullOrEmpty(Handle); }
        }

        public static bool TryResolve(string sessionId, string handle, out McpToolStateScope scope, out string error)
        {
            scope = null;
            error = null;

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                scope = new McpToolStateScope("session:" + sessionId.Trim(), null);
                return true;
            }

            string resolvedHandle = string.IsNullOrWhiteSpace(handle)
                ? Guid.NewGuid().ToString("N")
                : handle.Trim();
            if (!IsOpaqueHandle(resolvedHandle))
            {
                error = "deltaHandle must be the 32-character opaque hexadecimal handle returned by a previous stateless delta response";
                return false;
            }

            scope = new McpToolStateScope("delta:" + resolvedHandle.ToLowerInvariant(), resolvedHandle.ToLowerInvariant());
            return true;
        }

        private static bool IsOpaqueHandle(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != HandleLength)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = c >= '0' && c <= '9'
                    || c >= 'a' && c <= 'f'
                    || c >= 'A' && c <= 'F';
                if (!hex)
                    return false;
            }
            return true;
        }
    }
}
