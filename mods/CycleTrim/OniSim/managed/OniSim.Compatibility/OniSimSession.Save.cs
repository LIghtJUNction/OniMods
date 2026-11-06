using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace OniSim.Compatibility
{
    public sealed partial class OniSimSession
    {
        /// <summary>Copies the raw native SIMSAVE blob; no outer length prefix is included.</summary>
        public byte[] Save(int x = 0, int y = 0)
        { return Locked(() => SaveCore(x, y)); }

        /// <summary>Loads a raw SIMSAVE blob into the allocated, unstarted native world.</summary>
        public GridView Load(byte[] savedImage)
        {
            RejectReentry();
            var message = new OfficialMessage(OfficialMessages.Load, savedImage);
            return Locked(() =>
            {
                IntPtr result = SendCore(message, 0);
                if (result == IntPtr.Zero) throw new InvalidDataException("Native Load succeeded without its required success token.");
                return NewView();
            });
        }

        private byte[] SaveCore(int x, int y)
        {
            var length = new int[1];
            GCHandle pin = GCHandle.Alloc(length, GCHandleType.Pinned);
            byte[] image = null;
            Exception failure = null;
            bool acquired = false;
            try
            {
                // If the call boundary fails, a native save may exist and must stay rooted.
                saveCleanupPending = true;
                IntPtr source = api.BeginSave(pin.AddrOfPinnedObject(), x, y);
                int status = api.GetLastStatus();
                acquired = status == 0;
                if (status >= 1 && status <= 6 && status != 5) saveCleanupPending = false;
                ThrowNativeStatus(status, 0);
                CheckCallbackFailure(0);
                if (length[0] < 0 || length[0] > BinaryLayout.MaxPayloadBytes ||
                    (length[0] != 0 && source == IntPtr.Zero))
                    throw new InvalidDataException("Native save buffer length or pointer is invalid.");
                image = new byte[length[0]];
                if (image.Length != 0) Marshal.Copy(source, image, 0, image.Length);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                pin.Free();
                if (acquired)
                {
                    try { EndSaveCore(); }
                    catch (Exception cleanupError)
                    {
                        failure = failure == null ? cleanupError : new AggregateException(
                            "Save copying and native EndSave both failed.", failure, cleanupError);
                    }
                }
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return image;
        }

        private void EndSaveCore()
        {
            api.EndSave();
            int status = api.GetLastStatus();
            if (status == 0) saveCleanupPending = false;
            ThrowNativeStatus(status, 0);
            CheckCallbackFailure(0);
        }
    }
}
