using System.Collections.Generic;

namespace XnaFiddle.Plugins
{
    public class MonoGameExtendedPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "MonoGame.Extended";
        public string[] RequiredAssemblies => ["KNI.Extended"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("KNI.Extended", ["KNI.Extended"]);

        public void CleanUp() { }

        public bool IsUsedInSource(string source) => source.Contains("MonoGame.Extended");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = target.IsKni() ? "KNI.Extended" : "MonoGame.Extended", Version = PackageVersions.KniExtended }
        ];
    }
}
