using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OniOnline.Sdk
{
    public sealed class LogEntry
    {
        public string Name { get; private set; }
        public string Text { get; private set; }

        public LogEntry(string name, string text)
        {
            if (!ValidName(name)) throw new ArgumentException("invalid_log_name");
            Name = name;
            Text = text ?? "";
        }

        internal static bool ValidName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.Length <= 80 && name.IndexOfAny(new[] { '/', '\\', '\r', '\n' }) < 0 &&
                name != "." && name != "..";
        }
    }

    public static class SessionLogPrivacy
    {
        public static string Scrub(string text, IEnumerable<string> names)
        {
            if (text == null) return "";
            var builder = new StringBuilder();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (IsChat(line)) continue;
                builder.Append(RedactNames(RedactSteamIds(line), names));
                if (i + 1 < lines.Length) builder.Append('\n');
            }
            return builder.ToString();
        }

        public static void WriteBundle(string zipPath, IEnumerable<LogEntry> entries, IEnumerable<string> names)
        {
            if (string.IsNullOrWhiteSpace(zipPath)) throw new ArgumentException("invalid_log_bundle");
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var files = new List<KeyValuePair<string, byte[]>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (LogEntry entry in entries)
            {
                if (entry == null || !seen.Add(entry.Name)) throw new ArgumentException("invalid_log_entry");
                files.Add(new KeyValuePair<string, byte[]>(entry.Name, Encoding.UTF8.GetBytes(Scrub(entry.Text, names))));
            }
            string directory = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(zipPath, StoredZip.Create(files));
        }

        private static bool IsChat(string line)
        {
            string trimmed = line.TrimStart();
            return trimmed.StartsWith("CHAT ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("chat:", StringComparison.OrdinalIgnoreCase);
        }

        private static string RedactSteamIds(string line)
        {
            var builder = new StringBuilder();
            int index = 0;
            while (index < line.Length)
            {
                int start = line.IndexOf("7656", index, StringComparison.Ordinal);
                if (start < 0) { builder.Append(line, index, line.Length - index); break; }
                int end = start;
                while (end < line.Length && end - start < 20 && line[end] >= '0' && line[end] <= '9') end++;
                if (end - start >= 17 && (start == 0 || !char.IsDigit(line[start - 1])) && (end == line.Length || !char.IsDigit(line[end])))
                {
                    builder.Append(line, index, start - index);
                    builder.Append("[steam]");
                    index = end;
                }
                else
                {
                    builder.Append(line, index, start + 4 - index);
                    index = start + 4;
                }
            }
            return builder.ToString();
        }

        private static string RedactNames(string line, IEnumerable<string> names)
        {
            if (names == null) return line;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || name.Length < 2) continue;
                line = line.Replace(name, "[name]");
            }
            return line;
        }
    }

    internal static class StoredZip
    {
        internal static byte[] Create(IList<KeyValuePair<string, byte[]>> files)
        {
            var output = new MemoryStream();
            var directory = new MemoryStream();
            int count = 0;
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                byte[] name = Encoding.UTF8.GetBytes(file.Key);
                uint crc = Crc32(file.Value);
                uint offset = (uint)output.Length;
                WriteLocal(output, name, file.Value, crc);
                WriteCentral(directory, name, file.Value, crc, offset);
                count++;
            }
            uint directoryOffset = (uint)output.Length;
            byte[] central = directory.ToArray();
            output.Write(central, 0, central.Length);
            WriteEnd(output, count, (uint)central.Length, directoryOffset);
            return output.ToArray();
        }

        private static void WriteLocal(Stream output, byte[] name, byte[] data, uint crc)
        {
            Write(output, 0x04034b50);
            WriteShort(output, 20);
            WriteShort(output, 1 << 11);
            WriteShort(output, 0);
            WriteShort(output, 0);
            WriteShort(output, 0);
            Write(output, crc);
            Write(output, (uint)data.Length);
            Write(output, (uint)data.Length);
            WriteShort(output, (ushort)name.Length);
            WriteShort(output, 0);
            output.Write(name, 0, name.Length);
            output.Write(data, 0, data.Length);
        }

        private static void WriteCentral(Stream output, byte[] name, byte[] data, uint crc, uint offset)
        {
            Write(output, 0x02014b50);
            WriteShort(output, 20);
            WriteShort(output, 20);
            WriteShort(output, 1 << 11);
            WriteShort(output, 0);
            WriteShort(output, 0);
            WriteShort(output, 0);
            Write(output, crc);
            Write(output, (uint)data.Length);
            Write(output, (uint)data.Length);
            WriteShort(output, (ushort)name.Length);
            WriteShort(output, 0);
            WriteShort(output, 0);
            WriteShort(output, 0);
            WriteShort(output, 0);
            Write(output, 0);
            Write(output, offset);
            output.Write(name, 0, name.Length);
        }

        private static void WriteEnd(Stream output, int count, uint size, uint offset)
        {
            Write(output, 0x06054b50);
            WriteShort(output, 0);
            WriteShort(output, 0);
            WriteShort(output, (ushort)count);
            WriteShort(output, (ushort)count);
            Write(output, size);
            Write(output, offset);
            WriteShort(output, 0);
        }

        private static void Write(Stream output, uint value)
        { output.Write(new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) }, 0, 4); }

        private static void WriteShort(Stream output, ushort value)
        { output.Write(new[] { (byte)value, (byte)(value >> 8) }, 0, 2); }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }
    }
}
