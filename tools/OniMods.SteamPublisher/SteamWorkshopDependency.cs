using Steamworks;

internal static partial class SteamWorkshopPublisher
{
    internal static WorkshopDependencyItem QueryDependencyItem(ulong id)
    {
        var query = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(id)], 1);
        try
        {
            Require(SteamUGC.SetAllowCachedResponse(query, 0), "SetAllowCachedResponse dependencies");
            Require(SteamUGC.SetLanguage(query, "english"), "SetLanguage dependencies");
            Require(SteamUGC.SetReturnChildren(query, true), "SetReturnChildren");
            var result = WaitForCall<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query),
                QueryTimeout, "Workshop dependency query");
            if (result.m_eResult != EResult.k_EResultOK || result.m_unNumResultsReturned != 1)
                throw new InvalidOperationException("Workshop dependency query did not return one successful result");
            Require(SteamUGC.GetQueryUGCResult(query, 0, out var item), "GetQueryUGCResult dependency");
            if (item.m_eResult != EResult.k_EResultOK || item.m_nPublishedFileId.m_PublishedFileId != id
                || item.m_unNumChildren > 4096)
                throw new InvalidOperationException("Workshop dependency query identity or children budget failed");
            var children = new PublishedFileId_t[item.m_unNumChildren];
            if (children.Length != 0)
                Require(SteamUGC.GetQueryUGCChildren(query, 0, children, item.m_unNumChildren), "GetQueryUGCChildren");
            return new WorkshopDependencyItem(id, item.m_nCreatorAppID.m_AppId, item.m_nConsumerAppID.m_AppId,
                item.m_ulSteamIDOwner, item.m_rgchTitle, item.m_bBanned,
                item.m_eVisibility == ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
                item.m_eFileType == EWorkshopFileType.k_EWorkshopFileTypeCommunity,
                item.m_hFile.m_UGCHandle, item.m_nFileSize,
                children.Select(child => child.m_PublishedFileId).ToArray());
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(query); }
    }

    internal static WorkshopDependencyCallback AddDependency(ulong parent, ulong child)
    {
        var result = WaitForCall<AddUGCDependencyResult_t>(SteamUGC.AddDependency(
            new PublishedFileId_t(parent), new PublishedFileId_t(child)), QueryTimeout, "AddDependency");
        return new WorkshopDependencyCallback(result.m_eResult == EResult.k_EResultOK,
            result.m_nPublishedFileId.m_PublishedFileId, result.m_nChildPublishedFileId.m_PublishedFileId,
            result.m_eResult.ToString());
    }
}

internal sealed class SteamWorkshopDependencyApi : IWorkshopDependencyApi
{
    public WorkshopDependencyItem Query(ulong id) => SteamWorkshopPublisher.QueryDependencyItem(id);
    public WorkshopDependencyCallback Add(ulong parent, ulong child) => SteamWorkshopPublisher.AddDependency(parent, child);
}
