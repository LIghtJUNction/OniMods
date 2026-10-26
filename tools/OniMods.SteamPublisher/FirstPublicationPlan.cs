using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record FirstPublicationPlan(
    string ZipPath, string ZipSha256, long ZipBytes,
    string PreviewPath, string PreviewSha256,
    string Title, string EnglishDescription, string ChineseDescription,
    string[] Tags, string PlanSha256)
{
    internal static string Name => WorkshopTarget.DisplayName switch
    {
        "OniOnline" => "OniOnline",
        "Jlib" => "Jlib",
        _ => throw new InvalidOperationException("First publication supports only the fixed OniOnline and Jlib targets"),
    };
    internal static string StaticId => "LIghtJUNction." + Name;

    internal static void RequireTarget()
    {
        if (WorkshopTarget.DisplayName != Name || WorkshopTarget.WorkshopId != 0
            || WorkshopTarget.CreatorAppId != 636750 || WorkshopTarget.ConsumerAppId != 457140
            || WorkshopTarget.ExpectedOwner != 76561199137573787)
            throw new InvalidOperationException("First publication requires a fixed target, ID 0, owner and App IDs");
    }

    internal static FirstPublicationPlan Create(WorkshopMetadata metadata)
    {
        RequireTarget();
        if (!metadata.Title.StartsWith(Name, StringComparison.Ordinal)
            || metadata.Title.Length > 128 || metadata.EnglishDescription.Length is 0 or > 8000
            || metadata.ChineseDescription.Length is 0 or > 8000)
            throw new InvalidOperationException($"First publication requires reviewed {Name} metadata within Steam limits");
        var extension = Path.GetExtension(metadata.PreviewFile).ToLowerInvariant();
        var preview = File.ReadAllBytes(metadata.PreviewFile);
        if (extension is not (".png" or ".jpg" or ".jpeg") || preview.Length is 0 or > 10 * 1024 * 1024)
            throw new InvalidOperationException("First publication preview must be PNG/JPEG, 1 byte to 10 MiB");
        var modYaml = File.ReadAllText(Path.Combine(metadata.ContentFolder, "mod.yaml"));
        if (!modYaml.Split('\n').Any(line => line.Trim() == $"staticID: {StaticId}"
            || line.Trim() == $"staticID: \"{StaticId}\""))
            throw new InvalidOperationException($"First publication content has the wrong {Name} staticID");
        var package = LegacyPackage.Create(metadata);
        var plan = new FirstPublicationPlan(package.Path, Hash(package.Bytes), package.Bytes.Length,
            Path.GetFullPath(metadata.PreviewFile), Hash(preview), metadata.Title,
            metadata.EnglishDescription, metadata.ChineseDescription,
            Name == "Jlib" ? ["tweaks"] : ["tweaks", "Spaced Out!"], "");
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
        target = StaticId, creatorApp = 636750, consumerApp = 457140, owner = 76561199137573787UL,
        visibility = "PrivateThenPublic", ZipPath, ZipSha256, ZipBytes, PreviewPath, PreviewSha256,
        Title, EnglishDescription, ChineseDescription, Tags,
    })));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
