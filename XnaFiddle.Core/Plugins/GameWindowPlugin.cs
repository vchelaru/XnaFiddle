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

        // The nkast.Wasm.Dom.Window event-delegate fields that BlazorGameWindow's ctor re-subscribes
        // on EVERY game (BlazorGameWindow.cs: OnTouch*, OnKeyDown/Up, OnFocus/OnBlur). CleanUp() nulls
        // exactly these between runs to stop per-run subscription accumulation (issue #90).
        //
        // It must NEVER include mouse/gamepad/resize: those are subscribed ONCE — ConcreteMouse
        // .PlatformSetWindowHandle and ConcreteGamePad wire them, gated on Mouse.WindowHandle, which
        // BlazorGameWindow sets only on the FIRST game. Nulling OnMouseMove/Down/Up/Wheel or
        // OnGamepad* orphans input permanently after the first restart (mouse/Gum/cursor-trail go
        // dead) — that was issue #95. Exposed (and pinned by GameWindowPluginTests) so this contract
        // can't silently regress.
        public static readonly string[] ClearedWindowEventFields =
        {
            "OnTouchStart", "OnTouchMove", "OnTouchEnd", "OnTouchCancel",
            "OnKeyDown", "OnKeyUp", "OnFocus", "OnBlur",
        };

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

            try
            {
                // Null only the Window.Current event delegates that BlazorGameWindow's ctor
                // re-subscribes every game (ClearedWindowEventFields). These leak otherwise:
                // Window.Current is a page-lifetime singleton, old games are dropped without
                // Dispose(), so each Run's closures pile up; a single touch then fans out to stale
                // closures from dead games and trips a Mono runtime assertion (class-accessors.c) ->
                // abort() on the 2nd-3rd restart (issue #90). CleanUp runs before the next game's
                // ctor re-adds them, so each Run starts from a single subscriber.
                //
                // We must NOT blanket-clear every delegate field: mouse/gamepad are subscribed once
                // and never re-subscribed, so clearing them kills input after the first restart
                // (issue #95). See ClearedWindowEventFields. Resolved by name (nkast.Wasm.Dom is
                // browser-only).
                Type windowDomType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("nkast.Wasm.Dom.Window"))
                    .FirstOrDefault(t => t != null);
                var currentProp = windowDomType?.GetProperty("Current",
                    BindingFlags.Static | BindingFlags.Public);
                object window = currentProp?.GetValue(null);
                if (window != null)
                {
                    foreach (string name in ClearedWindowEventFields)
                        windowDomType.GetField(name, BindingFlags.Instance | BindingFlags.Public)
                            ?.SetValue(window, null);
                }
            }
            catch
            {
                // Intentionally swallowed — same rationale as the clears above. Under net8.0 (tests)
                // nkast.Wasm.Dom is absent so windowDomType is null and this block no-ops.
            }
        }
    }
}
