using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;
using OniSim.Compatibility;

namespace CycleTrim.Optimizations.Tests
{
    internal static class PresentationUploadRegression
    {
        private const string Owner = "CycleTrim.Tests.PresentationUpload";

        internal static Harmony Install()
        {
            var harmony = new Harmony(Owner);
            harmony.CreateClassProcessor(typeof(PropertyTexturePublicationPatch.ExternalUploads)).Patch();
            harmony.CreateClassProcessor(typeof(PropertyTexturePublicationPatch.PublicationScope)).Patch();
            return harmony;
        }

        internal static void Run()
        {
            var harmony = Install();
            try
            {
                using (var fixture = new Images())
                {
                    var textures = new PropertyTextures(8, 4);
                    PresentationUploadGate.SetReader(fixture.Read);
                    textures.LateUpdate();
                    fixture.EqualPixels(textures);
                    textures.LateUpdate();
                    EqualCalls(textures, 1, "unchanged images reuse their completed uploads");
                    Check(textures.LateUpdates == 2 && textures.LerpUpdates == 6 && textures.LerpBuffer == 0,
                        "LateUpdate and all three lerper updates continue after upload reuse");

                    fixture.SwapPointers();
                    textures.LateUpdate();
                    EqualCalls(textures, 1, "identical revision survives an alternating native buffer");
                    fixture.Header.Revision++;
                    textures.LateUpdate();
                    EqualCalls(textures, 2, "a generated publication updates every image");
                    fixture.Header.Generation++;
                    textures.LateUpdate();
                    EqualCalls(textures, 3, "a new session invalidates the previous revision");

                    var old = textures.Textures[1];
                    textures.Textures[1] = new UnityEngine.Texture2D(8, 4, 4);
                    textures.LateUpdate();
                    Check(textures.Textures[1].ApplyCalls == 1 && old.ApplyCalls == 3,
                        "a reconstructed texture receives its first upload");
                    textures.Textures[1].Reinitialize(4, 8, 4);
                    textures.LateUpdate();
                    Check(textures.Textures[1].ApplyCalls == 2, "equal-area resize invalidates the upload");
                    textures.Textures[1].Reinitialize(4, 8, 4);
                    textures.LateUpdate();
                    Check(textures.Textures[1].ApplyCalls == 3, "same-size storage recreation invalidates the upload");
                    textures.Textures[1].ModifyExternally(255);
                    textures.LateUpdate();
                    fixture.EqualPixels(textures);
                    Check(textures.Textures[1].ApplyCalls == 4, "external texture content updates invalidate the upload");

                    fixture.Available = false;
                    textures.LateUpdate(); textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == 5, "unknown metadata always uses original uploads");
                    fixture.Available = true;
                    textures.LateUpdate(); textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == 6, "recovery uploads once before becoming reusable");
                    fixture.Throws = true;
                    textures.LateUpdate();
                    fixture.Throws = false;
                    Check(textures.Textures[0].ApplyCalls == 7, "metadata exceptions retain original behavior");

                    fixture.Header.Revision++;
                    textures.Textures[0].ThrowLoad = true;
                    Throws(textures.LateUpdate, "load exception propagates");
                    textures.Textures[0].ThrowLoad = false;
                    textures.Textures[0].ThrowApply = true;
                    Throws(textures.LateUpdate, "apply exception propagates");
                    textures.Textures[0].ThrowApply = false;
                    int before = textures.Textures[0].ApplyCalls;
                    textures.LateUpdate(); textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == before + 1,
                        "failed Load/Apply never commits a reusable upload");
                    fixture.EqualPixels(textures);

