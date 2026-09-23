using Steamworks;

internal static partial class SteamWorkshopPublisher
{
    internal static (ulong NewId, CandidateCreationJournal Journal) PublishPrivateCandidate(
        LegacyCandidatePlan plan, SteamUGCDetails_t original)
    {
        var journalDirectory = CandidateCreationJournal.DefaultDirectory();
        CandidateCreationJournal.RequireNoPriorAttempt(
            journalDirectory, plan.Target.OriginalWorkshopId);

        ShareCloudFile(plan.Package.CloudFileName, plan.Package.Bytes, "candidate ZIP");
        ShareCloudFile(plan.PreviewCloudFileName, plan.PreviewBytes, "candidate preview");
        VerifyOriginalUnchanged(original);

        // CreateNew plus fsync happens before Steam receives the create call.
        // An ambiguous callback or process crash leaves this marker in place.
        var journal = CandidateCreationJournal.Begin(
            journalDirectory, plan.Target.OriginalWorkshopId,
            plan.PlanSha256);
        var callbackReceived = false;
        try
        {
            var call = SteamRemoteStorage.PublishWorkshopFile(
                plan.Package.CloudFileName,
                plan.PreviewCloudFileName,
                new AppId_t(WorkshopTarget.ConsumerAppId),
                plan.Title,
                plan.Description,
                ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
                Array.Empty<string>(),
                EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            var result = WaitForCall<RemoteStoragePublishFileResult_t>(
                call, UploadTimeout, "Private Legacy candidate creation");
            callbackReceived = true;
            var newId = result.m_nPublishedFileId.m_PublishedFileId;
            journal.RecordCallback(
                result.m_eResult.ToString(), newId,
                result.m_bUserNeedsToAcceptWorkshopLegalAgreement);
            if (result.m_eResult != EResult.k_EResultOK
                || result.m_bUserNeedsToAcceptWorkshopLegalAgreement
                || newId == 0 || newId == ulong.MaxValue
                || newId == plan.Target.OriginalWorkshopId)
            {
                throw new InvalidOperationException(
                    $"Private candidate callback was not a usable success: "
                    + $"result={result.m_eResult}, id={newId}, "
                    + $"legalAgreement={result.m_bUserNeedsToAcceptWorkshopLegalAgreement}; "
                    + $"journal={journal.Path}. Do not retry automatically.");
            }
            Console.WriteLine($"candidateWorkshopId={newId}");
            Console.WriteLine($"creationJournal={journal.Path}");
            return (newId, journal);
        }
        catch (Exception error) when (!callbackReceived)
        {
            journal.RecordUncertain(error.Message);
            throw new InvalidOperationException(
                $"Private candidate creation outcome is uncertain; "
                + $"journal={journal.Path}. Audit the owner's Workshop items; "
                + "do not repeat the create command.", error);
        }
    }

    internal static void VerifyOriginalUnchanged(SteamUGCDetails_t original)
    {
        var latest = QueryTarget();
        if (latest.m_rtimeUpdated != original.m_rtimeUpdated
            || latest.m_nFileSize != original.m_nFileSize
            || latest.m_eVisibility != original.m_eVisibility)
        {
            throw new InvalidOperationException(
                $"Original Workshop item changed during candidate creation: "
                + $"before={original.m_rtimeUpdated}/{original.m_nFileSize}/{original.m_eVisibility}, "
                + $"after={latest.m_rtimeUpdated}/{latest.m_nFileSize}/{latest.m_eVisibility}");
        }
    }

    private static void ShareCloudFile(string name, byte[] bytes, string purpose)
    {
        Require(SteamRemoteStorage.FileWrite(name, bytes, bytes.Length),
            $"{purpose} FileWrite");
        var shared = WaitForCall<RemoteStorageFileShareResult_t>(
            SteamRemoteStorage.FileShare(name), QueryTimeout,
            $"{purpose} FileShare");
        if (shared.m_eResult != EResult.k_EResultOK
            || shared.m_hFile.m_UGCHandle == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                $"{purpose} FileShare failed: {shared.m_eResult}");
        }
    }
}
