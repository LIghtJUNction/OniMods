using System.IO;
using System.Reflection;
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
    }
}
