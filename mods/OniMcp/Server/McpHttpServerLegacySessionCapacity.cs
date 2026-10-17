using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private void PruneExpiredLegacySessionsBeforeMainThreadAdmission()
        {
            List<McpSession> prunedSessions = null;
            lock (_sessionLock)
            {
                // The request's own session was just refreshed by transport validation.
                // With no other retained session there is nothing stale to discover.
                if (_sessions.Count > 1)
                {
                    System.DateTime now = _legacySessionPolicy.UtcNow();
                    prunedSessions = PruneExpiredLegacySessionsLocked(now);
                }
            }

            ClosePrunedLegacySessions(prunedSessions);
        }

        private bool TryRejectNewLegacySessionAtCapacity(HttpListenerResponse response)
        {
            List<McpSession> prunedSessions;
            bool capacityExceeded;
            System.DateTime now = _legacySessionPolicy.UtcNow();

            lock (_sessionLock)
            {
                prunedSessions = PruneExpiredLegacySessionsLocked(now);
                capacityExceeded = _sessions.Count >= _legacySessionPolicy.MaxRetainedSessions;
            }

            ClosePrunedLegacySessions(prunedSessions);
            if (!capacityExceeded)
                return false;

            SendJson(response, JsonRpcResponse.MakeError(null, MainThreadBusyErrorCode,
                "Legacy MCP session capacity is exhausted; retry after an idle session expires or is deleted",
                new JObject
                {
                    ["reasonCode"] = "legacy_session_capacity",
                    ["retryable"] = true,
                    ["maxRetainedSessions"] = _legacySessionPolicy.MaxRetainedSessions
                }), (int)HttpStatusCode.ServiceUnavailable);
            return true;
        }
    }
}
