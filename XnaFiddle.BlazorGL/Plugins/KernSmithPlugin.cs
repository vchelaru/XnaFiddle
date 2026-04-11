namespace XnaFiddle.Plugins
{
    public class KernSmithPlugin : ILibraryPlugin
    {
        public string Name => "KernSmith";
        public string[] RequiredAssemblies => ["KernSmith", "KernSmith.GumCommon", "KernSmith.KniGum"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("KernSmith.KniGum", ["KernSmith.KniGum", "KernSmith.GumCommon", "KernSmith"]);

        public void CleanUp() { }
    }
}
