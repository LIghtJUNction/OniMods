using System;
using System.Collections.Generic;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal readonly struct CreatureSchedulerFrame
    {
        internal CreatureSchedulerFrame(
            bool usedVanilla,
            int normalCalls,
            int priorityCalls,
            int[] normalBrainIndices)
        {
            UsedVanilla = usedVanilla;
            NormalCalls = normalCalls;
            PriorityCalls = priorityCalls;
            NormalBrainIndices = normalBrainIndices;
        }

        internal bool UsedVanilla { get; }

        internal int NormalCalls { get; }

        internal int PriorityCalls { get; }

        internal int[] NormalBrainIndices { get; }
    }

    internal sealed class CreatureBrainSchedulerSimulator
    {
        private readonly BrainRateCap rateCap = new BrainRateCap();
        private readonly List<bool> running;
        private readonly List<long> callCounts;
        private readonly Queue<int> priorityBrains = new Queue<int>();
        private int nextNormalBrain;

        internal CreatureBrainSchedulerSimulator(bool[] running)
        {
            this.running = new List<bool>(running);
            callCounts = new List<long>(running.Length);
            for (var index = 0; index < running.Length; index++)
            {
                callCounts.Add(0);
            }
            AllowPriorityBrains = true;
        }

        internal long[] CallCounts
        {
            get { return callCounts.ToArray(); }
        }

        internal long NormalCalls { get; private set; }

        internal long PriorityCalls { get; private set; }

        internal int NextNormalBrain
        {
            get { return nextNormalBrain; }
        }

        internal bool AllowPriorityBrains { get; set; }

        internal int DebugMaxPriorityBrainCountSeen { get; private set; }

        internal void Prioritize(int brainIndex)
        {
            if (!priorityBrains.Contains(brainIndex))
            {
                priorityBrains.Enqueue(brainIndex);
            }
        }

        internal void SetRunning(int brainIndex, bool isRunning)
        {
            running[brainIndex] = isRunning;
        }

        internal void AddBrain(bool isRunning)
        {
            running.Add(isRunning);
            callCounts.Add(0);
        }

        internal void RemoveBrainAt(int brainIndex)
        {
            running.RemoveAt(brainIndex);
            callCounts.RemoveAt(brainIndex);
            if (brainIndex < nextNormalBrain)
            {
                nextNormalBrain--;
            }
            else if (nextNormalBrain == running.Count)
            {
                nextNormalBrain = 0;
            }

            var retainedPriority = new Queue<int>();
            while (priorityBrains.Count > 0)
            {
                var queued = priorityBrains.Dequeue();
                if (queued == brainIndex)
                {
                    continue;
                }
                retainedPriority.Enqueue(queued > brainIndex ? queued - 1 : queued);
            }
            while (retainedPriority.Count > 0)
            {
                priorityBrains.Enqueue(retainedPriority.Dequeue());
            }
        }

        internal CreatureSchedulerFrame RunFrame(
            double elapsedSeconds,
            bool isOrdinaryCreatureGroup,
            int vanillaBudget)
        {
            return RunFrame(
                elapsedSeconds,
                isOrdinaryCreatureGroup,
                vanillaBudget,
                null);
        }

        internal CreatureSchedulerFrame RunFrame(
            double elapsedSeconds,
            bool isOrdinaryCreatureGroup,
            int vanillaBudget,
            Action onUpdate)
        {
            if (!isOrdinaryCreatureGroup
                || elapsedSeconds <= 0.0
                || double.IsNaN(elapsedSeconds)
                || double.IsInfinity(elapsedSeconds))
            {
                return Run(vanillaBudget, usedVanilla: true, onUpdate);
            }

            rateCap.BeginFrame(elapsedSeconds);
            var normalAllowance = 0;
            while (normalAllowance < vanillaBudget
                && rateCap.TryAcquireNormal(BrainGroup.Creature))
            {
                normalAllowance++;
            }

            return Run(normalAllowance, usedVanilla: false, onUpdate);
        }

        private CreatureSchedulerFrame Run(
            int normalAllowance,
            bool usedVanilla,
            Action onUpdate)
        {
            var frameNormalCalls = 0;
            var framePriorityCalls = 0;
            var normalBrainIndices = new List<int>();
            var cursor = new CreatureBrainScheduleCursor(normalAllowance);
            CreatureBrainSelection selection;
            while (cursor.TrySelect(
                running.Count,
                AllowPriorityBrains,
                priorityBrains.Count,
                ref nextNormalBrain,
                out selection))
            {
                DebugMaxPriorityBrainCountSeen =
                    CreatureBrainSchedulePolicy.ObservePriorityMaximum(
                        DebugMaxPriorityBrainCountSeen,
                        priorityBrains.Count);
                int brainIndex;
                if (selection.Kind == CreatureBrainSelectionKind.Priority)
                {
                    brainIndex = priorityBrains.Dequeue();
                }
                else
                {
                    brainIndex = selection.NormalBrainIndex;
                }

                if (brainIndex < 0 || brainIndex >= running.Count || !running[brainIndex])
                {
                    cursor.Complete(selection, isRunning: false);
                    continue;
                }

                callCounts[brainIndex]++;
                if (selection.Kind == CreatureBrainSelectionKind.Priority)
                {
                    framePriorityCalls++;
                    PriorityCalls++;
                }
                else
                {
                    frameNormalCalls++;
                    NormalCalls++;
                    normalBrainIndices.Add(brainIndex);
                }
                if (onUpdate != null)
                {
                    onUpdate();
                }
                cursor.Complete(selection, isRunning: true);
            }

            return new CreatureSchedulerFrame(
                usedVanilla,
                frameNormalCalls,
                framePriorityCalls,
                normalBrainIndices.ToArray());
        }

    }
}
