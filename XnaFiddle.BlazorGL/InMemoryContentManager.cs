using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace XnaFiddle
{
    public class InMemoryContentManager : ContentManager
    {
        // Static store: persists across recompilations
        private static readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        // Cache loaded assets so we don't re-decode every call
        private readonly Dictionary<string, object> _loaded = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryContentManager(IServiceProvider services)
            : base(services)
        {
        }

        public static void AddFile(string name, byte[] data)
        {
            // Store under original name and without extension
            _files[name] = data;
            string noExt = Path.GetFileNameWithoutExtension(name);
            if (!string.Equals(name, noExt, StringComparison.OrdinalIgnoreCase))
                _files[noExt] = data;
        }

        public static void RemoveFile(string name)
        {
            _files.Remove(name);
            string noExt = Path.GetFileNameWithoutExtension(name);
            _files.Remove(noExt);
        }

        public static void ClearFiles() => _files.Clear();

        public static IReadOnlyDictionary<string, byte[]> Files => _files;

        private GraphicsDevice GetGraphicsDevice()
        {
            var service = (IGraphicsDeviceService)ServiceProvider.GetService(typeof(IGraphicsDeviceService));
            return service.GraphicsDevice;
        }

        public override T Load<T>(string assetName)
        {
            if (_loaded.TryGetValue(assetName, out object cached))
                return (T)cached;

            if (_files.TryGetValue(assetName, out byte[] bytes))
            {
                // XNB files start with magic bytes 'X','N','B'. When present, skip the
                // raw-stream decode branches and fall through to base.Load<T>, which uses
                // KNI's standard ContentManager pipeline (OpenStream resolves via the JS
                // XHR intercept that serves these cached bytes back).
                bool isXnb = bytes.Length >= 3 && bytes[0] == (byte)'X' && bytes[1] == (byte)'N' && bytes[2] == (byte)'B';

                if (!isXnb && typeof(T) == typeof(Texture2D))
                {
                    using MemoryStream stream = new(bytes);
                    Texture2D texture = Texture2D.FromStream(GetGraphicsDevice(), stream);
                    // KNI's FromStream returns straight alpha; XNA-style code (SpriteBatch +
                    // BlendState.AlphaBlend) expects premultiplied. MonoGame premultiplies
                    // inside FromStream, KNI does not. XnaFiddle is always KNI so this is
                    // unconditional here; RawContentManager in exports runtime-detects instead.
                    Color[] pixels = new Color[texture.Width * texture.Height];
                    texture.GetData(pixels);
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        byte a = pixels[i].A;
                        pixels[i] = new Color((byte)(pixels[i].R * a / 255), (byte)(pixels[i].G * a / 255), (byte)(pixels[i].B * a / 255), a);
                    }
                    texture.SetData(pixels);
                    _loaded[assetName] = texture;
                    return (T)(object)texture;
                }

                if (!isXnb && typeof(T) == typeof(SoundEffect))
                {
                    using MemoryStream stream = new(bytes);
                    SoundEffect sfx = SoundEffect.FromStream(stream);
                    _loaded[assetName] = sfx;
                    return (T)(object)sfx;
                }
            }

            // Fall back to default ContentManager behavior
            return base.Load<T>(assetName);
        }

        public override void Unload()
        {
            foreach (object asset in _loaded.Values)
            {
                if (asset is IDisposable d)
                    d.Dispose();
            }
            _loaded.Clear();
            base.Unload();
        }
    }
}
