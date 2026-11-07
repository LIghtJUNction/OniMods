using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record FirstPublicationPlan(
    string StaticId,
    string ZipPath, string ZipSha256, long ZipBytes,
    string PreviewPath, string PreviewSha256,
    string Title, string EnglishDescription, string ChineseDescription,
    string[] Tags, string PlanSha256)
{
    internal static string Name => WorkshopTarget.DisplayName;

    internal static void RequireTarget()
    {
        if (WorkshopTarget.WorkshopId != 0 || WorkshopTarget.CreatorAppId != 636750
            || WorkshopTarget.ConsumerAppId != 457140)
            throw new InvalidOperationException("First publication requires an unassigned Workshop item ID and ONI App IDs");
        if (WorkshopTarget.ConfiguredOwner is null)
            throw new InvalidOperationException("First publication requires ONIM_PUBLISH_EXPECTED_OWNER");
    }

    internal static FirstPublicationPlan Create(WorkshopMetadata metadata)
    {
        RequireTarget();
        if (string.IsNullOrWhiteSpace(metadata.Title) || metadata.Title.Length > 128
            || metadata.EnglishDescription.Length is 0 or > 8000
            || metadata.ChineseDescription.Length is 0 or > 8000)
            throw new InvalidOperationException("First publication requires reviewed metadata within Steam limits");
        var extension = Path.GetExtension(metadata.PreviewFile).ToLowerInvariant();
        var preview = File.ReadAllBytes(metadata.PreviewFile);
        if (extension is not (".png" or ".jpg" or ".jpeg") || preview.Length is 0 or > 10 * 1024 * 1024)
            throw new InvalidOperationException("First publication preview must be PNG/JPEG, 1 byte to 10 MiB");
        var staticId = ReadStaticId(Path.Combine(metadata.ContentFolder, "mod.yaml"));
        if (!string.Equals(staticId, WorkshopTarget.StaticId, StringComparison.Ordinal))
            throw new InvalidOperationException("Selected mod staticID does not match the publication target");
        var tags = (Environment.GetEnvironmentVariable("ONIM_PUBLISH_TAGS") ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tags.Length == 0)
            throw new InvalidOperationException("First publication requires ONIM_PUBLISH_TAGS");
        var package = LegacyPackage.Create(metadata);
        var plan = new FirstPublicationPlan(staticId, package.Path, Hash(package.Bytes), package.Bytes.Length,
            Path.GetFullPath(metadata.PreviewFile), Hash(preview), metadata.Title,
            metadata.EnglishDescription, metadata.ChineseDescription, tags, "");
        return plan with { PlanSha256 = plan.ComputeHash() };
    }

    internal void Save(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }) + "\n"));
        stream.Flush(flushToDisk: true);
    }

    internal static FirstPublicationPlan Load(string path, string expectedHash)
    {
        RequireTarget();
        var plan = JsonSerializer.Deserialize<FirstPublicationPlan>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("First publication plan is empty");
        if (plan.StaticId != WorkshopTarget.StaticId
            || plan.StaticId != ReadPackagedStaticId(plan.ZipPath))
            throw new InvalidOperationException("First publication plan targets a different mod staticID");
        if (!string.Equals(plan.PlanSha256, expectedHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(plan.ComputeHash(), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("First publication plan SHA differs from the reviewed plan");
        plan.ReadPackage();
        plan.ReadPreview();
        return plan;
    }

    // Validate the exact owned byte array passed to Steam or the install verifier,
    // even when another preparation replaced an artifact after plan loading.
    internal LegacyPackage ReadPackage()
    {
        var package = LegacyPackage.LoadForVerification(ZipPath);
        if (package.Bytes.Length != ZipBytes || Hash(package.Bytes) != ZipSha256)
            throw new InvalidOperationException("First publication ZIP changed after preparation");
        return package;
    }

    internal byte[] ReadPreview()
    {
        var bytes = File.ReadAllBytes(PreviewPath);
        if (Hash(bytes) != PreviewSha256)
            throw new InvalidOperationException("First publication preview changed after preparation");
        return bytes;
    }

    private string ComputeHash() => Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        target = StaticId, creatorApp = WorkshopTarget.CreatorAppId,
        consumerApp = WorkshopTarget.ConsumerAppId, displayName = WorkshopTarget.DisplayName,
        visibility = "PrivateThenPublic", ZipPath, ZipSha256, ZipBytes, PreviewPath, PreviewSha256,
        Title, EnglishDescription, ChineseDescription, Tags,
    })));
    private static string ReadStaticId(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Mod metadata is missing", path);
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("staticID:", StringComparison.Ordinal)) continue;
            var value = trimmed["staticID:".Length..].Trim().Trim('"', '\'');
            if (value.Length > 0) return value;
        }
        throw new InvalidOperationException("Selected mod's mod.yaml has no staticID");
    }
    internal static string ReadPackagedStaticId(string zipPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry("mod.yaml")
            ?? throw new InvalidOperationException("Reviewed ZIP has no root mod.yaml");
        using var reader = new StreamReader(entry.Open());
        var temporary = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temporary, reader.ReadToEnd());
            return ReadStaticId(temporary);
        }
        finally { File.Delete(temporary); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
