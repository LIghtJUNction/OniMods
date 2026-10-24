using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OniOnline.Sdk
{
    // Called by trusted code installed on this computer, never from a peer's manifest.
    public static class ModSyncDeclarations
    {
        public const string FrameworkModId = "LIghtJUNction.OniOnline";
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ModSyncPolicy> Policies = new Dictionary<string, ModSyncPolicy>(StringComparer.Ordinal)
        { { FrameworkModId, ModSyncPolicy.Required } };
        private static readonly Dictionary<string, HashSet<string>> ContentExtensions =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private static bool frozen;

        public static void Register(string modId, ModSyncPolicy policy)
        {
            if (!ModSyncValidation.Text(modId, 128) || policy < ModSyncPolicy.Required || policy > ModSyncPolicy.Optional)
                throw new ArgumentException("invalid_mod_sync_declaration");
            lock (Gate)
            {
                if (Policies.TryGetValue(modId, out var existing))
                {
                    if (existing != policy) throw new InvalidOperationException("conflicting_mod_sync_declaration");
                    return;
                }
                if (frozen) throw new InvalidOperationException("mod_sync_declarations_frozen");
                Policies.Add(modId, policy);
            }
        }

        // Register package resource extensions that are meaningful to a mod but
        // are outside the built-in Unity/data allowlist. This declaration is
        // trusted local metadata, never accepted from a remote manifest.
        public static void RegisterContentExtensions(string modId, IEnumerable<string> extensions)
        {
            if (!ModSyncValidation.Text(modId, 128) || extensions == null)
                throw new ArgumentException("invalid_mod_content_declaration");
            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string extension in extensions)
            {
                if (!ValidExtension(extension)) throw new ArgumentException("invalid_mod_content_extension");
                normalized.Add(extension.ToLowerInvariant());
            }
            if (normalized.Count == 0) throw new ArgumentException("invalid_mod_content_declaration");
            lock (Gate)
            {
                if (frozen) throw new InvalidOperationException("mod_sync_declarations_frozen");
                if (!ContentExtensions.TryGetValue(modId, out var current))
                    ContentExtensions.Add(modId, current = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                foreach (string extension in normalized) current.Add(extension);
            }
        }

        // Capture once after plugin registration and before advertising or joining a session.
        // The immutable copy also prevents policy changes while confirmation UI is open.
        public static IReadOnlyDictionary<string, ModSyncPolicy> Snapshot()
        {
            lock (Gate)
            {
                frozen = true;
                return new ReadOnlyDictionary<string, ModSyncPolicy>(new Dictionary<string, ModSyncPolicy>(Policies, StringComparer.Ordinal));
            }
        }

        // Capture the same process-lifetime declaration boundary as Snapshot().
        // Each value is a read-only copy so callers cannot mutate the catalog
        // after plugin registration has finished.
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> ContentSnapshot()
        {
            lock (Gate)
            {
                frozen = true;
                var copy = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var pair in ContentExtensions)
                    copy[pair.Key] = new ReadOnlyCollection<string>(pair.Value.OrderBy(value => value, StringComparer.Ordinal).ToArray());
                return new ReadOnlyDictionary<string, IReadOnlyList<string>>(copy);
            }
        }

        private static bool ValidExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension) || extension.Length > 32 || extension[0] != '.') return false;
            for (int i = 1; i < extension.Length; i++)
            {
                char c = extension[i];
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_'))
                    return false;
            }
            return true;
        }
    }
}
