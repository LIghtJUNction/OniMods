using System;
using System.IO;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool TryGetBlueprintPath(string input, bool mustExist, out string path, out string error)
        {
            path = null;
            error = null;
            string value = (input ?? string.Empty).Trim();
            if (value.StartsWith("/active/blueprints/", StringComparison.Ordinal))
                value = value.Substring("/active/blueprints/".Length);
            else if (value.StartsWith(BlueprintPrefix, StringComparison.Ordinal))
                value = value.Substring(BlueprintPrefix.Length);
            else if (Path.IsPathRooted(value))
            {
                error = "Absolute host paths are not accepted for blueprints.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".."
                || value.Contains("/") || value.Contains("\\") || value.Contains(":") || value.Contains(".."))
            {
                error = "Blueprint target must be a single safe file name inside /active/blueprints/.";
                return false;
            }
            if (value.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 3) + ".blueprint";
            else if (string.IsNullOrEmpty(Path.GetExtension(value)))
                value += ".blueprint";
            else if (!value.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase))
            {
                error = "Blueprint targets must use the .blueprint extension.";
                return false;
            }

            string root = BlueprintRootFullPath();
            string candidate = Path.GetFullPath(Path.Combine(root, value));
            if (!IsBlueprintDiskPath(candidate))
            {
                error = "Blueprint path escapes the configured blueprint directory.";
                return false;
            }
            if (mustExist && !File.Exists(candidate))
            {
                string match = Directory.Exists(root)
                    ? Array.Find(Directory.GetFiles(root, "*.blueprint"), item => string.Equals(Path.GetFileName(item), value, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (match == null || !IsBlueprintDiskPath(match))
                {
                    error = "Blueprint not found.";
                    return false;
                }
                candidate = Path.GetFullPath(match);
            }
            if (!ValidateBlueprintPathComponents(candidate, out error))
                return false;
            if (File.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            {
                error = "Blueprint symlinks/reparse points are not accepted.";
                return false;
            }
            path = candidate;
            return true;
        }

        private static string BlueprintRootFullPath()
        {
            return Path.GetFullPath(BlueprintDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsBlueprintDiskPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".blueprint", StringComparison.OrdinalIgnoreCase))
                return false;
            string root = BlueprintRootFullPath() + Path.DirectorySeparatorChar;
            string canonical = Path.GetFullPath(path);
            return canonical.StartsWith(root, StringComparison.Ordinal) && !string.Equals(canonical, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
        }

        private static bool ValidateBlueprintPathComponents(string path, out string error)
        {
            error = null;
            string canonical = Path.GetFullPath(path);
            string root = Path.GetPathRoot(canonical);
            string current = root;
            foreach (string component in canonical.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                if (!Directory.Exists(current) && !File.Exists(current))
                    continue;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    error = "Blueprint path contains a symlink/reparse component: " + current;
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateBlueprintIoPath(string path, out string error)
        {
            if (!IsBlueprintDiskPath(path))
            {
                error = "Blueprint I/O path is outside the blueprint directory or has the wrong extension.";
                return false;
            }
            return ValidateBlueprintPathComponents(path, out error);
        }

        private static string ReadBlueprintText(string path)
        {
            if (!ValidateBlueprintIoPath(path, out string error))
                throw new InvalidOperationException(error);
            string text = File.ReadAllText(path);
            if (!ValidateBlueprintIoPath(path, out error))
                throw new InvalidOperationException("Blueprint path changed during read validation: " + error);
            return text;
        }

        private static bool AtomicWriteBlueprint(string path, string content, out string error)
        {
            error = null;
            if (!ValidateBlueprintIoPath(path, out error))
            {
                return false;
            }
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            if (!ValidateBlueprintIoPath(path, out error))
                return false;
            string temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, content);
                if (!ValidateBlueprintIoPath(path, out error))
                    throw new IOException("Blueprint path changed before atomic replace: " + error);
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
                if (!ValidateBlueprintIoPath(path, out error))
                    throw new IOException("Blueprint path changed after atomic replace: " + error);
                return true;
            }
            catch (Exception ex)
            {
                error = "Atomic blueprint write failed: " + ex.Message;
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }
    }
}
