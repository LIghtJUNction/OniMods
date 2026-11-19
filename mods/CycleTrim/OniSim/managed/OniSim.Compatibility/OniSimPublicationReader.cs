using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Threading;

namespace OniSim.Compatibility
{
    /// <summary>
    /// Read-only access to the active game's OniSim publication. Each Acquire
    /// pins the current GameData buffer until Release on the same thread.
    /// The reader does not initialize, own, or shut down the simulation.
    /// </summary>
    public sealed class OniSimPublicationReader
    {
        private readonly NativeApi api;
        [ThreadStatic]
        private static bool leaseHeld;
        [ThreadStatic]
        private static int leaseThreadId;

        private OniSimPublicationReader(NativeApi api)
        {
            this.api = api;
            if (api.AcquireMulticomponentUpdate == null || api.ReleaseMulticomponentUpdate == null)
                throw new NotSupportedException("Native library does not expose the multicomponent read-lease API.");
            LiquidVolumeFractionAvailable = HasLiquidVolumeFractionCapability(
                NativeApi.ReadUtf8(api.GetCapabilities()));
        }

        public static OniSimPublicationReader Open(string absoluteNativeLibraryPath)
        {
            return new OniSimPublicationReader(NativeApi.LoadExplicit(absoluteNativeLibraryPath));
        }

        /// <summary>True only when the native capability explicitly promises liquid occupancy.</summary>
        public bool LiquidVolumeFractionAvailable { get; private set; }

        /// <summary>
        /// A missing, malformed or non-boolean capability is unknown, not zero
        /// liquid. Parsing uses the framework serializer without adding a game dependency.
        /// </summary>
        public static bool HasLiquidVolumeFractionCapability(string capabilitiesJson)
        {
            if (!IsCompleteJsonObject(capabilitiesJson))
                return false;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(PublicationCapabilities));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(capabilitiesJson)))
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream, XmlDictionaryReaderQuotas.Max))
                {
                    var capabilities = serializer.ReadObject(reader) as PublicationCapabilities;
                    // Read through EOF so the reader validates the complete root
                    // and cannot leave trailing input unexamined.
                    while (reader.Read())
                        if (reader.NodeType != XmlNodeType.Whitespace
                            && reader.NodeType != XmlNodeType.SignificantWhitespace)
                            return false;
                    return capabilities != null && capabilities.LiquidVolumeFraction is bool
                        && (bool)capabilities.LiquidVolumeFraction;
                }
            }
            catch (SerializationException) { return false; }
            catch (XmlException) { return false; }
        }

        // DataContractJsonSerializer can accept EOF as an implicit object end.
        // Check structural completeness first; value syntax and types still use
        // the framework parser. This runs once at provider registration, not per cell.
        private static bool IsCompleteJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;
            int first = 0, last = json.Length - 1;
            while (char.IsWhiteSpace(json[first])) first++;
            while (char.IsWhiteSpace(json[last])) last--;
            if (json[first] != '{')
                return false;
            var closing = new Stack<char>();
            bool quoted = false, escaped = false;
            char previous = '\0';
            for (int i = first; i <= last; i++)
            {
                char value = json[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (value == '\\') escaped = true;
                    else if (value == '"') { quoted = false; previous = '"'; }
                    continue;
                }
                if (char.IsWhiteSpace(value)) continue;
                if (value == '"') quoted = true;
                else if (value == '{') closing.Push('}');
                else if (value == '[') closing.Push(']');
                else if (value == '}' || value == ']')
                {
                    if (previous == ',' || closing.Count == 0 || closing.Pop() != value)
                        return false;
                    if (closing.Count == 0)
                        return i == last;
                }
                previous = value;
            }
            return false;
        }

        [DataContract]
        private sealed class PublicationCapabilities
        {
            [DataMember(Name = "liquidVolumeFraction")]
            public object LiquidVolumeFraction = false;
        }

        public ulong ContentRevision { get { return api.GetMulticomponentRevision == null ? 0 : api.GetMulticomponentRevision(); } }

        public IntPtr Acquire()
        {
            if (leaseHeld)
                throw new InvalidOperationException("This reader already owns a publication lease.");
            IntPtr pointer = api.AcquireMulticomponentUpdate();
            CheckNativeStatus();
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException("Native multicomponent publication is unavailable.");
            leaseThreadId = Thread.CurrentThread.ManagedThreadId;
            leaseHeld = true;
            return pointer;
        }

        public void Release()
        {
            if (!leaseHeld)
                throw new InvalidOperationException("This reader does not own a publication lease.");
            if (leaseThreadId != Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("A multicomponent publication lease must be released on its acquiring thread.");

            api.ReleaseMulticomponentUpdate();
            CheckNativeStatus();
            leaseHeld = false;
            leaseThreadId = 0;
        }

        private void CheckNativeStatus()
        {
            int status = api.GetLastStatus();
            if (status == 0)
                return;
            string detail = NativeApi.ReadUtf8(api.GetLastError());
            throw new InvalidOperationException("OniSim publication operation failed (" + status + "): " + detail);
        }
    }
}
