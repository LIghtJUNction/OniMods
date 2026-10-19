using System;
using System.IO;
using System.Linq;
using OniMcp.Patches;

internal static class LoadScreenMigrationRegression
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-load-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string local = Path.Combine(root, "local");
            string cloud = Path.Combine(root, "cloud");
            Directory.CreateDirectory(local);
            Directory.CreateDirectory(cloud);
            byte[] save = { 1, 4, 8, 16 };
            string migratedSave = Path.Combine(cloud, "colony.sav");
            File.WriteAllBytes(migratedSave, save);
            Directory.Delete(local);
            string missingPreview = Path.Combine(local, "colony.png");

            bool nativeDirectoryFailure = false;
            try { File.Copy(missingPreview, Path.Combine(cloud, "colony.png")); }
            catch (DirectoryNotFoundException) { nativeDirectoryFailure = true; }
            Check(nativeDirectoryFailure, "fixture must exercise the missing-directory failure from File.Copy");
            Check(LoadScreenPreviewPolicy.IsMissingOptionalPreview(missingPreview, true),
                "optional preview in a removed source directory must finish successfully");
            Check(!Directory.Exists(local) && File.ReadAllBytes(migratedSave).SequenceEqual(save),
                "skipping a missing preview must not create a fake image or change the migrated save");

            Directory.CreateDirectory(local);
            Check(LoadScreenPreviewPolicy.IsMissingOptionalPreview(missingPreview, true),
                "missing preview in an existing directory is also optional");
            Check(!LoadScreenPreviewPolicy.IsMissingOptionalPreview(missingPreview, false),
                "strict migration must retain the native missing-file failure");
            Check(!LoadScreenPreviewPolicy.IsMissingOptionalPreview(Path.Combine(local, "colony.sav"), true),
                "required save data must never be skipped, even if ignoreMissing is supplied");
            Check(!LoadScreenPreviewPolicy.IsMissingOptionalPreview(Path.Combine(local, "notes.txt"), true),
                "unrelated missing files must remain native");

            byte[] preview = { 2, 5, 9 };
            File.WriteAllBytes(missingPreview, preview);
            Check(!LoadScreenPreviewPolicy.IsMissingOptionalPreview(missingPreview, true),
                "existing preview must use native copy, validation and deletion behavior");
            Check(File.ReadAllBytes(missingPreview).SequenceEqual(preview), "the guard must never change an existing preview");
            Check(!LoadScreenPreviewPolicy.IsMissingOptionalPreview(null, true), "invalid native paths must not be treated as optional previews");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS: Load menu optional-preview migration regressions");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

