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
                // this plugin lives in the platform-agnostic XnaFiddle.Core (net10.0), but the type
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
                // Intentionally swallowed — same rationale as the clears above. Under net10.0 (tests)
                // nkast.Wasm.Dom is absent so windowDomType is null and this block no-ops.
            }
        }

        // Clears KNI's Document element-id cache (nkast.Wasm.Dom.Document._elementsCache) so a
        // just-recreated <canvas> (a Reach<->HiDef profile switch — see Index.razor.cs
        // DoCompileAndRun) is re-resolved instead of returning the stale Canvas wrapper pointing
        // at the now-detached old element (-> black screen).
        //
        // Deliberately NOT part of CleanUp() / called on every restart: KNI's Document.GetElementById
        // does not reuse a cached wrapper by uid, it constructs a BRAND NEW C# Canvas wrapper any
        // time the id isn't in _elementsCache (Document.cs GetElementById -> CreateInstance<TElement>).
        // That fresh wrapper's WebGL/WebGL2 context is cached per-*wrapper-instance*
        // (Canvas.cs GetContext<T>'s _webglRenderingContext/_webgl2RenderingContext fields), so it
        // calls the JS bridge's getContext(...) again — which, for the SAME underlying <canvas>
        // element, the browser answers with the SAME already-registered context object. KNI's JS
        // registry (nkJSObject.RegisterObject) has no "already registered, return existing uid"
        // guard on that path (unlike e.g. Document.GetElementById's own JS side), so it throws
        // "object already registered" the moment a second Canvas wrapper asks for a context on an
        // unchanged canvas. Calling this only when the <canvas> element is ACTUALLY being replaced
        // keeps ordinary restarts on the cached wrapper (whose GetContext<T>() is already
        // instance-cached, so it never re-enters the JS bridge at all).
        public void ClearCanvasElementCache()
        {
            try
            {
                // Resolved by name — same rationale as CleanUp()'s reflection: this plugin lives in
                // the platform-agnostic XnaFiddle.Core, nkast.Wasm.Dom is browser-only.
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
                // Intentionally swallowed — same rationale as CleanUp()'s clears.
            }
        }
    }
}
