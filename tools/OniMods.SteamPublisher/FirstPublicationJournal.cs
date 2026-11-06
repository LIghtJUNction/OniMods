using System.Text;
using System.Text.Json;

internal sealed class FirstPublicationJournal
{
    private FirstPublicationJournal(string path) { Path = path; }
    internal string Path { get; }
    internal static string JournalPath(string directory) => System.IO.Path.Combine(directory, "onionline-first-publication.jsonl");
    internal static string DefaultPath => JournalPath(CandidateCreationJournal.DefaultDirectory());

    internal static FirstPublicationJournal Begin(string directory, string planSha256)
    {
        RequireHash(planSha256);
        Directory.CreateDirectory(directory);
        var path = JournalPath(directory);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Write(stream, new { @event = "first-create-intent", utc = DateTimeOffset.UtcNow, target = FirstPublicationPlan.StaticId, planSha256 });
        return new FirstPublicationJournal(path);
    }

    internal void RecordCallback(string result, ulong newId, bool legalAgreement)
        => Append(new { @event = "first-create-callback", utc = DateTimeOffset.UtcNow, result, newId, legalAgreement });
    internal void RecordUncertain(string reason)
        => Append(new { @event = "first-outcome-uncertain-no-retry", utc = DateTimeOffset.UtcNow, reason });

    internal static ulong ReadCreatedId(string path, string planSha256)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, planSha256).Id;
    }

    internal static void RecordPrivateVerified(string path, string planSha256)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var state = Read(stream, planSha256);
        if (state.PublicStarted) throw new InvalidOperationException("First publication already started its public stage");
        if (!state.PrivateVerified)
            Write(stream, new { @event = "first-private-verified", utc = DateTimeOffset.UtcNow, newId = state.Id });
    }

    internal static (FirstPublicationJournal Journal, ulong Id) BeginPublic(string path, string planSha256)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var state = Read(stream, planSha256);
        if (!state.PrivateVerified || state.PublicStarted)
            throw new InvalidOperationException("First publication public stage requires private verification and no prior public attempt");
        Write(stream, new { @event = "first-public-intent", utc = DateTimeOffset.UtcNow, newId = state.Id });
        return (new FirstPublicationJournal(path), state.Id);
    }

    internal void RecordPublicVerified()
        => Append(new { @event = "first-public-verified", utc = DateTimeOffset.UtcNow });

    private static (ulong Id, bool PrivateVerified, bool PublicStarted) Read(FileStream stream, string planSha256)
    {
        RequireHash(planSha256);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new InvalidOperationException("First publication lacks a successful creation callback; do not retry create");
        using var intent = JsonDocument.Parse(lines[0]);
        if (intent.RootElement.GetProperty("event").GetString() != "first-create-intent"
            || intent.RootElement.GetProperty("target").GetString() != FirstPublicationPlan.StaticId
            || !string.Equals(intent.RootElement.GetProperty("planSha256").GetString(), planSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("First publication journal has a different reviewed plan or target");
        ulong id = 0;
        bool verified = false, publicStarted = false, publicVerified = false;
        foreach (var line in lines.Skip(1))
        {
            using var entry = JsonDocument.Parse(line);
            var row = entry.RootElement;
            switch (row.GetProperty("event").GetString())
            {
                case "first-create-callback":
                    if (id != 0 || row.GetProperty("result").GetString() != "k_EResultOK"
                        || row.GetProperty("legalAgreement").GetBoolean())
                        throw new InvalidOperationException("First creation callback is failed, repeated or requires a legal agreement");
                    id = row.GetProperty("newId").GetUInt64();
                    if (id is 0 or ulong.MaxValue) throw new InvalidOperationException("First creation callback has an invalid item ID");
                    break;
                case "first-private-verified":
                    if (id == 0 || verified || publicStarted || row.GetProperty("newId").GetUInt64() != id)
                        throw new InvalidOperationException("First private verification journal order is invalid");
                    verified = true; break;
                case "first-public-intent":
                    if (!verified || publicStarted || row.GetProperty("newId").GetUInt64() != id)
                        throw new InvalidOperationException("First public stage journal order is invalid");
                    publicStarted = true; break;
                case "first-public-verified":
                    if (!publicStarted || publicVerified) throw new InvalidOperationException("First public verification journal order is invalid");
                    publicVerified = true; break;
                default: throw new InvalidOperationException("First publication has an uncertain or unknown journal event; inspect it without retrying writes");
            }
        }
        if (id == 0) throw new InvalidOperationException("First publication lacks a usable item ID");
        return (id, verified, publicStarted);
    }

    private void Append<T>(T entry)
    {
        using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.None);
        Write(stream, entry);
    }
    private static void Write<T>(FileStream stream, T entry)
    {
        stream.Seek(0, SeekOrigin.End);
        stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n"));
        stream.Flush(flushToDisk: true);
    }
    private static void RequireHash(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid first-publication plan SHA256");
    }
}
