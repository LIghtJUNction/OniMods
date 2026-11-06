using System;

namespace CycleTrim.Core
{
    internal static class ThreadLocalObjectPool<T>
        where T : class, new()
    {
        [ThreadStatic]
        private static T item;

        internal static T Rent()
        {
            var pooled = item;
            if (pooled != null)
            {
                item = null;
                return pooled;
            }

            return new T();
        }

        internal static void Return(T value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            // One retained object is enough for the normal non-reentrant hot path.
            // If a call is nested, let the extra object become collectible rather
            // than permanently growing per-thread retained capacity.
            if (item == null)
            {
                item = value;
            }
        }
    }
}
