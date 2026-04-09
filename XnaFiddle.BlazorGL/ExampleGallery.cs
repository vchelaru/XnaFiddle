using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace XnaFiddle
{
    public struct ExampleAsset
    {
        public string FileName;
        public byte[] Data;
    }

    public struct ExampleInfo
    {
        public string Name;
        public string Category;
        public string Description;
    }

    public static class ExampleGallery
    {
        public static readonly string[] Names = GetExampleNames();

        /// <summary>
        /// Full catalog with category and description metadata.
        /// Add new examples here — the order within each category is preserved in the UI.
        /// </summary>
        public static readonly ExampleInfo[] Catalog =
        [
            // Basics (always first)
            new ExampleInfo { Name = "BouncingBall",    Category = "Basics", Description = "A ball that bounces off the edges of the screen" },
            new ExampleInfo { Name = "MouseTrail",      Category = "Basics", Description = "Trail of circles that follow the mouse cursor" },
            new ExampleInfo { Name = "SoundPlayback",   Category = "Basics", Description = "Load and play a WAV sound effect with keyboard controls" },
            new ExampleInfo { Name = "TextureLoading",  Category = "Basics", Description = "Load and display a texture from a file" },

            // Libraries (alphabetical)
            new ExampleInfo { Name = "AetherPhysics",                Category = "Aether.Physics2D",  Description = "2D physics simulation with a bouncing ball and keyboard controls" },
            new ExampleInfo { Name = "AposShapes",                   Category = "Apos.Shapes",       Description = "Draw shapes with the Apos.Shapes library" },
            new ExampleInfo { Name = "FontStashSharp",               Category = "FontStashSharp",     Description = "Dynamic text rendering with multiple sizes and colors" },
            new ExampleInfo { Name = "DynamicFonts",                    Category = "Gum",                Description = "Runtime font generation with KernSmith — pick family, size, bold, italic, and outline" },
            new ExampleInfo { Name = "GumUI",                        Category = "Gum",                Description = "UI layout with buttons and text using Gum" },
            new ExampleInfo { Name = "Camera2D (MonoGame.Extended)", Category = "MonoGame.Extended",  Description = "Pan and zoom a 2D camera with keyboard and mouse" },
        ];

        /// <summary>
        /// Ordered list of categories. Examples appear under these headings in the browser.
        /// </summary>
        public static readonly string[] Categories = Catalog
            .Select(e => e.Category)
            .Distinct()
            .ToArray();

        /// <summary>
        /// Library categories (everything after "Basics"), in catalog order.
        /// </summary>
        public static readonly string[] LibraryCategories = Categories
            .Where(c => c != "Basics")
            .ToArray();

        private static string[] GetExampleNames()
        {
            Assembly assembly = typeof(ExampleGallery).Assembly;
            string prefix = "XnaFiddle.Examples.";
            string suffix = ".cs";
            string[] resources = assembly.GetManifestResourceNames();

            var names = new List<string>();
            for (int i = 0; i < resources.Length; i++)
            {
                if (resources[i].StartsWith(prefix) && resources[i].EndsWith(suffix))
                {
                    string name = resources[i].Substring(prefix.Length, resources[i].Length - prefix.Length - suffix.Length);
                    names.Add(name);
                }
            }
            names.Sort();
            return names.ToArray();
        }

        public static string Load(string name)
        {
            Assembly assembly = typeof(ExampleGallery).Assembly;
            string resourceName = "XnaFiddle.Examples." + name + ".cs";
            using Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Returns any asset files bundled with the named example.
        /// Assets use the naming convention: Examples/{ExampleName}.{AssetFileName.ext}
        /// e.g. "TextureLoading.KniIcon.png" → example "TextureLoading", asset "KniIcon.png"
        /// </summary>
        public static ExampleAsset[] LoadAssets(string name)
        {
            Assembly assembly = typeof(ExampleGallery).Assembly;
            // Asset resources are: XnaFiddle.Examples.{ExampleName}.{filename}.{ext}
            // Code resources are:  XnaFiddle.Examples.{ExampleName}.cs
            // So asset prefix is same as code resource minus ".cs"
            string assetPrefix = "XnaFiddle.Examples." + name + ".";
            string codeResource = assetPrefix + "cs";
            string[] resources = assembly.GetManifestResourceNames();

            var assets = new List<ExampleAsset>();
            for (int i = 0; i < resources.Length; i++)
            {
                if (resources[i].StartsWith(assetPrefix) && resources[i] != codeResource)
                {
                    // Extract the asset filename (everything after the example-name prefix)
                    string assetFileName = resources[i].Substring(assetPrefix.Length);
                    using Stream stream = assembly.GetManifestResourceStream(resources[i]);
                    if (stream == null) continue;
                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);
                    assets.Add(new ExampleAsset { FileName = assetFileName, Data = data });
                }
            }
            return assets.ToArray();
        }
    }
}
