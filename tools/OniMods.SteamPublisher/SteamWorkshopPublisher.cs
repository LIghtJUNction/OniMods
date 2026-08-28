using Steamworks;

internal static class SteamWorkshopPublisher
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(20);

    internal static void ValidateAccount()
    {
        if (!SteamUser.BLoggedOn())
        {
            throw new InvalidOperationException("Steam client is not logged in");
        }
        var steamId = SteamUser.GetSteamID().m_SteamID;
        if (steamId != WorkshopTarget.ExpectedOwner)
        {
            throw new InvalidOperationException(
                $"Steam account {steamId} is not the expected Workshop owner");
        }
    }

    internal static SteamUGCDetails_t QueryTarget()
    {
        var fileId = new PublishedFileId_t(WorkshopTarget.WorkshopId);
        var query = SteamUGC.CreateQueryUGCDetailsRequest([fileId], 1);
        SteamUGC.SetAllowCachedResponse(query, 0);
        try
        {
            var completed = WaitForCall<SteamUGCQueryCompleted_t>(
                SteamUGC.SendQueryUGCRequest(query), QueryTimeout, "Workshop query");
            if (completed.m_eResult != EResult.k_EResultOK
                || completed.m_unNumResultsReturned != 1)
            {
                throw new InvalidOperationException(
                    $"Workshop query failed: {completed.m_eResult}, results={completed.m_unNumResultsReturned}");
            }
            if (!SteamUGC.GetQueryUGCResult(query, 0, out var details))
            {
                throw new InvalidOperationException("SteamUGC.GetQueryUGCResult failed");
            }
            ValidateTarget(details);
            return details;
        }
        finally
        {
            SteamUGC.ReleaseQueryUGCRequest(query);
        }
    }

    internal static void SubmitUpdate(
        WorkshopMetadata metadata, SteamUGCDetails_t current, bool updatePreview)
    {
        var handle = StartUpdate();
        Require(SteamUGC.SetItemUpdateLanguage(handle, "english"), "SetItemUpdateLanguage");
        if (!string.Equals(metadata.Title, current.m_rgchTitle, StringComparison.Ordinal))
        {
            Require(SteamUGC.SetItemTitle(handle, metadata.Title), "SetItemTitle");
        }
        Require(
            SteamUGC.SetItemDescription(handle, metadata.EnglishDescription),
            "SetItemDescription");
        Require(SteamUGC.SetItemContent(handle, metadata.ContentFolder), "SetItemContent");
        if (updatePreview)
        {
            Require(SteamUGC.SetItemPreview(handle, metadata.PreviewFile), "SetItemPreview");
        }

        Console.WriteLine($"updatePreview={updatePreview}");
        ValidateUploadResult(WaitForUpload(handle, metadata.ChangeNote));
        SubmitLanguageUpdate("schinese", metadata.ChineseDescription);
    }

    internal static void SubmitLocalizedMetadata(WorkshopMetadata metadata)
    {
        SubmitLanguageUpdate("english", metadata.EnglishDescription);
        SubmitLanguageUpdate("schinese", metadata.ChineseDescription);
    }

    private static void SubmitLanguageUpdate(string language, string description)
    {
        var handle = StartUpdate();
        Require(SteamUGC.SetItemUpdateLanguage(handle, language), "SetItemUpdateLanguage");
        Require(SteamUGC.SetItemDescription(handle, description), "SetItemDescription");
        Console.WriteLine($"updateLanguage={language}");
        ValidateUploadResult(WaitForUpload(handle, string.Empty));
    }

    private static UGCUpdateHandle_t StartUpdate()
    {
        return SteamUGC.StartItemUpdate(
            new AppId_t(WorkshopTarget.AppId),
            new PublishedFileId_t(WorkshopTarget.WorkshopId));
    }

    private static void ValidateUploadResult(SubmitItemUpdateResult_t result)
    {
        if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            throw new InvalidOperationException("Steam requires the Workshop legal agreement");
        }
        if (result.m_nPublishedFileId.m_PublishedFileId != WorkshopTarget.WorkshopId)
        {
            throw new InvalidOperationException("Steam returned a different Workshop ID");
        }
    }

    private static SubmitItemUpdateResult_t WaitForUpload(
        UGCUpdateHandle_t handle, string changeNote)
    {
        var completed = false;
        var ioFailure = false;
        var result = default(SubmitItemUpdateResult_t);
        using var callResult = CallResult<SubmitItemUpdateResult_t>.Create((value, failed) =>
        {
            result = value;
            ioFailure = failed;
            completed = true;
        });
        callResult.Set(SteamUGC.SubmitItemUpdate(handle, changeNote));

        PumpUploadCallbacks(handle, () => completed);
        if (!completed)
        {
            throw new TimeoutException("Workshop upload timed out");
        }
        if (ioFailure || result.m_eResult != EResult.k_EResultOK)
        {
            throw new InvalidOperationException(
                $"Workshop upload failed: ioFailure={ioFailure}, result={result.m_eResult}, "
                + $"legalAgreement={result.m_bUserNeedsToAcceptWorkshopLegalAgreement}");
        }
        return result;
    }

    private static void PumpUploadCallbacks(UGCUpdateHandle_t handle, Func<bool> completed)
    {
        var deadline = DateTime.UtcNow + UploadTimeout;
        var nextProgress = DateTime.MinValue;
        while (!completed() && DateTime.UtcNow < deadline)
        {
            SteamAPI.RunCallbacks();
            if (DateTime.UtcNow >= nextProgress)
            {
                var status = SteamUGC.GetItemUpdateProgress(handle, out var sent, out var total);
                Console.WriteLine($"uploadStatus={status} bytes={sent}/{total}");
                nextProgress = DateTime.UtcNow.AddSeconds(2);
            }
            Thread.Sleep(50);
        }
    }

    private static T WaitForCall<T>(SteamAPICall_t call, TimeSpan timeout, string operation)
        where T : struct
    {
        var completed = false;
        var ioFailure = false;
        var result = default(T);
        using var callResult = CallResult<T>.Create((value, failed) =>
        {
            result = value;
            ioFailure = failed;
            completed = true;
        });
        callResult.Set(call);

        var deadline = DateTime.UtcNow + timeout;
        while (!completed && DateTime.UtcNow < deadline)
        {
            SteamAPI.RunCallbacks();
            Thread.Sleep(50);
        }
        if (!completed)
        {
            throw new TimeoutException($"{operation} timed out");
        }
        if (ioFailure)
        {
            throw new InvalidOperationException($"{operation} returned an I/O failure");
        }
        return result;
    }

    private static void ValidateTarget(SteamUGCDetails_t details)
    {
        if (details.m_nPublishedFileId.m_PublishedFileId != WorkshopTarget.WorkshopId
            || details.m_nConsumerAppID.m_AppId != WorkshopTarget.AppId
            || details.m_ulSteamIDOwner != WorkshopTarget.ExpectedOwner
            || (!string.IsNullOrEmpty(details.m_rgchTitle)
                && !details.m_rgchTitle.Contains(
                    WorkshopTarget.TitleContains, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Workshop target identity check failed for {WorkshopTarget.DisplayName}: "
                + $"id={details.m_nPublishedFileId.m_PublishedFileId}, "
                + $"consumerApp={details.m_nConsumerAppID.m_AppId}, "
                + $"owner={details.m_ulSteamIDOwner}, title={details.m_rgchTitle}");
        }
    }

    private static void Require(bool condition, string operation)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"SteamUGC.{operation} failed");
        }
    }

    internal static void PrintTarget(SteamUGCDetails_t details)
    {
        Console.WriteLine($"workshopId={details.m_nPublishedFileId.m_PublishedFileId}");
        Console.WriteLine($"consumerApp={details.m_nConsumerAppID.m_AppId}");
        Console.WriteLine($"owner={details.m_ulSteamIDOwner}");
        Console.WriteLine($"title={details.m_rgchTitle}");
        Console.WriteLine($"visibility={details.m_eVisibility}");
        Console.WriteLine($"banned={details.m_bBanned}");
        Console.WriteLine($"acceptedForUse={details.m_bAcceptedForUse}");
        Console.WriteLine($"fileSize={details.m_nFileSize}");
        Console.WriteLine($"updated={details.m_rtimeUpdated}");
    }
}
