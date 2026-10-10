using System.Text.Json;

internal sealed record VerifiedCandidateRecord(
    ulong OriginalId, ulong NewId, string CreationPlanSha256)
{
    internal static VerifiedCandidateRecord Load(string path, ulong expectedOriginalId)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Verified candidate journal is missing", path);
        }
        var lines = File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length != 3)
        {
            throw new InvalidOperationException(
                "Candidate journal must contain exactly intent, success callback, and verification");
        }
        using var intent = JsonDocument.Parse(lines[0]);
        using var callback = JsonDocument.Parse(lines[1]);
        using var verified = JsonDocument.Parse(lines[2]);
        var first = intent.RootElement;
        var second = callback.RootElement;
        var third = verified.RootElement;
        if (first.GetProperty("event").GetString() != "create-intent-before-steam-call"
            || first.GetProperty("originalId").GetUInt64() != expectedOriginalId
            || second.GetProperty("event").GetString() != "steam-create-callback"
            || second.GetProperty("result").GetString() != "k_EResultOK"
            || second.GetProperty("legalAgreement").GetBoolean()
            || third.GetProperty("event").GetString() != "consumer-verified")
        {
            throw new InvalidOperationException(
                "Candidate journal does not prove a verified private Legacy ZIP");
        }
        var newId = second.GetProperty("newId").GetUInt64();
        var planSha = first.GetProperty("planSha256").GetString() ?? string.Empty;
        if (newId == 0 || newId == expectedOriginalId
            || planSha.Length != 64 || !planSha.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Candidate journal has invalid ID or plan hash");
        }
        return new VerifiedCandidateRecord(expectedOriginalId, newId, planSha);
    }
}
