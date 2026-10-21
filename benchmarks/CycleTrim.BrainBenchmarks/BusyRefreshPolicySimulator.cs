using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal sealed class BusyRefreshPolicySimulator
    {
        private readonly CoupledRefreshGate gate;

        internal BusyRefreshPolicySimulator(int maxSkippedRefreshes)
        {
            gate = new CoupledRefreshGate(maxSkippedRefreshes);
        }

        internal bool ShouldRunPickup(RefreshStamp stamp)
        {
            return gate.Begin(stamp);
        }

        internal bool ShouldRunChore(RefreshStamp stamp)
        {
            return gate.Complete(stamp);
        }

        internal void Invalidate()
        {
            gate.Invalidate();
        }

        internal void Reset()
        {
            gate.Reset();
        }
    }
}
