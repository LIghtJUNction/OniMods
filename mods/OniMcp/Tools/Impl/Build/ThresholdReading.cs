using System;
using System.Reflection;

namespace OniMcp.Tools
{
    internal static class ThresholdReading
    {
        private static readonly FieldInfo Samples = typeof(LogicTemperatureSensor).GetField("temperatures", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static float? Current(IThresholdSwitch threshold)
        {
            float value = threshold.CurrentValue;
            if (float.IsNaN(value) || float.IsInfinity(value)) return null;
            if (threshold is LogicTemperatureSensor && !TemperatureSampleReadiness.Ready(Samples?.GetValue(threshold) as float[], value))
                return null;
            return value;
        }
    }
}
