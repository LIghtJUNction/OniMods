using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal sealed class BusyRefreshPolicySimulator
    {
        private readonly VersionedRefreshGate pickupGate;
        private readonly VersionedRefreshGate choreGate;
        private readonly bool isDuplicant;

        internal BusyRefreshPolicySimulator(
            int maxSkippedRefreshes,
            bool isDuplicant = true)
        {
            pickupGate = new VersionedRefreshGate(maxSkippedRefreshes);
            choreGate = new VersionedRefreshGate(maxSkippedRefreshes);
            this.isDuplicant = isDuplicant;
        }

        internal bool ShouldRunPickup(RefreshStamp stamp)
        {
            return !isDuplicant || pickupGate.ShouldRefresh(stamp);
        }

        internal bool ShouldRunChore(RefreshStamp stamp)
        {
            return !isDuplicant || choreGate.ShouldRefresh(stamp);
        }

        internal void Invalidate()
        {
            pickupGate.Invalidate();
            choreGate.Invalidate();
        }

        internal void Reset()
        {
            pickupGate.Reset();
            choreGate.Reset();
        }
    }
}
