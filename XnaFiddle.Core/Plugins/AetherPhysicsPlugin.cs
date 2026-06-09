using System.Collections.Generic;

namespace XnaFiddle.Plugins
{
    public class AetherPhysicsPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "Aether.Physics2D";
        public string[] RequiredAssemblies => ["Aether.Physics2D"];
        public string[] VersionAssemblies => ["Aether.Physics2D"];

        public void CleanUp() { }

        public bool IsUsedInSource(string source) => source.Contains("Aether.Physics2D");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = target.IsKni() ? "Aether.Physics2D.KNI" : "Aether.Physics2D", Version = PackageVersions.AetherPhysics }
        ];
    }
}
