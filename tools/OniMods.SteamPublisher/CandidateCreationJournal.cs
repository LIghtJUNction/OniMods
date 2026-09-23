using System.Text;
using System.Text.Json;

internal sealed class CandidateCreationJournal
{
    private CandidateCreationJournal(string path) => Path = path;

    internal string Path { get; }

    internal static string DefaultDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OniMods", "workshop-create");
        }
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "state", "onim", "workshop-create");
    }

    internal static string JournalPath(string directory, ulong originalId) =>
        System.IO.Path.Combine(directory,
            $"onimcp-legacy-candidate-from-{originalId}.jsonl");

    internal static void RequireNoPriorAttempt(string directory, ulong originalId)
    {
        var path = JournalPath(directory, originalId);
        if (File.Exists(path))
        {
            throw new InvalidOperationException(
                $"A Legacy candidate creation attempt is already recorded at {path}. "
                + "Audit the Steam owner items before considering any manual recovery; "
                + "this tool never retries a create request.");
        }
    }

    internal static CandidateCreationJournal Begin(
        string directory, ulong originalId, string planSha256)
    {
        if (planSha256.Length != 64 || !planSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Invalid candidate plan SHA256", nameof(planSha256));
        }
        Directory.CreateDirectory(directory);
        var path = JournalPath(directory, originalId);
        var initial = JsonSerializer.Serialize(new
        {
            @event = "create-intent-before-steam-call",
            utc = DateTimeOffset.UtcNow,
            originalId,
            planSha256,
        }) + "\n";
        using (var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            stream.Write(Encoding.UTF8.GetBytes(initial));
            stream.Flush(flushToDisk: true);
        }
        return new CandidateCreationJournal(path);
    }

    internal void RecordCallback(string result, ulong newId, bool legalAgreement)
    {
        Append(new
        {
            @event = "steam-create-callback",
            utc = DateTimeOffset.UtcNow,
            result,
            newId,
            legalAgreement,
        });
    }

    internal void RecordUncertain(string reason)
    {
        Append(new
        {
            @event = "create-outcome-uncertain-no-retry",
            utc = DateTimeOffset.UtcNow,
            reason,
        });
    }

    internal void RecordVerification(bool passed, string detail)
    {
        Append(new
        {
            @event = passed ? "consumer-verified" : "consumer-verification-failed",
            utc = DateTimeOffset.UtcNow,
            detail,
        });
    }

    private void Append<T>(T entry)
    {
        var line = JsonSerializer.Serialize(entry) + "\n";
        using var stream = new FileStream(
            Path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(Encoding.UTF8.GetBytes(line));
        stream.Flush(flushToDisk: true);
    }
}
