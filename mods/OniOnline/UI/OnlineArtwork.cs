using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using OniOnline.Runtime;
using PeterHan.PLib.Core;
using UnityEngine;

namespace OniOnline.UI
{
    internal static class OnlineArtwork
    {
        internal const string CardFile = "online-card-v2.png";
        internal const string LobbyFile = "online-lobby-v2.png";
        internal const string LogoFile = "onionline-icon-v1.png";
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);

        internal static Sprite Load(string filename = CardFile)
        {
            Texture2D texture = null;
            try
            {
                if (string.IsNullOrEmpty(filename) || Path.GetFileName(filename) != filename ||
                    !filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
                var runtime = OnlineRuntime.Instance;
                var directory = runtime == null ? null : AccessTools.Field(typeof(OnlineRuntime), "modPath")?.GetValue(runtime) as string;
                if (string.IsNullOrEmpty(directory)) directory = PUtil.GetModPath(typeof(ModInfo).Assembly);
                if (string.IsNullOrEmpty(directory)) return null;
                var path = Path.Combine(directory, "assets", filename);
                Sprite cached;
                if (Cache.TryGetValue(path, out cached) && cached != null) return cached;
                if (!File.Exists(path)) return null;
                var conversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true);
                // Unity 6 includes Span overloads that cannot be referenced by net48.
                var load = conversion.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (load == null) return null;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!(bool)load.Invoke(null, new object[] { texture, File.ReadAllBytes(path), true }))
                { UnityEngine.Object.Destroy(texture); return null; }
                texture.name = filename;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.hideFlags = HideFlags.HideAndDontSave;
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                sprite.name = filename;
                sprite.hideFlags = HideFlags.HideAndDontSave;
                Cache[path] = sprite;
                return sprite;
            }
            catch (Exception error)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                if (Warned.Add(filename)) Debug.LogWarning("[OniOnline] Artwork unavailable: " + filename + " (" + error.GetType().Name + ")");
                return null;
            }
        }
    }
}
