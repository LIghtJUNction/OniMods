using System.Text;
using System.Text.Json;

internal enum PromotionStage
{
    PrivateMetadata,
    PublishNew,
    LinkOld,
}

internal sealed class PromotionStageJournal
{
    private PromotionStageJournal(string path, string planSha256)
    {
        Path = path;
        PlanSha256 = planSha256;
    }

    internal string Path { get; }
    private string PlanSha256 { get; }

    internal static string DefaultPath(ulong originalId, ulong newId)
    {
        var baseDirectory = CandidateCreationJournal.DefaultDirectory();
        var prefix = originalId switch
        {
            LegacyCandidatePlan.OriginalWorkshopId => "onimcp",
            LegacyCandidatePlan.CycleTrimOriginalWorkshopId => "cycletrim",
            _ => throw new ArgumentException("Unknown fixed promotion target", nameof(originalId)),
        };
        return System.IO.Path.Combine(baseDirectory, "promotion",
            $"{prefix}-{originalId}-to-{newId}.jsonl");
    }

    internal static PromotionStageJournal Start(
        string path, string planSha256, PromotionStage stage)
    {
        if (planSha256.Length != 64 || !planSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Invalid promotion plan SHA256", nameof(planSha256));
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = new FileStream(
            path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var events = ReadEvents(stream);
        ValidatePlanHash(events, planSha256);
        RequireNoFailure(events);
        var stageName = StageName(stage);
        if (events.Any(entry => entry.Name == stageName + "-intent"))
        {
            throw new InvalidOperationException(
                $"Promotion stage {stageName} already started. Audit {path}; "
                + "do not repeat an uncertain Steam update automatically.");
        }
        var prerequisite = stage switch
        {
            PromotionStage.PrivateMetadata => null,
            PromotionStage.PublishNew => "private-metadata-verified",
            PromotionStage.LinkOld => "publish-new-web-verified",
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
        if (prerequisite is null ? events.Count != 0
            : !events.Any(entry => entry.Name == prerequisite))
        {
            throw new InvalidOperationException(
                $"Promotion stage {stageName} lacks prerequisite {prerequisite ?? "empty journal"}");
        }
        Append(stream, stageName + "-intent", planSha256, "before Steam update");
        return new PromotionStageJournal(path, planSha256);
    }

    internal void Complete(PromotionStage stage, string detail)
        => AppendResult(stage, StageName(stage) + "-verified", detail);

    internal void CompletePublicWebReadback(string detail)
        => AppendResult(PromotionStage.PublishNew,
            "publish-new-web-verified", detail);

    internal void Fail(PromotionStage stage, string detail)
        => AppendResult(stage, StageName(stage) + "-failed-no-auto-retry", detail);

    internal static void RequireCompleted(
        string path, string planSha256, PromotionStage stage)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Promotion journal does not exist");
        }
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var events = ReadEvents(stream);
        ValidatePlanHash(events, planSha256);
        RequireNoFailure(events);
        var expected = stage == PromotionStage.PublishNew
            ? "publish-new-web-verified"
            : StageName(stage) + "-verified";
        if (!events.Any(entry => entry.Name == expected))
        {
            throw new InvalidOperationException(
                $"Promotion prerequisite {expected} is not verified");
        }
    }

    private void AppendResult(PromotionStage stage, string name, string detail)
    {
        using var stream = new FileStream(
            Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var events = ReadEvents(stream);
        ValidatePlanHash(events, PlanSha256);
        var stageName = StageName(stage);
        if (!events.Any(entry => entry.Name == stageName + "-intent")
            || events.Any(entry => entry.Name == name))
        {
            throw new InvalidOperationException(
                $"Promotion journal cannot append {name} without a unique stage intent");
        }
        Append(stream, name, PlanSha256, detail);
    }

    private static List<(string Name, string PlanSha256)> ReadEvents(FileStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        var events = new List<(string, string)>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            events.Add((
                root.GetProperty("event").GetString() ?? string.Empty,
                root.GetProperty("planSha256").GetString() ?? string.Empty));
        }
        return events;
    }

    private static void ValidatePlanHash(
        IEnumerable<(string Name, string PlanSha256)> events, string planSha256)
    {
        if (events.Any(entry => !string.Equals(
            entry.PlanSha256, planSha256, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Promotion journal belongs to a different reviewed plan");
        }
    }

    private static void RequireNoFailure(
        IEnumerable<(string Name, string PlanSha256)> events)
    {
        if (events.Any(entry => entry.Name.EndsWith(
            "-failed-no-auto-retry", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Promotion journal records an uncertain or failed stage; audit before further writes");
        }
    }

    private static void Append(
        FileStream stream, string name, string planSha256, string detail)
    {
        var line = JsonSerializer.Serialize(new
        {
            @event = name,
            utc = DateTimeOffset.UtcNow,
            planSha256,
            detail,
        }) + "\n";
        stream.Position = stream.Length;
        stream.Write(Encoding.UTF8.GetBytes(line));
        stream.Flush(flushToDisk: true);
    }

    private static string StageName(PromotionStage stage) => stage switch
    {
        PromotionStage.PrivateMetadata => "private-metadata",
        PromotionStage.PublishNew => "publish-new",
        PromotionStage.LinkOld => "link-old",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };
}
