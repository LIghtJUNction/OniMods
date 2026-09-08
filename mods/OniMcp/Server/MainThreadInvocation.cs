using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace OniMcp.Server
{
    /// <summary>A timed-out queued invocation must never start changing game state.</summary>
    internal sealed class MainThreadInvocation<T>
    {
        private readonly object _gate = new object();
        private readonly Func<T> _action;
        private bool _started;
        private bool _completed;
        private bool _cancelled;
        private T _result;
        private ExceptionDispatchInfo _error;

        internal MainThreadInvocation(Func<T> action)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        internal void Execute()
        {
            lock (_gate)
            {
                if (_cancelled || _started)
                    return;
                _started = true;
            }

            T result = default(T);
            ExceptionDispatchInfo error = null;
            try { result = _action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }

            lock (_gate)
            {
                if (_completed)
                    return;
                _result = result;
                _error = error;
                _completed = true;
                Monitor.PulseAll(_gate);
            }
        }

        internal void Cancel()
        {
            lock (_gate)
            {
                if (_completed)
                    return;
                _cancelled = true;
                _error = ExceptionDispatchInfo.Capture(new InvalidOperationException("Unity main thread bridge has been destroyed."));
                _completed = true;
                Monitor.PulseAll(_gate);
            }
        }

        internal T Wait(int timeoutMs)
        {
            if (timeoutMs < Timeout.Infinite)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            var elapsed = Stopwatch.StartNew();
            lock (_gate)
            {
                while (!_completed)
                {
                    int remaining = timeoutMs == Timeout.Infinite
                        ? Timeout.Infinite
                        : (int)Math.Max(0L, timeoutMs - elapsed.ElapsedMilliseconds);
                    if (remaining == 0 || !Monitor.Wait(_gate, remaining))
                    {
                        // A running Unity call cannot be interrupted; it can safely complete later.
                        _cancelled = !_started;
                        throw new TimeoutException("Timed out waiting for Unity main thread.");
                    }
                }
                _error?.Throw();
                return _result;
            }
        }
    }
}
