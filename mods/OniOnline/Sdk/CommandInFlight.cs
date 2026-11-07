using System;
using System.Collections.Generic;
using System.Linq;

namespace OniOnline.Sdk
{
    // One in-flight command per key. A repeat is dropped until the authority
    // acknowledges it, the timeout expires, or the admission generation changes.
    public sealed class CommandInFlight
    {
        public const int MaximumInFlight = 128;
        public const double DefaultTimeoutSeconds = 90;
        private sealed class Pending { internal long Generation; internal double At; }
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.Ordinal);

        public int Count { get { return pending.Count; } }

        public bool TryAdmit(string key, long generation, double now)
        {
            if (!Valid(key) || generation <= 0) return false;
            Expire(now, DefaultTimeoutSeconds);
            if (pending.ContainsKey(key) || pending.Count >= MaximumInFlight) return false;
            pending.Add(key, new Pending { Generation = generation, At = now });
            return true;
        }

        public bool Acknowledge(string key, long generation)
        {
            Pending item;
            if (key == null || !pending.TryGetValue(key, out item) || item.Generation != generation) return false;
            pending.Remove(key);
            return true;
        }

        public int Expire(double now, double timeoutSeconds)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            string[] stale = pending.Where(pair => now - pair.Value.At >= timeoutSeconds).Select(pair => pair.Key).ToArray();
            foreach (string key in stale) pending.Remove(key);
            return stale.Length;
        }

        public void DropOtherGenerations(long generation)
        {
            string[] stale = pending.Where(pair => pair.Value.Generation != generation).Select(pair => pair.Key).ToArray();
            foreach (string key in stale) pending.Remove(key);
        }

        private static bool Valid(string key)
        { return !string.IsNullOrWhiteSpace(key) && key.Length <= 128 && key.IndexOfAny(new[] { '\r', '\n' }) < 0; }
    }
}
