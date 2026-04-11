namespace XnaFiddle.Plugins
{
    public class AposShapesPlugin : ILibraryPlugin
    {
        public string Name => "Apos.Shapes";
        public string[] RequiredAssemblies => ["Apos.Shapes.KNI"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("Apos.Shapes.KNI", ["Apos.Shapes.KNI"]);

        public void CleanUp() { }
    }
}
