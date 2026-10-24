using System;
using System.IO;
using System.Linq;
using OniOnline.Runtime;

internal static class NativeModContentDigestTests
{
    internal static void Run(System.Action<bool, string> check)
    {
        string fixture = Path.Combine(Path.GetTempPath(), "oni-mod-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            string left = Directory.CreateDirectory(Path.Combine(fixture, "left")).FullName;
            string right = Directory.CreateDirectory(Path.Combine(fixture, "right")).FullName;
            Write(left, "mod.yaml", "title: Test\n");
            Write(left, "Engine.dll", "compiled-code");
            Write(left, "data/recipes.json", "{\"recipes\":1}");
            Write(left, "assets/ui.png", "texture-v1");
            Write(left, "assets/ui.prefab", "prefab-v1");
            Write(left, "localization/menu.csv", "key,value\n");
            Write(right, "data/recipes.json", "{\"recipes\":1}");
            Write(right, "Engine.dll", "compiled-code");
            Write(right, "mod.yaml", "title: Test\n");
            Write(right, "assets/ui.png", "texture-v1");
            Write(right, "assets/ui.prefab", "prefab-v1");
            Write(right, "localization/menu.csv", "key,value\n");
            File.SetLastWriteTimeUtc(Path.Combine(right, "Engine.dll"), DateTime.UtcNow.AddDays(-1));
            string baseline = Digest(left, check);
            check(baseline.Length == 64 && baseline.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f'), "digest is lower-case SHA-256");
            check(baseline == Digest(right, check), "root paths, enumeration order and timestamps do not affect package identity");
            Write(right, "assets/ui.png", "texture-v2");
            check(baseline != Digest(right, check), "changed packaged image changes digest");
            Write(right, "assets/ui.png", "texture-v1");
            Write(right, "assets/ui.prefab", "prefab-v2");
            check(baseline != Digest(right, check), "changed Unity prefab changes digest");
            Write(right, "assets/ui.prefab", "prefab-v1");
            Write(right, "localization/menu.csv", "key,label\n");
            check(baseline != Digest(right, check), "changed packaged localization changes digest");
            Write(right, "localization/menu.csv", "key,value\n");
            Write(right, "data/recipes.json", "{\"recipes\":2}");
            check(baseline != Digest(right, check), "changed packaged data changes digest");
            Write(right, "data/recipes.json", "{\"recipes\":1}");
            File.Move(Path.Combine(right, "Engine.dll"), Path.Combine(right, "Other.dll"));
            check(baseline != Digest(right, check), "relative file identity affects digest");

            Write(left, ".env", "BILI_COOKIE=private-cookie");
            Write(left, "options.json", "{\"api_key\":\"private\"}");
            Write(left, "credentials.bin", "private-credential");
            Write(left, "settings/private.json", "private-config");
            Write(left, "logs/session.json", "private-session");
            Write(left, "saves/game.bin", "private-save");
            Write(left, "source/Code.cs", "private-source");
            Write(left, "README.md", "documentation");
            Write(left, "Engine.pdb", "private-paths");
            check(baseline == Digest(left, check), "private settings, credentials, saves, logs, source and documentation are excluded");
            Write(left, "assets/settings.png", "private-resource");
            Failure(left, "content_data_unclassified", check);
            File.Delete(Path.Combine(left, "assets/settings.png"));
            Write(left, "data/settings.json", "private-or-gameplay");
            Failure(left, "content_data_unclassified", check);
            File.Delete(Path.Combine(left, "data/settings.json"));
            Write(left, "unclassified.json", "private-or-gameplay");
            Failure(left, "content_data_unclassified", check);
            File.Delete(Path.Combine(left, "unclassified.json"));
            Write(left, "Authentication.dll", "plugin");
            check(baseline != Digest(left, check), "code filenames are not filtered as private JSON settings");

            string empty = Directory.CreateDirectory(Path.Combine(fixture, "empty")).FullName;
            Write(empty, "README.md", "no packaged data");
            Failure(empty, "content_empty", check);
            Failure(Path.Combine(fixture, "missing"), "content_unavailable", check);
            Failure("", "invalid_content_root", check);
            Failure(Path.GetPathRoot(fixture), "invalid_content_root", check);

            string mutation = Directory.CreateDirectory(Path.Combine(fixture, "mutation")).FullName;
            Write(mutation, "Main.dll", "first");
            int calls = 0;
            string digest, error;
            bool success = NativeModContentDigest.TryCompute(mutation, () => ++calls < 2, out digest, out error);
            check(!success && digest == null && error == "content_source_changed", "native catalog invalidation cancels hash without exposing partial identity");
            success = NativeModContentDigest.TryCompute(mutation, () => { throw new IOException("private/path/credential"); }, out digest, out error);
            check(!success && digest == null && error == "content_source_changed", "source callback exceptions expose no private paths");
            calls = 0;
            DateTime originalTime = File.GetLastWriteTimeUtc(Path.Combine(mutation, "Main.dll"));
            success = NativeModContentDigest.TryCompute(mutation, () =>
            {
                if (++calls == 3)
                {
                    Write(mutation, "Main.dll", "other");
                    File.SetLastWriteTimeUtc(Path.Combine(mutation, "Main.dll"), originalTime);
                }
                return true;
            }, out digest, out error);
            check(!success && digest == null && error == "content_changed", "second hash pass detects same-length content changes even when timestamp is restored");
            calls = 0;
            success = NativeModContentDigest.TryCompute(mutation, () =>
            {
                if (++calls == 3) Write(mutation, "Added.dll", "late-content");
                return true;
            }, out digest, out error);
            check(!success && digest == null && error == "content_changed", "rescanning rejects selected-file additions during hashing");

            string overFiles = Directory.CreateDirectory(Path.Combine(fixture, "file-count")).FullName;
            for (int i = 0; i <= NativeModContentDigest.MaxContentFiles; i++) Write(overFiles, i + ".dll", "x");
            Failure(overFiles, "content_file_count_budget", check);
            string overEntries = Directory.CreateDirectory(Path.Combine(fixture, "entry-count")).FullName;
            for (int i = 0; i <= NativeModContentDigest.MaxEntries; i++) Write(overEntries, i + ".txt", "");
            Failure(overEntries, "content_entry_budget", check);
            string deep = Directory.CreateDirectory(Path.Combine(fixture, "deep")).FullName;
            string relative = string.Join("/", Enumerable.Repeat("nested", NativeModContentDigest.MaxDepth + 1)) + "/Main.dll";
            Write(deep, relative, "x");
            Failure(deep, "content_depth_budget", check);
            string oversized = Directory.CreateDirectory(Path.Combine(fixture, "oversized")).FullName;
            Sparse(oversized, "Main.dll", NativeModContentDigest.MaxFileBytes + 1);
            Failure(oversized, "content_file_budget", check);
            string overTotal = Directory.CreateDirectory(Path.Combine(fixture, "total")).FullName;
            Sparse(overTotal, "A.dll", NativeModContentDigest.MaxFileBytes);
            Sparse(overTotal, "B.dll", NativeModContentDigest.MaxFileBytes);
            Sparse(overTotal, "C.dll", 1);
            Failure(overTotal, "content_byte_budget", check);

            string links = Directory.CreateDirectory(Path.Combine(fixture, "links")).FullName;
            Write(links, "Main.dll", "x");
            File.CreateSymbolicLink(Path.Combine(links, "outside.dll"), Path.Combine(left, "Engine.dll"));
            Failure(links, "content_link_not_allowed", check);
            File.Delete(Path.Combine(links, "outside.dll"));
            Directory.CreateSymbolicLink(Path.Combine(links, "outside"), left);
            Failure(links, "content_link_not_allowed", check);
            Directory.Delete(Path.Combine(links, "outside"));
            Directory.CreateSymbolicLink(Path.Combine(fixture, "linked-root"), links);
            Failure(Path.Combine(fixture, "linked-root"), "content_link_not_allowed", check);
            Directory.CreateSymbolicLink(Path.Combine(fixture, "linked-parent"), fixture);
            Failure(Path.Combine(fixture, "linked-parent", "left"), "content_link_not_allowed", check);
            if (Path.DirectorySeparatorChar == '/')
            {
                string odd = Directory.CreateDirectory(Path.Combine(fixture, "odd")).FullName;
                Write(odd, "back\\slash.dll", "x");
                Failure(odd, "content_path_invalid", check);
                File.Delete(Path.Combine(odd, "back\\slash.dll"));
                Write(odd, "Case.dll", "one");
                Write(odd, "case.dll", "two");
                Failure(odd, "content_name_collision", check);
            }
        }
        finally { Directory.Delete(fixture, true); }
    }

    private static string Digest(string root, System.Action<bool, string> check)
    {
        string digest, error;
        bool success = NativeModContentDigest.TryCompute(root, out digest, out error);
        check(success && error == null, "controlled package content can be fingerprinted: " + error);
        return digest;
    }

    private static void Failure(string root, string expected, System.Action<bool, string> check)
    {
        string digest, error;
        bool success = NativeModContentDigest.TryCompute(root, out digest, out error);
        check(!success && digest == null && error == expected, "content boundary returns " + expected + ", observed " + error);
        check(error != null && !error.Contains(root.Length == 0 ? "private-secret" : root), "failure does not expose source paths");
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, content);
    }

    private static void Sparse(string root, string relative, long length)
    {
        using (var stream = File.Create(Path.Combine(root, relative))) stream.SetLength(length);
    }
}
