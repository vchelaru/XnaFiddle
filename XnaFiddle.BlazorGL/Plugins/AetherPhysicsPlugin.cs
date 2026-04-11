namespace XnaFiddle.Plugins
{
    public class AetherPhysicsPlugin : ILibraryPlugin
    {
        public string Name => "Aether.Physics2D";
        public string[] RequiredAssemblies => ["Aether.Physics2D"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("Aether.Physics2D", ["Aether.Physics2D"]);

        public void CleanUp() { }
    }
}
