using Steamworks;

internal static partial class SteamWorkshopPublisher
{
    internal static WorkshopPromotionSnapshot CapturePromotionSnapshot(
        ulong originalId, ulong newId)
    {
        var original = CapturePromotionPage(originalId);
        var candidate = CapturePromotionPage(newId);
        return new WorkshopPromotionSnapshot(
            DateTimeOffset.UtcNow, original, candidate);
    }

    internal static WorkshopPageSnapshot CapturePromotionPage(ulong itemId)
    {
        var english = QueryLocalizedPage(itemId, "english");
        var chinese = QueryLocalizedPage(itemId, "schinese");
        var details = english.Details;
        if (details.m_nPublishedFileId.m_PublishedFileId != itemId
            || chinese.Details.m_nPublishedFileId.m_PublishedFileId != itemId
            || details.m_nCreatorAppID.m_AppId != WorkshopTarget.CreatorAppId
            || details.m_nConsumerAppID.m_AppId != WorkshopTarget.ConsumerAppId
            || details.m_ulSteamIDOwner != WorkshopTarget.ExpectedOwner
            || chinese.Details.m_ulSteamIDOwner != WorkshopTarget.ExpectedOwner
            || details.m_eVisibility != chinese.Details.m_eVisibility
            || details.m_nFileSize != chinese.Details.m_nFileSize)
        {
            throw new InvalidOperationException(
                $"Promotion page {itemId} has inconsistent Steam identity or language results");
        }
        var visibility = details.m_eVisibility switch
        {
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic
                => "Public",
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate
                => "Private",
            _ => details.m_eVisibility.ToString(),
        };
        return new WorkshopPageSnapshot(
            itemId, details.m_nCreatorAppID.m_AppId,
            details.m_nConsumerAppID.m_AppId,
            details.m_ulSteamIDOwner,
            details.m_rgchTitle, chinese.Details.m_rgchTitle,
            details.m_rgchDescription, chinese.Details.m_rgchDescription,
            english.Tags, visibility, details.m_nFileSize,
            details.m_rtimeUpdated);
    }

    private static (SteamUGCDetails_t Details, string[] Tags) QueryLocalizedPage(
        ulong itemId, string language)
    {
        var fileId = new PublishedFileId_t(itemId);
        var query = SteamUGC.CreateQueryUGCDetailsRequest([fileId], 1);
        Require(SteamUGC.SetLanguage(query, language), "SetLanguage");
        Require(SteamUGC.SetReturnLongDescription(query, true),
            "SetReturnLongDescription");
        Require(SteamUGC.SetAllowCachedResponse(query, 0),
            "SetAllowCachedResponse");
        try
        {
            var completed = WaitForCall<SteamUGCQueryCompleted_t>(
                SteamUGC.SendQueryUGCRequest(query), QueryTimeout,
                $"{language} Workshop page query");
            if (completed.m_eResult != EResult.k_EResultOK
                || completed.m_unNumResultsReturned != 1
                || !SteamUGC.GetQueryUGCResult(query, 0, out var details))
            {
                throw new InvalidOperationException(
                    $"{language} Workshop page query failed: {completed.m_eResult}");
            }
            var tags = new List<string>();
            var tagCount = SteamUGC.GetQueryUGCNumTags(query, 0);
            for (uint index = 0; index < tagCount; index++)
            {
                if (!SteamUGC.GetQueryUGCTag(query, 0, index,
                    out var tag, 1024))
                {
                    throw new InvalidOperationException(
                        $"Could not read Workshop tag {index} for {itemId}");
                }
                tags.Add(tag);
            }
            return (details, tags.ToArray());
        }
        finally
        {
            SteamUGC.ReleaseQueryUGCRequest(query);
        }
    }
}
