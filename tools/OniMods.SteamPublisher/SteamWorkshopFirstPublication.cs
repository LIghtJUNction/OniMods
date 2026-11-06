using Steamworks;

internal static partial class SteamWorkshopPublisher
{
    internal static (ulong Id, FirstPublicationJournal Journal) PublishFirstPrivate(FirstPublicationPlan plan)
    {
        FirstPublicationPlan.RequireTarget();
        RequireNoOwnedOniOnline();
        var package = plan.ReadPackage();
        var preview = plan.ReadPreview();
        var zipName = $"onim_onionline_first_{plan.ZipSha256[..16]}.zip";
        var previewName = $"onim_onionline_first_{plan.PreviewSha256[..16]}{Path.GetExtension(plan.PreviewPath).ToLowerInvariant()}";
        ShareCloudFile(zipName, package.Bytes, "first OniOnline ZIP");
        ShareCloudFile(previewName, preview, "first OniOnline preview");
        var journal = FirstPublicationJournal.Begin(CandidateCreationJournal.DefaultDirectory(), plan.PlanSha256);
        bool callbackReceived = false;
        try
        {
            var result = WaitForCall<RemoteStoragePublishFileResult_t>(SteamRemoteStorage.PublishWorkshopFile(
                zipName, previewName, new AppId_t(457140), plan.Title, plan.EnglishDescription,
                ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
                plan.Tags, EWorkshopFileType.k_EWorkshopFileTypeCommunity), UploadTimeout, "First OniOnline Private Legacy creation");
            callbackReceived = true;
            var id = result.m_nPublishedFileId.m_PublishedFileId;
            journal.RecordCallback(result.m_eResult.ToString(), id, result.m_bUserNeedsToAcceptWorkshopLegalAgreement);
            if (result.m_eResult != EResult.k_EResultOK || result.m_bUserNeedsToAcceptWorkshopLegalAgreement || id is 0 or ulong.MaxValue)
                throw new InvalidOperationException($"First OniOnline creation was not a usable success; journal={journal.Path}; do not retry create");
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
            "first Private OniOnline Legacy package");
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
            "first OniOnline bilingual page");
    }

    private static void RequireNoOwnedOniOnline()
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
                    if (item.m_rgchTitle.Contains("OniOnline", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"An owned OniOnline item already exists: {item.m_nPublishedFileId.m_PublishedFileId}; inspect it instead of creating another");
                }
                if (result.m_unNumResultsReturned == 0 || page * 50 >= result.m_unTotalMatchingResults) return;
            }
            finally { SteamUGC.ReleaseQueryUGCRequest(query); }
        }
        throw new InvalidOperationException("First-item duplicate check exceeded its bounded page limit");
    }
}
