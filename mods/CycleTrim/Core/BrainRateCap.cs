using System;

namespace CycleTrim.Core
{
    public enum BrainGroup
    {
        Dupe,
        Creature
    }

    public sealed class BrainRateCap
    {
        public const double DupeCallsPerSecond = 80.0;
        public const double CreatureCallsPerSecond = 400.0;

        private const int MaxDupeCallsPerFrame = 1;
        private const int MaxCreatureCallsPerFrame = 5;
        private const double TokenEpsilon = 1e-9;

        private double dupeTokens;
        private double creatureTokens;
        private int dupeCallsRemaining;
        private int creatureCallsRemaining;

        public void BeginFrame(double elapsedSeconds)
        {
            if (elapsedSeconds <= 0.0
                || double.IsNaN(elapsedSeconds)
                || double.IsInfinity(elapsedSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            }

            dupeTokens = Refill(
                dupeTokens,
                DupeCallsPerSecond * elapsedSeconds,
                MaxDupeCallsPerFrame);
            creatureTokens = Refill(
                creatureTokens,
                CreatureCallsPerSecond * elapsedSeconds,
                MaxCreatureCallsPerFrame);
            dupeCallsRemaining = MaxDupeCallsPerFrame;
            creatureCallsRemaining = MaxCreatureCallsPerFrame;
        }

        private static double Refill(double tokens, double added, int maxCallsPerFrame)
        {
            if (added >= maxCallsPerFrame)
            {
                // A slow frame receives only vanilla's per-frame budget. Any
                // excess is discarded instead of becoming catch-up debt.
                return maxCallsPerFrame;
            }

            return Math.Min(maxCallsPerFrame + added, tokens + added);
        }

        public bool TryAcquireNormal(BrainGroup group)
        {
            switch (group)
            {
                case BrainGroup.Dupe:
                    return TryAcquire(ref dupeTokens, ref dupeCallsRemaining);
                case BrainGroup.Creature:
                    return TryAcquire(ref creatureTokens, ref creatureCallsRemaining);
                default:
                    throw new ArgumentOutOfRangeException(nameof(group));
            }
        }

        public void RunPriority(Action updateBrain)
        {
            if (updateBrain == null)
            {
                throw new ArgumentNullException(nameof(updateBrain));
            }

            updateBrain();
        }

        private static bool TryAcquire(ref double tokens, ref int callsRemaining)
        {
            if (callsRemaining <= 0 || tokens + TokenEpsilon < 1.0)
            {
                return false;
            }

            tokens = Math.Max(0.0, tokens - 1.0);
            callsRemaining--;
            return true;
        }
    }
}
