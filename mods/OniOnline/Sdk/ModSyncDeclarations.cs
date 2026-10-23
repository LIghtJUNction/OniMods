using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OniOnline.Sdk
{
    // Called by trusted code installed on this computer, never from a peer's manifest.
    public static class ModSyncDeclarations
    {
        public const string FrameworkModId = "LIghtJUNction.OniOnline";
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ModSyncPolicy> Policies = new Dictionary<string, ModSyncPolicy>(StringComparer.Ordinal)
        { { FrameworkModId, ModSyncPolicy.Required } };
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
    }
}
