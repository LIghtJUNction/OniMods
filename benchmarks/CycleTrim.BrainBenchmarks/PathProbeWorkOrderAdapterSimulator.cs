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
            if (!sentinelSafe || !exactCreature)
            {
                return;
            }
            RefreshCalls++;
            StateLookups++;
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
