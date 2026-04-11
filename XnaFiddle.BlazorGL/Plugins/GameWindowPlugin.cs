using System;
using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;

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
                var field = typeof(BlazorGameWindow).GetField("_instances",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (field?.GetValue(null) is IDictionary dict)
                    dict.Clear();
            }
            catch
            {
                // Intentionally swallowed. This reflects a single well-known field on a type
                // in our own codebase (_instances on BlazorGameWindow). The only realistic
                // failure is a refactor that renames the field, which would be caught immediately
                // in development. There is nothing actionable to do if this fails at runtime.
            }
        }
    }
}
