using System;
using System.Reflection;
using Steamworks;

namespace OniOnline.Runtime
{
    internal static class SteamRoomCleanup
    {
        // SteamManager.Initialized lazily creates a manager and may initialize Steam again.
        // These reviewed 744825 SDK fields are read without calling any Steam entry point.
        private static readonly FieldInfo Client = typeof(SteamAPI).Assembly.GetType("Steamworks.CSteamAPIContext")?
            .GetField("m_pSteamClient", BindingFlags.NonPublic | BindingFlags.Static);
        internal static bool ClientAvailable
        {
            get
            {
                if (!CallbackDispatcher.IsInitialized) return false;
                if (Client == null || Client.FieldType != typeof(IntPtr)) throw new InvalidOperationException("steam_cleanup_contract_missing");
                return (IntPtr)Client.GetValue(null) != IntPtr.Zero;
            }
        }
    }
}
