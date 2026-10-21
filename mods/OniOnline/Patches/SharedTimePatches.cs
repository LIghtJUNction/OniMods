using HarmonyLib;
using OniOnline.Multiplayer;

namespace OniOnline.Patches
{
    // Public Pause/Unpause retain their native nested-ticket semantics, including save/error dialogs.
    [HarmonyPatch(typeof(SpeedControlScreen), nameof(SpeedControlScreen.TogglePause))]
    internal static class SharedTimeTogglePausePatch
    {
        private static bool Prefix(bool playsound)
        {
            SharedTimeController controller = SharedTimeController.Active;
            if (!playsound || controller == null || controller.ApplyingNative || !controller.ShouldIntercept) return true;
            // This explicit user action may release one pre-existing local pause. The public
            // ticket remains held, so it cannot resume the room or clear a dialog automatically.
            if (controller.AllowNativeLocalToggle) return true;
            if (controller.Paused) controller.VoteResume();
            else controller.PauseAll();
            return false;
        }
    }

    [HarmonyPatch(typeof(SpeedControlScreen), nameof(SpeedControlScreen.SetSpeed))]
    internal static class SharedTimeSpeedPatch
    {
        private static bool Prefix(int __0)
        {
            SharedTimeController controller = SharedTimeController.Active;
            if (controller == null || controller.ApplyingNative || !controller.ShouldIntercept) return true;
            int speed = ((__0 % 3 + 3) % 3) + 1;
            // Native Unpause calls SetSpeed with the already approved value. Let it refresh UI/timeScale.
            if (speed == controller.Speed) return true;
            controller.VoteSpeed(speed);
            return false;
        }
    }

    [HarmonyPatch(typeof(SpeedControlScreen), nameof(SpeedControlScreen.OnKeyDown))]
    internal static class SharedTimeSpeedKeysPatch
    {
        private static bool Prefix(KButtonEvent e)
        {
            SharedTimeController controller = SharedTimeController.Active;
            if (controller == null || controller.ApplyingNative || !controller.ShouldIntercept) return true;
            if (e.TryConsume(Action.CycleSpeed))
            { controller.VoteSpeed(controller.Speed % 3 + 1); return false; }
            if (e.TryConsume(Action.SpeedUp))
            { controller.VoteSpeed(System.Math.Min(3, controller.Speed + 1)); return false; }
            if (e.TryConsume(Action.SlowDown))
            { controller.VoteSpeed(System.Math.Max(1, controller.Speed - 1)); return false; }
            return true;
        }
    }
}
