using System.Text.Json;
using System.Security.Cryptography;
using System.IO.Compression;

Environment.SetEnvironmentVariable("ONIM_PUBLISH_CREATOR_APP_ID", "636750");
Environment.SetEnvironmentVariable("ONIM_PUBLISH_CONSUMER_APP_ID", "457140");
Environment.SetEnvironmentVariable("ONIM_PUBLISH_EXPECTED_OWNER", "76561199137573787");
var oniPlanSha = RunPlanCase(
    "OniMcp", LegacyCandidatePlan.OriginalWorkshopId,
    "ONI MCP Server Test", LegacyCandidatePlan.CandidateTitle);
var cyclePlanSha = RunPlanCase(
    "CycleTrim", LegacyCandidatePlan.CycleTrimOriginalWorkshopId,
    "CycleTrim Test", LegacyCandidatePlan.CycleTrimCandidateTitle);
if (oniPlanSha == cyclePlanSha)
{
    throw new InvalidOperationException("Different source items shared a candidate plan SHA");
}
RunPromotionCase();
RunCycleTrimPromotionCase();
RunFirstPublicationCase();
RunPackagedDescriptionLayoutCase();

var directory = Path.Combine(Path.GetTempPath(),
    "onim-candidate-journal-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new HashSet<string>(StringComparer.Ordinal);
    foreach (var oldId in new[]
    {
        LegacyCandidatePlan.OriginalWorkshopId,
        LegacyCandidatePlan.CycleTrimOriginalWorkshopId,
    })
    {
        var first = CandidateCreationJournal.Begin(directory, oldId, new string('A', 64));
        if (!File.Exists(first.Path) || !paths.Add(first.Path))
        {
            throw new InvalidOperationException("Candidate journal is missing or shared");
        }
        var expectedPrefix = oldId == LegacyCandidatePlan.OriginalWorkshopId
            ? "onimcp-" : "cycletrim-";
        if (!Path.GetFileName(first.Path).StartsWith(
            expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Candidate journal filename changed");
        }
        try
        {
            CandidateCreationJournal.Begin(directory, oldId, new string('B', 64));
            throw new InvalidOperationException("A second candidate creation acquired the same journal");
        }
        catch (IOException)
        {
            // FileMode.CreateNew is the cross-process one-shot gate.
        }
        first.RecordUncertain("Steam callback timed out");
        try
        {
            CandidateCreationJournal.RequireNoPriorAttempt(directory, oldId);
            throw new InvalidOperationException("Uncertain callback did not block a retry");
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("already recorded", StringComparison.Ordinal))
        {
            // This item remains blocked, independently of the other item.
        }
        var lines = File.ReadAllLines(first.Path);
        if (lines.Length != 2)
        {
            throw new InvalidOperationException("Journal lost its intent or uncertain outcome");
        }
        using var intent = JsonDocument.Parse(lines[0]);
        using var uncertain = JsonDocument.Parse(lines[1]);
        if (intent.RootElement.GetProperty("event").GetString()
                != "create-intent-before-steam-call"
            || uncertain.RootElement.GetProperty("event").GetString()
                != "create-outcome-uncertain-no-retry")
        {
            throw new InvalidOperationException("Journal events are incomplete");
        }
    }
    Console.WriteLine("independent one-shot candidate journals passed");
}
finally
{
    if (Directory.Exists(directory))
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void RunPackagedDescriptionLayoutCase()
{
    var root = Path.Combine(Path.GetTempPath(), "onim-description-layout-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        var preview = Path.Combine(root, "preview.png");
        File.WriteAllBytes(preview, [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(root, "docs", "steam-description-en.md"), "English package description\n");
        File.WriteAllText(Path.Combine(root, "docs", "steam-description-zh.md"), "中文包说明\n");
        var vdf = Path.Combine(root, "workshop.vdf");
        static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        File.WriteAllText(vdf, string.Join('\n',
            "\"workshopitem\"", "{", "\"appid\" \"457140\"",
            $"\"publishedfileid\" \"{WorkshopTarget.WorkshopId}\"",
            $"\"contentfolder\" \"{Escape(root)}\"", $"\"previewfile\" \"{Escape(preview)}\"",
            "\"title\" \"OniOnline description test\"", "\"description\" \"Combined fallback\"",
            "\"changenote\" \"Test\"", "}"));
        var docs = WorkshopMetadataReader.Read(vdf);
        if (docs.EnglishDescription != "English package description" || docs.ChineseDescription != "中文包说明")
            throw new InvalidOperationException("Packaged docs descriptions were replaced by the combined VDF fallback");
        File.WriteAllText(Path.Combine(root, "steam-description-en.md"), "Legacy root description");
        var legacy = WorkshopMetadataReader.Read(vdf);
        if (legacy.EnglishDescription != "Legacy root description" || legacy.ChineseDescription != "中文包说明")
            throw new InvalidOperationException("Legacy root description precedence changed");
        File.Delete(Path.Combine(root, "steam-description-en.md"));
        Directory.Delete(Path.Combine(root, "docs"), recursive: true);
        var fallback = WorkshopMetadataReader.Read(vdf);
        if (fallback.EnglishDescription != "Combined fallback" || fallback.ChineseDescription != "Combined fallback")
            throw new InvalidOperationException("Description fallback failed when no localized file was packaged");
        Console.WriteLine("packaged docs, legacy root descriptions and combined fallback passed");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static string RunPlanCase(
    string name, ulong oldId, string sourceTitle, string expectedTitle)
{
    Environment.SetEnvironmentVariable("ONIM_PUBLISH_WORKSHOP_ID", oldId.ToString());
    Environment.SetEnvironmentVariable("ONIM_PUBLISH_NAME", name);
    var planDirectory = Path.Combine(Path.GetTempPath(),
        "onim-candidate-plan-" + Guid.NewGuid().ToString("N"));
    try
    {
        var content = Path.Combine(planDirectory, name);
        Directory.CreateDirectory(content);
        File.WriteAllBytes(Path.Combine(content, name + ".dll"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(content, "mod.yaml"), "title: Test\n");
        File.WriteAllText(Path.Combine(content, "mod_info.yaml"), "version: Test\n");
        var preview = Path.Combine(content, "preview.png");
        File.WriteAllBytes(preview, [0x89, 0x50, 0x4E, 0x47]);
        var metadata = new WorkshopMetadata(
            content, preview, sourceTitle,
            "English description", "中文说明", "Test");
        var firstPlan = LegacyCandidatePlan.Create(metadata);
        var repeatedPlan = LegacyCandidatePlan.Create(metadata);
        if (firstPlan.PlanSha256 != repeatedPlan.PlanSha256
            || !firstPlan.Package.Bytes.SequenceEqual(repeatedPlan.Package.Bytes))
        {
            throw new InvalidOperationException("Repeated offline plan changed its ZIP or SHA");
        }
        if (firstPlan.Target.OriginalWorkshopId != oldId
            || firstPlan.Title != expectedTitle
            || !firstPlan.Description.StartsWith(
                "PRIVATE LEGACY TEST CANDIDATE", StringComparison.Ordinal)
            || !firstPlan.PreviewBytes.SequenceEqual(File.ReadAllBytes(preview)))
        {
            throw new InvalidOperationException("Candidate target, title, notice, or preview changed");
        }
        using (var zip = ZipFile.OpenRead(firstPlan.Package.Path))
        {
            var entries = zip.Entries.Select(entry => entry.FullName).ToHashSet();
            if (!entries.Contains(name + ".dll")
                || !entries.Contains("mod.yaml")
                || !entries.Contains("mod_info.yaml"))
            {
                throw new InvalidOperationException("Legacy ZIP is missing ONI root files");
            }
        }
        var previousOutput = Console.Out;
        using var planOutput = new StringWriter();
        try
        {
            Console.SetOut(planOutput);
            firstPlan.Print();
        }
        finally
        {
            Console.SetOut(previousOutput);
        }
        var printed = planOutput.ToString();
        var zipSha = Convert.ToHexString(SHA256.HashData(firstPlan.Package.Bytes));
        var previewSha = Convert.ToHexString(SHA256.HashData(firstPlan.PreviewBytes));
        if (!printed.Contains("candidateVisibility=Private", StringComparison.Ordinal)
            || !printed.Contains("creatorApp=636750", StringComparison.Ordinal)
            || !printed.Contains("consumerApp=457140", StringComparison.Ordinal)
            || !printed.Contains($"sourceWorkshopId={oldId}", StringComparison.Ordinal)
            || !printed.Contains("legacyZipSha256=" + zipSha, StringComparison.Ordinal)
            || !printed.Contains("previewSha256=" + previewSha, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Offline plan output lost identity or exact hashes");
        }
        Console.WriteLine($"{name} private candidate plan and repeated SHA passed");
        return firstPlan.PlanSha256;
    }
    finally
    {
        if (Directory.Exists(planDirectory))
        {
            Directory.Delete(planDirectory, recursive: true);
        }
    }
}

static void RunPromotionCase()
{
    Environment.SetEnvironmentVariable("ONIM_PUBLISH_WORKSHOP_ID", "3731864673");
    Environment.SetEnvironmentVariable("ONIM_PUBLISH_NAME", "OniMcp");
    var root = Path.Combine(Path.GetTempPath(),
        "onim-promotion-" + Guid.NewGuid().ToString("N"));
    try
    {
        var content = Path.Combine(root, "OniMcp");
        Directory.CreateDirectory(content);
        File.WriteAllBytes(Path.Combine(content, "OniMcp.dll"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(content, "mod.yaml"), "title: Test\n");
        File.WriteAllText(Path.Combine(content, "mod_info.yaml"), "version: 0.2.5\n");
        var preview = Path.Combine(content, "preview.png");
        File.WriteAllBytes(preview, [0x89, 0x50, 0x4E, 0x47]);
        var metadata = new WorkshopMetadata(
            content, preview, "ONI MCP Server (Early Access)",
            "[h2]v0.2.5 Workshop installation update[/h2]\nEnglish body",
            "[h2]v0.2.5 创意工坊安装更新[/h2]\n中文正文", "Test");
        var candidate = LegacyCandidatePlan.Create(metadata);
        var createJournal = CandidateCreationJournal.Begin(
            root, LegacyCandidatePlan.OriginalWorkshopId, candidate.PlanSha256);
        createJournal.RecordCallback("k_EResultOK", 3806839864, false);
        createJournal.RecordVerification(true, "Private Legacy ZIP installed with matching bytes");
        const string oldEnglish = "[h1]Old English body[/h1]";
        const string oldChinese = "[h1]原中文正文[/h1]";
        var baseline = new WorkshopPromotionSnapshot(
            DateTimeOffset.UtcNow,
            new WorkshopPageSnapshot(
                3731864673, 636750, 457140, 76561199137573787,
                "ONI MCP Server (Early Access)", string.Empty,
                oldEnglish, oldChinese, WorkshopPromotionPlan.ApprovedTags,
                "Public", 3614199, 123),
            new WorkshopPageSnapshot(
                3806839864, 636750, 457140, 76561199137573787,
                LegacyCandidatePlan.CandidateTitle,
                LegacyCandidatePlan.CandidateTitle,
                candidate.Description, candidate.Description, [],
                "Private", candidate.Package.Bytes.Length, 456));
        var plan = WorkshopPromotionPlan.Create(
            metadata, candidate.Package.Path, baseline,
            createJournal.Path, 3806839864);
        var repeated = WorkshopPromotionPlan.Create(
            metadata, candidate.Package.Path, baseline,
            createJournal.Path, 3806839864);
        if (plan.PlanSha256 != repeated.PlanSha256)
        {
            throw new InvalidOperationException("Repeated promotion plan changed SHA");
        }
        RunRecoveryCase(plan, root);
        var publicJson = JsonSerializer.Serialize(new
        {
            response = new
            {
                publishedfiledetails = new[]
                {
                    new
                    {
                        result = 1,
                        publishedfileid = "3806839864",
                        creator_app_id = 636750,
                        consumer_app_id = 457140,
                        creator = "76561199137573787",
                        title = plan.NewTitle,
                        description = plan.NewDescriptionEnglish,
                        visibility = 0,
                        tags = plan.NewTags.Select(tag => new { tag }).ToArray(),
                        file_size = plan.ZipBytes.ToString(),
                    },
                },
            },
        });
        var publicPage = SteamPublicPage.Parse(publicJson, plan.NewId);
        if (!SteamPublicPageReadback.Matches(plan, publicPage, oldPage: false))
        {
            throw new InvalidOperationException("Public Web readback rejected reviewed new page");
        }
        var hiddenJson = publicJson.Replace("\"result\":1", "\"result\":9",
            StringComparison.Ordinal);
        try
        {
            SteamPublicPage.Parse(hiddenJson, plan.NewId);
            throw new InvalidOperationException("Private result=9 passed public Web readback");
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("not visible", StringComparison.Ordinal))
        {
        }
        var review = plan.ReviewMarkdown();
        var newChinese = review.Split("## New item / 简体中文", 2)[1]
            .Split("## New tags", 2)[0];
        var newEnglish = review.Split("## New item / English", 2)[1]
            .Split("## New item / 简体中文", 2)[0];
        var proposedChinese = ProposedDescription(newChinese);
        var proposedEnglish = ProposedDescription(newEnglish);
        if (!proposedChinese.Contains("v0.2.5 创意工坊安装更新", StringComparison.Ordinal)
            || proposedChinese == proposedEnglish
            || proposedChinese.Contains("Workshop installation update", StringComparison.Ordinal)
            || !newChinese.Contains("Steam currently serves the English fallback", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chinese new-page review showed English copy");
        }
        var oldChineseSection = review.Split("## Old item / 简体中文", 2)[1];
        var proposedOldChinese = ProposedDescription(oldChineseSection);
        if (!proposedOldChinese.Contains("旧订阅不会自动迁移", StringComparison.Ordinal)
            || !proposedOldChinese.Contains("3806839864", StringComparison.Ordinal)
            || !proposedOldChinese.EndsWith(oldChinese, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Old Chinese migration copy lost link or body");
        }
        var (planPath, reviewPath) = plan.Save(Path.Combine(root, "promotion.json"));
        if (!File.Exists(reviewPath)
            || WorkshopPromotionPlan.Load(planPath, plan.PlanSha256).PlanSha256
                != plan.PlanSha256)
        {
            throw new InvalidOperationException("Promotion review or plan readback failed");
        }
        var stagePath = Path.Combine(root, "stages.jsonl");
        try
        {
            PromotionStageJournal.Start(
                stagePath, plan.PlanSha256, PromotionStage.PublishNew);
            throw new InvalidOperationException("Public stage bypassed private metadata gate");
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("lacks prerequisite", StringComparison.Ordinal))
        {
        }
        var stage1 = PromotionStageJournal.Start(
            stagePath, plan.PlanSha256, PromotionStage.PrivateMetadata);
        try
        {
            PromotionStageJournal.Start(
                stagePath, plan.PlanSha256, PromotionStage.PrivateMetadata);
            throw new InvalidOperationException("Repeated metadata stage was admitted");
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("already started", StringComparison.Ordinal))
        {
        }
        stage1.Complete(PromotionStage.PrivateMetadata, "Private copy and ZIP verified");
        try
        {
            PromotionStageJournal.Start(
                stagePath, new string('B', 64), PromotionStage.PublishNew);
            throw new InvalidOperationException("Different plan SHA reused promotion journal");
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("different reviewed plan", StringComparison.Ordinal))
        {
        }
        var stage2 = PromotionStageJournal.Start(
            stagePath, plan.PlanSha256, PromotionStage.PublishNew);
        stage2.Complete(PromotionStage.PublishNew, "Steam item is Public");
        stage2.CompletePublicWebReadback("Public Web API matched reviewed title");
        var stage3 = PromotionStageJournal.Start(
            stagePath, plan.PlanSha256, PromotionStage.LinkOld);
        stage3.Complete(PromotionStage.LinkOld, "Old page links new public item");
        var failedPath = Path.Combine(root, "failed-stages.jsonl");
        var failedStage = PromotionStageJournal.Start(
            failedPath, plan.PlanSha256, PromotionStage.PrivateMetadata);
        failedStage.Fail(PromotionStage.PrivateMetadata, "Steam callback uncertain");
        try
        {
            PromotionStageJournal.Start(
                failedPath, plan.PlanSha256, PromotionStage.PublishNew);
            throw new InvalidOperationException("Publication bypassed the gate");
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("uncertain or failed stage", StringComparison.Ordinal))
        {
        }
        Console.WriteLine("promotion Chinese review, plan SHA, and stage gates passed");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static string ProposedDescription(string section) =>
    section.Split("Description after (complete, proposed):\n\n```text\n", 2)[1]
        .Split("\n```", 2)[0];

static void RunCycleTrimPromotionCase()
{
    Environment.SetEnvironmentVariable("ONIM_PUBLISH_WORKSHOP_ID", "3766318556");
    Environment.SetEnvironmentVariable("ONIM_PUBLISH_NAME", "CycleTrim");
    var root = Path.Combine(Path.GetTempPath(),
        "cycletrim-promotion-" + Guid.NewGuid().ToString("N"));
    try
    {
        var content = Path.Combine(root, "CycleTrim");
        Directory.CreateDirectory(content);
        File.WriteAllBytes(Path.Combine(content, "CycleTrim.dll"), [4, 5, 6]);
        File.WriteAllText(Path.Combine(content, "mod.yaml"), "title: CycleTrim\n");
        File.WriteAllText(Path.Combine(content, "mod_info.yaml"), "version: 0.3.4\n");
        var preview = Path.Combine(content, "preview.png");
        File.WriteAllBytes(preview, [0x89, 0x50, 0x4E, 0x47]);
        var metadata = new WorkshopMetadata(
            content, preview, "CycleTrim (Early Development)",
            "[h2]v0.3.4 Workshop installation update[/h2]\nEnglish body",
            "[h2]v0.3.4 创意工坊安装更新[/h2]\n中文正文", "Test");
        var candidate = LegacyCandidatePlan.Create(metadata);
        var journal = CandidateCreationJournal.Begin(
            root, LegacyCandidatePlan.CycleTrimOriginalWorkshopId,
            candidate.PlanSha256);
        journal.RecordCallback("k_EResultOK", 3806858440, false);
        journal.RecordVerification(true, "Private Legacy ZIP installed with matching bytes");
        const string oldEnglish = "[h1]Old CycleTrim body[/h1]";
        const string oldChinese = "[h1]旧版 CycleTrim 中文正文[/h1]";
        var snapshot = new WorkshopPromotionSnapshot(
            DateTimeOffset.UtcNow,
            new WorkshopPageSnapshot(
                3766318556, 636750, 457140, 76561199137573787,
                "CycleTrim (Early Development)", string.Empty,
                oldEnglish, oldChinese, WorkshopPromotionPlan.CycleTrimApprovedTags,
                "Public", 400000, 100),
            new WorkshopPageSnapshot(
                3806858440, 636750, 457140, 76561199137573787,
                LegacyCandidatePlan.CycleTrimCandidateTitle,
                LegacyCandidatePlan.CycleTrimCandidateTitle,
                candidate.Description, candidate.Description, [],
                "Private", candidate.Package.Bytes.Length, 200));
        var plan = WorkshopPromotionPlan.Create(
            metadata, candidate.Package.Path, snapshot, journal.Path, 3806858440);
        var repeated = WorkshopPromotionPlan.Create(
            metadata, candidate.Package.Path, snapshot, journal.Path, 3806858440);
        if (plan.PlanSha256 != repeated.PlanSha256
            || plan.OriginalId != 3766318556
            || plan.NewId != 3806858440
            || plan.NewTitle != "CycleTrim (Early Development)"
            || plan.OldTitle != "CycleTrim (Moved to Workshop 3806858440)"
            || !plan.NewTags.SequenceEqual(WorkshopPromotionPlan.CycleTrimApprovedTags)
            || !plan.OldDescriptionEnglish.EndsWith(oldEnglish, StringComparison.Ordinal)
            || !plan.OldDescriptionChinese.EndsWith(oldChinese, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CycleTrim promotion plan changed identity, SHA, tags, or old body");
        }
        RunRecoveryCase(plan, root);
        var review = plan.ReviewMarkdown();
        var newChinese = ProposedDescription(
            review.Split("## New item / 简体中文", 2)[1].Split("## New tags", 2)[0]);
        var oldChineseSection = ProposedDescription(
            review.Split("## Old item / 简体中文", 2)[1]);
        if (!newChinese.Contains("v0.3.4 创意工坊安装更新", StringComparison.Ordinal)
            || !oldChineseSection.Contains("原条目的 Steam 内容格式无法被《缺氧》安装", StringComparison.Ordinal)
            || !oldChineseSection.Contains("旧订阅不会自动迁移", StringComparison.Ordinal)
            || !oldChineseSection.Contains("3806858440", StringComparison.Ordinal)
            || !PromotionStageJournal.DefaultPath(3766318556, 3806858440)
                .Contains("cycletrim-3766318556-to-3806858440.jsonl", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CycleTrim review or independent stage journal is wrong");
        }
        var (planPath, _) = plan.Save(Path.Combine(root, "promotion.json"));
        if (WorkshopPromotionPlan.Load(planPath, plan.PlanSha256).PlanSha256
            != plan.PlanSha256)
        {
            throw new InvalidOperationException("CycleTrim plan could not be read back");
        }
        Console.WriteLine("CycleTrim promotion plan, bilingual copy, and journal path passed");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void RunRecoveryCase(WorkshopPromotionPlan plan, string root)
{
    var formalCandidate = plan.Baseline.Candidate with
    {
        Title = plan.NewTitle,
        TitleChinese = plan.NewTitleChinese,
        DescriptionEnglish = plan.NewDescriptionEnglish,
        DescriptionChinese = plan.NewDescriptionChinese,
        Tags = plan.NewTags,
        Visibility = "Private",
    };
    var ready = plan.Baseline with { Candidate = formalCandidate };
    WorkshopPromotionRecoveryReadback.RequireMatchingPages(plan, ready);

    ExpectRecoveryPageRejection(plan, ready with
    {
        Original = ready.Original with { DescriptionChinese = "changed" },
    });
    ExpectRecoveryPageRejection(plan, ready with
    {
        Original = ready.Original with { UpdatedAt = ready.Original.UpdatedAt + 1 },
    });
    ExpectRecoveryPageRejection(plan, ready with
    {
        Original = ready.Original with { Tags = ["changed"] },
    });
    ExpectRecoveryPageRejection(plan, ready with
    {
        Candidate = formalCandidate with { Visibility = "Public" },
    });
    ExpectRecoveryPageRejection(plan, ready with
    {
        Candidate = formalCandidate with { DescriptionChinese = "changed" },
    });
    ExpectRecoveryPageRejection(plan, ready with
    {
        Candidate = formalCandidate with { Tags = [] },
    });
    ExpectRecoveryPageRejection(plan, ready with
    {
        Candidate = formalCandidate with { FileSize = 0 },
    });
    ExpectRecoveryPageRejection(plan, ready with
    {
        Candidate = formalCandidate with { OwnerId = 0 },
    });

    var path = Path.Combine(root, "recovery.jsonl");
    PromotionStageJournal.Start(
        path, plan.PlanSha256, PromotionStage.PrivateMetadata);
    PromotionStageJournal.RequireRecoverablePrivateMetadata(path, plan.PlanSha256);
    ExpectRecoveryJournalRejection(path, new string('F', 64));
    PromotionStageJournal.CompleteRecoveredPrivateMetadata(path, plan.PlanSha256);
    PromotionStageJournal.RequireCompleted(
        path, plan.PlanSha256, PromotionStage.PrivateMetadata);
    ExpectRecoveryJournalRejection(path, plan.PlanSha256);

    var failedPath = Path.Combine(root, "failed-recovery.jsonl");
    var failed = PromotionStageJournal.Start(
        failedPath, plan.PlanSha256, PromotionStage.PrivateMetadata);
    failed.Fail(PromotionStage.PrivateMetadata, "callback uncertain");
    ExpectRecoveryJournalRejection(failedPath, plan.PlanSha256);
    Console.WriteLine($"{plan.OriginalId} readback-only recovery gates passed");
}

static void ExpectRecoveryPageRejection(
    WorkshopPromotionPlan plan, WorkshopPromotionSnapshot snapshot)
{
    try
    {
        WorkshopPromotionRecoveryReadback.RequireMatchingPages(plan, snapshot);
        throw new InvalidOperationException("Recovery accepted a changed Workshop page");
    }
    catch (InvalidOperationException error) when (
        error.Message.Contains("differs from", StringComparison.Ordinal)
        || error.Message.Contains("does not match", StringComparison.Ordinal))
    {
    }
}

static void ExpectRecoveryJournalRejection(string path, string planSha256)
{
    try
    {
        PromotionStageJournal.RequireRecoverablePrivateMetadata(path, planSha256);
        throw new InvalidOperationException("Recovery accepted an ineligible journal");
    }
    catch (InvalidOperationException error) when (
        error.Message.Contains("recovery requires", StringComparison.OrdinalIgnoreCase)
        || error.Message.Contains("different reviewed plan", StringComparison.Ordinal))
    {
    }
}

static void RunFirstPublicationCase()
{
    var target = new Dictionary<string, string>
    {
        ["ONIM_PUBLISH_NAME"] = "OniOnline",
        ["ONIM_PUBLISH_WORKSHOP_ID"] = "0",
        ["ONIM_PUBLISH_CREATOR_APP_ID"] = "636750",
        ["ONIM_PUBLISH_CONSUMER_APP_ID"] = "457140",
        ["ONIM_PUBLISH_EXPECTED_OWNER"] = "76561199137573787",
    };
    var previous = target.Keys.ToDictionary(
        name => name, Environment.GetEnvironmentVariable);
    var root = Path.Combine(Path.GetTempPath(),
        "onionline-first-publication-" + Guid.NewGuid().ToString("N"));
    try
    {
        foreach (var (name, value) in target)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
        var content = Path.Combine(root, "OniOnline");
        Directory.CreateDirectory(content);
        var dllPath = Path.Combine(content, "OniOnline.dll");
        File.WriteAllBytes(dllPath, [1, 2, 3]);
        var yamlPath = Path.Combine(content, "mod.yaml");
        const string yaml = "title: OniOnline\nstaticID: LIghtJUNction.OniOnline\n";
        File.WriteAllText(yamlPath, yaml);
        File.WriteAllText(Path.Combine(content, "mod_info.yaml"), "version: 0.1.0\n");
        var previewPath = Path.Combine(root, "preview.png");
        byte[] previewBytes = [0x89, 0x50, 0x4E, 0x47];
        File.WriteAllBytes(previewPath, previewBytes);
        var metadata = new WorkshopMetadata(content, previewPath,
            "OniOnline (Early Development)", "Reviewed English body", "已审阅的中文说明", "Test");
        var plan = FirstPublicationPlan.Create(metadata);
        var zipBytes = File.ReadAllBytes(plan.ZipPath);
        var repeated = FirstPublicationPlan.Create(metadata);
        if (plan.PlanSha256 != repeated.PlanSha256
            || !zipBytes.SequenceEqual(File.ReadAllBytes(repeated.ZipPath))
            || plan.Title != metadata.Title
            || plan.EnglishDescription != metadata.EnglishDescription
            || plan.ChineseDescription != metadata.ChineseDescription
            || !plan.Tags.SequenceEqual(new[] { "tweaks", "Spaced Out!" })
            || plan.ZipBytes != zipBytes.Length
            || plan.ZipSha256 != Convert.ToHexString(SHA256.HashData(zipBytes))
            || plan.PreviewSha256 != Convert.ToHexString(SHA256.HashData(previewBytes)))
        {
            throw new InvalidOperationException("First-publication plan lost reviewed metadata, artifact hashes or determinism");
        }
        using (var archive = ZipFile.OpenRead(plan.ZipPath))
        {
            var names = archive.Entries.Select(entry => entry.FullName).ToHashSet();
            if (!names.SetEquals(new[] { "OniOnline.dll", "mod.yaml", "mod_info.yaml" }))
            {
                throw new InvalidOperationException("First-publication ZIP changed its ONI root layout");
            }
        }
        var planPath = Path.Combine(root, "reviewed-plan.json");
        plan.Save(planPath);
        ExpectFirstPublicationException<IOException>(
            () => plan.Save(planPath), "overwriting an existing reviewed plan");
        if (FirstPublicationPlan.Load(planPath, plan.PlanSha256).PlanSha256 != plan.PlanSha256)
        {
            throw new InvalidOperationException("First-publication plan failed saved readback");
        }
        ExpectFirstPublicationException<InvalidOperationException>(
            () => FirstPublicationPlan.Load(planPath, new string('F', 64)), "a different reviewed SHA");

        foreach (var changed in new[]
        {
            metadata with { Title = metadata.Title + " revised" },
            metadata with { EnglishDescription = metadata.EnglishDescription + " revised" },
            metadata with { ChineseDescription = metadata.ChineseDescription + "（修订）" },
        })
        {
            if (FirstPublicationPlan.Create(changed).PlanSha256 == plan.PlanSha256)
            {
                throw new InvalidOperationException("Reviewed metadata changed without changing the first-publication SHA");
            }
        }
        foreach (var tampered in new[]
        {
            plan with { Title = "OniOnline changed" },
            plan with { ChineseDescription = "changed" },
            plan with { Tags = ["changed"] },
            plan with { ZipBytes = plan.ZipBytes + 1 },
        })
        {
            var tamperedPath = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
            tampered.Save(tamperedPath);
            ExpectFirstPublicationException<InvalidOperationException>(
                () => FirstPublicationPlan.Load(tamperedPath, plan.PlanSha256), "tampered plan content");
        }
        File.WriteAllBytes(dllPath, [9, 8, 7]);
        var changedPackage = FirstPublicationPlan.Create(metadata);
        if (changedPackage.PlanSha256 == plan.PlanSha256 || changedPackage.ZipSha256 == plan.ZipSha256)
        {
            throw new InvalidOperationException("Different ZIP bytes retained the reviewed first-publication SHA");
        }
        ExpectFirstPublicationException<InvalidOperationException>(
            () => FirstPublicationPlan.Load(planPath, plan.PlanSha256), "a valid ZIP changed after review");
        File.WriteAllBytes(plan.ZipPath, zipBytes);
        File.WriteAllBytes(previewPath, [1, 2, 3]);
        ExpectFirstPublicationException<InvalidOperationException>(
            () => FirstPublicationPlan.Load(planPath, plan.PlanSha256), "a preview changed after review");
        File.WriteAllBytes(previewPath, previewBytes);

        foreach (var (name, invalidValue) in new[]
        {
            ("ONIM_PUBLISH_NAME", "OniMcp"),
            ("ONIM_PUBLISH_WORKSHOP_ID", "3731864673"),
            ("ONIM_PUBLISH_CREATOR_APP_ID", "457140"),
            ("ONIM_PUBLISH_CONSUMER_APP_ID", "636750"),
            ("ONIM_PUBLISH_EXPECTED_OWNER", "1"),
        })
        {
            Environment.SetEnvironmentVariable(name, invalidValue);
            ExpectFirstPublicationException<InvalidOperationException>(
                FirstPublicationPlan.RequireTarget, "a different target " + name);
            ExpectFirstPublicationException<InvalidOperationException>(
                () => FirstPublicationPlan.Load(planPath, plan.PlanSha256), "readback with a different target " + name);
            Environment.SetEnvironmentVariable(name, target[name]);
        }
        foreach (var invalid in new[]
        {
            metadata with { Title = "CycleTrim" },
            metadata with { Title = "OniOnline" + new string('x', 120) },
            metadata with { EnglishDescription = "" },
            metadata with { ChineseDescription = "" },
            metadata with { EnglishDescription = new string('x', 8001) },
            metadata with { ChineseDescription = new string('中', 8001) },
        })
        {
            ExpectFirstPublicationException<InvalidOperationException>(
                () => FirstPublicationPlan.Create(invalid), "invalid first-publication metadata");
        }
        var unsupportedPreview = Path.Combine(root, "preview.gif");
        File.WriteAllBytes(unsupportedPreview, previewBytes);
        ExpectFirstPublicationException<InvalidOperationException>(
            () => FirstPublicationPlan.Create(metadata with { PreviewFile = unsupportedPreview }), "an unsupported preview format");
        foreach (var invalidPreview in new[] { Array.Empty<byte>(), new byte[10 * 1024 * 1024 + 1] })
        {
            File.WriteAllBytes(previewPath, invalidPreview);
            ExpectFirstPublicationException<InvalidOperationException>(
                () => FirstPublicationPlan.Create(metadata), "an empty or oversized preview");
        }
        File.WriteAllBytes(previewPath, previewBytes);
        File.WriteAllText(yamlPath, "staticID: LIghtJUNction.CycleTrim\n");
        ExpectFirstPublicationException<InvalidOperationException>(
            () => FirstPublicationPlan.Create(metadata), "a different mod staticID");
        File.WriteAllText(yamlPath, yaml);
        RunFirstPublicationJournalCase(Path.Combine(root, "journals"), plan.PlanSha256);
        Console.WriteLine("OniOnline first-publication plan identity, hashes, limits and artifact readback passed");
    }
    finally
    {
        foreach (var (name, value) in previous)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void RunFirstPublicationJournalCase(string directory, string planSha)
{
    ExpectFirstPublicationException<ArgumentException>(
        () => FirstPublicationJournal.Begin(directory, "short"), "a short journal plan hash");
    ExpectFirstPublicationException<ArgumentException>(
        () => FirstPublicationJournal.Begin(directory, new string('Z', 64)), "a non-hex journal plan hash");
    var journal = FirstPublicationJournal.Begin(directory, planSha);
    if (journal.Path != FirstPublicationJournal.JournalPath(directory))
    {
        throw new InvalidOperationException("First-publication journal used a different path");
    }
    var initialLines = File.ReadAllLines(journal.Path);
    using (var intent = JsonDocument.Parse(initialLines.Single()))
    {
        if (intent.RootElement.GetProperty("event").GetString() != "first-create-intent"
            || intent.RootElement.GetProperty("target").GetString() != FirstPublicationPlan.StaticId
            || intent.RootElement.GetProperty("planSha256").GetString() != planSha)
        {
            throw new InvalidOperationException("First-publication create intent was not durable before the callback");
        }
    }
    ExpectFirstPublicationException<IOException>(
        () => FirstPublicationJournal.Begin(directory, planSha), "a repeated create request");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.ReadCreatedId(journal.Path, planSha), "a missing create callback");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.RecordPrivateVerified(journal.Path, planSha), "private verification before creation");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.BeginPublic(journal.Path, planSha), "public intent before creation");
    const ulong createdId = 3808000001;
    journal.RecordCallback("k_EResultOK", createdId, false);
    if (FirstPublicationJournal.ReadCreatedId(journal.Path, planSha) != createdId)
    {
        throw new InvalidOperationException("First-publication journal lost its created item ID");
    }
    var otherSha = (planSha[0] == 'A' ? "B" : "A") + planSha[1..];
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.ReadCreatedId(journal.Path, otherSha), "a journal from a different plan");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.RecordPrivateVerified(journal.Path, otherSha), "private verification with a different plan");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.BeginPublic(journal.Path, otherSha), "public intent with a different plan");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.BeginPublic(journal.Path, planSha), "public intent without private readback");
    FirstPublicationJournal.RecordPrivateVerified(journal.Path, planSha);
    FirstPublicationJournal.RecordPrivateVerified(journal.Path, planSha);
    if (File.ReadAllLines(journal.Path).Length != 3)
    {
        throw new InvalidOperationException("Repeated private readback appended another journal event");
    }
    using (var writer = new FileStream(journal.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
    {
        ExpectFirstPublicationException<IOException>(
            () => FirstPublicationJournal.BeginPublic(journal.Path, planSha), "a public write while another writer owns the journal");
    }
    var (publicJournal, publicId) = FirstPublicationJournal.BeginPublic(journal.Path, planSha);
    if (publicId != createdId || File.ReadAllLines(journal.Path).Length != 4)
    {
        throw new InvalidOperationException("Public intent was not recorded before returning its item ID");
    }
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.BeginPublic(journal.Path, planSha), "a repeated public request");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.RecordPrivateVerified(journal.Path, planSha), "private verification after public intent");
    publicJournal.RecordPublicVerified();
    if (FirstPublicationJournal.ReadCreatedId(journal.Path, planSha) != createdId)
    {
        throw new InvalidOperationException("Verified public journal lost its item ID");
    }
    var expectedEvents = new[]
    {
        "first-create-intent", "first-create-callback", "first-private-verified",
        "first-public-intent", "first-public-verified",
    };
    var events = File.ReadAllLines(journal.Path).Select(line =>
    {
        using var row = JsonDocument.Parse(line);
        return row.RootElement.GetProperty("event").GetString();
    });
    if (!events.SequenceEqual(expectedEvents))
    {
        throw new InvalidOperationException("First-publication journal changed its complete event sequence");
    }
    ExpectFirstPublicationException<IOException>(
        () => FirstPublicationJournal.Begin(directory, planSha), "re-creation after successful publication");

    foreach (var (result, id, legalAgreement) in new[]
    {
        ("k_EResultFail", createdId, false),
        ("k_EResultOK", 0UL, false),
        ("k_EResultOK", ulong.MaxValue, false),
        ("k_EResultOK", createdId, true),
    })
    {
        var rejected = FirstPublicationJournal.Begin(
            Path.Combine(directory, Guid.NewGuid().ToString("N")), planSha);
        rejected.RecordCallback(result, id, legalAgreement);
        ExpectFirstPublicationJournalBlocked(rejected.Path, planSha);
    }
    foreach (var callbackBeforeUncertain in new[] { false, true })
    {
        var uncertainDirectory = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        var uncertain = FirstPublicationJournal.Begin(uncertainDirectory, planSha);
        if (callbackBeforeUncertain)
        {
            uncertain.RecordCallback("k_EResultOK", createdId, false);
        }
        uncertain.RecordUncertain("Steam callback timed out");
        ExpectFirstPublicationJournalBlocked(uncertain.Path, planSha);
        ExpectFirstPublicationException<IOException>(
            () => FirstPublicationJournal.Begin(uncertainDirectory, planSha), "re-creation after an uncertain result");
    }
    var repeatedCallback = FirstPublicationJournal.Begin(
        Path.Combine(directory, "repeated-callback"), planSha);
    repeatedCallback.RecordCallback("k_EResultOK", createdId, false);
    repeatedCallback.RecordCallback("k_EResultOK", createdId, false);
    ExpectFirstPublicationJournalBlocked(repeatedCallback.Path, planSha);
    var outOfOrder = FirstPublicationJournal.Begin(
        Path.Combine(directory, "out-of-order"), planSha);
    outOfOrder.RecordCallback("k_EResultOK", createdId, false);
    outOfOrder.RecordPublicVerified();
    ExpectFirstPublicationJournalBlocked(outOfOrder.Path, planSha);
    Console.WriteLine("OniOnline first-publication one-shot creation, ordered verification and exclusive public intent passed");
}

static void ExpectFirstPublicationJournalBlocked(string path, string planSha)
{
    var before = File.ReadAllBytes(path);
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.ReadCreatedId(path, planSha), "an ineligible journal item ID");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.RecordPrivateVerified(path, planSha), "private verification of an ineligible journal");
    ExpectFirstPublicationException<InvalidOperationException>(
        () => FirstPublicationJournal.BeginPublic(path, planSha), "public intent for an ineligible journal");
    if (!before.SequenceEqual(File.ReadAllBytes(path)))
    {
        throw new InvalidOperationException("Rejected journal recovery changed the retained evidence");
    }
}

static void ExpectFirstPublicationException<TException>(Action action, string scenario)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (Exception error) when (error is TException)
    {
        return;
    }
    throw new InvalidOperationException("First publication accepted " + scenario);
}
