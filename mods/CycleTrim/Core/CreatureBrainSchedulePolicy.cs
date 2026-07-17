using System;

namespace CycleTrim.Core
{
    public static class CreatureBrainSchedulePolicy
    {
        public static int ObservePriorityMaximum(int currentMaximum, int priorityCount)
        {
            return priorityCount > currentMaximum ? priorityCount : currentMaximum;
        }
    }

    public enum CreatureBrainSelectionKind
    {
        None,
        Priority,
        Normal
    }

    public readonly struct CreatureBrainSelection
    {
        internal CreatureBrainSelection(CreatureBrainSelectionKind kind, int normalBrainIndex)
        {
            Kind = kind;
            NormalBrainIndex = normalBrainIndex;
        }

        public CreatureBrainSelectionKind Kind { get; }

        public int NormalBrainIndex { get; }
    }

    public struct CreatureBrainScheduleCursor
    {
        private int scanned;
        private int normalAllowance;

        public CreatureBrainScheduleCursor(int normalAllowance)
        {
            if (normalAllowance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(normalAllowance));
            }

            scanned = 0;
            this.normalAllowance = normalAllowance;
        }

        public int NormalAllowance
        {
            get { return normalAllowance; }
        }

        public bool TrySelect(
            int currentBrainCount,
            bool allowPriority,
            int priorityCount,
            ref int nextNormalBrain,
            out CreatureBrainSelection selection)
        {
            if (currentBrainCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(currentBrainCount));
            }
            if (priorityCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(priorityCount));
            }

            var usePriority = allowPriority && priorityCount > 0;
            // The current game uses scanned != brains.Count. Re-reading Count
            // preserves dynamic additions, while >= deliberately guarantees
            // termination if an update shrinks the list below scanned.
            if (scanned >= currentBrainCount
                || (!usePriority && normalAllowance == 0))
            {
                selection = default(CreatureBrainSelection);
                return false;
            }

            nextNormalBrain = ClampNormalIndex(nextNormalBrain, currentBrainCount);
            scanned++;
            if (usePriority)
            {
                selection = new CreatureBrainSelection(
                    CreatureBrainSelectionKind.Priority,
                    -1);
                return true;
            }

            var selectedIndex = nextNormalBrain;
            nextNormalBrain++;
            if (nextNormalBrain == currentBrainCount)
            {
                nextNormalBrain = 0;
            }
            selection = new CreatureBrainSelection(
                CreatureBrainSelectionKind.Normal,
                selectedIndex);
            return true;
        }

        public void Complete(CreatureBrainSelection selection, bool isRunning)
        {
            if (isRunning && selection.Kind == CreatureBrainSelectionKind.Normal)
            {
                normalAllowance--;
            }
        }

        private static int ClampNormalIndex(int nextNormalBrain, int currentBrainCount)
        {
            if (currentBrainCount == 0 || nextNormalBrain < 0)
            {
                return 0;
            }
            if (nextNormalBrain >= currentBrainCount)
            {
                return currentBrainCount - 1;
            }
            return nextNormalBrain;
        }
    }
}
