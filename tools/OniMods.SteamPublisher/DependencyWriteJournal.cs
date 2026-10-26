using System.Text;
using System.Text.Json;

internal sealed class DependencyWriteJournal
{
    private DependencyWriteJournal(string path) { Path = path; }
    internal string Path { get; }

    internal static DependencyWriteJournal Begin(string directory, WorkshopDependencyTarget target, string planSha256)
    {
        if (planSha256.Length != 64 || !planSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid dependency plan SHA256");
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, $"{target.ParentId}-requires-{target.ChildId}.jsonl");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        Write(stream, new { phase = "dependency-write-intent", utc = DateTimeOffset.UtcNow,
            target.ParentName, target.ParentId, target.ChildId, jlibPlanSha256 = planSha256,
            operation = "SteamUGC.AddDependency" });
        return new DependencyWriteJournal(path);
    }

    internal void Record(WorkshopDependencyEvent entry)
    {
        using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.None);
        Write(stream, entry);
    }

    private static void Write<T>(FileStream stream, T entry)
    {
        stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n"));
        stream.Flush(flushToDisk: true);
    }
}
