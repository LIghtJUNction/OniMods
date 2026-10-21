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
        private int readyFrames;

        internal static string Status => ReferenceEquals(lastGame, Game.Instance) ? status : "not_requested";

        internal static bool Begin(Game game)
        {
            var bridge = MainThreadBridge.Instance;
            if (game == null || bridge == null || active != null)
                return false;
            lastGame = game;
            status = "waiting_for_base";
            active = bridge.gameObject.AddComponent<NewGamePauseWatcher>();
            active.target = game;
            active.deadline = Time.realtimeSinceStartup + 15f;
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
            if (!target.baseAlreadyCreated || Components.LiveMinionIdentities.Count != 3)
            {
                readyFrames = 0;
                return;
            }
            if (++readyFrames < 2)
                return;
            Pause();
            Finish(SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused
                ? "ready_paused" : "failed_to_pause");
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
