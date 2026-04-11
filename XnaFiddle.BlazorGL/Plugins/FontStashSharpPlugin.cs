namespace XnaFiddle.Plugins
{
    public class FontStashSharpPlugin : ILibraryPlugin
    {
        public string Name => "FontStashSharp";
        public string[] RequiredAssemblies => ["FontStashSharp.Kni", "FontStashSharp.Base", "FontStashSharp.Rasterizers.StbTrueTypeSharp"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("FontStashSharp.Kni", ["FontStashSharp.Kni", "FontStashSharp.Base"]);

        public void CleanUp() { }
    }
}
