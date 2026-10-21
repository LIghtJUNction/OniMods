namespace OniOnline.Multiplayer
{
    // Native speed uses indices 0..2; network speed uses 1..3.
    internal interface INativeTimeControl
    {
        void Refresh();
        int Generation { get; }
        bool Available { get; }
        int PauseCount { get; }
        bool IsPaused { get; }
        int Speed { get; }
        void Pause();
        void Unpause();
        void SetSpeed(int index);
    }
}
