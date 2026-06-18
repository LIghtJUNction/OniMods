using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace OniMcp.Support
{
    public static class SpriteLoader
    {
        public static Sprite LoadSpriteFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            byte[] data = File.ReadAllBytes(path);
            if (LooksLikeGitLfsPointer(data))
            {
                Debug.LogWarning($"[OniMcp] Skipping image asset because it is a Git LFS pointer, not real image data: {path}");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadImage(texture, data))
            {
                Object.Destroy(texture);
                return null;
            }

            texture.name = Path.GetFileNameWithoutExtension(path);
            texture.filterMode = FilterMode.Bilinear;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        private static bool LoadImage(Texture2D texture, byte[] data)
        {
            try
            {
                var imageConversion = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (imageConversion == null)
                    return false;

                MethodInfo loadImage = imageConversion.GetMethod(
                    "LoadImage",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Texture2D), typeof(byte[]) },
                    null);
                if (loadImage == null)
                    return false;

                return (bool)loadImage.Invoke(null, new object[] { texture, data });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[OniMcp] Failed to decode image asset: {e.Message}");
                return false;
            }
        }

        private static bool LooksLikeGitLfsPointer(byte[] data)
        {
            if (data == null || data.Length == 0)
                return false;

            int length = Mathf.Min(data.Length, 256);
            string prefix = Encoding.UTF8.GetString(data, 0, length);
            return prefix.StartsWith("version https://git-lfs.github.com/spec/v1");
        }
    }
}
