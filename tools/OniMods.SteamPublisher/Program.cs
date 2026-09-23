using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Steamworks;

internal static class Program
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary converts every publisher failure into a non-zero exit code.")]
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var queryOnly = args.Contains("--query-only", StringComparer.Ordinal);
        var consumerContext = args.Contains("--consumer-context", StringComparer.Ordinal);
        var validateOnly = args.Contains("--validate-vdf", StringComparer.Ordinal);
        var metadataOnly = args.Contains("--metadata-only", StringComparer.Ordinal);
        var verifyInstalled = args.Contains("--verify-installed", StringComparer.Ordinal);
        var prepareCandidate = args.Contains("--prepare-private-candidate", StringComparer.Ordinal);
        var createCandidate = args.Contains("--create-private-candidate", StringComparer.Ordinal);
        var capturePromotion = args.Contains("--capture-promotion-snapshot", StringComparer.Ordinal);
        var preparePromotion = args.Contains("--prepare-promotion", StringComparer.Ordinal);
        var stagePrivateMetadata = args.Contains("--stage-private-metadata", StringComparer.Ordinal);
        var publishPromotedItem = args.Contains("--publish-promoted-item", StringComparer.Ordinal);
        var linkOldItem = args.Contains("--link-old-item", StringComparer.Ordinal);
        var updatePreview = args.Contains("--update-preview", StringComparer.Ordinal);
        var noChangeNote = args.Contains("--no-change-note", StringComparer.Ordinal);
        var promotionStageCount = new[]
        {
            stagePrivateMetadata, publishPromotedItem, linkOldItem,
        }.Count(selected => selected);
        if (promotionStageCount > 0)
        {
            if (promotionStageCount != 1
                || queryOnly || consumerContext || validateOnly || metadataOnly
                || verifyInstalled || prepareCandidate || createCandidate
                || capturePromotion || preparePromotion || updatePreview || noChangeNote)
            {
                throw new ArgumentException("Promotion stage cannot be combined with another mode");
            }
            if (!args.Contains("--confirm-promotion-stage", StringComparer.Ordinal))
            {
                throw new ArgumentException("Promotion stage requires --confirm-promotion-stage");
            }
            var planPath = ReadOption(args, "--plan");
            var planHash = ReadOption(args, "--expected-plan-sha256");
            if (string.IsNullOrWhiteSpace(planPath)
                || planHash.Length != 64 || !planHash.All(Uri.IsHexDigit))
            {
                throw new ArgumentException(
                    "Promotion stage requires --plan <path> and reviewed --expected-plan-sha256 <hash>");
            }
            var stage = stagePrivateMetadata ? PromotionStage.PrivateMetadata
                : publishPromotedItem ? PromotionStage.PublishNew
                : PromotionStage.LinkOld;
            return WorkshopPromotionRunner.Run(stage, planPath, planHash);
        }
        if (capturePromotion || preparePromotion)
        {
            if (capturePromotion == preparePromotion
                || queryOnly || consumerContext || validateOnly || metadataOnly
                || verifyInstalled || prepareCandidate || createCandidate
                || updatePreview || noChangeNote)
            {
                throw new ArgumentException("Promotion mode cannot be combined with another mode");
            }
            return capturePromotion
                ? CapturePromotionSnapshot(args)
                : PreparePromotion(args);
        }
        if (prepareCandidate || createCandidate)
        {
            if (prepareCandidate == createCandidate
                || queryOnly || consumerContext || validateOnly || metadataOnly
                || verifyInstalled || updatePreview || noChangeNote)
            {
                throw new ArgumentException("Private candidate mode cannot be combined with another mode");
            }
            return RunPrivateCandidate(args, createCandidate);
        }
        if (verifyInstalled)
        {
            if (queryOnly || validateOnly || metadataOnly || updatePreview || noChangeNote)
            {
                throw new ArgumentException("--verify-installed cannot be combined with another mode");
            }
            return VerifyInConsumerContext(args);
        }
        if (consumerContext && !queryOnly)
        {
            throw new ArgumentException("--consumer-context is only valid with --query-only");
        }
        var vdfPath = ReadOption(args, "--vdf");
        if ((queryOnly && validateOnly)
            || (!queryOnly && string.IsNullOrWhiteSpace(vdfPath)))
        {
            throw new ArgumentException(
                "Usage: OniMods.SteamPublisher --query-only [--consumer-context] | --validate-vdf --vdf <path> | --metadata-only --vdf <path> | --vdf <path>");
        }

        SteamAppContext.ValidateHints();
        var metadata = queryOnly
            ? null
            : WorkshopMetadataReader.Read(Path.GetFullPath(vdfPath));
        if (noChangeNote && metadata is not null)
        {
            metadata = metadata with { ChangeNote = string.Empty };
        }
        if (validateOnly)
        {
            var package = LegacyPackage.Create(metadata!);
            Console.WriteLine($"title={metadata!.Title}");
            Console.WriteLine($"englishDescriptionChars={metadata.EnglishDescription.Length}");
            Console.WriteLine($"chineseDescriptionChars={metadata.ChineseDescription.Length}");
            Console.WriteLine($"legacyZip={package.Path}");
            Console.WriteLine($"legacyZipBytes={package.Bytes.Length}");
            Console.WriteLine($"creatorApp={WorkshopTarget.CreatorAppId}");
            Console.WriteLine($"consumerApp={WorkshopTarget.ConsumerAppId}");
            Console.WriteLine($"{WorkshopTarget.DisplayName} legacy Workshop ZIP is valid");
            return 0;
        }
        var role = consumerContext ? SteamAppRole.Consumer : SteamAppRole.Creator;
        SteamAppContext.Prepare(role);
        if (!SteamAPI.IsSteamRunning())
        {
            throw new InvalidOperationException("Steam client is not running");
        }
        if (!SteamAPI.Init())
        {
            throw new InvalidOperationException("SteamAPI.Init failed; Steam must be logged in");
        }

        LegacyPackage? uploadedPackage = null;
        uint previousServerUpdated = 0;
        try
        {
            SteamAppContext.VerifyActive(role);
            SteamWorkshopPublisher.ValidateAccount();
            var before = SteamWorkshopPublisher.QueryTarget();
            SteamWorkshopPublisher.PrintTarget(before);
            if (queryOnly)
            {
                Console.WriteLine($"{WorkshopTarget.DisplayName} Workshop target is valid");
                return 0;
            }

            if (metadataOnly)
            {
                SteamWorkshopPublisher.SubmitLocalizedMetadata(metadata!);
            }
            else
            {
                var package = LegacyPackage.Create(metadata!);
                Console.WriteLine($"legacyZip={package.Path}");
                Console.WriteLine($"legacyZipBytes={package.Bytes.Length}");
                SteamWorkshopPublisher.SubmitLegacyUpdate(
                    metadata!, package, before, updatePreview);
                uploadedPackage = package;
                previousServerUpdated = before.m_rtimeUpdated;
            }
            var after = SteamWorkshopPublisher.QueryTarget();
            SteamWorkshopPublisher.PrintTarget(after);
            if (after.m_rtimeUpdated < before.m_rtimeUpdated)
            {
                throw new InvalidOperationException("Workshop timestamp moved backwards after upload");
            }
        }
        finally
        {
            SteamAPI.Shutdown();
        }

        if (uploadedPackage is not null)
        {
            RunConsumerVerifier(uploadedPackage, previousServerUpdated);
        }
        Console.WriteLine($"{WorkshopTarget.DisplayName} Steam Workshop update completed");
        Console.WriteLine(WorkshopTarget.Url);
        return 0;
    }

    private static int VerifyInConsumerContext(string[] args)
    {
        var zipPath = ReadOption(args, "--zip");
        var updatedText = ReadOption(args, "--previous-updated");
        var expectedHash = ReadOption(args, "--expected-sha256");
        var candidateText = ReadOption(args, "--candidate-id");
        var candidateTitle = ReadOption(args, "--candidate-title");
        var candidatePublic = args.Contains("--candidate-public", StringComparer.Ordinal);
        ulong? candidateId = null;
        if (!string.IsNullOrWhiteSpace(candidateText))
        {
            var target = LegacyCandidatePlan.ResolveFixedTarget();
            if (!ulong.TryParse(candidateText, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parsedId)
                || parsedId == 0 || parsedId == target.OriginalWorkshopId)
            {
                throw new ArgumentException("Invalid private candidate Workshop ID");
            }
            candidateId = parsedId;
        }
        if (!candidateId.HasValue
            && (!string.IsNullOrEmpty(candidateTitle) || candidatePublic))
        {
            throw new ArgumentException("Candidate title/visibility requires --candidate-id");
        }
        if (candidateId.HasValue && string.IsNullOrWhiteSpace(candidateTitle))
        {
            candidateTitle = LegacyCandidatePlan.ResolveFixedTarget().CandidateTitle;
        }
        if (string.IsNullOrWhiteSpace(zipPath)
            || !uint.TryParse(updatedText, NumberStyles.None,
                CultureInfo.InvariantCulture, out var previousUpdated)
            || expectedHash.Length != 64
            || !expectedHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Usage: OniMods.SteamPublisher --verify-installed --zip <path> --previous-updated <timestamp> --expected-sha256 <hash>");
        }
        var package = LegacyPackage.LoadForVerification(zipPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(package.Bytes));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Workshop ZIP changed after upload; verification cannot use a different package");
        }
        SteamAppContext.Prepare(SteamAppRole.Consumer);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
        {
            throw new InvalidOperationException(
                "Steam consumer context could not initialize for install verification");
        }
        try
        {
            SteamAppContext.VerifyActive(SteamAppRole.Consumer);
            SteamWorkshopPublisher.ValidateAccount();
            var current = candidateId.HasValue
                ? SteamWorkshopPublisher.QueryItem(
                    candidateId.Value,
                    candidateTitle,
                    requirePrivate: !candidatePublic,
                    requirePublic: candidatePublic)
                : SteamWorkshopPublisher.QueryTarget();
            SteamWorkshopPublisher.VerifyInstalledLegacy(
                package, current, previousUpdated, candidateId,
                candidateTitle, candidatePublic);
            return 0;
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    private static int RunPrivateCandidate(string[] args, bool create)
    {
        var vdfPath = ReadOption(args, "--vdf");
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            throw new ArgumentException(
                "Usage: --prepare-private-candidate --vdf <path> | "
                + "--create-private-candidate --vdf <path> "
                + "--expected-plan-sha256 <hash> --confirm-private-create-once");
        }
        SteamAppContext.ValidateHints();
        var metadata = WorkshopMetadataReader.Read(Path.GetFullPath(vdfPath));
        var plan = LegacyCandidatePlan.Create(metadata);
        plan.Print();
        var journalDirectory = CandidateCreationJournal.DefaultDirectory();
        Console.WriteLine($"creationJournal={CandidateCreationJournal.JournalPath(
            journalDirectory, plan.Target.OriginalWorkshopId)}");
        if (!create)
        {
            Console.WriteLine("privateCandidatePrepared=true; Steam API not initialized");
            return 0;
        }

        var expectedPlanHash = ReadOption(args, "--expected-plan-sha256");
        if (!args.Contains("--confirm-private-create-once", StringComparer.Ordinal)
            || !string.Equals(expectedPlanHash, plan.PlanSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Private creation requires --confirm-private-create-once and the "
                + "exact planSha256 printed by the offline preparation command");
        }
        CandidateCreationJournal.RequireNoPriorAttempt(
            journalDirectory, plan.Target.OriginalWorkshopId);
        SteamAppContext.Prepare(SteamAppRole.Creator);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
        {
            throw new InvalidOperationException(
                "Steam creator context could not initialize for private candidate creation");
        }

        ulong newId;
        CandidateCreationJournal journal;
        try
        {
            SteamAppContext.VerifyActive(SteamAppRole.Creator);
            SteamWorkshopPublisher.ValidateAccount();
            var original = SteamWorkshopPublisher.QueryTarget();
            (newId, journal) = SteamWorkshopPublisher.PublishPrivateCandidate(
                plan, original);
            try
            {
                WaitForPrivateCandidate(newId, plan.Package.Bytes.Length, plan.Title);
                SteamWorkshopPublisher.VerifyOriginalUnchanged(original);
            }
            catch (Exception error)
            {
                journal.RecordVerification(false,
                    "Creator-side readback failed: " + error.Message);
                throw;
            }
        }
        finally
        {
            SteamAPI.Shutdown();
        }

        try
        {
            RunConsumerVerifier(plan.Package, previousUpdated: 0, candidateId: newId);
            journal.RecordVerification(true, "Private Legacy ZIP installed with matching bytes");
        }
        catch (Exception error)
        {
            journal.RecordVerification(false,
                "Consumer-side readback failed: " + error.Message);
            throw;
        }
        Console.WriteLine("privateCandidateVerified=true");
        Console.WriteLine($"candidateWorkshopUrl=https://steamcommunity.com/sharedfiles/filedetails/?id={newId}");
        Console.WriteLine("visibility=Private; original Workshop ID unchanged");
        return 0;
    }

    private static int CapturePromotionSnapshot(string[] args)
    {
        var output = ReadOption(args, "--output");
        var newId = ReadExpectedNewId(args);
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException(
                "Usage: --capture-promotion-snapshot --expected-new-id <id> --output <path>");
        }
        var target = LegacyCandidatePlan.ResolveFixedTarget();
        if (target.OriginalWorkshopId != LegacyCandidatePlan.OriginalWorkshopId)
        {
            throw new InvalidOperationException("Promotion snapshot is limited to OniMcp");
        }
        var record = VerifiedCandidateRecord.Load(
            CandidateCreationJournal.JournalPath(
                CandidateCreationJournal.DefaultDirectory(), target.OriginalWorkshopId),
            target.OriginalWorkshopId);
        if (record.NewId != newId)
        {
            throw new InvalidOperationException("Expected new ID differs from verified journal");
        }
        SteamAppContext.Prepare(SteamAppRole.Creator);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
        {
            throw new InvalidOperationException(
                "Steam creator context could not initialize for read-only snapshot");
        }
        WorkshopPromotionSnapshot snapshot;
        try
        {
            SteamAppContext.VerifyActive(SteamAppRole.Creator);
            SteamWorkshopPublisher.ValidateAccount();
            snapshot = SteamWorkshopPublisher.CapturePromotionSnapshot(
                target.OriginalWorkshopId, newId);
        }
        finally
        {
            SteamAPI.Shutdown();
        }
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var serialized = JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions { WriteIndented = true }) + "\n";
        File.WriteAllText(output, serialized);
        Console.WriteLine($"promotionSnapshot={output}");
        Console.WriteLine($"promotionSnapshotSha256={Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serialized)))}");
        Console.WriteLine($"oldTitle={snapshot.Original.Title}");
        Console.WriteLine($"newPrivateTitle={snapshot.Candidate.Title}");
        Console.WriteLine($"oldVisibility={snapshot.Original.Visibility}");
        Console.WriteLine($"newVisibility={snapshot.Candidate.Visibility}");
        Console.WriteLine("promotionSnapshotReadOnly=true");
        return 0;
    }

    private static int PreparePromotion(string[] args)
    {
        var vdfPath = ReadOption(args, "--vdf");
        var zipPath = ReadOption(args, "--zip");
        var snapshotPath = ReadOption(args, "--snapshot");
        var outputPath = ReadOption(args, "--output");
        var newId = ReadExpectedNewId(args);
        if (new[] { vdfPath, zipPath, snapshotPath, outputPath }
            .Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Usage: --prepare-promotion --vdf <path> --zip <path> "
                + "--snapshot <path> --expected-new-id <id> --output <path>");
        }
        SteamAppContext.ValidateHints();
        var metadata = WorkshopMetadataReader.Read(Path.GetFullPath(vdfPath));
        var snapshot = JsonSerializer.Deserialize<WorkshopPromotionSnapshot>(
            File.ReadAllText(snapshotPath))
            ?? throw new InvalidOperationException("Promotion snapshot JSON is empty");
        var journalPath = CandidateCreationJournal.JournalPath(
            CandidateCreationJournal.DefaultDirectory(),
            LegacyCandidatePlan.OriginalWorkshopId);
        var plan = WorkshopPromotionPlan.Create(
            metadata, zipPath, snapshot, journalPath, newId);
        var (planPath, reviewPath) = plan.Save(outputPath);
        Console.WriteLine($"promotionPlan={planPath}");
        Console.WriteLine($"promotionReview={reviewPath}");
        Console.WriteLine($"promotionPlanSha256={plan.PlanSha256}");
        Console.WriteLine($"promotionStageJournal={PromotionStageJournal.DefaultPath(
            plan.OriginalId, plan.NewId)}");
        Console.WriteLine($"verifiedNewId={plan.NewId}");
        Console.WriteLine($"verifiedZipSha256={plan.ZipSha256}");
        Console.WriteLine($"newTitle={plan.NewTitle}");
        Console.WriteLine($"oldMovedTitle={plan.OldTitle}");
        Console.WriteLine($"newTags={string.Join(", ", plan.NewTags)}");
        Console.WriteLine("promotionPreparedOffline=true; Steam API not initialized");
        return 0;
    }

    private static ulong ReadExpectedNewId(string[] args)
    {
        if (!ulong.TryParse(ReadOption(args, "--expected-new-id"),
            NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id == 0 || id == LegacyCandidatePlan.OriginalWorkshopId)
        {
            throw new ArgumentException("A distinct --expected-new-id is required");
        }
        return id;
    }

    private static void WaitForPrivateCandidate(
        ulong newId, int expectedBytes, string expectedTitle)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        string lastError = "not queried";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var details = SteamWorkshopPublisher.QueryItem(
                    newId, expectedTitle,
                    requirePrivate: true);
                if (details.m_nFileSize == expectedBytes)
                {
                    SteamWorkshopPublisher.PrintTarget(details);
                    return;
                }
                lastError = $"candidate fileSize={details.m_nFileSize}, expected={expectedBytes}";
            }
            catch (Exception error) when (
                error is InvalidOperationException or TimeoutException)
            {
                lastError = error.Message;
            }
            Thread.Sleep(5000);
        }
        throw new TimeoutException(
            $"Private candidate {newId} did not pass creator-side readback: {lastError}");
    }

    internal static void RunConsumerVerifier(
        LegacyPackage package, uint previousUpdated, ulong? candidateId = null,
        string? candidateTitle = null, bool candidatePublic = false)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = SteamAppContext.HintDirectory(SteamAppRole.Consumer),
        };
        var consumerAppId = WorkshopTarget.ConsumerAppId.ToString(
            CultureInfo.InvariantCulture);
        start.Environment["SteamAppId"] = consumerAppId;
        start.Environment["SteamGameId"] = consumerAppId;
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--verify-installed");
        start.ArgumentList.Add("--zip");
        start.ArgumentList.Add(package.Path);
        start.ArgumentList.Add("--previous-updated");
        start.ArgumentList.Add(previousUpdated.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--expected-sha256");
        start.ArgumentList.Add(Convert.ToHexString(SHA256.HashData(package.Bytes)));
        if (candidateId.HasValue)
        {
            start.ArgumentList.Add("--candidate-id");
            start.ArgumentList.Add(candidateId.Value.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(candidateTitle))
            {
                start.ArgumentList.Add("--candidate-title");
                start.ArgumentList.Add(candidateTitle);
            }
            if (candidatePublic)
            {
                start.ArgumentList.Add("--candidate-public");
            }
        }
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start consumer verification process");
        if (!child.WaitForExit(TimeSpan.FromMinutes(7)))
        {
            child.Kill(entireProcessTree: true);
            throw new TimeoutException("Consumer verification process timed out");
        }
        if (child.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Consumer verification process failed: exit={child.ExitCode}");
        }
    }

    private static string ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }
        return string.Empty;
    }
}
