using System;

namespace OniOnline.Core
{
    // A half-second ring keeps playback bounded. A stalled audio consumer gets the
    // newest speech instead of replaying an ever-growing delayed conversation.
    internal sealed class VoicePcmBuffer
    {
        private readonly object sync = new object();
        private readonly float[] samples;
        private int read;
        private int count;

        internal VoicePcmBuffer(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            samples = new float[capacity];
        }

        internal int Count { get { lock (sync) return count; } }
        internal void Write(byte[] pcm, int bytes)
        {
            if (pcm == null || bytes < 0 || bytes > pcm.Length || (bytes & 1) != 0) throw new ArgumentException("invalid_pcm");
            lock (sync)
            {
                int first = Math.Max(0, bytes / 2 - samples.Length);
                for (int index = first; index < bytes / 2; index++)
                {
                    if (count == samples.Length) { read = (read + 1) % samples.Length; count--; }
                    short value = (short)(pcm[index * 2] | pcm[index * 2 + 1] << 8);
                    samples[(read + count++) % samples.Length] = value / 32768f;
                }
            }
        }

        internal void Read(float[] target)
        {
            if (target == null) return;
            lock (sync)
            {
                for (int i = 0; i < target.Length; i++)
                {
                    target[i] = count > 0 ? samples[read] : 0;
                    if (count > 0) { samples[read] = 0; read = (read + 1) % samples.Length; count--; }
                }
            }
        }

        internal void Clear()
        {
            lock (sync) { Array.Clear(samples, 0, samples.Length); read = 0; count = 0; }
        }
    }
}
