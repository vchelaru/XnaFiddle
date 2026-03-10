using System;
using System.Collections.Generic;
using System.IO;
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
                if (typeof(T) == typeof(Texture2D))
                {
                    using MemoryStream stream = new(bytes);
                    Texture2D texture = Texture2D.FromStream(GetGraphicsDevice(), stream);
                    _loaded[assetName] = texture;
                    return (T)(object)texture;
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
