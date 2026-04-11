namespace XnaFiddle.Plugins
{
    public class MonoGameExtendedPlugin : ILibraryPlugin
    {
        public string Name => "MonoGame.Extended";
        public string[] RequiredAssemblies => ["KNI.Extended"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("KNI.Extended", ["KNI.Extended"]);

        public void CleanUp() { }
    }
}
