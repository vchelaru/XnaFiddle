using System.Collections.Generic;

namespace XnaFiddle.Plugins
{
    public class FontStashSharpPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "FontStashSharp";
        public string[] RequiredAssemblies => ["FontStashSharp.Kni", "FontStashSharp.Base", "FontStashSharp.Rasterizers.StbTrueTypeSharp"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("FontStashSharp.Kni", ["FontStashSharp.Kni", "FontStashSharp.Base"]);

        public void CleanUp() { }

        public bool IsUsedInSource(string source) => source.Contains("FontStashSharp");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = target.IsKni() ? "FontStashSharp.Kni" : "FontStashSharp.MonoGame", Version = PackageVersions.FontStashSharp }
        ];
    }
}
