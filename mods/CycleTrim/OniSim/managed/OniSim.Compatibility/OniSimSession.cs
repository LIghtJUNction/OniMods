using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace OniSim.Compatibility
{
    public sealed partial class OniSimSession : IDisposable
    {
        private static readonly object GlobalGate = new object();
        private static OniSimSession activeSession;
        [ThreadStatic] private static bool insideOperation;
        [ThreadStatic] private static bool insideCallback;

        private readonly NativeApi api;
        private readonly Func<int, IntPtr, int> callback;
        private readonly NativeApi.MessageCallback nativeCallback;
        private Exception callbackFailure;
        private int cellCount;
        private bool shutdownPending;
        private bool saveCleanupPending;
        private bool disposed;

        private OniSimSession(NativeApi native, Func<int, IntPtr, int> handler)
        { api = native; callback = handler; nativeCallback = DispatchCallback; }

        public static OniSimSession Open(string absoluteNativeLibraryPath)
        { return Open(absoluteNativeLibraryPath, null); }

        public static OniSimSession Open(string absoluteNativeLibraryPath, Func<int, IntPtr, int> callback)
        {
            RejectReentry();
            lock (GlobalGate)
            {
                if (activeSession != null) throw new InvalidOperationException("The process already owns an active native simulation session.");
                insideOperation = true;
                OniSimSession session = null;
                bool initializationRejected = false;
                bool ownsNativeState = false;
                try
                {
                    session = new OniSimSession(NativeApi.LoadExplicit(absoluteNativeLibraryPath), callback);
                    activeSession = session; // Roots the callback and retains the lease until explicit disposal.
                    session.api.TryInitialize(session.nativeCallback);
                    int status = session.api.GetLastStatus();
                    initializationRejected = status == 3 || status == 4;
                    ownsNativeState = status == 0;
                    session.ThrowNativeStatus(status, 0);
                    session.CheckCallbackFailure(0);
                    return session;
                }
                catch (Exception constructionError)
                {
                    Exception cleanupError = null;
                    if (ownsNativeState)
                    {
                        try { session.ShutdownCore(); }
                        catch (Exception error) { cleanupError = error; }
                    }
                    else if (session != null && !initializationRejected)
                    {
                        session.shutdownPending = true;
                        throw new InvalidOperationException(
                            "Native initialization outcome is unknown; the process lease and callback remain rooted. Restart the process before opening another session.", constructionError);
                    }
                    else if (session != null)
                    {
                        // Only InvalidState/ResourceLimit prove initialization was rejected.
                        // In particular, do not shut down another caller's active state.
                        session.ReleaseLease();
                    }
                    if (cleanupError != null)
                        throw new AggregateException(session.shutdownPending ?
                            "Native initialization cleanup is unconfirmed; the process lease and callback remain rooted." :
                            "Native initialization and shutdown both reported failure.", constructionError, cleanupError);
                    throw;
                }
                finally { insideOperation = false; }
            }
        }

        public string Describe()
        {
            return Locked(() =>
            {
                IntPtr text = api.GetCapabilities();
                CheckOperation(0, 0);
                return NativeApi.ReadUtf8(text) ?? throw new InvalidDataException("Native capability JSON is null.");
            });
        }

        public GridView AllocateCells(int width, int height, byte radiation, bool headless)
        {
            RejectReentry();
            OfficialMessages.ValidateDimensions(width, height);
            if (radiation > 1) throw new ArgumentOutOfRangeException("radiation");
            var payload = new byte[10];
            BinaryLayout.WriteI32(payload, 0, width); BinaryLayout.WriteI32(payload, 4, height);
            payload[8] = radiation; payload[9] = headless ? (byte)1 : (byte)0;
            return Locked(() => { SendCore(new OfficialMessage(OfficialMessages.Allocate, payload, true), 0); return NewView(); });
        }

        public GridView InitializeFromCells(byte[] gridImage)
        {
            RejectReentry();
            GridImage image = GridImage.Parse(gridImage);
            return Locked(() =>
            {
                SendCore(new OfficialMessage(OfficialMessages.Initialize, image.OwnedBytes, true), 0);
                return NewView();
            });
        }

        public GridView Start()
        {
            return Locked(() =>
            {
                SendCore(new OfficialMessage(OfficialMessages.Start, new byte[0], true), 0);
                return NewView();
            });
        }

        public void ReplaceCell(int index, float temperature, float mass, int diseaseCount,
            ushort elementIdx, byte diseaseIdx)
        {
            Locked(() =>
            {
                if (cellCount == 0) throw new InvalidOperationException("Allocate or initialize the grid before replacing a cell.");
                if (index < 0 || index >= cellCount) throw new ArgumentOutOfRangeException("index");
                OfficialMessages.ValidateCellValues(temperature, mass);
                if (diseaseCount < 0) throw new ArgumentOutOfRangeException("diseaseCount");
                if (elementIdx == ushort.MaxValue) throw new ArgumentOutOfRangeException("elementIdx");
                var payload = new byte[28];
                BinaryLayout.WriteI32(payload, 0, index); BinaryLayout.WriteI32(payload, 4, -1);
                BinaryLayout.WriteF32(payload, 8, temperature); BinaryLayout.WriteF32(payload, 12, mass);
                BinaryLayout.WriteI32(payload, 16, diseaseCount); BinaryLayout.WriteU16(payload, 20, elementIdx);
                payload[22] = 1; payload[23] = diseaseIdx; payload[24] = 1;
                SendCore(new OfficialMessage(OfficialMessages.Modify, payload, true), 0);
                return true;
            });
        }

        /// <summary>Sends one accepted official message. Returned pointers remain native-owned.</summary>
        public IntPtr SendMessage(OfficialMessage message)
        {
            RejectReentry();
            OfficialMessages.Validate(message);
            return Locked(() => SendCore(message, 0));
        }

        public BatchResult SendBatch(IList<OfficialMessage> messages)
        {
            RejectReentry();
            if (messages == null) throw new ArgumentNullException("messages");
            return Locked(() =>
            {
                int count = messages.Count;
                if (count < 1 || count > 4096) throw new ArgumentOutOfRangeException("messages");
                var prepared = new OfficialMessage[count];
                long totalBytes = 0;
                for (int i = 0; i < count; i++)
                {
                    OfficialMessage message = messages[i];
                    OfficialMessages.Validate(message);
                    totalBytes += message.OwnedPayload.Length;
                    if (totalBytes > BinaryLayout.MaxPayloadBytes) throw new ArgumentOutOfRangeException("messages");
                    prepared[i] = message;
                }
                uint executed = 0;
                foreach (OfficialMessage message in prepared)
                {
                    try { SendCore(message, executed); executed++; }
                    catch (OniSimOperationException) { throw; }
                    catch (Exception error)
                    {
                        throw new OniSimOperationException(0, executed,
                            "Managed dispatch failed after a batch prefix completed.", null, error);
                    }
                }
                return new BatchResult(executed);
            });
        }

        public byte[] Snapshot()
        {
            return Locked(() =>
            {
                int required = api.CopyGridImage(IntPtr.Zero, 0);
                CheckOperation(0, 0);
                if (required < 14 || required > 14 + 40 * BinaryLayout.MaxCells)
                    throw new InvalidDataException("Native grid image length is invalid.");
                var image = new byte[required];
                GCHandle pin = GCHandle.Alloc(image, GCHandleType.Pinned);
                try
                {
                    int copied = api.CopyGridImage(pin.AddrOfPinnedObject(), image.Length);
                    CheckOperation(0, 0);
                    if (copied != required) throw new InvalidDataException("Native grid image length changed while copying.");
                }
                finally { pin.Free(); }
                GridImage.Validate(image);
                return image;
            });
        }

        public GridView GetGridView() { return Locked(NewView); }

        internal byte[] GetDescriptor(ulong generation)
        {
            return Locked(() =>
            {
                ValidateViewCore(generation);
                var descriptor = new byte[GridView.DescriptorBytes];
                Marshal.Copy(GridPointer(), descriptor, 0, descriptor.Length);
                return descriptor;
            });
        }

        internal IntPtr GetGridPointer(ulong generation)
        { return Locked(() => { ValidateViewCore(generation); return GridPointer(); }); }
        internal void ValidateView(ulong generation)
        { Locked(() => { ValidateViewCore(generation); return true; }); }

        private GridView NewView()
        { GridPointer(); return new GridView(this, ReadGeneration()); }
        private void ValidateViewCore(ulong generation)
        {
            if (generation != ReadGeneration()) throw new InvalidOperationException("GridView belongs to an expired native grid generation.");
        }
        private ulong ReadGeneration()
        { ulong generation = api.GetGeneration(); CheckOperation(0, 0); return generation; }
        private IntPtr GridPointer()
        {
            IntPtr pointer = api.GetGrid(); CheckOperation(0, 0);
            if (pointer == IntPtr.Zero) throw new InvalidDataException("Native Grid descriptor is null.");
            return pointer;
        }

        private IntPtr SendCore(OfficialMessage message, uint executed)
        {
            byte[] payload = message.OwnedPayload;
            GCHandle pin = default(GCHandle);
            try
            {
                IntPtr data = IntPtr.Zero;
                if (payload.Length != 0) { pin = GCHandle.Alloc(payload, GCHandleType.Pinned); data = pin.AddrOfPinnedObject(); }
                IntPtr result = api.HandleMessage(message.Id, payload.Length, data);
                CheckNativeStatus(executed); // No other export may overwrite TLS status first.
                if (message.Id == OfficialMessages.Allocate || message.Id == OfficialMessages.Initialize)
                    cellCount = checked(BinaryLayout.ReadI32(payload, 0) * BinaryLayout.ReadI32(payload, 4));
                CheckCallbackFailure(checked(executed + 1));
                return result;
            }
            finally { if (pin.IsAllocated) pin.Free(); }
        }

        private void CheckNativeStatus(uint executed)
        { ThrowNativeStatus(api.GetLastStatus(), executed); }

        private void ThrowNativeStatus(int status, uint executed)
        {
            if (status != 0)
            {
                string detail = null;
                Exception detailFailure = null;
                try { detail = NativeApi.ReadUtf8(api.GetLastError()); }
                catch (Exception error) { detailFailure = error; }
                Exception callbackError = Interlocked.Exchange(ref callbackFailure, null);
                Exception inner = detailFailure == null ? callbackError : callbackError == null ? detailFailure :
                    new AggregateException(detailFailure, callbackError);
                throw new OniSimOperationException(status, executed, "Native operation failed.", detail, inner);
            }
        }
        private void CheckCallbackFailure(uint executed)
        {
            Exception error = Interlocked.Exchange(ref callbackFailure, null);
            if (error != null) throw new OniSimOperationException(0, executed, "Managed callback failed after a native operation.", null, error);
        }
        private void CheckOperation(uint failedExecuted, uint successfulExecuted)
        { CheckNativeStatus(failedExecuted); CheckCallbackFailure(successfulExecuted); }

        private int DispatchCallback(int id, IntPtr data)
        {
            bool previous = insideCallback; insideCallback = true;
            try { return callback == null ? 0 : callback(id, data); }
            catch (Exception error) { Interlocked.CompareExchange(ref callbackFailure, error, null); return 0; }
            finally { insideCallback = previous; }
        }

        private T Locked<T>(Func<T> action)
        {
            RejectReentry();
            lock (GlobalGate)
            {
                if (disposed || !ReferenceEquals(activeSession, this)) throw new ObjectDisposedException("OniSimSession");
                if (shutdownPending)
                    throw new InvalidOperationException("Native shutdown is unconfirmed; only Dispose may retry cleanup.");
                if (saveCleanupPending)
                    throw new InvalidOperationException("Native save cleanup is unconfirmed; Dispose is required before another operation.");
                insideOperation = true;
                try { return action(); }
                finally { insideOperation = false; }
            }
        }
        private static void RejectReentry()
        {
            if (insideOperation || insideCallback)
                throw new InvalidOperationException("Calling the managed simulation facade from a native callback is not supported.");
        }

        public void Dispose()
        {
            RejectReentry();
            lock (GlobalGate)
            {
                if (disposed) return;
                insideOperation = true;
                try { ShutdownCore(); }
                finally
                {
                    insideOperation = false;
                    GC.KeepAlive(nativeCallback);
                }
            }
        }

        private void ShutdownCore()
        {
            shutdownPending = true;
            api.Shutdown();
            int status = api.GetLastStatus();
            // Only these two statuses confirm native state and callbacks were released.
            if (status == 0 || status == 4) ReleaseLease();
            ThrowNativeStatus(status, 0);
            CheckCallbackFailure(0);
        }

        private void ReleaseLease()
        {
            disposed = true; shutdownPending = false; saveCleanupPending = false; cellCount = 0;
            if (ReferenceEquals(activeSession, this)) activeSession = null;
        }
    }
}
