using System;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class TemperatureSampleReadiness
    {
        // LogicTemperatureSensor starts with an empty eight-sample buffer and
        // CurrentValue=0. It publishes an average only after filling that buffer.
        internal static bool Ready(float[] samples, float average)
            => samples != null && samples.Length == 8 && samples.All(value => value > 0 && !float.IsInfinity(value))
                && average > 0 && !float.IsInfinity(average);
    }
}
