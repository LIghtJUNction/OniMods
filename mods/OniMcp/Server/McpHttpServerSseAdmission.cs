using System;
using System.Net;
using System.Threading;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        // Legacy SSE streams are intentionally long-lived. Keep them bounded, but
        // separate from the pre-body/front-door quota used to protect POST reads.
        internal const int MaxPendingLegacySseConnections = 8;

        private readonly object _legacySseAdmissionLock = new object();
        private int _legacySseAdmissionGeneration;
        private int _pendingLegacySseConnections;

        private bool TryAcquireLegacySseAdmission(HttpListenerResponse response,
            out LegacySseAdmissionLease lease)
        {
            lock (_legacySseAdmissionLock)
            {
                if (_running && _pendingLegacySseConnections < MaxPendingLegacySseConnections)
                {
                    _pendingLegacySseConnections++;
                    lease = new LegacySseAdmissionLease(this, _legacySseAdmissionGeneration);
                    return true;
                }
            }

            lease = null;
            try
            {
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                response.KeepAlive = false;
                response.ContentLength64 = 0;
                response.Close();
            }
            catch { }
            return false;
        }

        private void ResetLegacySseAdmission()
        {
            lock (_legacySseAdmissionLock)
            {
                unchecked { _legacySseAdmissionGeneration++; }
                _pendingLegacySseConnections = 0;
            }
        }

        private bool IsLegacySseAdmissionCurrent(int generation)
        {
            lock (_legacySseAdmissionLock)
            {
                return _running && generation == _legacySseAdmissionGeneration;
            }
        }

        private void ReleaseLegacySseAdmission(int generation)
        {
            lock (_legacySseAdmissionLock)
            {
                if (generation != _legacySseAdmissionGeneration)
                    return;
                if (_pendingLegacySseConnections <= 0)
                    throw new InvalidOperationException("Legacy SSE admission accounting underflow.");
                _pendingLegacySseConnections--;
            }
        }

        private sealed class LegacySseAdmissionLease
        {
            private McpHttpServer _owner;
            private readonly int _generation;

            internal LegacySseAdmissionLease(McpHttpServer owner, int generation)
            {
                _owner = owner;
                _generation = generation;
            }

            internal bool IsCurrentGeneration()
            {
                var owner = Volatile.Read(ref _owner);
                return owner != null && owner.IsLegacySseAdmissionCurrent(_generation);
            }

            internal void Release()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null)
                    owner.ReleaseLegacySseAdmission(_generation);
            }
        }
    }
}
