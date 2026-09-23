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

    internal static void SubmitLegacyUpdate(
        WorkshopMetadata metadata, LegacyPackage package, SteamUGCDetails_t current,
        bool updatePreview)
    {
        // ONI's installed-mod path expects a legacy Workshop file containing a ZIP.
        // SteamUGC.SetItemContent always uploads a directory, even if it holds one ZIP.
        Require(
            SteamRemoteStorage.FileWrite(
                package.CloudFileName, package.Bytes, package.Bytes.Length),
            "FileWrite");
        var shared = WaitForCall<RemoteStorageFileShareResult_t>(
            SteamRemoteStorage.FileShare(package.CloudFileName),
            QueryTimeout, "Workshop file share");
        if (shared.m_eResult != EResult.k_EResultOK)
        {
            throw new InvalidOperationException(
                $"Workshop file share failed: {shared.m_eResult}");
        }

        var handle = SteamRemoteStorage.CreatePublishedFileUpdateRequest(
            new PublishedFileId_t(WorkshopTarget.WorkshopId));
        if (handle.m_PublishedFileUpdateHandle == ulong.MaxValue)
        {
            throw new InvalidOperationException("Legacy Workshop update handle is invalid");
        }
        Require(
            SteamRemoteStorage.UpdatePublishedFileFile(handle, package.CloudFileName),
            "UpdatePublishedFileFile");
        if (!string.Equals(metadata.Title, current.m_rgchTitle, StringComparison.Ordinal))
        {
            Require(
                SteamRemoteStorage.UpdatePublishedFileTitle(handle, metadata.Title),
                "UpdatePublishedFileTitle");
        }
        Require(
            SteamRemoteStorage.UpdatePublishedFileDescription(
                handle, metadata.EnglishDescription),
            "UpdatePublishedFileDescription");
        if (updatePreview)
        {
            // PreviewFile is a local file. The RemoteStorage update API requires
            // a Steam Cloud filename; stage and share it before updating.
            var previewBytes = File.ReadAllBytes(metadata.PreviewFile);
            var previewName = $"onim_{WorkshopTarget.WorkshopId}_preview.png";
            Require(SteamRemoteStorage.FileWrite(
                previewName, previewBytes, previewBytes.Length), "Preview FileWrite");
            var previewShare = WaitForCall<RemoteStorageFileShareResult_t>(
                SteamRemoteStorage.FileShare(previewName),
                QueryTimeout, "Workshop preview share");
            if (previewShare.m_eResult != EResult.k_EResultOK)
            {
                throw new InvalidOperationException(
                    $"Workshop preview share failed: {previewShare.m_eResult}");
            }
            Require(SteamRemoteStorage.UpdatePublishedFilePreviewFile(handle, previewName),
                "UpdatePublishedFilePreviewFile");
        }

        Console.WriteLine($"updatePreview={updatePreview}");
        if (!string.IsNullOrWhiteSpace(metadata.ChangeNote))
        {
            Require(SteamRemoteStorage.UpdatePublishedFileSetChangeDescription(
                handle, metadata.ChangeNote), "UpdatePublishedFileSetChangeDescription");
        }
        var result = WaitForCall<RemoteStorageUpdatePublishedFileResult_t>(
            SteamRemoteStorage.CommitPublishedFileUpdate(handle),
            UploadTimeout, "Legacy Workshop update");
        if (result.m_eResult != EResult.k_EResultOK
            || result.m_nPublishedFileId.m_PublishedFileId != WorkshopTarget.WorkshopId
            || result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            throw new InvalidOperationException(
                $"Legacy Workshop update failed: result={result.m_eResult}, "
                + $"id={result.m_nPublishedFileId.m_PublishedFileId}, "
                + $"legalAgreement={result.m_bUserNeedsToAcceptWorkshopLegalAgreement}");
        }
        SubmitLanguageUpdate("english", metadata.EnglishDescription);
        SubmitLanguageUpdate("schinese", metadata.ChineseDescription);
    }

    internal static void VerifyInstalledLegacy(
        LegacyPackage package, SteamUGCDetails_t before)
    {
        var fileId = new PublishedFileId_t(WorkshopTarget.WorkshopId);
        var expectedHash = System.Security.Cryptography.SHA256.HashData(package.Bytes);
        var started = DateTime.UtcNow;
        var deadline = started.AddMinutes(5);
        var nextRemoteQuery = started;
        var lastDownload = DateTime.MinValue;
        var remote = before;
        var remoteQueryError = "none";
        var localState = EItemState.k_EItemStateNone;
        var localPath = "<not installed>";
        var localKind = "missing";
        var localBytes = 0L;
        var localZipError = "none";
        var downloadAttempts = 0;
        var downloadPending = false;
        var downloadCallbackReceived = false;
        var downloadResult = EResult.k_EResultOK;
        using var callback = Callback<DownloadItemResult_t>.Create(result =>
        {
            if (result.m_unAppID.m_AppId == WorkshopTarget.AppId
                && result.m_nPublishedFileId.m_PublishedFileId == WorkshopTarget.WorkshopId)
            {
                downloadResult = result.m_eResult;
                downloadCallbackReceived = true;
            }
        });

        // A successful commit can precede propagation to the client's UGC
        // cache. Wait for the server file size, then wait for DownloadItem's
        // callback before touching the installed path, as Steam requires.
        while (DateTime.UtcNow < deadline)
        {
            SteamAPI.RunCallbacks();
            var now = DateTime.UtcNow;
            if (now >= nextRemoteQuery)
            {
                try
                {
                    remote = QueryTarget();
                    remoteQueryError = "none";
                }
                catch (Exception error) when (
                    error is InvalidOperationException or TimeoutException)
                {
                    remoteQueryError = error.Message;
                }
                nextRemoteQuery = DateTime.UtcNow.AddSeconds(5);
            }

            if (downloadPending && downloadCallbackReceived)
            {
                downloadPending = false;
                if (downloadResult != EResult.k_EResultOK)
                {
                    throw new InvalidOperationException(
                        $"Workshop verification download failed: {downloadResult}; "
                        + $"serverSize={remote.m_nFileSize}, "
                        + $"serverUpdated={remote.m_rtimeUpdated}");
                }
            }

            var serverHasPackageSize = remote.m_nFileSize == package.Bytes.Length;
            localState = (EItemState)SteamUGC.GetItemState(fileId);
            var needsUpdate = (localState & EItemState.k_EItemStateNeedsUpdate) != 0;
            var shouldDownload = !downloadPending
                && (downloadAttempts == 0
                    ? serverHasPackageSize || now - started >= TimeSpan.FromSeconds(30)
                    : downloadAttempts < 2 && needsUpdate
                        && now - lastDownload >= TimeSpan.FromSeconds(10));
            if (shouldDownload)
            {
                if (!SteamUGC.DownloadItem(fileId, true))
                {
                    throw new InvalidOperationException(
                        $"Steam refused verification download: state={localState}, "
                        + $"serverSize={remote.m_nFileSize}");
                }
                downloadAttempts++;
                lastDownload = now;
                downloadPending = true;
                downloadCallbackReceived = false;
            }

            if (!downloadPending && downloadAttempts > 0
                && (localState & EItemState.k_EItemStateInstalled) != 0
                && SteamUGC.GetItemInstallInfo(
                    fileId, out _, out var installedPath, 4096, out _))
            {
                localPath = installedPath;
                localKind = File.Exists(installedPath) ? "file"
                    : Directory.Exists(installedPath) ? "directory" : "missing";
                if (localKind == "file")
                {
                    try
                    {
                        localBytes = new FileInfo(installedPath).Length;
                        if ((localState & EItemState.k_EItemStateLegacyItem) != 0)
                        {
                            LegacyPackage.ValidateArchive(installedPath);
                            var installedHash =
                                System.Security.Cryptography.SHA256.HashData(
                                    File.ReadAllBytes(installedPath));
                            if (expectedHash.SequenceEqual(installedHash))
                            {
                                Console.WriteLine($"installedLegacyZip={installedPath}");
                                Console.WriteLine($"installedState={localState}");
                                Console.WriteLine($"serverFileSize={remote.m_nFileSize}");
                                return;
                            }
                            localZipError = "ZIP bytes differ from uploaded package";
                        }
                    }
                    catch (Exception error) when (
                        error is IOException or InvalidDataException
                            or InvalidOperationException)
                    {
                        localZipError = error.Message;
                    }
                }
            }
            Thread.Sleep(500);
        }
        throw new InvalidOperationException(
            "Steam accepted the upload, but ONI-compatible installation is still unverified. "
            + $"serverFileSize={remote.m_nFileSize}, "
            + $"expectedFileSize={package.Bytes.Length}, "
            + $"serverUpdated={remote.m_rtimeUpdated}, "
            + $"previousServerUpdated={before.m_rtimeUpdated}, "
            + $"serverQueryError={remoteQueryError}; "
            + $"localState={localState}, localPathKind={localKind}, "
            + $"localPath={localPath}, localBytes={localBytes}, "
            + $"localZipError={localZipError}, downloadAttempts={downloadAttempts}, "
            + $"downloadPending={downloadPending}. The client cache may be stale; "
            + "no second upload was attempted.");
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
            throw new InvalidOperationException($"Steam {operation} failed");
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
