using System;
using System.Collections.Generic;

namespace OniOnline.Sdk
{
    public enum AnimCorrectionAction
    {
        None = 0,
        Replay = 1,
        NudgeSpeed = 2,
        SnapTime = 3
    }

    public struct AnimSnapshot
    {
        public long EntityId { get; }
        public int AnimHash { get; }
        public byte PlayMode { get; }
        public float Speed { get; }
        public float ElapsedTime { get; }
        public double Timestamp { get; }

        public AnimSnapshot(long entityId, int animHash, byte playMode, float speed, float elapsedTime, double timestamp = 0)
        {
            EntityId = entityId;
            AnimHash = animHash;
            PlayMode = playMode;
            Speed = speed;
            ElapsedTime = elapsedTime;
            Timestamp = timestamp;
        }
    }

    public struct AnimCorrection
    {
        public AnimCorrectionAction Action { get; }
        public int TargetAnimHash { get; }
        public byte TargetPlayMode { get; }
        public float TargetElapsedTime { get; }
        public float RecommendedSpeed { get; }

        public AnimCorrection(AnimCorrectionAction action, int animHash, byte playMode, float elapsedTime, float recommendedSpeed)
        {
            Action = action;
            TargetAnimHash = animHash;
            TargetPlayMode = playMode;
            TargetElapsedTime = elapsedTime;
            RecommendedSpeed = recommendedSpeed;
        }

        public static AnimCorrection None => new AnimCorrection(AnimCorrectionAction.None, 0, 0, 0, 1.0f);
    }

    /// <summary>
    /// Evaluates animation discrepancies between local simulation and host authority.
    /// Provides smooth speed adjustment for small drifts and hard snaps for desynchronizations.
    /// </summary>
    public static class AnimReconciler
    {
        public const float DefaultDriftTolerance = 0.15f;
        public const float DefaultMaxNudgeDrift = 0.6f;

        public static AnimCorrection Evaluate(
            AnimSnapshot local,
            AnimSnapshot authoritative,
            float driftTolerance = DefaultDriftTolerance,
            float maxNudgeDrift = DefaultMaxNudgeDrift)
        {
            if (local.EntityId != authoritative.EntityId)
            {
                throw new ArgumentException("entity_id_mismatch");
            }

            if (float.IsNaN(driftTolerance) || driftTolerance < 0f) driftTolerance = DefaultDriftTolerance;
            if (float.IsNaN(maxNudgeDrift) || maxNudgeDrift < driftTolerance) maxNudgeDrift = Math.Max(driftTolerance, DefaultMaxNudgeDrift);

            float baseSpeed = authoritative.Speed > 0f && !float.IsNaN(authoritative.Speed) && !float.IsInfinity(authoritative.Speed)
                ? authoritative.Speed
                : 1.0f;

            // 1. If playing different animations or play modes, force immediate replay of the authoritative anim
            if (local.AnimHash != authoritative.AnimHash || local.PlayMode != authoritative.PlayMode)
            {
                return new AnimCorrection(
                    AnimCorrectionAction.Replay,
                    authoritative.AnimHash,
                    authoritative.PlayMode,
                    Math.Max(0f, float.IsNaN(authoritative.ElapsedTime) ? 0f : authoritative.ElapsedTime),
                    baseSpeed);
            }

            float localElapsed = Math.Max(0f, float.IsNaN(local.ElapsedTime) ? 0f : local.ElapsedTime);
            float authElapsed = Math.Max(0f, float.IsNaN(authoritative.ElapsedTime) ? 0f : authoritative.ElapsedTime);

            // 2. Compute time drift accounting for latency timestamp difference if provided
            float latencyElapsed = 0f;
            if (authoritative.Timestamp > 0 && local.Timestamp > authoritative.Timestamp &&
                !double.IsNaN(local.Timestamp) && !double.IsNaN(authoritative.Timestamp))
            {
                latencyElapsed = (float)(local.Timestamp - authoritative.Timestamp) * baseSpeed;
            }
            float expectedElapsedTime = authElapsed + latencyElapsed;
            float drift = localElapsed - expectedElapsedTime;
            float absDrift = Math.Abs(drift);

            // Within tolerance: no correction needed
            if (absDrift <= driftTolerance)
            {
                return AnimCorrection.None;
            }

            // Small drift: nudge playback speed temporarily (e.g. +/- 15%) to smooth out drift without teleporting frames
            if (absDrift <= maxNudgeDrift)
            {
                // If local is ahead, slow down slightly; if local is behind, speed up slightly
                float nudgeFactor = drift > 0 ? 0.85f : 1.15f;
                return new AnimCorrection(
                    AnimCorrectionAction.NudgeSpeed,
                    authoritative.AnimHash,
                    authoritative.PlayMode,
                    localElapsed,
                    baseSpeed * nudgeFactor);
            }

            // Large drift: snap directly to authoritative elapsed time
            return new AnimCorrection(
                AnimCorrectionAction.SnapTime,
                authoritative.AnimHash,
                authoritative.PlayMode,
                expectedElapsedTime,
                baseSpeed);
        }
    }

    /// <summary>
    /// Utility for evenly distributing periodic entity animation syncs across frames.
    /// Prevents network burst spikes in large colonies.
    /// </summary>
    public static class AnimSyncSharder
    {
        public static int GetShard(long entityId, int totalShards)
        {
            if (totalShards <= 1) return 0;
            unchecked
            {
                ulong positive = (ulong)entityId & 0x7FFFFFFFFFFFFFFFUL;
                return (int)(positive % (ulong)totalShards);
            }
        }

        public static bool IsEntityInShard(long entityId, int targetShard, int totalShards)
        {
            return GetShard(entityId, totalShards) == targetShard;
        }
    }
}
