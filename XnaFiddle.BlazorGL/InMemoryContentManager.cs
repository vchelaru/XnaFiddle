using System;
using System.Collections.Generic;
using System.IO;
using FlatRedBall.AnimationChain;
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
        private AchxLoader _achxLoader;

        public InMemoryContentManager(IServiceProvider services)
            : base(services)
        {
        }

        public static void AddFile(string name, byte[] data)
        {
            foreach (string key in GetCandidateKeys(name))
                _files[key] = data;
        }

        public static void RemoveFile(string name)
        {
            foreach (string key in GetCandidateKeys(name))
                _files.Remove(key);
        }

        public static void ClearFiles() => _files.Clear();

        public static IReadOnlyDictionary<string, byte[]> Files => _files;

        private GraphicsDevice GetGraphicsDevice()
        {
            var service = (IGraphicsDeviceService)ServiceProvider.GetService(typeof(IGraphicsDeviceService));
            return service.GraphicsDevice;
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string normalized = path.Replace('\\', '/').Trim();
            try { normalized = Uri.UnescapeDataString(normalized); }
            catch { /* best-effort decode only */ }

            while (normalized.StartsWith("/", StringComparison.Ordinal))
                normalized = normalized.Substring(1);

            if (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            if (normalized.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("Content/".Length);

            return normalized;
        }

        private static HashSet<string> GetCandidateKeys(string path)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static void AddNameAndNoExtension(HashSet<string> target, string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;

                target.Add(key);
                string noExt = Path.GetFileNameWithoutExtension(key);
                if (!string.Equals(key, noExt, StringComparison.OrdinalIgnoreCase))
                    target.Add(noExt);
            }

            AddNameAndNoExtension(keys, path);

            string normalized = NormalizeAssetPath(path);
            AddNameAndNoExtension(keys, normalized);

            string fileName = Path.GetFileName(normalized);
            AddNameAndNoExtension(keys, fileName);

            return keys;
        }

        private static bool TryGetFileBytes(string path, out byte[] bytes)
        {
            foreach (string key in GetCandidateKeys(path))
            {
                if (_files.TryGetValue(key, out bytes))
                    return true;
            }

            bytes = null;
            return false;
        }

        private static string EnsureAchxExtension(string assetName)
        {
            string normalized = NormalizeAssetPath(assetName);
            if (string.IsNullOrEmpty(normalized))
                return assetName;

            if (!string.IsNullOrEmpty(Path.GetExtension(normalized)))
                return normalized;

            string withExtension = normalized + ".achx";
            return _files.ContainsKey(withExtension) ? withExtension : normalized;
        }

        public override T Load<T>(string assetName)
        {
            if (_loaded.TryGetValue(assetName, out object cached))
                return (T)cached;
            string normalizedAssetName = NormalizeAssetPath(assetName);
            if (_loaded.TryGetValue(normalizedAssetName, out cached))
                return (T)cached;

            if (TryGetFileBytes(assetName, out byte[] bytes))
            {
                // XNB files start with magic bytes 'X','N','B'. When present, skip the
                // raw-stream decode branches and fall through to base.Load<T>, which uses
                // KNI's standard ContentManager pipeline (OpenStream resolves via the JS
                // XHR intercept that serves these cached bytes back).
                bool isXnb = bytes.Length >= 3 && bytes[0] == (byte)'X' && bytes[1] == (byte)'N' && bytes[2] == (byte)'B';

                if (!isXnb && typeof(T) == typeof(AnimationChainList))
                {
                    string achxPath = EnsureAchxExtension(assetName);
                    _achxLoader ??= new AchxLoader(GetGraphicsDevice());

                    Stream OpenStreamOrNull(string path) =>
                        TryGetFileBytes(path, out byte[] data) ? new MemoryStream(data, writable: false) : null;

                    AnimationChainList chainList = _achxLoader.Load(achxPath, OpenStreamOrNull, OpenStreamOrNull);
                    // .achx files can contain negative or out-of-bounds pixel coordinates (e.g. LeftCoordinate=-1),
                    // which cause GL_INVALID_OPERATION when passed to SpriteBatch.Draw. Sanitize and premultiply
                    // all frames after loading so they are always safe to draw.
                    SanitizeFrames(chainList);
                    _loaded[assetName] = chainList;
                    if (!string.IsNullOrEmpty(normalizedAssetName))
                        _loaded[normalizedAssetName] = chainList;
                    return (T)(object)chainList;
                }

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
                    if (!string.IsNullOrEmpty(normalizedAssetName))
                        _loaded[normalizedAssetName] = texture;
                    return (T)(object)texture;
                }

                if (!isXnb && typeof(T) == typeof(SoundEffect))
                {
                    using MemoryStream stream = new(bytes);
                    SoundEffect sfx = SoundEffect.FromStream(stream);
                    _loaded[assetName] = sfx;
                    if (!string.IsNullOrEmpty(normalizedAssetName))
                        _loaded[normalizedAssetName] = sfx;
                    return (T)(object)sfx;
                }
            }

            // Fall back to default ContentManager behavior
            return base.Load<T>(assetName);
        }

        // Clamp all frame source rectangles to valid bounds and premultiply textures.
        // .achx files regularly contain negative LeftCoordinate / TopCoordinate values
        // (e.g. LeftCoordinate=-1) that produce out-of-range Rectangle values. WebGL
        // raises GL_INVALID_OPERATION (0x0502) when SpriteBatch submits such a rect.
        private static void SanitizeFrames(AnimationChainList chainList)
        {
            var premultiplied = new HashSet<Texture2D>(ReferenceEqualityComparer.Instance);
            foreach (AnimationChain chain in chainList)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    AnimationFrame frame = chain[i];
                    if (frame.Texture == null)
                        continue;

                    // Premultiply alpha once per unique texture instance.
                    // AchxLoader uses Texture2D.FromStream which returns straight alpha;
                    // SpriteBatch with BlendState.AlphaBlend expects premultiplied.
                    if (premultiplied.Add(frame.Texture))
                        PremultiplyTexture(frame.Texture);

                    if (!frame.SourceRectangle.HasValue)
                        continue;

                    Rectangle r = frame.SourceRectangle.Value;
                    int texW = frame.Texture.Width;
                    int texH = frame.Texture.Height;

                    // Normalize negative dimension (shouldn't happen but be safe)
                    if (r.Width < 0) { r.X += r.Width; r.Width = -r.Width; }
                    if (r.Height < 0) { r.Y += r.Height; r.Height = -r.Height; }

                    // Clamp negative origin into texture space
                    if (r.X < 0) { r.Width += r.X; r.X = 0; }
                    if (r.Y < 0) { r.Height += r.Y; r.Y = 0; }

                    // Null out completely degenerate rects (draws full texture instead)
                    if (r.X >= texW || r.Y >= texH || r.Width <= 0 || r.Height <= 0)
                    {
                        frame.SourceRectangle = null;
                        continue;
                    }

                    // Clamp right/bottom edges to texture boundary
                    if (r.Right > texW) r.Width = texW - r.X;
                    if (r.Bottom > texH) r.Height = texH - r.Y;

                    frame.SourceRectangle = r;
                }
            }
        }

        private static void PremultiplyTexture(Texture2D texture)
        {
            Color[] pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte a = pixels[i].A;
                pixels[i] = new Color(
                    (byte)(pixels[i].R * a / 255),
                    (byte)(pixels[i].G * a / 255),
                    (byte)(pixels[i].B * a / 255),
                    a);
            }
            texture.SetData(pixels);
        }

        public override void Unload()
        {
            _achxLoader?.Dispose();
            _achxLoader = null;

            var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (object asset in _loaded.Values)
            {
                if (!disposed.Add(asset))
                    continue;
                if (asset is IDisposable d)
                    d.Dispose();
            }
            _loaded.Clear();
            base.Unload();
        }
    }
}
