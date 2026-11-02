using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HarmonyLib;
using KMod;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace CycleTrim.Conformance
{
    // Separate, opt-in test mod. This assembly is never part of CycleTrim.zip.
    public sealed class TextureProbe : UserMod2
    {
        private const string ManagedSha = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        private static readonly int[] Properties = { 5, 9, 14, 15, 11 };
        private static readonly int[] Strides = { 8, 4, 4, 4, 1 };
        private static readonly string[] Names = { "Flow", "Liquid", "LiquidData", "MaterialData", "ExposedToSunlight" };
        private static string root;
        private static int lastRequest;
        private static float nextCheck;
        private static Batch pending;
        private static bool backwallInputRecorded;

        private sealed class Batch
        {
            internal int Remaining = 5;
            internal readonly JObject Result;
            internal readonly float Deadline = Time.realtimeSinceStartup + 30;
            internal Batch(int request)
            {
                Result = new JObject { ["request"] = request, ["complete"] = false,
                    ["graphicsDevice"] = SystemInfo.graphicsDeviceType.ToString(),
                    ["frame"] = Time.frameCount, ["width"] = Grid.WidthInCells,
                    ["height"] = Grid.HeightInCells, ["textures"] = new JObject() };
            }
        }

        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            string[] args = Environment.GetCommandLineArgs();
            int control = Array.IndexOf(args, "--oniharness-control");
            if (Array.IndexOf(args, "--cycletrim-texture-probe") < 0 || control < 0 || control + 1 >= args.Length)
                return;
            root = Path.GetFullPath(args[control + 1]);
            if (Hash(File.ReadAllBytes(typeof(Game).Assembly.Location)) != ManagedSha)
                throw new InvalidOperationException("Texture probe requires the pinned installed managed assembly");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback)
                throw new InvalidOperationException("Texture probe requires graphics and async GPU readback");
            harmony.Patch(AccessTools.Method(typeof(PropertyTextures), "LateUpdate"),
                postfix: new HarmonyMethod(typeof(TextureProbe), nameof(Observe)));
            harmony.Patch(AccessTools.Method(typeof(SimMessages), "SetBackwallData"),
                prefix: new HarmonyMethod(typeof(TextureProbe), nameof(ObserveBackwallInput)));
            Debug.Log("[CycleTrim.TextureProbe] Read-only native/Unity/GPU comparison armed");
        }

        private static void ObserveBackwallInput(int cell, ushort elemIdx, float mass, float temperature)
        {
            if (backwallInputRecorded || ElementLoader.elements == null || elemIdx < ElementLoader.elements.Count)
                return;
            backwallInputRecorded = true;
            try
            {
                File.WriteAllText(Path.Combine(root, "backwall-sentinel-input.json"), new JObject
                {
                    ["cell"] = cell, ["elementIndex"] = elemIdx,
                    ["registeredElements"] = ElementLoader.elements.Count,
                    ["mass"] = mass, ["temperature"] = temperature
                }.ToString());
            }
            catch (Exception error) { Debug.LogWarning("[CycleTrim.TextureProbe] Input receipt failed: " + error.Message); }
        }

        private static void Observe(PropertyTextures __instance)
        {
            // LateUpdate's original upload/Apply path must finish first. Do not
            // change its inputs, replace any texture, or suppress its exceptions.
            if (pending != null)
            {
                if (Time.realtimeSinceStartup > pending.Deadline)
                {
                    pending.Result["error"] = "GPU readback deadline expired";
                    Finish(pending);
                }
                return;
            }
            if (Time.realtimeSinceStartup < nextCheck || Game.Instance == null ||
                !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading()) return;
            nextCheck = Time.realtimeSinceStartup + 1;
            string requestPath = Path.Combine(root, "texture-probe-request.json");
            if (!File.Exists(requestPath)) return;
            Batch batch = null;
            try
            {
                int request = (int)JObject.Parse(File.ReadAllText(requestPath))["request"];
                if (request <= lastRequest) return;
                lastRequest = request;
                batch = pending = new Batch(request);
                var textures = AccessTools.Field(typeof(PropertyTextures), "externallyUpdatedTextures")
                    .GetValue(__instance) as Texture2D[];
                if (textures == null || textures.Length != 17)
                    throw new InvalidOperationException("Unexpected external texture table");
                IntPtr[] pointers = { PropertyTextures.externalFlowTex, PropertyTextures.externalLiquidTex,
                    PropertyTextures.externalLiquidDataTex, PropertyTextures.externalMaterialDataTex,
                    PropertyTextures.externalExposedToSunlight };
                for (int index = 0; index < Properties.Length; index++)
                    Compare(batch, textures[Properties[index]], pointers[index], index);
            }
            catch (Exception error)
            {
                if (batch == null) batch = pending = new Batch(lastRequest);
                batch.Result["error"] = error.ToString();
                Finish(batch);
            }
        }

        private static void Compare(Batch batch, Texture2D texture, IntPtr pointer, int index)
        {
            int length = checked(Grid.WidthInCells * Grid.HeightInCells * Strides[index]);
            if (texture == null || pointer == IntPtr.Zero || texture.width != Grid.WidthInCells ||
                texture.height != Grid.HeightInCells || texture.GetNativeTexturePtr() == IntPtr.Zero)
                throw new InvalidOperationException("Unbound or incorrectly sized " + Names[index] + " texture");
            byte[] source = new byte[length];
            Marshal.Copy(pointer, source, 0, length);
            byte[] uploaded = texture.GetRawTextureData();
            var result = new JObject { ["bytes"] = length, ["format"] = texture.format.ToString(),
                ["nativeSha256"] = Hash(source), ["unityCpuSha256"] = Hash(uploaded),
                ["cpuEqual"] = source.SequenceEqual(uploaded), ["gpuComplete"] = false };
            ((JObject)batch.Result["textures"])[Names[index]] = result;
            AsyncGPUReadback.Request(texture, 0, readback =>
            {
                if (!ReferenceEquals(pending, batch)) return;
                try
                {
                    if (readback.hasError) throw new InvalidOperationException("GPU readback failed");
                    byte[] gpu = readback.GetData<byte>().ToArray();
                    result["gpuBytes"] = gpu.Length;
                    result["gpuSha256"] = Hash(gpu);
                    result["gpuEqual"] = source.SequenceEqual(gpu);
                    result["gpuComplete"] = true;
                }
                catch (Exception error) { result["error"] = error.ToString(); }
                if (--batch.Remaining == 0) Finish(batch);
            });
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static void Finish(Batch batch)
        {
            var results = ((JObject)batch.Result["textures"]).Properties().Select(item => (JObject)item.Value).ToArray();
            batch.Result["complete"] = true;
            batch.Result["passed"] = batch.Result["error"] == null && results.Length == 5 &&
                results.All(item => (bool?)item["cpuEqual"] == true && (bool?)item["gpuEqual"] == true && item["error"] == null);
            batch.Result["capturedAt"] = System.DateTime.UtcNow.ToString("o");
            string path = Path.Combine(root, "texture-probe.json");
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, batch.Result.ToString());
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            catch (Exception error) { Debug.LogWarning("[CycleTrim.TextureProbe] GPU receipt failed: " + error.Message); }
            finally { if (ReferenceEquals(pending, batch)) pending = null; }
        }
    }
}
