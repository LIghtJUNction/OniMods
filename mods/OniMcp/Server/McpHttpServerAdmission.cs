using System;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        // A single batch request is already capped at 20 child calls. Keeping the
        // external request backlog to the same finite width permits normal parallel
        // reads without allowing an arbitrary number of Unity-thread actions to pile up.
        internal const int MaxPendingMainThreadHttpRequests = 20;
        internal const int MainThreadBusyErrorCode = -32050;

        private readonly object _mainThreadAdmissionLock = new object();
        private int _mainThreadAdmissionGeneration;
        private int _pendingMainThreadHttpRequests;

        private bool TryAcquireMainThreadHttpAdmission(HttpListenerResponse response, object requestId,
            string sessionId, bool modern, out MainThreadHttpAdmissionLease lease)
        {
            lock (_mainThreadAdmissionLock)
            {
                if (_running && _pendingMainThreadHttpRequests < MaxPendingMainThreadHttpRequests)
                {
                    _pendingMainThreadHttpRequests++;
                    lease = new MainThreadHttpAdmissionLease(this, _mainThreadAdmissionGeneration);
                    return true;
                }
            }

            lease = null;
            if (modern)
            {
                response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
            }
            else if (!string.IsNullOrEmpty(sessionId))
            {
                SetResponseSessionId(response, sessionId);
                SetResponseProtocolVersion(response, sessionId);
            }

            SendJson(response, JsonRpcResponse.MakeError(requestId, MainThreadBusyErrorCode,
                "MCP server is busy; retry after pending game-thread work completes", new JObject
                {
                    ["reasonCode"] = "server_busy",
                    ["retryable"] = true,
                    ["maxPendingRequests"] = MaxPendingMainThreadHttpRequests
                }), (int)HttpStatusCode.ServiceUnavailable);
            return false;
        }

        private void EnqueueAdmittedMainThread(MainThreadHttpAdmissionLease lease, Action action,
            Action staleAction = null)
        {
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            try
            {
                MainThreadBridge.Enqueue(() =>
                {
                    try
                    {
                        if (lease.IsCurrentGeneration())
                            action();
                        else
                            staleAction?.Invoke();
                    }
                    finally
                    {
                        lease.Release();
                    }
                });
            }
            catch
            {
                lease.Release();
                throw;
            }
        }

        private void ResetMainThreadHttpAdmission()
        {
            lock (_mainThreadAdmissionLock)
            {
                unchecked { _mainThreadAdmissionGeneration++; }
                _pendingMainThreadHttpRequests = 0;
            }
        }

        private bool IsMainThreadHttpAdmissionCurrent(int generation)
        {
            lock (_mainThreadAdmissionLock)
            {
                return _running && generation == _mainThreadAdmissionGeneration;
            }
        }

        private void ReleaseMainThreadHttpAdmission(int generation)
        {
            lock (_mainThreadAdmissionLock)
            {
                if (generation != _mainThreadAdmissionGeneration)
                    return;
                if (_pendingMainThreadHttpRequests <= 0)
                    throw new InvalidOperationException("Main-thread HTTP admission accounting underflow.");
                _pendingMainThreadHttpRequests--;
            }
        }

        private static void CloseStaleHttpResponse(HttpListenerResponse response)
        {
            try
            {
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                response.ContentLength64 = 0;
                response.Close();
            }
            catch { }
        }

        private sealed class MainThreadHttpAdmissionLease
        {
            private McpHttpServer _owner;
            private readonly int _generation;

            internal MainThreadHttpAdmissionLease(McpHttpServer owner, int generation)
            {
                _owner = owner;
                _generation = generation;
            }

            internal bool IsCurrentGeneration()
            {
                var owner = Volatile.Read(ref _owner);
                return owner != null && owner.IsMainThreadHttpAdmissionCurrent(_generation);
            }

            internal void Release()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null)
                    owner.ReleaseMainThreadHttpAdmission(_generation);
            }
        }
    }
}
