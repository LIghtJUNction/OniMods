using System;
using System.Linq;

namespace OniOnline.Sdk
{
    public enum SnapshotAdmissionStatus { Idle, Holding, Offered, Loaded, Failed }

    // The authority holds the world, offers one save snapshot, and waits until the
    // client reports the same checksum and fingerprint. This does not run a simulation.
    public sealed class SnapshotAdmission
    {
        public const double ClientWaitSeconds = 90;
        public const double HostDeadlineSeconds = 180;
        public const double StallLogSeconds = 5;

        private byte[] checksum = new byte[0];
        private double holdAt, offeredAt, lastProgressAt, now = -1;
        private long acknowledged;

        public long Generation { get; private set; }
        public SnapshotAdmissionStatus Status { get; private set; }
        public string Failure { get; private set; }
        public int StallReports { get; private set; }
        public uint SnapshotId { get; private set; }
        public string Fingerprint { get; private set; }
        public long TotalBytes { get; private set; }
        public bool IsHolding { get { return Status == SnapshotAdmissionStatus.Holding || Status == SnapshotAdmissionStatus.Offered; } }

        public void SetGeneration(long generation, double seconds)
        {
            Touch(seconds);
            if (generation == Generation) return;
            Generation = generation;
            Clear();
        }

        public void Hold(double seconds)
        {
            Touch(seconds);
            if (Status == SnapshotAdmissionStatus.Holding || Status == SnapshotAdmissionStatus.Offered)
                throw new InvalidOperationException("snapshot_hold_busy");
            Clear();
            Status = SnapshotAdmissionStatus.Holding;
            holdAt = seconds;
            lastProgressAt = seconds;
        }

        public void Offer(double seconds, uint snapshotId, byte[] snapshotChecksum, string fingerprint, long bytes)
        {
            Touch(seconds);
            if (Status != SnapshotAdmissionStatus.Holding) throw new InvalidOperationException("snapshot_not_held");
            if (snapshotId == 0 || snapshotChecksum == null || snapshotChecksum.Length == 0 || snapshotChecksum.Length > 64 ||
                string.IsNullOrEmpty(fingerprint) || fingerprint.Length > 128 || bytes < 0)
                throw new ArgumentException("invalid_snapshot_offer");
            SnapshotId = snapshotId;
            checksum = snapshotChecksum.ToArray();
            Fingerprint = fingerprint;
            TotalBytes = bytes;
            acknowledged = 0;
            offeredAt = seconds;
            lastProgressAt = seconds;
            Status = SnapshotAdmissionStatus.Offered;
        }

        public void NoteProgress(double seconds, long acknowledgedBytes)
        {
            Touch(seconds);
            if (Status != SnapshotAdmissionStatus.Offered) return;
            if (acknowledgedBytes < acknowledged || acknowledgedBytes > TotalBytes)
                throw new ArgumentOutOfRangeException(nameof(acknowledgedBytes));
            if (acknowledgedBytes > acknowledged)
            {
                acknowledged = acknowledgedBytes;
                lastProgressAt = seconds;
            }
        }

        // True when this call records a stall or moves the admission to Failed.
        public bool Pump(double seconds)
        {
            Touch(seconds);
            if (Status == SnapshotAdmissionStatus.Holding && seconds - holdAt >= HostDeadlineSeconds)
            { Fail("host_load_timeout"); return true; }
            if (Status == SnapshotAdmissionStatus.Offered && seconds - holdAt >= HostDeadlineSeconds)
            { Fail("host_load_timeout"); return true; }
            if (Status == SnapshotAdmissionStatus.Offered && seconds - offeredAt >= ClientWaitSeconds)
            { Fail("client_load_timeout"); return true; }
            if (Status == SnapshotAdmissionStatus.Offered && acknowledged < TotalBytes && seconds - lastProgressAt >= StallLogSeconds)
            {
                StallReports++;
                lastProgressAt = seconds;
                return true;
            }
            return false;
        }

        public bool ReportLoaded(double seconds, uint snapshotId, byte[] snapshotChecksum, string fingerprint)
        {
            Touch(seconds);
            if (Status != SnapshotAdmissionStatus.Offered || snapshotId != SnapshotId) return false;
            if (snapshotChecksum == null || !snapshotChecksum.SequenceEqual(checksum))
            { Fail("checksum_mismatch"); return false; }
            if (!string.Equals(fingerprint, Fingerprint, StringComparison.Ordinal))
            { Fail("fingerprint_mismatch"); return false; }
            Status = SnapshotAdmissionStatus.Loaded;
            Failure = null;
            return true;
        }

        private void Fail(string reason)
        {
            Status = SnapshotAdmissionStatus.Failed;
            Failure = reason;
        }

        private void Clear()
        {
            Status = SnapshotAdmissionStatus.Idle;
            Failure = null;
            SnapshotId = 0;
            Fingerprint = null;
            checksum = new byte[0];
            TotalBytes = 0;
            acknowledged = 0;
            StallReports = 0;
        }

        private void Touch(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || (now >= 0 && seconds < now))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            now = seconds;
        }
    }
}
