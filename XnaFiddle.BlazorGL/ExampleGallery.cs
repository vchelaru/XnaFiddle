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

            // 2D Shaders (alphabetical) — post-processing pixel shaders applied to a sprite.
            // Compiled in-browser from HLSL .fx via ShadowDusk; HiDef, but the OpenGL .mgfx is
            // profile-agnostic so it loads under Reach/WebGL1 too.
            new ExampleInfo { Name = "Blur",            Category = "2D Shaders", Description = "Two-pass separable Gaussian blur using render targets" },
            new ExampleInfo { Name = "BlurPostProcess", Category = "2D Shaders", Description = "Full-screen post-processing blur: the same Blur.fx applied to the whole scene via a screen-sized render target (Space toggles, Up/Down adjust)" },
            new ExampleInfo { Name = "Dots",            Category = "2D Shaders", Description = "Halftone dot pattern with angle and scale parameters" },
            new ExampleInfo { Name = "Fading",          Category = "2D Shaders", Description = "Vertical fade driven by texture coordinates" },
            new ExampleInfo { Name = "Grayscale",       Category = "2D Shaders", Description = "Grayscale pixel shader (.fx) compiled in-browser" },
            new ExampleInfo { Name = "Invert",          Category = "2D Shaders", Description = "Invert an image's colors" },
            new ExampleInfo { Name = "Pixelated",       Category = "2D Shaders", Description = "Pixelate an image by snapping UVs to a grid" },
            new ExampleInfo { Name = "Saturate",        Category = "2D Shaders", Description = "Bloom / brightness boost with adjustable parameters" },
            new ExampleInfo { Name = "Scanlines",       Category = "2D Shaders", Description = "CRT-style scanlines" },
            new ExampleInfo { Name = "Sepia",           Category = "2D Shaders", Description = "Sepia tone, with a tint parameter set from C#" },
            new ExampleInfo { Name = "Tint",            Category = "2D Shaders", Description = "Multiply an image by a tint color passed from C#" },

            // 3D Shaders — full-scene procedural shaders (vertex + pixel) that render a 3D scene,
            // not a post-process over a sprite.
            new ExampleInfo { Name = "Ocean",           Category = "3D Shaders", Description = "Procedural raymarched ocean with a day/night sky — drag to rotate the camera" },

            // Libraries (alphabetical)
            new ExampleInfo { Name = "AetherPhysics",                Category = "Aether.Physics2D",  Description = "2D physics simulation with a bouncing ball and keyboard controls" },
            new ExampleInfo { Name = "AposShapes",                   Category = "Apos.Shapes",       Description = "Draw shapes with the Apos.Shapes library" },
            new ExampleInfo { Name = "AnimationChain",               Category = "AnimationChain",    Description = "Play a sprite animation from a FlatRedBall .achx file with AnimationPlayer — arrow keys switch animations" },
            new ExampleInfo { Name = "FontStashSharp",               Category = "FontStashSharp",     Description = "Dynamic text rendering with multiple sizes and colors" },
            new ExampleInfo { Name = "DynamicFonts",                    Category = "Gum",                Description = "Runtime font generation with KernSmith — pick family, size, bold, italic, and outline" },
            new ExampleInfo { Name = "GumUI",                        Category = "Gum",                Description = "UI layout with buttons and text using Gum" },
            new ExampleInfo { Name = "GumShapes",                    Category = "Gum",                Description = "Filled, outlined, gradient, dashed, and shadowed shapes with Gum's CircleRuntime and RectangleRuntime" },
            new ExampleInfo { Name = "MlemTextFormatting",           Category = "MLEM",               Description = "Text formatting using MLEM, which supports coloring, in-text icons, text animations and more" },
            new ExampleInfo { Name = "MlemUi",                       Category = "MLEM",               Description = "A mouse, keyboard, gamepad and touch ready Ui system that features automatic anchoring, sizing and several ready-to-use element types" },
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
        /// Built-in categories that are part of XnaFiddle itself (not a third-party library).
        /// These render above the divider in the example browser, with no library separator.
        /// </summary>
        public static readonly string[] BuiltInCategories = { "Basics", "2D Shaders", "3D Shaders" };

        /// <summary>
        /// Third-party library categories, in catalog order. The example browser draws a
        /// divider before the first of these to separate built-ins from libraries.
        /// </summary>
        public static readonly string[] LibraryCategories = Categories
            .Where(c => !System.Array.Exists(BuiltInCategories, b => b == c))
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
