using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DatabaseTools
    {
        private static void EnsureCodexReady()
        {
            if (CodexCache.entries != null && CodexCache.entries.Count > 0)
                return;

            CodexCache.CodexCacheInit();
        }

        private static int ClampMaxResults(JObject args)
        {
            if (args != null && args.TryGetValue("maxResults", out JToken token) && token != null)
            {
                if (int.TryParse(token.ToString(), out int value))
                    return Math.Max(1, Math.Min(10, value));
            }
            return 3;
    }
}
}
