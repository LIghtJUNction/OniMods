using System.Reflection;
using HarmonyLib;

namespace OniOnline.Multiplayer
{
    internal sealed class NativeTimeControl : INativeTimeControl
    {
        private static readonly FieldInfo PauseCountField = AccessTools.Field(typeof(SpeedControlScreen), "pauseCount");
        private SpeedControlScreen screen;
        public int Generation { get; private set; }
        public bool Available { get { return screen != null; } }
        public int PauseCount { get { return screen == null || PauseCountField == null ? int.MaxValue : (int)PauseCountField.GetValue(screen); } }
        public bool IsPaused { get { return screen != null && screen.IsPaused; } }
        public int Speed { get { return screen == null ? 0 : screen.GetSpeed(); } }
        public void Refresh()
        {
            var current = SpeedControlScreen.Instance;
            if (ReferenceEquals(current, screen)) return;
            screen = current;
            Generation++;
        }
        public void Pause() { screen.Pause(playSound: false); }
        public void Unpause() { screen.Unpause(playSound: false); }
        public void SetSpeed(int index) { screen.SetSpeed(index); }
    }
}
