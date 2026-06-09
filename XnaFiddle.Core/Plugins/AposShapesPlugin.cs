using System.Collections.Generic;

namespace XnaFiddle.Plugins
{
    public class AposShapesPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "Apos.Shapes";
        public string[] RequiredAssemblies => ["Apos.Shapes.KNI"];
        public string[] VersionAssemblies => ["Apos.Shapes.KNI"];

        public void CleanUp() { }

        public bool IsUsedInSource(string source) => source.Contains("Apos.Shapes");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = target.IsKni() ? "Apos.Shapes.KNI" : "Apos.Shapes", Version = PackageVersions.AposShapes }
        ];
    }
}
