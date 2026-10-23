using System.Linq;
using OniOnline.Multiplayer;

namespace OniOnline.Core
{
    internal struct StarMapSelection
    {
        internal string PeerId;
        internal int NativeWorldId;
        internal bool RoomCellOccupied;
    }

    internal static class StarMapSelectionPolicy
    {
        internal static bool RemoteInteractionActive(bool remoteView, bool screenActive, bool defaultMode)
        { return remoteView && screenActive && defaultMode; }

        internal static bool RestoreNativeSelection(bool screenActive, bool defaultMode)
        { return screenActive && defaultMode; }

        internal static StarMapSelection Resolve(StarSystemState room, bool homeView, bool localSystem,
            int nativeWorldId, int q, int r)
        {
            // Equal hex coordinates do not identify the same place across systems.
            // A guest's home chart contains its own native worlds, not room members.
            var member = homeView && !localSystem ? null : room?.Members.FirstOrDefault(m => m.Q == q && m.R == r);
            return new StarMapSelection { PeerId = member?.Planet == null ? null : member.PeerId,
                NativeWorldId = homeView ? nativeWorldId : -1, RoomCellOccupied = member != null };
        }
    }
}
