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
        public (string Label, string[] AssemblyNames) VersionInfo => ("", []);

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
        }
    }
}
