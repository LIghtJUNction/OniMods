using System;
using OniOnline.Network;

namespace OniOnline.Runtime
{
    // Keep all native reads inside a guarded adapter entry point. Capability
    // negotiation with a peer cannot authorize this process's local game APIs.
    internal static class NativeAdapterAvailability
    {
        private static NativeAdapterGuard Current
        {
            get { return new NativeAdapterGuard(OnlineRuntime.Instance?.GameVersion,
                () => DlcManager.IsExpansion1Active()); }
        }

        internal static bool Supports(string capability) { return Current.Supports(capability); }
        internal static void Require(string capability) { Current.Require(capability); }
        internal static T Read<T>(string capability, Func<T> readNative) { return Current.Read(capability, readNative); }
    }
}
