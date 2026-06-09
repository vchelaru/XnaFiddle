using System.Collections.Generic;

namespace XnaFiddle.Plugins
{
    public class KernSmithPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "KernSmith";
        public string[] RequiredAssemblies => ["KernSmith", "KernSmith.GumCommon", "KernSmith.KniGum"];
        public string[] VersionAssemblies => ["KernSmith.KniGum", "KernSmith.GumCommon", "KernSmith"];

        public void CleanUp() { }

        public bool IsUsedInSource(string source) => source.Contains("KernSmith");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = "KernSmith", Version = PackageVersions.KernSmith },
            new() { Id = target.IsKni() ? "KernSmith.KniGum" : "KernSmith.MonoGameGum", Version = PackageVersions.KernSmith },
            new() { Id = "KernSmith.Rasterizers.StbTrueType", Version = PackageVersions.KernSmith },
        ];
    }
}
