using UnityEngine;

namespace OniOnline.UI
{
    internal static class NativeGameplayUi
    {
        private static int checkedFrame = -1;
        private static Game checkedGame;
        private static bool ready;

        internal static bool Ready
        {
            get
            {
                var game = Game.Instance;
                if (game == null || !game.baseAlreadyCreated || game.IsLoading() || ManagementMenu.Instance == null) return false;
                if (MainMenu.Instance != null && MainMenu.Instance.IsActive()) return false;
                // Several input/UI components ask each frame. Resolve startup
                // screens once instead of scanning Unity's object set repeatedly.
                if (checkedFrame == Time.frameCount && checkedGame == game) return ready;
                checkedFrame = Time.frameCount;
                checkedGame = game;
                var characters = Object.FindFirstObjectByType<MinionSelectScreen>();
                var destination = Object.FindFirstObjectByType<ColonyDestinationSelectScreen>();
                ready = (characters == null || !characters.IsScreenActive()) &&
                    (destination == null || !destination.IsScreenActive()) &&
                    global::OniOnline.Runtime.OnlineInputControls.ModalStackAllowsGameplayUi();
                return ready;
            }
        }
    }
}
