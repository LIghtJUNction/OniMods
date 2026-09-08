namespace CycleTrim.BrainBenchmarks
{
    internal sealed class PathProbeWorkOrderAdapterSimulator
    {
        internal int RefreshCalls { get; private set; }
        internal int CloneCalls { get; private set; }
        internal int RecycleCalls { get; private set; }
        internal int StateLookups { get; private set; }

        internal void Prepare(bool exactCreature, bool sentinelSafe, bool completedHit)
        {
            if (!sentinelSafe)
            {
                return;
            }
            StateLookups++;
            if (!exactCreature)
            {
                // Unsupported work invalidates any existing tracked state.
                return;
            }
            RefreshCalls++;
            if (completedHit)
            {
                RecycleCalls++;
            }
            else
            {
                CloneCalls++;
            }
        }

        internal void ResetCounts()
        {
            RefreshCalls = 0;
            CloneCalls = 0;
            RecycleCalls = 0;
            StateLookups = 0;
        }
    }
}
