using System;
using System.Collections.Generic;
using System.IO;
using Steamworks;

namespace OniOnline.Runtime
{
    // Downloads through the user's existing Steam client only. This class has no publishing API.
    internal sealed class SteamWorkshopModContentApi : ISteamModContentApi
    {
        private sealed class Query
        {
            internal UGCQueryHandle_t Handle;
            internal CallResult<SteamUGCQueryCompleted_t> Result;
        }
        private readonly List<Query> queries = new List<Query>();
        private readonly List<CallResult<RemoteStorageSubscribePublishedFileResult_t>> subscriptions =
            new List<CallResult<RemoteStorageSubscribePublishedFileResult_t>>();
        private Callback<DownloadItemResult_t> downloadResult;
        private bool disposed;
        public event System.Action<SteamModOperation> DownloadCompleted;

        internal SteamWorkshopModContentApi()
        {
            if (!Available()) throw new InvalidOperationException("steam_client_unavailable");
            downloadResult = Callback<DownloadItemResult_t>.Create(result =>
            {
                if (disposed) return;
                DownloadCompleted?.Invoke(new SteamModOperation
                {
                    ItemId = result.m_nPublishedFileId.m_PublishedFileId,
                    AppId = result.m_unAppID.m_AppId,
                    Success = result.m_eResult == EResult.k_EResultOK
                });
            });
        }

        public bool Inspect(ulong itemId, System.Action<SteamModDetails> completed)
        {
            if (disposed || itemId == 0 || completed == null || !Available()) return false;
            var query = new Query { Handle = UGCQueryHandle_t.Invalid };
            try
            {
                query.Handle = SteamUGC.CreateQueryUGCDetailsRequest(new[] { new PublishedFileId_t(itemId) }, 1);
                if (query.Handle == UGCQueryHandle_t.Invalid) return false;
                queries.Add(query);
                query.Result = CallResult<SteamUGCQueryCompleted_t>.Create((result, ioFailure) =>
                {
                    if (disposed) return;
                    var details = new SteamModDetails();
                    try
                    {
                        if (!ioFailure && result.m_eResult == EResult.k_EResultOK && result.m_handle == query.Handle &&
                            result.m_unNumResultsReturned == 1 && Available() &&
                            SteamUGC.GetQueryUGCResult(query.Handle, 0, out var native))
                        {
                            details.ItemId = native.m_nPublishedFileId.m_PublishedFileId;
                            details.ConsumerAppId = native.m_nConsumerAppID.m_AppId;
                            details.Success = native.m_eResult == EResult.k_EResultOK;
                            details.Banned = native.m_bBanned;
                        }
                    }
                    catch { details.Success = false; }
                    finally { Release(query); }
                    completed(details);
                });
                var call = SteamUGC.SendQueryUGCRequest(query.Handle);
                if (call == SteamAPICall_t.Invalid) { Release(query); return false; }
                query.Result.Set(call);
                return true;
            }
            catch { Release(query); return false; }
        }

        public bool Subscribe(ulong itemId, System.Action<SteamModOperation> completed)
        {
            if (disposed || itemId == 0 || completed == null || !Available()) return false;
            CallResult<RemoteStorageSubscribePublishedFileResult_t> callback = null;
            try
            {
                callback = CallResult<RemoteStorageSubscribePublishedFileResult_t>.Create((result, ioFailure) =>
                {
                    subscriptions.Remove(callback);
                    callback.Dispose();
                    if (!disposed) completed(new SteamModOperation
                    {
                        ItemId = result.m_nPublishedFileId.m_PublishedFileId,
                        Success = !ioFailure && result.m_eResult == EResult.k_EResultOK
                    });
                });
                subscriptions.Add(callback);
                var call = SteamUGC.SubscribeItem(new PublishedFileId_t(itemId));
                if (call == SteamAPICall_t.Invalid)
                { subscriptions.Remove(callback); callback.Dispose(); return false; }
                callback.Set(call);
                return true;
            }
            catch
            {
                if (callback != null) { subscriptions.Remove(callback); callback.Dispose(); }
                return false;
            }
        }

        public bool Download(ulong itemId)
        { return !disposed && itemId != 0 && Available() && SteamUGC.DownloadItem(new PublishedFileId_t(itemId), false); }

        public bool TryGetState(ulong itemId, out SteamModDownloadState state)
        {
            state = null;
            if (disposed || itemId == 0 || !Available()) return false;
            var id = new PublishedFileId_t(itemId);
            var flags = (EItemState)SteamUGC.GetItemState(id);
            state = new SteamModDownloadState
            {
                Subscribed = (flags & EItemState.k_EItemStateSubscribed) != 0,
                Installed = (flags & EItemState.k_EItemStateInstalled) != 0,
                NeedsUpdate = (flags & EItemState.k_EItemStateNeedsUpdate) != 0,
                Downloading = (flags & EItemState.k_EItemStateDownloading) != 0,
                Pending = (flags & EItemState.k_EItemStateDownloadPending) != 0
            };
            if (SteamUGC.GetItemDownloadInfo(id, out var downloaded, out var total))
            { state.DownloadedBytes = downloaded; state.TotalBytes = total; }
            // ONI's Legacy ZIP package here may be a file, not a directory. Do not unpack or execute it.
            if (state.Installed && SteamUGC.GetItemInstallInfo(id, out var size, out var folder, 4096, out var timestamp))
                state.HasInstallInfo = !string.IsNullOrEmpty(folder) && (Directory.Exists(folder) || File.Exists(folder));
            return true;
        }

        private static bool Available()
        {
            try { return SteamRoomCleanup.ClientAvailable; }
            catch { return false; }
        }

        private void Release(Query query)
        {
            queries.Remove(query);
            try { query.Result?.Dispose(); } catch { }
            if (query.Handle == UGCQueryHandle_t.Invalid) return;
            try { if (Available()) SteamUGC.ReleaseQueryUGCRequest(query.Handle); } catch { }
            query.Handle = UGCQueryHandle_t.Invalid;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelPendingRequests();
            try { downloadResult?.Dispose(); } catch { }
            downloadResult = null;
            DownloadCompleted = null;
        }

        public void CancelPendingRequests()
        {
            foreach (var query in queries.ToArray()) Release(query);
            foreach (var subscription in subscriptions.ToArray())
                try { subscription.Dispose(); } catch { }
            subscriptions.Clear();
        }
    }
}
