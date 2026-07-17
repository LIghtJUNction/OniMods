using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal sealed class BusyRefreshPolicySimulator
    {
        private readonly VersionedRefreshGate pickupGate;
        private readonly VersionedRefreshGate choreGate;

        internal BusyRefreshPolicySimulator(int maxSkippedRefreshes)
        {
            pickupGate = new VersionedRefreshGate(maxSkippedRefreshes);
            choreGate = new VersionedRefreshGate(maxSkippedRefreshes);
        }

        internal bool ShouldRunPickup(RefreshStamp stamp)
        {
            return pickupGate.ShouldRefresh(stamp);
        }

        internal bool ShouldRunChore(RefreshStamp stamp)
        {
            return choreGate.ShouldRefresh(stamp);
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
