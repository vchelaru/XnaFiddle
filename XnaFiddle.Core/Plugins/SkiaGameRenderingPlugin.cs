using System;
using System.Collections.Generic;
using System.Reflection;

namespace XnaFiddle.Plugins
{
    /// <summary>
    /// GPU SkiaSharp-to-Texture2D rendering (github.com/vchelaru/SkiaGameRendering). Pure harness
    /// registration — the library's own <c>SkiaGameRendering.SkiaRenderer</c> static class is
    /// already the runtime singleton, so this plugin holds no state of its own.
    ///
    /// The in-browser runtime always uses the WebGL backend (SkiaGameRendering.Kni.WebGL) — see
    /// the project-export skill's "in-browser runtime vs. export targets" note. Export targets get
    /// whichever platform package matches (GetExportPackages below); some platforms the upstream
    /// library hasn't implemented yet (KNI/MonoGame Android, MonoGame WindowsDX12/DesktopVK, FNA)
    /// get no package — a fiddle using SkiaGameRendering won't build if exported to one of those.
    /// </summary>
    public class SkiaGameRenderingPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "SkiaGameRendering";
        public string[] RequiredAssemblies => ["SkiaGameRendering.Kni.WebGL"];
        public string[] VersionAssemblies => ["SkiaGameRendering.Kni.WebGL"];

        public bool IsUsedInSource(string source) => source.Contains("SkiaGameRendering");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source)
        {
            string id = target switch
            {
                ExportTarget.KniDesktopGL => "SkiaGameRendering.Kni.DesktopGL",
                ExportTarget.KniWindowsDX => "SkiaGameRendering.Kni.WindowsDX",
                ExportTarget.KniBlazorGL => "SkiaGameRendering.Kni.WebGL",
                ExportTarget.MonoGameDesktopGL => "SkiaGameRendering",
                ExportTarget.MonoGameWindowsDX => "SkiaGameRendering.WindowsDX",
                // KniAndroid, MonoGameAndroid, MonoGameWindowsDX12, MonoGameDesktopVK, and FNA
                // have no SkiaGameRendering backend yet (upstream README platform table) — no
                // package to add, so the export builds without SkiaRenderer resolving.
                _ => null,
            };
            return id == null ? [] : [new() { Id = id, Version = PackageVersions.SkiaGameRendering }];
        }

        // Each Run creates a fresh GraphicsDevice (see the game-lifecycle skill), but the page's
        // SkiaGameWebGlHost attachment (Index.razor.cs's one-time SkiaRenderer.AttachHost call) is
        // page-lifetime, not per-Run. SkiaRenderer.Initialize(GraphicsDevice) throws if it's asked
        // to pair a new GraphicsDevice with an already-initialized backend, so the old
        // backend/GraphicsDevice pairing must be torn down between runs. SkiaRenderer.Dispose() only
        // clears that pairing — it does NOT detach the host — so the next game's own
        // IsReady/Initialize poll in Draw() (see SkiaGameRenderingExample.cs) transparently rebuilds
        // a fresh backend from the same host. This exact Dispose-then-Initialize sequence is what
        // the upstream sample's RecreateBackend() does, and Dispose() is a documented no-op when
        // nothing is initialized yet (first run), so this is safe every time. Resolved by name:
        // XnaFiddle.Core (net10.0) never takes a compile-time dependency on the browser-only KNI
        // package (see the code-style skill's "submodules are off-limits" reflection convention,
        // applied here to a third-party package for the same reason — no browser reference from Core).
        public void CleanUp()
        {
            try
            {
                Type rendererType = Type.GetType("SkiaGameRendering.SkiaRenderer, SkiaGameRendering.Kni.WebGL");
                MethodInfo disposeMethod = rendererType?.GetMethod("Dispose", BindingFlags.Static | BindingFlags.Public);
                disposeMethod?.Invoke(null, null);
            }
            catch (Exception e)
            {
                // Log but don't rethrow — partial cleanup is better than aborting the run. This
                // uses reflection against the library's public API, so failures here most likely
                // mean an upstream signature change and will show up clearly in the console.
                Console.WriteLine($"[XnaFiddle] {Name} cleanup failed: {e}");
            }
        }
    }
}
