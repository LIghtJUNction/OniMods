using System.Linq;
using HarmonyLib;
using OniMcp.Server;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    internal sealed class NewGamePauseWatcher : MonoBehaviour
    {
        private static NewGamePauseWatcher active;
        private static Game lastGame;
        private static string status = "not_requested";
        private Game target;
        private float deadline;
        private readonly NewGameOpeningPolicy opening = new NewGameOpeningPolicy();
        private WattsonMessage submittedWelcome;

        internal static string Status => ReferenceEquals(lastGame, Game.Instance) ? status : "not_requested";

        internal static bool Begin(Game game)
        {
            var bridge = MainThreadBridge.Instance;
            if (game == null || bridge == null || active != null || game.baseAlreadyCreated
                || game.GameStarted() || SaveLoader.Instance == null || SaveLoader.Instance.loadedFromSave)
                return false;
            lastGame = game;
            status = "waiting_for_base";
            active = bridge.gameObject.AddComponent<NewGamePauseWatcher>();
            active.target = game;
            active.deadline = Time.realtimeSinceStartup + 60f;
            return true;
        }

        private void Update()
        {
            if (!ReferenceEquals(Game.Instance, target))
            {
                Finish("cancelled_game_changed");
                return;
            }
            if (Time.realtimeSinceStartup >= deadline)
            {
                Pause();
                Finish("failed_start_timeout");
                return;
            }
            var confirmations = UnityEngine.Object.FindObjectsByType<ConfirmDialogScreen>(FindObjectsSortMode.None)
                .Where(IsVisible).ToArray();
            foreach (var dialog in confirmations)
            {
                if (IsModDifference(dialog))
                    dialog.OnSelect_OK();
            }

            var welcome = UnityEngine.Object.FindObjectsByType<WattsonMessage>(FindObjectsSortMode.None)
                .FirstOrDefault(screen => screen.GetType() == typeof(WattsonMessage) && IsVisible(screen));
            if (welcome != null && submittedWelcome != welcome && confirmations.Length == 0)
            {
                var button = ReadField<KButton>(welcome, "button");
                if (button != null && button.enabled && button.gameObject.activeInHierarchy && button.isInteractable)
                {
                    // Use the native Start button so audio, camera, HUD and initial-save callbacks run.
                    submittedWelcome = welcome;
                    button.SignalClick(KKeyCode.Mouse0);
                }
            }
            // Do not pause the birth animation: baseAlreadyCreated precedes WattsonMessage's
            // native Start flow. Only its completion marks GameStarted and restores the HUD.
            bool ready = opening.ObserveReady(Time.realtimeSinceStartup, target.baseAlreadyCreated,
                Components.LiveMinionIdentities.Count, target.GameStarted(), welcome != null,
                confirmations.Length != 0);
            status = confirmations.Length != 0 ? "waiting_for_opening_confirmation"
                : welcome != null ? "waiting_for_welcome_close" : "waiting_for_game_started";
            if (!ready)
                return;
            Pause();
            Finish(SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused
                ? "ready_paused" : "failed_to_pause");
        }

        private static bool IsVisible(KScreen screen)
        {
            return screen != null && screen.gameObject.activeInHierarchy && screen.IsActive();
        }

        private static bool IsModDifference(ConfirmDialogScreen dialog)
        {
            if (dialog.GetType() != typeof(ConfirmDialogScreen))
                return false;
            var confirm = AccessTools.Field(typeof(ConfirmDialogScreen), "confirmAction");
            var cancel = AccessTools.Field(typeof(ConfirmDialogScreen), "cancelAction");
            var configurable = AccessTools.Field(typeof(ConfirmDialogScreen), "configurableAction");
            if (confirm == null || cancel == null || configurable == null)
                return false;
            var title = ReadField<LocText>(dialog, "titleText");
            return NewGameOpeningPolicy.IsInformationalModDifference(dialog.GetType().FullName,
                title == null ? null : title.text,
                global::STRINGS.UI.FRONTEND.MOD_DIALOGS.SAVE_GAME_MODS_DIFFER.TITLE,
                confirm.GetValue(dialog) != null || cancel.GetValue(dialog) != null
                    || configurable.GetValue(dialog) != null, dialog.deactivateOnConfirmAction);
        }

        private static T ReadField<T>(object instance, string name) where T : class
        {
            return AccessTools.Field(instance.GetType(), name)?.GetValue(instance) as T;
        }

        private static void Pause()
        {
            GameControlTools.ControlGameSpeed().Handler(new JObject { ["action"] = "pause" });
        }

        private void Finish(string result)
        {
            status = result;
            if (ReferenceEquals(active, this))
                active = null;
            Destroy(this);
        }
    }
}
