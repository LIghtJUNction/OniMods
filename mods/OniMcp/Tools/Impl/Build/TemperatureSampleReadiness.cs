using System;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class TemperatureSampleReadiness
    {
        // A dirty threshold can publish a partial warm-up buffer. Even after
        // all eight slots are filled, that old average remains until the next
        // native tick resets the counter and publishes the complete average.
        // At counter 8, conservatively wait for that publication (also in later
        // rounds); counters 0-7 with a full buffer retain the published reading.
        internal static bool Ready(float[] samples, float average, int? samplesCollected)
            => samples != null && samples.Length == 8 && samples.All(value => value > 0 && !float.IsInfinity(value))
                && average > 0 && !float.IsInfinity(average)
                && samplesCollected.HasValue && samplesCollected.Value >= 0 && samplesCollected.Value < 8;
    }
}
