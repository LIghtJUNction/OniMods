using System;
using System.Collections.Generic;

namespace CycleTrim.Core
{
    internal static class ThreadLocalObjectPool<T>
        where T : class, new()
    {
        [ThreadStatic]
        private static Stack<T> items;

        internal static T Rent()
        {
            var pool = items;
            return pool != null && pool.Count != 0 ? pool.Pop() : new T();
        }

        internal static void Return(T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var pool = items;
            if (pool == null)
            {
                pool = new Stack<T>(1);
                items = pool;
            }

            pool.Push(item);
        }
    }
}
