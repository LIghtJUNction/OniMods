using System;

namespace OniMcp.Core
{
    /// <summary>
    /// Interface for tool call results that complete asynchronously across Unity frames.
    /// Used by browser-like condition-waiting execution (e.g. step_until).
    /// </summary>
    public interface IDeferredMcpResponse
    {
        void RegisterCompletion(Action<object, Exception> onComplete);
    }

    /// <summary>
    /// CallToolResult that can complete across multiple game frames while Unity ticks.
    /// </summary>
    public class DeferredCallToolResult : CallToolResult, IDeferredMcpResponse
    {
        private Action<object, Exception> _callback;
        private readonly object _lock = new object();
        private bool _completed;
        private object _result;
        private Exception _exception;

        public bool IsCompleted { get { lock (_lock) return _completed; } }
        public CallToolResult Result { get { lock (_lock) return _result as CallToolResult; } }
        public Exception Exception { get { lock (_lock) return _exception; } }

        public void RegisterCompletion(Action<object, Exception> onComplete)
        {
            if (onComplete == null)
                return;

            Action<object, Exception> immediateCb = null;
            object immediateResult = null;
            Exception immediateEx = null;

            lock (_lock)
            {
                if (_completed)
                {
                    immediateCb = onComplete;
                    immediateResult = _result;
                    immediateEx = _exception;
                }
                else
                {
                    _callback = onComplete;
                }
            }

            immediateCb?.Invoke(immediateResult, immediateEx);
        }

        public void Complete(CallToolResult result, Exception ex = null)
        {
            Action<object, Exception> cb = null;
            lock (_lock)
            {
                if (_completed)
                    return;

                _completed = true;
                _result = result;
                _exception = ex;
                if (result != null)
                {
                    Content = result.Content;
                    IsError = result.IsError;
                }
                cb = _callback;
            }

            cb?.Invoke(result, ex);
        }
    }
}
