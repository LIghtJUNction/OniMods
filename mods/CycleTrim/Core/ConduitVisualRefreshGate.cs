namespace CycleTrim.Core
{
    public enum ConduitAnimationQuality
    {
        Full = 1,
        Half = 2,
        Quarter = 4
    }

    // Schedules only replacement of a previously completed visual mesh. The
    // caller owns invalidation for changed inputs and all simulation updates.
    internal struct ConduitVisualRefreshGate
    {
        internal const double MaximumAgeSeconds = 0.1;

        private bool reusable;
        private bool observed;
        private int completedFrame;
        private int observedFrame;
        private double completedAt;
        private double observedAt;

        internal bool ShouldRebuild(
            int frame, double now, ConduitAnimationQuality quality, bool forceRefresh)
        {
            // Compare with the previous observation, including skipped frames.
            // A clock can move backwards while still being newer than the mesh.
            bool movedBackwards = observed && (frame < observedFrame || now < observedAt);
            observed = true;
            observedFrame = frame;
            observedAt = now;

            bool rebuild = !reusable || forceRefresh || frame < 0 || !IsFinite(now) || movedBackwards
                || (quality != ConduitAnimationQuality.Half && quality != ConduitAnimationQuality.Quarter)
                || (long)frame - completedFrame >= (int)quality
                || now - completedAt >= MaximumAgeSeconds;
            if (rebuild)
            {
                // A failed or empty rebuild must not make the previous mesh
                // eligible again. Only the caller's successful Complete does.
                reusable = false;
            }
            return rebuild;
        }

        internal void Complete(int frame, double now)
        {
            completedFrame = observedFrame = frame;
            completedAt = observedAt = now;
            observed = true;
            reusable = frame >= 0 && IsFinite(now);
        }

        internal void Invalidate()
        {
            reusable = false;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
