using System.Collections.Generic;

namespace XnaFiddle.Plugins
{
    public class FlatRedBallAnimationChainPlugin : ILibraryPlugin, IExportableLibrary
    {
        public string Name => "FlatRedBall.AnimationChain";
        public string[] RequiredAssemblies => ["AnimationChain.KNI"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("FlatRedBall.AnimationChain", ["AnimationChain.KNI"]);

        public void CleanUp() { }

        public bool IsUsedInSource(string source) =>
            source.Contains("AnimationChain") || source.Contains("AnimationPlayer");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
        [
            new() { Id = target.IsKni() ? "FlatRedBall.AnimationChain.KNI" : "FlatRedBall.AnimationChain.MonoGame", Version = PackageVersions.FlatRedBallAnimationChain }
        ];
    }
}
