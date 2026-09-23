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
