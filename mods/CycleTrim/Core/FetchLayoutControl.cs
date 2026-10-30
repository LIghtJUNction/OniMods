using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace CycleTrim.Core
{
    // Private, opt-in experiment protocol. Acknowledgments bind a selected layout
    // and its actual call counters to one process and one caller-generated nonce.
    internal sealed class FetchLayoutControl
    {
        private const int MaximumRequestLength = 128;
        private const string ArgumentPrefix = "--cycletrim-layout-control=";
        private readonly string controlPath;
        private readonly int processId;
        private readonly char[] requestBuffer = new char[MaximumRequestLength + 1];
        private volatile bool compact;
        private long baselineCalls;
        private long compactCalls;
        private string previous;

        internal bool Compact => compact;

        internal FetchLayoutControl(string controlPath, int processId)
        {
            if (string.IsNullOrWhiteSpace(controlPath))
                throw new ArgumentException("An experiment control path is required.", nameof(controlPath));
            if (processId <= 0)
                throw new ArgumentOutOfRangeException(nameof(processId));
            this.controlPath = Path.GetFullPath(controlPath);
            this.processId = processId;
        }

        internal static string FindControlPath(string[] arguments)
        {
            string path = null;
            foreach (var argument in arguments)
            {
                if (argument == null || !argument.StartsWith(ArgumentPrefix, StringComparison.Ordinal))
                    continue;
                if (path != null)
                    throw new ArgumentException("Specify the fetch layout control argument only once.");
                path = argument.Substring(ArgumentPrefix.Length);
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("The fetch layout control path cannot be empty.");
                path = Path.GetFullPath(path);
            }
            return path;
        }

        internal void RecordUpdate(bool compactMode)
        {
            if (compactMode)
                Interlocked.Increment(ref compactCalls);
            else
                Interlocked.Increment(ref baselineCalls);
        }

        // Unity calls this only on the main thread. Never switch a live collector
        // halfway through a simulation step, loading operation or nested callback.
        internal bool Poll(bool loadedAndPaused)
        {
            if (!loadedAndPaused || !File.Exists(controlPath))
                return false;

            string requested;
            using (var stream = new FileStream(controlPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
            {
                var count = reader.ReadBlock(requestBuffer, 0, requestBuffer.Length);
                if (count > MaximumRequestLength)
                    throw new InvalidDataException("Fetch layout request exceeds 128 characters.");
                requested = new string(requestBuffer, 0, count).Trim();
            }
            if (requested == previous)
                return false;
            var fields = requested.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || (fields[0] != "baseline" && fields[0] != "compact")
                || !ValidNonce(fields[1]))
                throw new InvalidDataException("Expected baseline|compact and an ASCII request nonce (1..64 characters).");

            var oldMode = compact;
            var destination = controlPath + ".ack";
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                compact = fields[0] == "compact";
                var acknowledgment = processId.ToString(CultureInfo.InvariantCulture) + " "
                    + fields[0] + " " + fields[1] + " "
                    + Interlocked.Read(ref baselineCalls).ToString(CultureInfo.InvariantCulture) + " "
                    + Interlocked.Read(ref compactCalls).ToString(CultureInfo.InvariantCulture);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(acknowledgment);
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null);
                else
                    File.Move(temporary, destination);
                previous = requested;
                return true;
            }
            catch
            {
                compact = oldMode;
                throw;
            }
            finally
            {
                // Delete only this invocation's uniquely named temporary file.
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static bool ValidNonce(string value)
        {
            if (value.Length == 0 || value.Length > 64)
                return false;
            foreach (var character in value)
            {
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || character == '-' || character == '_'))
                    return false;
            }
            return true;
        }
    }
}