                    textures.Loading = true;
                    fixture.Header.Revision++;
                    before = textures.Textures[0].ApplyCalls;
                    textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == before, "loading guard is preserved");
                    textures.Loading = false;
                    textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == before + 1, "first upload after loading is required");

                    fixture.Header.Revision = 0;
                    before = textures.Textures[0].ApplyCalls;
                    textures.LateUpdate(); textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == before + 2, "zero revision never authorizes reuse");
                    fixture.Header.Revision = 100;
                    fixture.Header.Version = 2;
                    before = textures.Textures[0].ApplyCalls;
                    textures.LateUpdate(); textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == before + 2, "unknown metadata version retains uploads");
                    fixture.Header.Version = 1;
                    textures.LateUpdate();
                    fixture.MismatchFlowPointer();
                    before = textures.Textures[0].ApplyCalls;
                    textures.LateUpdate(); textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == before + 2,
                        "an external pointer outside the captured native publication cannot reuse a stamp");
                    PropertyTextures.externalFlowTex = fixture.Header.Flow;
                    textures.LateUpdate();

                    fixture.Header.Revision++;
                    PresentationUploadGate.ReadPublication sameReader = fixture.Read;
                    PresentationUploadGate.SetReader(sameReader);
                    textures.Textures[0].BeforeApply = () => PresentationUploadGate.SetReader(sameReader);
                    textures.LateUpdate();
                    textures.Textures[0].BeforeApply = null;
                    before = textures.Textures[0].ApplyCalls;
                    textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == before + 1,
                        "resetting even the same reader during Apply cannot commit an old upload into the new cache");

                    var nestedTextures = new PropertyTextures(8, 4);
                    fixture.Header.Revision++;
                    textures.Textures[0].BeforeLoad = nestedTextures.LateUpdate;
                    textures.LateUpdate();
                    textures.Textures[0].BeforeLoad = null;
                    fixture.EqualPixels(textures); fixture.EqualPixels(nestedTextures);
                    Check(nestedTextures.LateUpdates == 1 && nestedTextures.LerpUpdates == 3,
                        "nested LateUpdate during Load preserves both complete presentations");
                    fixture.Header.Revision++;
                    textures.Textures[0].BeforeApply = nestedTextures.LateUpdate;
                    textures.LateUpdate();
                    textures.Textures[0].BeforeApply = null;
                    fixture.EqualPixels(textures); fixture.EqualPixels(nestedTextures);
                    Check(nestedTextures.LateUpdates == 2 && nestedTextures.LerpUpdates == 6,
                        "nested LateUpdate during Apply restores the outer scope");
                    textures.LateUpdate();

                    // Same dimensions and format do not make two different images
                    // interchangeable when a caller reuses one texture instance.
                    textures.Textures[2] = textures.Textures[1];
                    before = textures.Textures[1].ApplyCalls;
                    textures.LateUpdate(); textures.LateUpdate();
                    Check(textures.Textures[1].ApplyCalls == before + 3,
                        "liquid and liquid-data identities remain distinct");

                    // Exercise the unknown-body guard without changing live IL.
                    var unmatched = new[] { new CodeInstruction(OpCodes.Ret) };
                    var retained = PropertyTexturePublicationPatch.ExternalUploads.Transpiler(unmatched).ToArray();
                    Check(retained.Length == 1 && retained[0].opcode == OpCodes.Ret,
                        "unknown call shape is preserved");
                    textures.Textures[2] = new UnityEngine.Texture2D(8, 4, 4);
                    before = textures.Textures[0].ApplyCalls;
                    textures.LateUpdate(); textures.LateUpdate();
                    Check(textures.Textures[0].ApplyCalls == before + 2,
                        "unsupported original shape declines optimization");
                }
                Console.WriteLine("PASS presentation upload identity, lifecycle, failure, and lerper regressions (Unity host stubs)");
            }
            finally
            {
                PresentationUploadGate.SetReader((OniSimPublicationReader)null);
                harmony.UnpatchAll(Owner);
            }
        }

        internal static void Check(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Presentation upload: " + detail);
        }

        private static void EqualCalls(PropertyTextures textures, int expected, string detail)
        {
            Check(textures.Textures.All(texture => texture.LoadCalls == expected && texture.ApplyCalls == expected), detail);
        }

        private static void Throws(Action action, string detail)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(detail);
        }

        private sealed class Images : IDisposable
        {
            private readonly IntPtr[][] pointers = { new IntPtr[5], new IntPtr[5] };
            private readonly byte[][] pixels = new byte[5][];
            internal PresentationUpdate Header;
            internal bool Available = true, Throws;

            internal Images()
            {
                for (int i = 0; i < 5; i++)
                {
                    pixels[i] = Enumerable.Range(0, 32 * PropertyTextures.Stride(i))
                        .Select(value => (byte)(value * 17 + i)).ToArray();
                    for (int buffer = 0; buffer < 2; buffer++)
                    {
                        pointers[buffer][i] = Marshal.AllocHGlobal(pixels[i].Length);
                        Marshal.Copy(pixels[i], 0, pointers[buffer][i], pixels[i].Length);
                    }
                }
                Header = new PresentationUpdate { Version = 1, CellCount = 32, Generation = 1, Revision = 1 };
                SetPointers(0);
            }

            internal bool Read(out PresentationUpdate update)
            {
                if (Throws) throw new InvalidOperationException("injected metadata failure");
                update = Header;
                return Available;
            }

            internal void SwapPointers() { SetPointers(1); }
            internal void MismatchFlowPointer() { PropertyTextures.externalFlowTex = pointers[0][0]; }
            private void SetPointers(int index)
            {
                var p = pointers[index];
                Header.Flow = p[0]; Header.Liquid = p[1]; Header.LiquidData = p[2];
                Header.MaterialData = p[3]; Header.ExposedToSunlight = p[4];
                PropertyTextures.SetPointers(p);
            }

            internal void EqualPixels(PropertyTextures textures)
            {
                for (int i = 0; i < 5; i++)
                    Check(pixels[i].SequenceEqual(textures.Textures[i].UploadedBytes), "uploaded bytes match source image " + i);
            }

            public void Dispose()
            {
                foreach (var buffer in pointers)
                    foreach (var pointer in buffer) Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
