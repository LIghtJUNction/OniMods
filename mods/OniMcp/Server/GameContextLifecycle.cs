namespace OniMcp.Server
{
    /// <summary>
    /// Separates save-load context changes from HTTP backlog admission. A request
    /// admitted before a load must not run against the scene created by that load.
    /// Game state is inspected only on the Unity thread when queued work executes.
    /// </summary>
    internal static class GameContextLifecycle
    {
        private static readonly object Sync = new object();
        private static int _generation;
        private static bool _saveLoadPending;
        private static bool _loadStartedWithGame;

        internal static int CaptureGeneration()
        {
            lock (Sync)
                return _generation;
        }

        internal static int BeginSaveLoad()
        {
            bool hadGame = Game.Instance != null;
            lock (Sync)
            {
                unchecked { _generation++; }
                _saveLoadPending = true;
                _loadStartedWithGame = hadGame;
                return _generation;
            }
        }

        internal static void SaveLoadFailed(int generation)
        {
            // If DoLoad failed before ONI entered its loading state, the old
            // context is still usable. If teardown already began, keep the gate
            // closed until a non-loading Game is available again.
            var game = Game.Instance;
            bool gameReady = game != null && !game.IsLoading() && Grid.CellCount > 0;
            lock (Sync)
            {
                if (_generation == generation && (gameReady || (game == null && !_loadStartedWithGame)))
                    _saveLoadPending = false;
            }
        }

        internal static string RejectionReason(int capturedGeneration)
        {
            var game = Game.Instance;
            bool loading = game != null && game.IsLoading();
            bool gameReady = game != null && !loading && Grid.CellCount > 0;
            lock (Sync)
            {
                if (capturedGeneration != _generation)
                    return "stale_game_context";
                if (loading || (_saveLoadPending && !gameReady))
                    return "game_loading";
                // DoLoad set the old Game to loading and cleared Grid.CellCount
                // before returning. A non-loading Game with a populated grid is
                // the earliest safe point for the new context to accept work.
                _saveLoadPending = false;
                return null;
            }
        }
    }
}
