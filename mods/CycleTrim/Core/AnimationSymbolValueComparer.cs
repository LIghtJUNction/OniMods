using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

        internal static void BeginPatchingSession() { suppressionEnabled = true; }
        internal static void DisableSuppression() { suppressionEnabled = false; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool NeedsDirty(object owner, int state)
        {
            // A failed range can have changed bytes without reaching MarkDirty.
            // Keep original dirtying for that instance on every later call.
            return !suppressionEnabled || state != 1 || FailedWrites.TryGetValue(owner, out _);
        }

        internal static void RememberFailure(object owner, Exception error)
        {
            if (owner != null && error != null)
                FailedWrites.GetValue(owner, NewFailure);
        }

        // Each setter invocation owns this state: 0 = no fields visited,
        // 1 = only identical fields, 3 = at least one changed field.
        // The transpiler invokes these small helpers once per field. Request
        // inlining so those calls need not dominate a large unchanged range.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteFloat(ref float destination, float value, ref int state)
        {
            state |= 1;
            if (SameBits(destination, value))
                return;
            destination = value;
            state = 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteVector(ref Vector2 destination, Vector2 value, ref int state)
        {
            state |= 1;
            if (SameBits(destination.x, value.x) && SameBits(destination.y, value.y))
                return;
            destination = value;
            state = 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool SameBits(float left, float right)
        {
            return new FloatBits { Value = left }.Bits == new FloatBits { Value = right }.Bits;
        }

        // net48 has no BitConverter.SingleToInt32Bits. This value-type union
        // performs the same reinterpretation without unsafe code or allocation.
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] internal float Value;
            [FieldOffset(0)] internal int Bits;
        }
    }
}
