using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record WorkshopPageSnapshot(
    ulong Id,
    uint CreatorAppId,
    uint ConsumerAppId,
    ulong OwnerId,
    string Title,
    string TitleChinese,
    string DescriptionEnglish,
    string DescriptionChinese,
    string[] Tags,
    string Visibility,
    long FileSize,
    uint UpdatedAt);

internal sealed record WorkshopPromotionSnapshot(
    DateTimeOffset CapturedAt,
    WorkshopPageSnapshot Original,
    WorkshopPageSnapshot Candidate);

internal sealed record WorkshopPromotionPlan(
    ulong OriginalId,
    ulong NewId,
    string CreationPlanSha256,
    string ZipPath,
    string ZipSha256,
    long ZipBytes,
    WorkshopPromotionSnapshot Baseline,
    string NewTitle,
    string NewTitleChinese,
    string NewDescriptionEnglish,
    string NewDescriptionChinese,
    string[] NewTags,
    string OldTitle,
    string OldTitleChinese,
    string OldDescriptionEnglish,
    string OldDescriptionChinese,
    string PlanSha256)
{
    internal static readonly string[] ApprovedTags =
    [
        "Base Game",
        "Spaced Out!",
        "The Bionic Booster Pack",
        "The Frosty Planet Pack",
        "New Features",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    internal static WorkshopPromotionPlan Create(
        WorkshopMetadata metadata, string zipPath,
        WorkshopPromotionSnapshot snapshot, string creationJournalPath,
        ulong expectedNewId)
    {
        var target = LegacyCandidatePlan.ResolveFixedTarget();
        if (target.OriginalWorkshopId != LegacyCandidatePlan.OriginalWorkshopId)
        {
            throw new InvalidOperationException("Promotion is limited to OniMcp");
        }
        var verified = VerifiedCandidateRecord.Load(
            creationJournalPath, target.OriginalWorkshopId);
        if (verified.NewId != expectedNewId)
        {
            throw new InvalidOperationException(
                $"Expected new ID {expectedNewId} differs from verified journal ID {verified.NewId}");
        }
        var candidatePlan = LegacyCandidatePlan.FromExistingPackage(metadata, zipPath);
        if (!string.Equals(candidatePlan.PlanSha256,
            verified.CreationPlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The supplied ZIP, preview, and descriptions do not match the verified creation journal");
        }
        var zipHash = Convert.ToHexString(
            SHA256.HashData(candidatePlan.Package.Bytes));
        ValidateSnapshot(snapshot, verified.NewId,
            candidatePlan.Package.Bytes.Length);
        var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={verified.NewId}";
        if (snapshot.Original.DescriptionEnglish.Contains(url, StringComparison.Ordinal)
            || snapshot.Original.DescriptionChinese.Contains(url, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Old page already contains this migration link");
        }
        var oldEnglishNotice =
            "[h1]Moved to the new ONI MCP Server Workshop item[/h1]\n"
            + "[b]The original item's Steam content format cannot be installed by ONI.[/b]\n"
            + $"[url={url}]Open and subscribe to ONI MCP Server 0.2.5[/url]\n"
            + "[b]Subscriptions do not transfer automatically.[/b] "
            + "Unsubscribe from this old item, then subscribe to the new item. "
            + "This page remains available as a migration link and history.";
        var oldChineseNotice =
            "[h1]ONI MCP Server 已迁移至新创意工坊条目[/h1]\n"
            + "[b]原条目的 Steam 内容格式无法被《缺氧》安装。[/b]\n"
            + $"[url={url}]打开并订阅新的 ONI MCP Server 0.2.5[/url]\n"
            + "[b]旧订阅不会自动迁移。[/b] 请先取消订阅本旧条目，再订阅新条目。"
            + "旧页面保留作为迁移入口和历史记录。";
        var oldEnglish = oldEnglishNotice + "\n\n"
            + snapshot.Original.DescriptionEnglish;
        var oldChinese = oldChineseNotice + "\n\n"
            + snapshot.Original.DescriptionChinese;
        var oldTitle = $"ONI MCP Server (Moved to Workshop {verified.NewId})";
        var oldTitleChinese = $"ONI MCP Server（已迁移至工坊条目 {verified.NewId}）";
        if (oldTitle.Length > 128 || oldTitleChinese.Length > 128
            || oldEnglish.Length > 8000
            || oldChinese.Length > 8000)
        {
            throw new InvalidOperationException("Migration copy exceeds Steam limits");
        }
        var draft = new WorkshopPromotionPlan(
            target.OriginalWorkshopId, verified.NewId,
            verified.CreationPlanSha256, Path.GetFullPath(zipPath),
            zipHash, candidatePlan.Package.Bytes.Length, snapshot,
            metadata.Title, metadata.Title, metadata.EnglishDescription,
            metadata.ChineseDescription, ApprovedTags,
            oldTitle, oldTitleChinese, oldEnglish, oldChinese, string.Empty);
        return draft with { PlanSha256 = draft.ComputeSha256() };
    }

    internal static WorkshopPromotionPlan Load(string path, string expectedSha256)
    {
        var plan = JsonSerializer.Deserialize<WorkshopPromotionPlan>(
            File.ReadAllText(path))
            ?? throw new InvalidOperationException("Promotion plan JSON is empty");
        var computed = plan.ComputeSha256();
        if (!string.Equals(computed, plan.PlanSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(computed, expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Promotion plan SHA256 does not match reviewed input");
        }
        var package = LegacyPackage.LoadForVerification(plan.ZipPath);
        var packageSha = Convert.ToHexString(SHA256.HashData(package.Bytes));
        if (!string.Equals(packageSha, plan.ZipSha256,
                StringComparison.OrdinalIgnoreCase)
            || package.Bytes.Length != plan.ZipBytes)
        {
            throw new InvalidOperationException("Promotion ZIP changed after plan review");
        }
        return plan;
    }

    internal (string PlanPath, string ReviewPath) Save(string outputPath)
    {
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath,
            JsonSerializer.Serialize(this, JsonOptions) + "\n");
        var reviewPath = Path.ChangeExtension(outputPath, ".review.md");
        File.WriteAllText(reviewPath, ReviewMarkdown());
        return (outputPath, reviewPath);
    }

    internal string ComputeSha256()
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(new
        {
            OriginalId, NewId, CreationPlanSha256, ZipSha256, ZipBytes,
            Baseline, NewTitle, NewTitleChinese, NewDescriptionEnglish,
            NewDescriptionChinese, NewTags, OldTitle, OldTitleChinese,
            OldDescriptionEnglish, OldDescriptionChinese,
        });
        return Convert.ToHexString(SHA256.HashData(data));
    }

    internal string ReviewMarkdown()
    {
        var text = new StringBuilder();
        text.AppendLine("# ONI MCP Server Workshop migration review");
        text.AppendLine();
        text.AppendLine($"- Plan SHA256: `{PlanSha256}`");
        text.AppendLine($"- Verified creation plan SHA256: `{CreationPlanSha256}`");
        text.AppendLine($"- New ID: `{NewId}`; old ID: `{OriginalId}`");
        text.AppendLine($"- ZIP: `{ZipSha256}` ({ZipBytes} bytes)");
        text.AppendLine($"- Stage journal: `{PromotionStageJournal.DefaultPath(OriginalId, NewId)}`");
        text.AppendLine("- Stage order: new page formal copy while Private; verify Legacy ZIP; publish new page; public Web API readback; add link to old page.");
        text.AppendLine("- No stage sets new content or deletes an item.");
        text.AppendLine();
        WriteComparison(text, "New item / English",
            Baseline.Candidate.Title, Baseline.Candidate.DescriptionEnglish,
            NewTitle, NewDescriptionEnglish);
        WriteComparison(text, "New item / 简体中文",
            Baseline.Candidate.TitleChinese, Baseline.Candidate.DescriptionChinese,
            NewTitleChinese, NewDescriptionChinese,
            Baseline.Candidate.DescriptionChinese == Baseline.Candidate.DescriptionEnglish
                ? "Steam currently serves the English fallback; this is the observed before state."
                : null);
        text.AppendLine($"## New tags\n\nBefore: `{string.Join(", ", Baseline.Candidate.Tags)}`\n\nAfter: `{string.Join(", ", NewTags)}`\n");
        WriteComparison(text, "Old item / English",
            Baseline.Original.Title, Baseline.Original.DescriptionEnglish,
            OldTitle, OldDescriptionEnglish);
        WriteComparison(text, "Old item / 简体中文",
            Baseline.Original.TitleChinese, Baseline.Original.DescriptionChinese,
            OldTitleChinese, OldDescriptionChinese);
        return text.ToString();
    }

    private static void WriteComparison(
        StringBuilder text, string heading,
        string beforeTitle, string beforeDescription,
        string afterTitle, string afterDescription,
        string? beforeNote = null)
    {
        text.AppendLine($"## {heading}");
        text.AppendLine($"\nTitle after: `{afterTitle}`");
        text.AppendLine($"\nTitle before: `{beforeTitle}`");
        text.AppendLine("\nDescription after (complete, proposed):\n\n```text");
        text.AppendLine(afterDescription);
        text.AppendLine("```");
        text.AppendLine($"\nDescription before (complete, observed){(beforeNote is null ? "" : ": " + beforeNote)}:\n\n```text");
        text.AppendLine(beforeDescription);
        text.AppendLine("```\n");
    }

    private static void ValidateSnapshot(
        WorkshopPromotionSnapshot snapshot, ulong newId, int expectedZipBytes)
    {
        foreach (var page in new[] { snapshot.Original, snapshot.Candidate })
        {
            if (page.CreatorAppId != 636750 || page.ConsumerAppId != 457140
                || page.OwnerId != 76561199137573787
                || string.IsNullOrWhiteSpace(page.DescriptionEnglish)
                || string.IsNullOrWhiteSpace(page.DescriptionChinese))
            {
                throw new InvalidOperationException("Snapshot has wrong owner, App IDs, or missing language copy");
            }
        }
        if (snapshot.Original.Id != LegacyCandidatePlan.OriginalWorkshopId
            || snapshot.Original.Visibility != "Public"
            || !snapshot.Original.Title.Contains("ONI MCP Server", StringComparison.OrdinalIgnoreCase)
            || snapshot.Candidate.Id != newId
            || snapshot.Candidate.Visibility != "Private"
            || snapshot.Candidate.Title != LegacyCandidatePlan.CandidateTitle
            || snapshot.Candidate.FileSize != expectedZipBytes)
        {
            throw new InvalidOperationException("Snapshot does not match the old and private candidate items");
        }
        if (!snapshot.Original.Tags.ToHashSet(StringComparer.Ordinal)
            .SetEquals(ApprovedTags))
        {
            throw new InvalidOperationException("Old item tags differ from the approved baseline");
        }
    }
}
