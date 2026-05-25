using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static class PreviewTokenRegistry
    {
        private class Entry
        {
            public JObject Args;
            public System.DateTime ExpiresAt;
        }

        private static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private static readonly object _lock = new object();

        public static string Register(JObject args)
        {
            var storedArgs = (JObject)args.DeepClone();
            storedArgs.Remove("dryRun");
            storedArgs.Remove("previewToken");
            var token = Guid.NewGuid().ToString("N").Substring(0, 8);
            lock (_lock)
            {
                _entries[token] = new Entry
                {
                    Args = storedArgs,
                    ExpiresAt = System.DateTime.UtcNow.AddMinutes(5)
                };
                Cleanup();
            }
            return token;
        }

        public static JObject Get(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            lock (_lock)
            {
                Entry entry;
                if (!_entries.TryGetValue(token, out entry))
                    return null;

                if (System.DateTime.UtcNow > entry.ExpiresAt)
                {
                    _entries.Remove(token);
                    return null;
                }

                return (JObject)entry.Args.DeepClone();
            }
        }

        private static void Cleanup()
        {
            var now = System.DateTime.UtcNow;
            var expired = new List<string>();
            foreach (var kv in _entries)
            {
                if (now > kv.Value.ExpiresAt)
                    expired.Add(kv.Key);
            }
            foreach (var key in expired)
                _entries.Remove(key);
        }
    }
}
