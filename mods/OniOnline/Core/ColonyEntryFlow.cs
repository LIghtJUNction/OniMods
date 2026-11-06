using System;

namespace OniOnline.Core
{
    internal enum ColonyEntryPurpose { None, CreateSystem, JoinExisting, JoinNew, JoinLoad }
    internal enum ColonyEntryStage { None, AwaitingDisconnect, Reserving, AwaitingNative, AwaitingAdmission, StartingHost, HostQueued, HostFailed }

    // Owns user intent only. Runtime performs native navigation, room cleanup,
    // reservations and connection work after the corresponding transition succeeds.
    internal sealed class ColonyEntryFlow
    {
        private string roomId;
        private long admissionStamp;
        internal ColonyEntryPurpose Purpose { get; private set; }
        internal ColonyEntryStage Stage { get; private set; }
        internal bool RequiresLeaveConnection { get { return Stage == ColonyEntryStage.AwaitingDisconnect; } }
        internal bool AwaitingNative { get { return Stage == ColonyEntryStage.AwaitingNative; } }
        private bool Joining { get { return Purpose == ColonyEntryPurpose.JoinExisting ||
            Purpose == ColonyEntryPurpose.JoinNew || Purpose == ColonyEntryPurpose.JoinLoad; } }

        internal void BeginCreation(bool hasConnection = false)
        {
            CancelFrontend();
            Purpose = ColonyEntryPurpose.CreateSystem;
            Stage = hasConnection ? ColonyEntryStage.AwaitingDisconnect : ColonyEntryStage.AwaitingNative;
        }

        internal void BeginJoin(ColonyEntryPurpose purpose, string roomId, long admissionStamp)
        {
            if (purpose != ColonyEntryPurpose.JoinExisting && purpose != ColonyEntryPurpose.JoinNew && purpose != ColonyEntryPurpose.JoinLoad)
                throw new ArgumentException("invalid_colony_entry_purpose", nameof(purpose));
            Guid ignored;
            if (roomId == null || !Guid.TryParseExact(roomId, "N", out ignored))
                throw new ArgumentException("invalid_colony_entry_room", nameof(roomId));
            if (admissionStamp <= 0) throw new ArgumentOutOfRangeException(nameof(admissionStamp));
            CancelFrontend();
            Purpose = purpose; Stage = ColonyEntryStage.Reserving;
            this.roomId = roomId; this.admissionStamp = admissionStamp;
        }

        // Check this before consuming a reservation, native-ready or admission callback.
        // A changed admission invalidates the old intent, even in the same room.
        internal bool Matches(string currentRoomId, long currentAdmissionStamp)
        {
            if (!Joining) return false;
            if (roomId == currentRoomId && admissionStamp == currentAdmissionStamp && currentAdmissionStamp > 0) return true;
            CancelFrontend(); return false;
        }

        internal bool ReservationConfirmed()
        {
            if (!Joining || Stage != ColonyEntryStage.Reserving) return false;
            Stage = Purpose == ColonyEntryPurpose.JoinExisting ? ColonyEntryStage.AwaitingAdmission : ColonyEntryStage.AwaitingNative;
            return true;
        }

        internal bool NativeReady()
        {
            if (!Joining || Stage != ColonyEntryStage.AwaitingNative) return false;
            Stage = ColonyEntryStage.AwaitingAdmission; return true;
        }

        internal bool AdmissionConfirmed(bool admitted)
        {
            if (!admitted || !Joining || Stage != ColonyEntryStage.AwaitingAdmission) return false;
            CancelFrontend(); return true;
        }

        internal void CancelFrontend()
        {
            Purpose = ColonyEntryPurpose.None; Stage = ColonyEntryStage.None;
            roomId = null; admissionStamp = 0;
        }

        internal bool TryBeginHost(bool ready)
        {
            if (!ready || Purpose != ColonyEntryPurpose.CreateSystem || Stage != ColonyEntryStage.AwaitingNative) return false;
            Stage = ColonyEntryStage.StartingHost; return true;
        }

        internal bool CreationQueued()
        {
            if (Purpose != ColonyEntryPurpose.CreateSystem || Stage != ColonyEntryStage.StartingHost) return false;
            Stage = ColonyEntryStage.HostQueued; return true;
        }

        internal bool CreationFailed()
        {
            if (Purpose != ColonyEntryPurpose.CreateSystem ||
                Stage != ColonyEntryStage.StartingHost && Stage != ColonyEntryStage.HostQueued) return false;
            Stage = ColonyEntryStage.HostFailed; return true;
        }

        internal bool CreationConnected()
        {
            if (Purpose != ColonyEntryPurpose.CreateSystem ||
                Stage != ColonyEntryStage.StartingHost && Stage != ColonyEntryStage.HostQueued) return false;
            CancelFrontend(); return true;
        }

        internal bool RetryCreation(bool hasConnection = false)
        {
            if (Purpose != ColonyEntryPurpose.CreateSystem || Stage != ColonyEntryStage.HostFailed) return false;
            Stage = hasConnection ? ColonyEntryStage.AwaitingDisconnect : ColonyEntryStage.AwaitingNative;
            return true;
        }

        internal void ConnectionEnded()
        {
            if (Purpose == ColonyEntryPurpose.CreateSystem && Stage == ColonyEntryStage.AwaitingDisconnect)
                Stage = ColonyEntryStage.AwaitingNative;
            else if (Joining) CancelFrontend();
            else CreationFailed();
        }
    }
}
