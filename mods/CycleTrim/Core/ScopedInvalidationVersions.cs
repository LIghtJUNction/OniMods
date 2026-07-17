using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycleTrim.Core
{
    public sealed class ScopedInvalidationVersions<T>
        where T : class
    {
        private sealed class Counter
        {
            internal long Value;
        }

        private static readonly ConditionalWeakTable<T, Counter>.CreateValueCallback
            CounterFactory = CreateCounter;
        private readonly ConditionalWeakTable<T, Counter> counters =
            new ConditionalWeakTable<T, Counter>();

        public long Get(T scope)
        {
            if (scope == null)
            {
                return 0;
            }

            return Interlocked.Read(ref counters.GetValue(scope, CounterFactory).Value);
        }

        public long Bump(T scope)
        {
            if (scope == null)
            {
                return 0;
            }

            return Interlocked.Increment(
                ref counters.GetValue(scope, CounterFactory).Value);
        }

        private static Counter CreateCounter(T scope)
        {
            return new Counter();
        }
    }

    public static class InvalidationSuppression
    {
        [ThreadStatic]
        private static int nesting;

        public static bool IsSuppressed
        {
            get { return nesting != 0; }
        }

        public static void Enter()
        {
            nesting++;
        }

        public static void Exit()
        {
            if (nesting <= 0)
            {
                throw new InvalidOperationException(
                    "Invalidation suppression scopes are unbalanced.");
            }

            nesting--;
        }

        public static void ExitIfEntered(bool entered)
        {
            if (entered)
            {
                Exit();
            }
        }
    }
}
