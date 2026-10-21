using System;
using System.IO;

namespace OniMcp.Patches
{
    internal static class LoadScreenPreviewPolicy
    {
        internal static bool IsMissingOptionalPreview(string source, bool ignoreMissing)
        {
            if (!ignoreMissing || !string.Equals(Path.GetExtension(source), ".png", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                File.GetAttributes(source);
                return false;
            }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
