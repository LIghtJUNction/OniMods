using System.Text.Json;
using Steamworks;

var modernItem = new SteamUGCDetails_t
{
    m_hFile = new UGCHandle_t(ulong.MaxValue),
};
var unusedMetadata = new WorkshopMetadata(
    string.Empty, string.Empty, "ONI MCP Server", "English", "Chinese", "test");
var unusedPackage = new LegacyPackage(string.Empty, [1], "unused.zip");
try
{
    SteamWorkshopPublisher.SubmitLegacyUpdate(
        unusedMetadata, unusedPackage, modernItem, updatePreview: false);
    throw new InvalidOperationException("Modern old-ID item reached a Steam write path");
}
catch (InvalidOperationException error) when (
    error.Message.Contains("no legacy file handle", StringComparison.Ordinal))
{
    // Rejection precedes FileWrite/FileShare/Commit.
}

var directory = Path.Combine(Path.GetTempPath(),
    "onim-candidate-journal-" + Guid.NewGuid().ToString("N"));
try
{
    const ulong oldId = LegacyCandidatePlan.OriginalWorkshopId;
    var first = CandidateCreationJournal.Begin(directory, oldId, new string('A', 64));
    if (!File.Exists(first.Path))
    {
        throw new InvalidOperationException("Create intent was not written before Steam call");
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
        // The journal remains until a human audits the owner account.
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
    Console.WriteLine("one-shot candidate journal passed");
}
finally
{
    if (Directory.Exists(directory))
    {
        Directory.Delete(directory, recursive: true);
    }
}
