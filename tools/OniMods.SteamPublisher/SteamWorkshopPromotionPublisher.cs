using Steamworks;
using System.Security.Cryptography;

internal static partial class SteamWorkshopPublisher
{
    internal static void RequireOriginalBaseline(WorkshopPromotionPlan plan)
    {
        var current = CapturePromotionPage(plan.OriginalId);
        var baseline = plan.Baseline.Original;
        if (current.Title != baseline.Title
            || current.TitleChinese != baseline.TitleChinese
            || current.DescriptionEnglish != baseline.DescriptionEnglish
            || current.DescriptionChinese != baseline.DescriptionChinese
            || current.Visibility != "Public"
            || current.FileSize != baseline.FileSize
            || current.UpdatedAt != baseline.UpdatedAt
            || !current.Tags.ToHashSet(StringComparer.Ordinal)
                .SetEquals(baseline.Tags))
        {
            throw new InvalidOperationException(
                "Old public item changed since the reviewed snapshot; regenerate the plan");
        }
    }

    internal static void RequirePrivateCandidateBaseline(WorkshopPromotionPlan plan)
    {
        var current = CapturePromotionPage(plan.NewId);
        var baseline = plan.Baseline.Candidate;
        if (current.Title != baseline.Title
            || current.TitleChinese != baseline.TitleChinese
            || current.DescriptionEnglish != baseline.DescriptionEnglish
            || current.DescriptionChinese != baseline.DescriptionChinese
            || current.Visibility != "Private"
            || current.FileSize != plan.ZipBytes
            || current.UpdatedAt != baseline.UpdatedAt
            || !current.Tags.ToHashSet(StringComparer.Ordinal)
                .SetEquals(baseline.Tags))
        {
            throw new InvalidOperationException(
                "Private candidate changed since the reviewed snapshot; regenerate the plan");
        }
    }

    internal static void SubmitPrivateMetadata(WorkshopPromotionPlan plan)
    {
        SubmitLocalizedPage(plan.NewId, "english", plan.NewTitle,
            plan.NewDescriptionEnglish, plan.NewTags);
        WaitForPromotionPage(plan.NewId, page =>
            page.Visibility == "Private"
            && page.Title == plan.NewTitle
            && page.DescriptionEnglish == plan.NewDescriptionEnglish
            && page.FileSize == plan.ZipBytes
            && page.Tags.ToHashSet(StringComparer.Ordinal).SetEquals(plan.NewTags),
            "new English metadata while Private");
        SubmitLocalizedPage(plan.NewId, "schinese", plan.NewTitleChinese,
            plan.NewDescriptionChinese, tags: null);
        RequireFormalNewPage(plan, requirePublic: false);
    }

    internal static void RequireFormalNewPage(
        WorkshopPromotionPlan plan, bool requirePublic)
    {
        WaitForPromotionPage(plan.NewId, page =>
            page.Visibility == (requirePublic ? "Public" : "Private")
            && page.Title == plan.NewTitle
            && page.TitleChinese == plan.NewTitleChinese
            && page.DescriptionEnglish == plan.NewDescriptionEnglish
            && page.DescriptionChinese == plan.NewDescriptionChinese
            && page.FileSize == plan.ZipBytes
            && page.Tags.ToHashSet(StringComparer.Ordinal).SetEquals(plan.NewTags),
            requirePublic ? "new Public formal page" : "new Private formal page");
    }

    internal static void SubmitPublicVisibility(WorkshopPromotionPlan plan)
    {
        var handle = StartPromotionUpdate(plan.NewId);
        Require(SteamUGC.SetItemVisibility(handle,
            ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic),
            "SetItemVisibility Public");
        SubmitPromotionUpdate(handle, plan.NewId);
        RequireFormalNewPage(plan, requirePublic: true);
    }

