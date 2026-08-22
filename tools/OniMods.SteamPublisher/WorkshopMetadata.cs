using System.Text;
using System.Text.RegularExpressions;

internal sealed record WorkshopMetadata(
    string ContentFolder,
    string PreviewFile,
    string Title,
    string EnglishDescription,
    string ChineseDescription,
    string ChangeNote);

internal static partial class WorkshopMetadataReader
{
    internal static WorkshopMetadata Read(string vdfPath)
    {
        if (!File.Exists(vdfPath))
        {
            throw new FileNotFoundException("Workshop VDF not found", vdfPath);
        }

        var values = ReadEntries(vdfPath);
        RequireValue(values, "appid", WorkshopTarget.AppId.ToString());
        RequireValue(values, "publishedfileid", WorkshopTarget.WorkshopId.ToString());
        var contentFolder = RequirePath(values, "contentfolder", directory: true);
        var combinedDescription = RequireText(values, "description");
        var metadata = new WorkshopMetadata(
            contentFolder,
            RequirePath(values, "previewfile", directory: false),
            RequireText(values, "title"),
            ReadLocalizedDescription(contentFolder, "steam-description-en.md")
                ?? combinedDescription,
            ReadLocalizedDescription(contentFolder, "steam-description-zh.md")
                ?? combinedDescription,
            RequireText(values, "changenote"));
        ValidateLengths(metadata);
        return metadata;
    }

    private static Dictionary<string, string> ReadEntries(string vdfPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(vdfPath))
        {
            var match = VdfEntryPattern().Match(line);
            if (match.Success)
            {
                values[match.Groups["key"].Value] = UnescapeVdf(match.Groups["value"].Value);
            }
        }
        return values;
    }

    private static string? ReadLocalizedDescription(string contentFolder, string fileName)
    {
        var path = Path.Combine(contentFolder, fileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static void ValidateLengths(WorkshopMetadata metadata)
    {
        if (metadata.Title.Length > 128)
        {
            throw new InvalidOperationException("Workshop title exceeds 128 characters");
        }
        ValidateDescriptionLength("English", metadata.EnglishDescription);
        ValidateDescriptionLength("Chinese", metadata.ChineseDescription);
    }

    private static void ValidateDescriptionLength(string language, string description)
    {
        if (description.Length > 8000)
        {
            throw new InvalidOperationException(
                $"{language} Workshop description exceeds 8000 characters");
        }
    }

    private static string RequireText(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Workshop VDF is missing {key}");
        }
        return value;
    }

    private static void RequireValue(
        IReadOnlyDictionary<string, string> values, string key, string expected)
    {
        var value = RequireText(values, key);
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Workshop VDF {key} is {value}, expected {expected}");
        }
    }

    private static string RequirePath(
        IReadOnlyDictionary<string, string> values, string key, bool directory)
    {
        var path = Path.GetFullPath(RequireText(values, key));
        var exists = directory ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
        {
            throw new InvalidOperationException($"Workshop VDF {key} does not exist: {path}");
        }
        return path;
    }

    private static string UnescapeVdf(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index + 1 >= value.Length)
            {
                result.Append(value[index]);
                continue;
            }
            index++;
            result.Append(value[index] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '"' => '"',
                _ => value[index],
            });
        }
        return result.ToString();
    }

    [GeneratedRegex("""^\s*"(?<key>[^"]+)"\s+"(?<value>(?:\\.|[^"])*)"\s*$""")]
    private static partial Regex VdfEntryPattern();
}
