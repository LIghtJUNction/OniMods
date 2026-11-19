using System.Globalization;
using System.Text;

namespace CycleTrim.Core
{
    internal readonly struct FrameEnvironmentSample
    {
        internal readonly bool ApplicationIsFocused;
        internal readonly bool AppHasFocus;
        internal readonly bool? SleepWhenOutOfFocus;
        internal readonly bool OverrideRequested;
        internal readonly bool OverrideApplied;
        internal readonly int VSyncCount;
        internal readonly int TargetFrameRate;

        internal FrameEnvironmentSample(bool applicationIsFocused, bool appHasFocus,
            bool? sleepWhenOutOfFocus, bool overrideRequested, bool overrideApplied,
            int vSyncCount, int targetFrameRate)
        {
            ApplicationIsFocused = applicationIsFocused;
            AppHasFocus = appHasFocus;
            SleepWhenOutOfFocus = sleepWhenOutOfFocus;
            OverrideRequested = overrideRequested;
            OverrideApplied = overrideApplied;
            VSyncCount = vSyncCount;
            TargetFrameRate = targetFrameRate;
        }

        internal bool SameFocus(FrameEnvironmentSample other)
        {
            return ApplicationIsFocused == other.ApplicationIsFocused && AppHasFocus == other.AppHasFocus;
        }

        internal bool SameConditions(FrameEnvironmentSample other)
        {
            return SameFocus(other) && SleepWhenOutOfFocus == other.SleepWhenOutOfFocus
                && OverrideRequested == other.OverrideRequested && OverrideApplied == other.OverrideApplied
                && VSyncCount == other.VSyncCount && TargetFrameRate == other.TargetFrameRate;
        }
    }

    // Observe both frame boundaries and focus events. Counts describe observations,
    // not frames; a change at the closing boundary conservatively invalidates the
    // completed window. Storage stays constant as a window grows.
    internal sealed class FrameEnvironmentWindow
    {
        private FrameEnvironmentSample last;
        internal int Observations { get; private set; }
        internal int Changes { get; private set; }
        internal int FocusChanges { get; private set; }
        private bool allSettingsKnown = true;

        internal void Observe(FrameEnvironmentSample sample)
        {
            if (Observations != 0)
            {
                if (!last.SameConditions(sample)) Changes++;
                if (!last.SameFocus(sample)) FocusChanges++;
            }
            last = sample;
            Observations++;
            allSettingsKnown &= sample.SleepWhenOutOfFocus.HasValue;
        }

        internal void Reset()
        {
            Observations = Changes = FocusChanges = 0;
            allSettingsKnown = true;
        }

        internal void AppendJson(StringBuilder json)
        {
            json.Append("{\"applicationIsFocused\":").Append(Boolean(last.ApplicationIsFocused))
                .Append(",\"appHasFocus\":").Append(Boolean(last.AppHasFocus))
                .Append(",\"sleepWhenOutOfFocus\":").Append(NullableBoolean(last.SleepWhenOutOfFocus))
                .Append(",\"effectiveSleepWhenOutOfFocus\":")
                .Append(NullableBoolean(last.SleepWhenOutOfFocus.HasValue
                    ? (bool?)(last.SleepWhenOutOfFocus.Value && !(last.OverrideRequested && last.OverrideApplied)) : null))
                .Append(",\"backgroundThrottleOverrideRequested\":").Append(Boolean(last.OverrideRequested))
                .Append(",\"backgroundThrottleOverrideApplied\":").Append(Boolean(last.OverrideApplied))
                .Append(",\"vSyncCount\":").Append(last.VSyncCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\"targetFrameRate\":").Append(last.TargetFrameRate.ToString(CultureInfo.InvariantCulture))
                .Append(",\"observations\":").Append(Observations.ToString(CultureInfo.InvariantCulture))
                .Append(",\"observedChanges\":").Append(Changes.ToString(CultureInfo.InvariantCulture))
                .Append(",\"focusChanges\":").Append(FocusChanges.ToString(CultureInfo.InvariantCulture))
                .Append(",\"settingsKnown\":").Append(Boolean(Observations != 0 && allSettingsKnown))
                .Append(",\"stable\":").Append(Boolean(Observations != 0 && Changes == 0))
                .Append('}');
        }

        private static string Boolean(bool value) { return value ? "true" : "false"; }
        private static string NullableBoolean(bool? value) { return value.HasValue ? Boolean(value.Value) : "null"; }
    }
}
