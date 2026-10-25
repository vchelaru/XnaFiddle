using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace XnaFiddle.Plugins
{
    public class GumPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "Gum";
        public string[] RequiredAssemblies => ["KniGum", "GumCommon", "FlatRedBall.InterpolationCore"];
        public string[] VersionAssemblies => ["GumCommon", "KniGum"];

        public bool IsUsedInSource(string source) =>
            source.Contains("Gum.") || source.Contains("using Gum;");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = target.IsKni() ? "Gum.KNI" : "Gum.MonoGame", Version = PackageVersions.Gum }
        ];

        public void CleanUp()
        {
            try
            {
                var gumServiceType = Type.GetType("Gum.GumService, KniGum");
                if (gumServiceType == null) return;
                var defaultProp = gumServiceType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public);
                var gumService = defaultProp?.GetValue(null);
                if (gumService == null) return;

                // Gum's own teardown: clears roots, registrations, and static state so the next
                // run's GumService.Initialize() starts clean. Hand-resetting individual statics
                // here instead leaves Gum's per-service element tracking inconsistent, and the
                // next run throws "built under GumService #2 but is being added under #1".
                gumServiceType.GetMethod("Uninitialize", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
                    ?.Invoke(gumService, null);

                // Clear the static Forms-control template registry. A Gum.Themes.* theme's
                // Apply() only ADDS entries here (FormsUtilities.InitializeDefaults uses
                // TryAdd), so a themed run leaves this dictionary permanently pointed at the
                // theme's visuals. Without clearing it, every later run — including an
                // unrelated Gum example that never touches theming — silently renders with
                // whatever theme a previous run last applied, because the next
                // FormsUtilities.InitializeDefaults() can no longer overwrite the occupied keys.
                var frameworkElementType = Type.GetType("Gum.Forms.Controls.FrameworkElement, KniGum");
                var templatesProp = frameworkElementType?.GetProperty("DefaultFormsTemplates", BindingFlags.Static | BindingFlags.Public);
                (templatesProp?.GetValue(null) as IDictionary)?.Clear();
            }
            catch (Exception e)
            {
                // Log but don't rethrow — partial cleanup is better than aborting the run.
                // This uses reflection against KniGum internals, so failures here are most
                // likely caused by a KniGum API change and will show up clearly in the console.
                Console.WriteLine($"[XnaFiddle] {Name} cleanup failed: {e}");
            }
        }
    }
}
