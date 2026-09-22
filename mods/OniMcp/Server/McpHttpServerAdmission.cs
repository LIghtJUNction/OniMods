using System;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        // This is intentionally separate from Unity/main-thread admission below.
        // Eight front-door slots preserve ordinary MCP client concurrency while
        // leaving worker-pool headroom to accept and reject a stalled-body overload.
        // The host regression exercises this boundary with real partial TCP bodies.
        internal const int MaxPendingHttpFrontDoorRequests = 8;

        // A single batch request is already capped at 20 child calls. Keeping the
        // external request backlog to the same finite width permits normal parallel
        // reads without allowing an arbitrary number of Unity-thread actions to pile up.
        internal const int MaxPendingMainThreadHttpRequests = 20;
        // MCP 2026-07-28 reserves -32000..-32019 for implementation-defined server errors.
        internal const int MainThreadBusyErrorCode = -32000;

        private readonly object _httpFrontDoorAdmissionLock = new object();
        private int _httpFrontDoorAdmissionGeneration;
        private int _pendingHttpFrontDoorRequests;

        private readonly object _mainThreadAdmissionLock = new object();
        private int _mainThreadAdmissionGeneration;
        private int _pendingMainThreadHttpRequests;

        private bool TryAcquireHttpFrontDoorAdmission(out HttpFrontDoorAdmissionLease lease)
        {
            lock (_httpFrontDoorAdmissionLock)
            {
                if (_running && _pendingHttpFrontDoorRequests < MaxPendingHttpFrontDoorRequests)
                {
                    _pendingHttpFrontDoorRequests++;
                    lease = new HttpFrontDoorAdmissionLease(this, _httpFrontDoorAdmissionGeneration);
                    return true;
                }
            }

            lease = null;
            return false;
        }

        private static void RejectHttpFrontDoorBusy(HttpListenerResponse response)
        {
            try
            {
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                response.KeepAlive = false;
                response.ContentLength64 = 0;
                response.Close();
            }
            catch { }
        }

        private void ResetHttpFrontDoorAdmission()
        {
            lock (_httpFrontDoorAdmissionLock)
            {
                unchecked { _httpFrontDoorAdmissionGeneration++; }
                _pendingHttpFrontDoorRequests = 0;
            }
        }

        private bool IsHttpFrontDoorAdmissionCurrent(int generation)
        {
            lock (_httpFrontDoorAdmissionLock)
            {
                return _running && generation == _httpFrontDoorAdmissionGeneration;
            }
        }

        private void ReleaseHttpFrontDoorAdmission(int generation)
        {
            lock (_httpFrontDoorAdmissionLock)
            {
                if (generation != _httpFrontDoorAdmissionGeneration)
                    return;
                if (_pendingHttpFrontDoorRequests <= 0)
                    throw new InvalidOperationException("HTTP front-door admission accounting underflow.");
                _pendingHttpFrontDoorRequests--;
            }
        }

        private bool TryAcquireMainThreadHttpAdmission(HttpListenerResponse response, object requestId,
            string sessionId, bool modern, out MainThreadHttpAdmissionLease lease)
        {
            // On the legacy POST path, sessionless requests can reach this point only
            // for initialize: ping returns earlier and all other methods require a session.
            // Capacity is therefore deterministic transport state and must not consume
            // or be masked by a scarce Unity/main-thread admission slot.
            if (!modern && string.IsNullOrEmpty(sessionId) && TryRejectNewLegacySessionAtCapacity(response))
            {
                lease = null;
                return false;
            }

            if (!modern && !string.IsNullOrEmpty(sessionId))
                PruneExpiredLegacySessionsBeforeMainThreadAdmission();

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

        private void EnqueueAdmittedMainThread(MainThreadHttpAdmissionLease lease, System.Action action,
            System.Action staleAction = null)
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

        private sealed class HttpFrontDoorAdmissionLease
        {
            private McpHttpServer _owner;
            private readonly int _generation;

            internal HttpFrontDoorAdmissionLease(McpHttpServer owner, int generation)
            {
                _owner = owner;
                _generation = generation;
            }

            internal bool IsCurrentGeneration()
            {
                var owner = Volatile.Read(ref _owner);
                return owner != null && owner.IsHttpFrontDoorAdmissionCurrent(_generation);
            }

            internal void Release()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null)
                    owner.ReleaseHttpFrontDoorAdmission(_generation);
            }
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
