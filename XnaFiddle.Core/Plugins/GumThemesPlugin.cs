using System.Collections.Generic;
using System.Linq;

namespace XnaFiddle.Plugins
{
    /// <summary>
    /// Gum's Gum.Themes.* visual theme packages (Bubblegum, DarkPro, Neon, ForestGlade,
    /// Retro95, ...). Each theme ships as its own NuGet package — id "Gum.Themes.&lt;Name&gt;.Kni"
    /// / ".MonoGame" — at the same version cadence as Gum.KNI/Gum.MonoGame (see GumVersion in
    /// Directory.Build.props), so no separate version property is needed.
    ///
    /// Detection is per-theme, the same sub-package pattern MlemPlugin uses for MLEM.Ui /
    /// MLEM.Extended: only themes actually referenced in source get an export package, so an
    /// exported project doesn't drag in all wired themes just because one is used.
    /// </summary>
    public class GumThemesPlugin : ILibraryPlugin, IExportableLibrary
    {
        // Subset of the themes Gum ships (also available upstream: Editor, Hazard, Meadow,
        // Template) wired into XnaFiddle. Namespace segment == package id suffix and assembly
        // name, e.g. "Gum.Themes.Bubblegum" -> package/assembly "Gum.Themes.Bubblegum.Kni".
        public static readonly string[] ThemeNames = ["Bubblegum", "DarkPro", "Neon", "ForestGlade", "Retro95"];

        public string Name => "Gum.Themes";

        // The in-browser runtime is always KNI (see the project-export skill), so only the
        // .Kni assemblies need to be resolvable by Roslyn here; .MonoGame is export-only.
        public string[] RequiredAssemblies => ThemeNames.Select(t => $"Gum.Themes.{t}.Kni").ToArray();

        // No banner entry — GumPlugin's "Gum <version>" entry already covers this cadence.
        public string[] VersionAssemblies => [];

        public void CleanUp() { }

        public bool IsUsedInSource(string source) =>
            ThemeNames.Any(t => source.Contains("Gum.Themes." + t));

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source)
        {
            string suffix = target.IsKni() ? "Kni" : "MonoGame";
            return ThemeNames
                .Where(t => source.Contains("Gum.Themes." + t))
                .Select(t => new ExportPackage { Id = $"Gum.Themes.{t}.{suffix}", Version = PackageVersions.Gum })
                .ToList();
        }
    }
}
