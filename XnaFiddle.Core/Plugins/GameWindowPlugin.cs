using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace XnaFiddle.Plugins
{
    public class GameWindowPlugin : ILibraryPlugin
    {
        public string Name => "GameWindow";
        public string[] RequiredAssemblies => [];
        public string[] VersionAssemblies => [];

        public void CleanUp()
        {
            try
            {
                // Resolve KNI's BlazorGameWindow by name rather than a compile-time reference:
                // this plugin lives in the platform-agnostic XnaFiddle.Core (net8.0), but the type
                // is in the browser-only KNI Blazor platform assembly. Clearing its static
                // _instances dictionary prevents stale window handles leaking across runs.
                Type windowType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Microsoft.Xna.Framework.BlazorGameWindow"))
                    .FirstOrDefault(t => t != null);
                var field = windowType?.GetField("_instances",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (field?.GetValue(null) is IDictionary dict)
                    dict.Clear();
            }
            catch
            {
                // Intentionally swallowed. The only realistic failure is a rename of the type or
                // field, which would surface immediately in development. Nothing actionable at runtime.
            }

            try
            {
                // Clear KNI's Document element-id cache so a swapped/recreated canvas is re-resolved
                // rather than served the stale Canvas wrapper (which points at the now-detached old
                // <canvas> -> black screen). A Reach<->HiDef profile switch recreates theCanvas to
                // get a fresh WebGL context type (see Index.razor.cs DoCompileAndRun). Resolved by
                // name for the same reason as above: nkast.Wasm.Dom lives in the browser-only
                // assembly. Clearing this every run is harmless — a same-element run just re-resolves
                // the same context. Wrapped in its own try/catch with the same swallow rationale.
                Type windowDomType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("nkast.Wasm.Dom.Window"))
                    .FirstOrDefault(t => t != null);
                var currentProp = windowDomType?.GetProperty("Current",
                    BindingFlags.Static | BindingFlags.Public);
                object window = currentProp?.GetValue(null);
                var documentProp = window?.GetType().GetProperty("Document",
                    BindingFlags.Instance | BindingFlags.Public);
                object document = documentProp?.GetValue(window);
                var cacheField = document?.GetType().GetField("_elementsCache",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (cacheField?.GetValue(document) is IDictionary cache)
                    cache.Clear();
            }
            catch
            {
                // Intentionally swallowed — same rationale as the _instances clear above.
            }
        }
    }
}
