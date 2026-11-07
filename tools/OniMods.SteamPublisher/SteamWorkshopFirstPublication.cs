using Steamworks;

internal static partial class SteamWorkshopPublisher
{
    internal static (ulong Id, FirstPublicationJournal Journal) PublishFirstPrivate(FirstPublicationPlan plan)
    {
        FirstPublicationPlan.RequireTarget();
        var package = plan.ReadPackage();
        var preview = plan.ReadPreview();
        var slug = string.Concat(WorkshopTarget.DisplayName.Select(ch => char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')).Trim('-');
        var zipName = $"onim_{slug}_first_{plan.ZipSha256[..16]}.zip";
        var previewName = $"onim_{slug}_first_{plan.PreviewSha256[..16]}{Path.GetExtension(plan.PreviewPath).ToLowerInvariant()}";
        ShareCloudFile(zipName, package.Bytes, $"first {WorkshopTarget.DisplayName} ZIP");
        ShareCloudFile(previewName, preview, $"first {WorkshopTarget.DisplayName} preview");
        var journal = FirstPublicationJournal.Begin(CandidateCreationJournal.DefaultDirectory(), plan.PlanSha256);
        bool callbackReceived = false;
        try
        {
            var result = WaitForCall<RemoteStoragePublishFileResult_t>(SteamRemoteStorage.PublishWorkshopFile(
                zipName, previewName, new AppId_t(457140), plan.Title, plan.EnglishDescription,
                ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
                plan.Tags, EWorkshopFileType.k_EWorkshopFileTypeCommunity), UploadTimeout, $"First {WorkshopTarget.DisplayName} Private Legacy creation");
            callbackReceived = true;
            var id = result.m_nPublishedFileId.m_PublishedFileId;
            journal.RecordCallback(result.m_eResult.ToString(), id, result.m_bUserNeedsToAcceptWorkshopLegalAgreement);
            if (result.m_eResult != EResult.k_EResultOK || result.m_bUserNeedsToAcceptWorkshopLegalAgreement || id is 0 or ulong.MaxValue)
                throw new InvalidOperationException($"First creation was not a usable success; journal={journal.Path}; do not retry create");
            Console.WriteLine($"firstWorkshopId={id}; creationJournal={journal.Path}");
            RequireFirstCreatedPage(plan, id);
            return (id, journal);
        }
        catch (Exception error) when (!callbackReceived)
        {
            journal.RecordUncertain(error.Message);
            throw;
        }
    }

    internal static void RequireFirstCreatedPage(FirstPublicationPlan plan, ulong id)
    {
        WaitForPromotionPage(id, page => page.Visibility == "Private" && page.Title == plan.Title
            && page.DescriptionEnglish == plan.EnglishDescription && page.FileSize == plan.ZipBytes,
            $"first Private {WorkshopTarget.DisplayName} Legacy package");
    }

    internal static void PublishFirstPublic(FirstPublicationPlan plan, ulong id)
    {
        SubmitLocalizedPage(id, "english", plan.Title, plan.EnglishDescription, plan.Tags);
        SubmitLocalizedPage(id, "schinese", plan.Title, plan.ChineseDescription, tags: null);
        RequireFirstFormalPage(plan, id, publicItem: false);
        var update = StartPromotionUpdate(id);
        Require(SteamUGC.SetItemVisibility(update,
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic), "SetItemVisibility Public");
        SubmitPromotionUpdate(update, id);
        RequireFirstFormalPage(plan, id, publicItem: true);
    }

    private static void RequireFirstFormalPage(FirstPublicationPlan plan, ulong id, bool publicItem)
    {
        WaitForPromotionPage(id, page => page.Visibility == (publicItem ? "Public" : "Private")
            && page.Title == plan.Title && page.TitleChinese == plan.Title
            && page.DescriptionEnglish == plan.EnglishDescription && page.DescriptionChinese == plan.ChineseDescription
            && page.FileSize == plan.ZipBytes && page.Tags.ToHashSet(StringComparer.Ordinal).SetEquals(plan.Tags),
            $"first {WorkshopTarget.DisplayName} bilingual page");
    }

    internal static void RequireNoOwnedTarget(FirstPublicationPlan plan)
    {
        var account = SteamUser.GetSteamID().GetAccountID();
        for (uint page = 1; page <= 10; page++)
        {
            var query = SteamUGC.CreateQueryUserUGCRequest(account, EUserUGCList.k_EUserUGCList_Published,
                EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items, EUserUGCListSortOrder.k_EUserUGCListSortOrder_LastUpdatedDesc,
                new AppId_t(0), new AppId_t(457140), page);
            try
            {
                Require(SteamUGC.SetAllowCachedResponse(query, 0), "SetAllowCachedResponse owned items");
                var result = WaitForCall<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query), QueryTimeout, "First-item owned-items duplicate check");
                if (result.m_eResult != EResult.k_EResultOK) throw new InvalidOperationException("First-item owned-items query failed");
                for (uint index = 0; index < result.m_unNumResultsReturned; index++)
                {
                    Require(SteamUGC.GetQueryUGCResult(query, index, out var item), "GetQueryUGCResult owned items");
                    if (item.m_eResult != EResult.k_EResultOK || item.m_ulSteamIDOwner != WorkshopTarget.ExpectedOwner)
                        throw new InvalidOperationException("First-item duplicate check returned an invalid owned item");
                    var id = item.m_nPublishedFileId.m_PublishedFileId;
                    if (item.m_rgchTitle.Equals(plan.Title, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"An owned item titled {plan.Title} already exists: {id}; inspect it instead of creating another");
                    var tagCount = SteamUGC.GetQueryUGCNumTags(query, index);
                    for (uint tagIndex = 0; tagIndex < tagCount; tagIndex++)
                    {
                        if (!SteamUGC.GetQueryUGCTag(query, index, tagIndex, out var tag, 1024))
                            throw new InvalidOperationException($"Could not read Workshop tag {tagIndex} for owned item {id}");
                        if (tag == plan.StaticId)
                            throw new InvalidOperationException($"An owned item already uses staticID {plan.StaticId}: {id}; inspect it instead of creating another");
                    }
                    if (InstalledStaticId(item.m_nPublishedFileId) == plan.StaticId)
                        throw new InvalidOperationException($"An installed Workshop ZIP already uses staticID {plan.StaticId}: {id}; inspect it instead of creating another");
                }
                if (result.m_unNumResultsReturned == 0 || page * 50 >= result.m_unTotalMatchingResults) return;
            }
            finally { SteamUGC.ReleaseQueryUGCRequest(query); }
        }
        throw new InvalidOperationException("First-item duplicate check exceeded its bounded page limit");
    }

    private static string? InstalledStaticId(PublishedFileId_t fileId)
    {
        var state = (EItemState)SteamUGC.GetItemState(fileId);
        if ((state & EItemState.k_EItemStateInstalled) == 0
            || (state & EItemState.k_EItemStateLegacyItem) == 0)
            return null;
        if (!SteamUGC.GetItemInstallInfo(fileId, out _, out var path, 4096, out _) || !File.Exists(path))
            return null;
        try { return FirstPublicationPlan.ReadPackagedStaticId(path); }
        catch (Exception error) when (error is InvalidOperationException or IOException or InvalidDataException)
        {
            return null;
        }
    }
}