    internal static void SubmitOldMigrationCopy(WorkshopPromotionPlan plan)
    {
        SubmitLocalizedPage(plan.OriginalId, "english", plan.OldTitle,
            plan.OldDescriptionEnglish, tags: null);
        WaitForPromotionPage(plan.OriginalId, page =>
            page.Visibility == "Public"
            && page.Title == plan.OldTitle
            && page.DescriptionEnglish == plan.OldDescriptionEnglish
            && page.FileSize == plan.Baseline.Original.FileSize
            && page.Tags.ToHashSet(StringComparer.Ordinal)
                .SetEquals(plan.Baseline.Original.Tags),
            "old English migration copy");
        SubmitLocalizedPage(plan.OriginalId, "schinese", plan.OldTitleChinese,
            plan.OldDescriptionChinese, tags: null);
        WaitForPromotionPage(plan.OriginalId, page =>
            page.Visibility == "Public"
            && page.Title == plan.OldTitle
            && page.TitleChinese == plan.OldTitleChinese
            && page.DescriptionEnglish == plan.OldDescriptionEnglish
            && page.DescriptionChinese == plan.OldDescriptionChinese
            && page.FileSize == plan.Baseline.Original.FileSize
            && page.Tags.ToHashSet(StringComparer.Ordinal)
                .SetEquals(plan.Baseline.Original.Tags),
            "old bilingual migration copy");
    }

    internal static void RequireInstalledLegacyReadOnly(
        WorkshopPromotionPlan plan)
    {
        var id = new PublishedFileId_t(plan.NewId);
        var state = (EItemState)SteamUGC.GetItemState(id);
        if ((state & EItemState.k_EItemStateInstalled) == 0
            || (state & EItemState.k_EItemStateLegacyItem) == 0
            || (state & EItemState.k_EItemStateNeedsUpdate) != 0
            || !SteamUGC.GetItemInstallInfo(
                id, out _, out var installedPath, 4096, out _)
            || !File.Exists(installedPath)
            || Directory.Exists(installedPath))
        {
            throw new InvalidOperationException(
                $"Private candidate is not an installed Legacy ZIP file: state={state}");
        }
        LegacyPackage.ValidateArchive(installedPath);
        var file = new FileInfo(installedPath);
        var digest = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(installedPath)));
        if (file.Length != plan.ZipBytes
            || !string.Equals(digest, plan.ZipSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Installed Legacy ZIP differs from the reviewed promotion package");
        }
        Console.WriteLine($"recoveryInstalledLegacyZip={installedPath}");
        Console.WriteLine($"recoveryInstalledState={state}");
        Console.WriteLine($"recoveryInstalledSha256={digest}");
    }

    private static void SubmitLocalizedPage(
        ulong itemId, string language, string title,
        string description, string[]? tags)
    {
        var handle = StartPromotionUpdate(itemId);
        Require(SteamUGC.SetItemUpdateLanguage(handle, language),
            "SetItemUpdateLanguage");
        Require(SteamUGC.SetItemTitle(handle, title), "SetItemTitle");
        Require(SteamUGC.SetItemDescription(handle, description),
            "SetItemDescription");
        if (tags is not null)
        {
            Require(SteamUGC.SetItemTags(handle, tags), "SetItemTags");
        }
        SubmitPromotionUpdate(handle, itemId);
    }

    private static UGCUpdateHandle_t StartPromotionUpdate(ulong itemId)
    {
        var handle = SteamUGC.StartItemUpdate(
            new AppId_t(WorkshopTarget.ConsumerAppId),
            new PublishedFileId_t(itemId));
        if (handle.m_UGCUpdateHandle == ulong.MaxValue)
        {
            throw new InvalidOperationException("Steam returned an invalid item update handle");
        }
        return handle;
    }

    private static void SubmitPromotionUpdate(UGCUpdateHandle_t handle, ulong itemId)
    {
        var result = WaitForUpload(handle, string.Empty);
        if (result.m_nPublishedFileId.m_PublishedFileId != itemId
            || result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            throw new InvalidOperationException(
                $"Promotion update callback targeted the wrong item or legal agreement: "
                + $"id={result.m_nPublishedFileId.m_PublishedFileId}, "
                + $"needsAgreement={result.m_bUserNeedsToAcceptWorkshopLegalAgreement}");
        }
    }

    private static void WaitForPromotionPage(
        ulong itemId, Func<WorkshopPageSnapshot, bool> valid,
        string purpose)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        var lastError = "no result";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var page = CapturePromotionPage(itemId);
                if (valid(page))
                {
                    Console.WriteLine($"promotionReadback={purpose} itemId={itemId}");
                    return;
                }
                lastError = $"visibility={page.Visibility}, title={page.Title}, "
                    + $"fileSize={page.FileSize}, tags={string.Join(",", page.Tags)}";
            }
            catch (Exception error) when (
                error is InvalidOperationException or TimeoutException)
            {
                lastError = error.Message;
            }
            Thread.Sleep(5000);
        }
        throw new TimeoutException(
            $"Timed out waiting for {purpose} readback: {lastError}");
    }
}
