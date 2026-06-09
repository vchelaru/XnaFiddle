using System;
using System.Collections.Generic;
using System.Reflection;

namespace XnaFiddle.Plugins
{
    public class GumShapesPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "Gum.Shapes";

        // The shape runtimes (CircleRuntime / RectangleRuntime) ship in base Gum (KniGum,
        // registered by GumPlugin). This package adds the Apos.Shapes-backed fill/effects
        // renderer — its KniGumShapes assembly is what user code needs to compile a
        // reference to ShapeRenderer.
        public string[] RequiredAssemblies => ["KniGumShapes"];
        public string[] VersionAssemblies => ["KniGumShapes"];

        // CircleRuntime / RectangleRuntime alone only need Gum.KNI; the fill renderer (and
        // therefore this package) is required only when ShapeRenderer is used to initialize
        // it. Detecting "ShapeRenderer" avoids a false positive against base-Gum's plain
        // ColoredRectangleRuntime, which contains "RectangleRuntime" as a substring.
        public bool IsUsedInSource(string source) => source.Contains("ShapeRenderer");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = target.IsKni() ? "Gum.Shapes.KNI" : "Gum.Shapes.MonoGame", Version = PackageVersions.GumShapes }
        ];

        public void CleanUp()
        {
            try
            {
                var shapeRendererType = Type.GetType("MonoGameAndGum.Renderables.ShapeRenderer, KniGumShapes");
                if (shapeRendererType == null) return;
                var selfProp = shapeRendererType.GetProperty("Self", BindingFlags.Static | BindingFlags.Public);
                var self = selfProp?.GetValue(null);
                if (self == null) return;

                // ShapeRenderer.Self is a process-wide singleton whose ShapeBatch is bound to
                // the GraphicsDevice of the run that first initialized it. XnaFiddle recreates
                // the device every run, so drop the stale ShapeBatch and clear IsInitialized;
                // the next game's ShapeRenderer.Self.Initialize() then rebuilds the batch
                // against the new device. Without this the second run draws with a ShapeBatch
                // pointing at a disposed GraphicsDevice. IsInitialized has a non-public setter,
                // so its auto-property backing field is set directly.
                var sbField = shapeRendererType.GetField("_sb", BindingFlags.Instance | BindingFlags.NonPublic);
                sbField?.SetValue(self, null);

                var isInitField = shapeRendererType.GetField("<IsInitialized>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                isInitField?.SetValue(self, false);
            }
            catch (Exception e)
            {
                // Log but don't rethrow — partial cleanup is better than aborting the run.
                // This uses reflection against KniGumShapes internals, so failures here are
                // most likely caused by a package API change and will show up in the console.
                Console.WriteLine($"[XnaFiddle] {Name} cleanup failed: {e}");
            }
        }
    }
}
