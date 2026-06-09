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
        public (string Label, string[] AssemblyNames) VersionInfo => ("Gum.KNI", ["GumCommon", "KniGum"]);

        public bool IsUsedInSource(string source) =>
            source.Contains("MonoGameGum") || source.Contains("Gum.");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = target.IsKni() ? "Gum.KNI" : "Gum.MonoGame", Version = PackageVersions.Gum }
        ];

        public void CleanUp()
        {
            try
            {
                var gumServiceType = Type.GetType("MonoGameGum.GumService, KniGum");
                if (gumServiceType == null) return;
                var defaultProp = gumServiceType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public);
                var gumService = defaultProp?.GetValue(null);
                if (gumService == null) return;

                // Clear Root, PopupRoot, and ModalRoot children.
                // These are persistent statics — old controls accumulate across runs without this.
                foreach (var rootPropName in new[] { "Root", "PopupRoot", "ModalRoot" })
                {
                    var rootProp = gumServiceType.GetProperty(rootPropName, BindingFlags.Instance | BindingFlags.Public);
                    var root = rootProp?.GetValue(gumService);
                    if (root == null) continue;
                    var childrenProp = root.GetType().GetProperty("Children", BindingFlags.Instance | BindingFlags.Public);
                    (childrenProp?.GetValue(root) as IList)?.Clear();
                }

                // Reset SystemManagers.Default so GumService.Initialize creates a fresh one
                var systemManagersType = Type.GetType("RenderingLibrary.SystemManagers, GumCommon");
                if (systemManagersType != null)
                {
                    var defaultPropSM = systemManagersType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public);
                    defaultPropSM?.SetValue(null, null);
                }

                // Clear LoaderManager cache WITHOUT disposing textures
                var loaderManagerType = Type.GetType("RenderingLibrary.Content.LoaderManager, GumCommon");
                if (loaderManagerType != null)
                {
                    var selfProp = loaderManagerType.GetProperty("Self", BindingFlags.Static | BindingFlags.Public);
                    var loaderInstance = selfProp?.GetValue(null);
                    if (loaderInstance != null)
                    {
                        var cacheField = loaderManagerType.GetField("mCachedDisposables", BindingFlags.Instance | BindingFlags.NonPublic);
                        (cacheField?.GetValue(loaderInstance) as IDictionary)?.Clear();
                    }
                }

                // Reset IsInitialized so the next game can call GumService.Initialize()
                var isInitProp = gumServiceType.GetProperty("IsInitialized", BindingFlags.Instance | BindingFlags.Public);
                isInitProp?.SetValue(gumService, false);
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
