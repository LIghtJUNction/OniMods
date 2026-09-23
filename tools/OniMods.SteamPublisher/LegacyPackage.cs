using System.IO.Compression;
using System.Security.Cryptography;

internal sealed record LegacyPackage(string Path, byte[] Bytes, string CloudFileName)
{
    private const int MaximumCloudFileBytes = 100 * 1024 * 1024;

    internal static LegacyPackage Create(WorkshopMetadata metadata)
    {
        var folder = metadata.ContentFolder;
        ValidateSourceFolder(new DirectoryInfo(folder));
        var output = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(folder)!,
            $"{WorkshopTarget.DisplayName}.workshop-legacy.zip");
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            ZipFile.CreateFromDirectory(
                folder, temporary, CompressionLevel.Optimal, includeBaseDirectory: false);
            ValidateArchive(temporary);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        var bytes = File.ReadAllBytes(output);
        if (bytes.Length == 0 || bytes.Length > MaximumCloudFileBytes)
        {
            throw new InvalidOperationException(
                $"Legacy Workshop ZIP must be 1 to {MaximumCloudFileBytes} bytes");
        }
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var cloudFileName = $"onim_{WorkshopTarget.WorkshopId}_{digest[..16]}.zip";
        return new LegacyPackage(output, bytes, cloudFileName);
    }

    private static void ValidateSourceFolder(DirectoryInfo directory)
    {
        if (IsLink(directory))
        {
            throw new InvalidOperationException(
                $"Workshop content contains a symbolic link: {directory.FullName}");
        }
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (IsLink(entry))
            {
                throw new InvalidOperationException(
                    $"Workshop content contains a symbolic link: {entry.FullName}");
            }
            if (IsSensitiveName(entry.Name))
            {
                throw new InvalidOperationException(
                    $"Workshop content contains a sensitive file name: {entry.FullName}");
            }
            if (entry is DirectoryInfo child)
            {
                ValidateSourceFolder(child);
            }
        }
    }

    private static bool IsLink(FileSystemInfo entry) =>
        entry.LinkTarget is not null
        || (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool IsSensitiveName(string name) =>
        name.Equals("OniMcpConfig.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals("credentials.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals("secrets.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".ssh", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".env", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateArchive(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var isDirectory = name.EndsWith("/", StringComparison.Ordinal);
            var relativePath = isDirectory ? name[..^1] : name;
            var segments = relativePath.Split('/');
            if (relativePath.Length == 0
                || name.StartsWith("/", StringComparison.Ordinal)
                || name.Contains("\\", StringComparison.Ordinal)
                || segments.Any(segment => segment is "" or "." or "..")
                || segments.Any(IsSensitiveName)
                || !names.Add(name))
            {
                throw new InvalidOperationException($"Unsafe Workshop ZIP entry: {name}");
            }
            if (isDirectory)
            {
                continue;
            }
            using var stream = entry.Open();
            stream.CopyTo(Stream.Null);
        }

        foreach (var required in new[]
        {
            "mod.yaml", "mod_info.yaml", $"{WorkshopTarget.DisplayName}.dll",
        })
        {
            if (!names.Contains(required))
            {
                throw new InvalidOperationException(
                    $"Legacy Workshop ZIP is missing root entry {required}");
            }
        }
    }
}
