using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycleTrim.Core
{
    internal static class AnimationSymbolValueComparer
    {
        private sealed class FailedWrite { }
        private static readonly ConditionalWeakTable<object, FailedWrite> FailedWrites =
            new ConditionalWeakTable<object, FailedWrite>();
        private static readonly ConditionalWeakTable<object, FailedWrite>.CreateValueCallback NewFailure =
            owner => new FailedWrite();
        private static volatile bool suppressionEnabled;
        private static volatile bool hasFailures;

        internal static void BeginPatchingSession() { suppressionEnabled = true; }
        internal static void DisableSuppression() { suppressionEnabled = false; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool NeedsDirty(object owner, int state)
        {
            // A failed range can have changed bytes without reaching MarkDirty.
            // Keep original dirtying for that instance on every later call.
            return !suppressionEnabled || state != 1
                || (hasFailures && FailedWrites.TryGetValue(owner, out _));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void RememberFailure(object owner, Exception error)
        {
            // Harmony supplies a constant null exception on successful calls.
            // Keep this guard small enough to fold away with its finalizer.
            if (owner != null && error != null)
                RecordFailure(owner);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RecordFailure(object owner)
        {
            FailedWrites.GetValue(owner, NewFailure);
            // Publish after the weak entry exists, and never clear this
            // flag on a patching-session change: surviving failed owners
            // must keep their original dirtying behavior across sessions.
            hasFailures = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe bool SameBits(float left, float right)
        {
            // Only reinterpret these two 4-byte value parameters. No pointer
            // escapes or addresses game storage. This lets the JIT use register
            // bit moves instead of spilling explicit-layout unions to memory.
            return *(int*)&left == *(int*)&right;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool SameVectorBits(Vector2 left, Vector2 right)
        {
            // Keep each comparison 32-bit: promoted frame components can make
            // a packed 64-bit union compile to two narrow stores and a wide
            // reload. These comparisons preserve NaNs and signed zero without
            // reconstructing that wider temporary.
            return SameBits(left.x, right.x) && SameBits(left.y, right.y);
        }

    }
}
