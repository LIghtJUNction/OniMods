using System;
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
        }

        public static OniSimPublicationReader Open(string absoluteNativeLibraryPath)
        {
            return new OniSimPublicationReader(NativeApi.LoadExplicit(absoluteNativeLibraryPath));
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
