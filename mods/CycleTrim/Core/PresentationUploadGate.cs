using System;
using System.Runtime.CompilerServices;
using OniSim.Compatibility;
using UnityEngine;

namespace CycleTrim.Core
{
    /// <summary>
    /// Reuses a completed upload only for the same texture and native derived
    /// revision. It does not skip LateUpdate, simulation, or texture lerping.
    /// </summary>
    internal static class PresentationUploadGate
    {
        internal delegate bool ReadPublication(out PresentationUpdate update);
        private static ReadPublication readPublication;
        private static ConditionalWeakTable<Texture2D, Upload> uploads =
            new ConditionalWeakTable<Texture2D, Upload>();
        private static readonly ConditionalWeakTable<Texture2D, Upload>.CreateValueCallback NewUpload =
            _ => new Upload();
        [ThreadStatic] private static Scope scope;
        [ThreadStatic] private static long operation;

        internal sealed class Upload
        {
            internal bool Complete;
            internal ulong Generation, Revision;
            internal uint TextureUpdates;
            internal int Width, Height, Length;
            internal int Stride, Image;
        }

        internal struct Scope
        {
            internal bool Active;
            internal object Cache;
            internal PresentationUpdate Publication;
            internal Pending Pending;
        }

        internal struct Pending
        {
            internal Texture2D Texture;
            internal object Cache;
            internal Upload Record;
            internal IntPtr Pointer;
            internal int Length, Stride, Image, Width, Height;
            internal bool Candidate, Skipped;
            internal uint TextureUpdates;
            internal ulong Generation, Revision;
            internal long Operation;
        }

        internal static void SetReader(OniSimPublicationReader reader)
        {
            SetReader(reader == null ? (ReadPublication)null : reader.TryGetPresentationUpdate);
        }

        internal static void SetReader(ReadPublication reader)
        {
            readPublication = reader;
            uploads = new ConditionalWeakTable<Texture2D, Upload>();
        }

        internal static Scope Begin(bool supported)
        {
            Scope previous = scope;
            scope = default(Scope);
            var reader = readPublication;
            var cache = uploads;
            if (supported && reader != null)
            {
                try
                {
                    PresentationUpdate publication;
                    if (reader(out publication) && publication.IsValid
                        && ReferenceEquals(reader, readPublication) && ReferenceEquals(cache, uploads))
                    {
                        scope.Active = true;
                        scope.Cache = cache;
                        scope.Publication = publication;
                    }
                }
                catch (Exception)
                {
                    // An optional optimization cannot suppress the game's
                    // original uploads when its metadata is unavailable.
                }
            }
            return previous;
        }

        internal static void End(Scope previous) { scope = previous; }

        internal static void LoadRawTextureData(Texture2D texture, IntPtr pointer, int length)
        {
            long currentOperation = ++operation;
            scope.Pending = default(Pending);
            Pending pending = new Pending
            {
                Texture = texture, Pointer = pointer, Length = length, Operation = currentOperation,
                Cache = scope.Cache
            };
            if (scope.Active && ReferenceEquals(scope.Cache, uploads) && texture != null)
            {
                var publication = scope.Publication;
                int image = publication.ImageIndex(pointer);
                int stride = image == 0 ? 8 : image == 4 ? 1 : image > 0 ? 4 : 0;
                int width = texture.width, height = texture.height;
                if (stride != 0 && width > 0 && height > 0
                    && (long)width * height == publication.CellCount
                    && (long)publication.CellCount * stride == length)
                {
                    pending.Candidate = true;
                    pending.Stride = stride;
                    pending.Image = image;
                    pending.Width = width;
                    pending.Height = height;
                    pending.Generation = publication.Generation;
                    pending.Revision = publication.Revision;
                    Upload previous;
                    uploads.TryGetValue(texture, out previous);
                    pending.Record = previous;
                    if (previous != null && previous.Complete
                        && previous.Generation == pending.Generation && previous.Revision == pending.Revision
                        && previous.Width == width && previous.Height == height && previous.Length == length
                        && previous.Stride == stride && previous.Image == image)
                    {
                        // A new revision already requires an upload. Query the
                        // Unity mutation counter only when reuse is possible.
                        pending.TextureUpdates = texture.updateCount;
                        if (previous.TextureUpdates == pending.TextureUpdates)
                        {
                            pending.Skipped = true;
                            scope.Pending = pending;
                            return;
                        }
                    }
                }
            }

            if (pending.Candidate && ReferenceEquals(pending.Cache, uploads))
            {
                // The lookup above already found (or ruled out) this entry.
                // Keep it for the paired Apply while the same table is current.
                if (pending.Record != null) pending.Record.Complete = false;
            }
            else
                Invalidate(texture);
            texture.LoadRawTextureData(pointer, length);
            // A nested upload cannot authorize an outer Apply using its stamp.
            if (operation == currentOperation)
                scope.Pending = pending;
        }

        internal static void Apply(Texture2D texture)
        {
            Pending pending = scope.Pending;
            scope.Pending = default(Pending);
            bool matches = ReferenceEquals(pending.Texture, texture) && pending.Operation == operation;
            if (matches && pending.Skipped)
            {
                if (texture != null && texture.width == pending.Width && texture.height == pending.Height
                    && texture.updateCount == pending.TextureUpdates && ReferenceEquals(pending.Cache, uploads))
                    return;
                // A texture was changed between the paired calls. Restore the
                // original input and perform the upload instead of reusing it.
                Invalidate(texture);
                texture.LoadRawTextureData(pending.Pointer, pending.Length);
            }

            if (!matches)
                Invalidate(texture);
            texture.Apply();
            if (!matches || !pending.Candidate || pending.Operation != operation || texture == null
                || !ReferenceEquals(pending.Cache, uploads)
                || texture.width != pending.Width || texture.height != pending.Height)
                return;
            // A matching operation and cache identity keep the original entry
            // valid even through the callback boundaries checked above. Only
            // the first successful upload needs to create an entry here.
            Upload uploaded = pending.Record ?? uploads.GetValue(texture, NewUpload);
            uploaded.Generation = pending.Generation;
            uploaded.Revision = pending.Revision;
            uploaded.Width = pending.Width;
            uploaded.Height = pending.Height;
            uploaded.Length = pending.Length;
            uploaded.Stride = pending.Stride;
            uploaded.Image = pending.Image;
            uploaded.TextureUpdates = texture.updateCount;
            uploaded.Complete = true;
        }

        private static void Invalidate(Texture2D texture)
        {
            Upload previous;
            if (!ReferenceEquals(texture, null) && uploads.TryGetValue(texture, out previous))
                previous.Complete = false;
        }
    }
}
