using OniOnline.Network;

namespace OniOnline.Core
{
    // Steam hosting completes later than the click that opened an existing colony.
    internal sealed class LoadedColonyRoomStart
    {
        private object requestedColony;

        internal void Begin(object colony) { requestedColony = colony; }
        internal void Cancel() { requestedColony = null; }

        internal bool Complete(RoomSession session, object currentColony)
        {
            var requested = requestedColony;
            Cancel();
            return requested != null && ReferenceEquals(requested, currentColony) &&
                session != null && session.IsHost && session.Joined && session.StartGame();
        }
    }
}
