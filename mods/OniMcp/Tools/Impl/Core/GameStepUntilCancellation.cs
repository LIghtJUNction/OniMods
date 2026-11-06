namespace OniMcp.Tools
{
    public static partial class GameControlTools
    {
        private static StepUntilWatcher _activeWatcher;
        private static readonly object _watcherLock = new object();

        private static void CancelStepForSpeedCommand(string action)
        {
            StepUntilWatcher watcher;
            lock (_watcherLock)
            {
                watcher = _activeWatcher;
                _activeWatcher = null;
            }

            // Complete and invalidate the old wait before the new command changes
            // speed. Its completion may acquire a pause that the new command owns
            // releasing; a later Unity update must not pause the new operation.
            if (watcher != null)
                watcher.Cancel("interrupted_by_" + action);
        }
    }
}
