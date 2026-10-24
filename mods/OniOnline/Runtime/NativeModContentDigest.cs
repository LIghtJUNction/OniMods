using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OniOnline.Runtime
{
    // Hash only a mod's declared package root, never an ONI user-data root.
    // JSON/YAML/localization data outside the known package-data directories is
    // ambiguous: fail closed instead of omitting gameplay data or fingerprinting
    // private settings. Excluded: hidden files/directories, private settings,
    // source, logs and saves. Common Unity/package resources are included even
    // when they are not text data; otherwise a changed texture, prefab or audio
    // file could pass the content gate with the same assembly and version.
    // Two hash passes and metadata scans reject ordinary concurrent updates; a
    // caller must also hold its catalog epoch stable. This is not a filesystem lock.
    internal static class NativeModContentDigest
    {
        internal const int MaxContentFiles = 512;
        internal const int MaxEntries = 4096;
        internal const int MaxDepth = 8;
        internal const long MaxFileBytes = 128L * 1024 * 1024;
        internal const long MaxTotalBytes = 256L * 1024 * 1024;
        private static readonly HashSet<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".dll", ".so", ".dylib", ".bytes", ".bin" };
        private static readonly HashSet<string> ResourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Images and UI resources
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".tif", ".tiff", ".webp", ".svg", ".ico",
            // Unity-authored assets and runtime bundles
            ".asset", ".prefab", ".mat", ".shader", ".anim", ".controller", ".overridecontroller",
            ".spriteatlas", ".fontsettings", ".guiskin", ".physicmaterial", ".physicmaterial2d",
            ".terrainlayer", ".mask", ".flare", ".cubemap", ".rendertexture", ".bundle", ".manifest",
            ".resource", ".ress", ".resss",
            // Fonts and audio
            ".ttf", ".otf", ".wav", ".ogg", ".mp3", ".aif", ".aiff", ".flac"
        };
        private static readonly HashSet<string> DataExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".json", ".yaml", ".yml", ".csv", ".po", ".xml", ".loc", ".properties" };
        private static readonly HashSet<string> DataRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "data", "worldgen", "elements", "buildings", "templates", "content", "assets", "localization", "strings", "translations", "lang" };
        private static readonly HashSet<string> PrivateDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "config", "configs", "configuration", "settings", "options", "cache", "caches", "logs", "log",
            "save", "saves", "save_files", "cloud_save_files", "source", "src", "obj", "credentials",
            "secrets", "tokens", "cookies", "sessions", "userdata", "user-data"
        };
        private static readonly string[] PrivateDataNames =
        { "config", "setting", "option", "secret", "credential", "cookie", "password", "apikey", "api_key", "api-key", "token", "session" };
        private sealed class ContentFailure : Exception
        {
            internal readonly string Code;
            internal ContentFailure(string code) { Code = code; }
        }
        private sealed class ContentFile
        {
            internal string Path, Name;
            internal long Length, LastWrite;
            internal FileAttributes Attributes;
        }

        internal static bool TryCompute(string contentRoot, out string sha256, out string error)
        { return TryCompute(contentRoot, null, out sha256, out error); }

        // The caller may invalidate a scan when its native loader/catalog epoch
        // changes. No filenames or contents are given to this callback.
        internal static bool TryCompute(string contentRoot, Func<bool> sourceStillCurrent, out string sha256, out string error)
        {
            sha256 = null; error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(contentRoot)) throw new ContentFailure("invalid_content_root");
                string root = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (root.Length == 0 || string.Equals(root, Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new ContentFailure("invalid_content_root");
                if (!Directory.Exists(root)) throw new ContentFailure("content_unavailable");
                RejectLinkedAncestors(root);
                RequireCurrent(sourceStillCurrent);
                var first = Scan(root);
                byte[] digest = Hash(first, sourceStillCurrent);
                RequireCurrent(sourceStillCurrent);
                var second = Scan(root);
                RequireSame(first, second);
                byte[] repeated = Hash(second, sourceStillCurrent);
                var final = Scan(root);
                RequireSame(second, final);
                RequireCurrent(sourceStillCurrent);
                if (!digest.SequenceEqual(repeated)) throw new ContentFailure("content_changed");
                sha256 = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
                return true;
            }
            catch (ContentFailure failure) { error = failure.Code; }
            catch (IOException) { error = "content_read_failed"; }
            catch (UnauthorizedAccessException) { error = "content_read_failed"; }
            catch (System.Security.SecurityException) { error = "content_read_failed"; }
            catch (ArgumentException) { error = "content_path_invalid"; }
            catch (NotSupportedException) { error = "content_path_invalid"; }
            return false;
        }

        private static void RequireCurrent(Func<bool> predicate)
        {
            if (predicate == null) return;
            bool current;
            try { current = predicate(); }
            catch (Exception) { throw new ContentFailure("content_source_changed"); }
            if (!current) throw new ContentFailure("content_source_changed");
        }

        private static void RejectLinkedAncestors(string root)
        {
            for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ContentFailure("content_link_not_allowed");
        }

        private static List<ContentFile> Scan(string root)
        {
            var files = new List<ContentFile>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<KeyValuePair<string, int>>();
            pending.Push(new KeyValuePair<string, int>(root, 0));
            long total = 0;
            int entries = 0;
            while (pending.Count != 0)
            {
                var directory = pending.Pop();
                if ((File.GetAttributes(directory.Key) & FileAttributes.ReparsePoint) != 0)
                    throw new ContentFailure("content_link_not_allowed");
                foreach (string path in Directory.EnumerateFileSystemEntries(directory.Key))
                {
                    if (++entries > MaxEntries) throw new ContentFailure("content_entry_budget");
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ContentFailure("content_link_not_allowed");
                    string relative = path.Substring(root.Length + 1);
                    string name = CanonicalName(relative);
                    string leaf = Path.GetFileName(path);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (leaf.StartsWith(".", StringComparison.Ordinal) || PrivateDirectories.Contains(leaf)) continue;
                        if (directory.Value + 1 > MaxDepth) throw new ContentFailure("content_depth_budget");
                        pending.Push(new KeyValuePair<string, int>(path, directory.Value + 1));
                        continue;
                    }
                    if (!Include(name, leaf)) continue;
                    var info = new FileInfo(path);
                    if (info.Length > MaxFileBytes) throw new ContentFailure("content_file_budget");
                    if (files.Count >= MaxContentFiles) throw new ContentFailure("content_file_count_budget");
                    total += info.Length;
                    if (total > MaxTotalBytes) throw new ContentFailure("content_byte_budget");
                    if (!names.Add(name)) throw new ContentFailure("content_name_collision");
                    files.Add(new ContentFile { Path = path, Name = name, Length = info.Length,
                        LastWrite = info.LastWriteTimeUtc.Ticks, Attributes = attributes });
                }
            }
            if (files.Count == 0) throw new ContentFailure("content_empty");
            files.Sort((a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
            return files;
        }

        private static string CanonicalName(string relative)
        {
            if (relative.Length == 0 || relative.Length > 512 || relative.IndexOf(':') >= 0)
                throw new ContentFailure("content_path_invalid");
            foreach (char c in relative) if (char.IsControl(c)) throw new ContentFailure("content_path_invalid");
            string name = relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/').Normalize(NormalizationForm.FormC);
            if (name.IndexOf('\\') >= 0) throw new ContentFailure("content_path_invalid");
            if (name.Split('/').Any(part => part.Length == 0 || part == "." || part == "..")) throw new ContentFailure("content_path_invalid");
            return name;
        }

        private static bool Include(string name, string leaf)
        {
            if (leaf.StartsWith(".", StringComparison.Ordinal)) return false;
            string extension = Path.GetExtension(leaf);
            string stem = Path.GetFileNameWithoutExtension(leaf).ToLowerInvariant();
            if ((extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bytes", StringComparison.OrdinalIgnoreCase)) &&
                PrivateDataNames.Any(fragment => stem.Contains(fragment)))
            {
                if (name.IndexOf('/') >= 0) throw new ContentFailure("content_data_unclassified");
                return false;
            }
            if (ResourceExtensions.Contains(extension))
            {
                // A private-looking resource at package root is ignored; the
                // same name inside a public package directory is ambiguous and
                // must be declared rather than silently fingerprinted.
                if (PrivateDataNames.Any(fragment => stem.Contains(fragment)))
                {
                    if (name.IndexOf('/') >= 0) throw new ContentFailure("content_data_unclassified");
                    return false;
                }
                return true;
            }
            if (CodeExtensions.Contains(extension)) return true;
            if (!DataExtensions.Contains(extension)) return false;
            if (PrivateDataNames.Any(fragment => stem.Contains(fragment)))
            {
                // A settings-looking file beneath packaged data may affect play.
                // Do not silently omit it or read it as if it were public content.
                if (name.IndexOf('/') >= 0) throw new ContentFailure("content_data_unclassified");
                return false;
            }
            if (name.IndexOf('/') < 0 && (leaf.Equals("mod.yaml", StringComparison.OrdinalIgnoreCase) || leaf.Equals("mod_info.yaml", StringComparison.OrdinalIgnoreCase))) return true;
            int slash = name.IndexOf('/');
            if (slash > 0 && DataRoots.Contains(name.Substring(0, slash))) return true;
            throw new ContentFailure("content_data_unclassified");
        }

        private static byte[] Hash(List<ContentFile> files, Func<bool> sourceStillCurrent)
        {
            using (var material = new MemoryStream())
            using (var writer = new BinaryWriter(material, new UTF8Encoding(false, true), true))
            {
                writer.Write(0x31444d4f); // OMD1: file identity plus hashes, never timestamps or absolute roots.
                writer.Write(files.Count);
                byte[] buffer = new byte[32768];
                foreach (var file in files)
                {
                    RequireCurrent(sourceStillCurrent);
                    RequireUnchanged(file);
                    byte[] hash;
                    using (var algorithm = SHA256.Create())
                    using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                    {
                        if (stream.Length != file.Length) throw new ContentFailure("content_changed");
                        long read = 0;
                        int count;
                        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            read += count;
                            if (read > file.Length) throw new ContentFailure("content_changed");
                            algorithm.TransformBlock(buffer, 0, count, buffer, 0);
                        }
                        if (read != file.Length) throw new ContentFailure("content_changed");
                        algorithm.TransformFinalBlock(new byte[0], 0, 0);
                        hash = algorithm.Hash;
                    }
                    RequireUnchanged(file);
                    writer.Write(file.Name); writer.Write(file.Length); writer.Write(hash);
                }
                writer.Flush();
                using (var algorithm = SHA256.Create()) return algorithm.ComputeHash(material.ToArray());
            }
        }

        private static void RequireUnchanged(ContentFile expected)
        {
            RejectLinkedAncestors(Path.GetDirectoryName(expected.Path));
            FileAttributes attributes = File.GetAttributes(expected.Path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ContentFailure("content_link_not_allowed");
            var info = new FileInfo(expected.Path);
            if (attributes != expected.Attributes || info.Length != expected.Length || info.LastWriteTimeUtc.Ticks != expected.LastWrite)
                throw new ContentFailure("content_changed");
        }

        private static void RequireSame(List<ContentFile> expected, List<ContentFile> actual)
        {
            if (expected.Count != actual.Count) throw new ContentFailure("content_changed");
            for (int i = 0; i < expected.Count; i++)
                if (expected[i].Name != actual[i].Name || expected[i].Length != actual[i].Length ||
                    expected[i].LastWrite != actual[i].LastWrite || expected[i].Attributes != actual[i].Attributes)
                    throw new ContentFailure("content_changed");
        }
    }
}
