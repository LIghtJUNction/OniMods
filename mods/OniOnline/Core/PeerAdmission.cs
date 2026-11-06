using System;

namespace OniOnline.Core
{
    // A connected seat may still be waiting for its mod manifest. Call this at
    // every data or command boundary; a previous successful check is not a grant.
    internal static class PeerAdmission
    {
        internal static bool Allows(Func<string, bool> canUsePeer, string peer)
        {
            if (string.IsNullOrEmpty(peer)) return false;
            try { return canUsePeer == null || canUsePeer(peer); }
            catch (Exception) { return false; }
        }

        internal static long Generation(Func<string, long> generationForPeer, string peer)
        {
            if (string.IsNullOrEmpty(peer)) return 0;
            try
            {
                long value = generationForPeer == null ? 1 : generationForPeer(peer);
                return value > 0 ? value : 0;
            }
            catch (Exception) { return 0; }
        }
    }
}
