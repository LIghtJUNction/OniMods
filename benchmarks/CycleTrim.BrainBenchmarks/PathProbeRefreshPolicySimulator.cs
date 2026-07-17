using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal sealed class PathProbeRefreshPolicySimulator
    {
        private readonly VersionedRefreshGate gate;

        internal PathProbeRefreshPolicySimulator(int maxSkippedRefreshes)
        {
            gate = new VersionedRefreshGate(maxSkippedRefreshes);
        }

        internal bool ShouldRun(RefreshStamp stamp)
        {
            return gate.ShouldRefresh(stamp);
        }

        internal bool PreserveVanilla()
        {
            gate.Invalidate();
            return true;
        }
    }
}
