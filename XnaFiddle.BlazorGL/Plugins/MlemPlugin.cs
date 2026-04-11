using System;
using System.Reflection;

namespace XnaFiddle.Plugins
{
    public class MlemPlugin : ILibraryPlugin
    {
        public string Name => "MLEM";
        public string[] RequiredAssemblies => ["MLEM.KNI", "MLEM.Ui.KNI", "MLEM.Extended.KNI"];
        public (string Label, string[] AssemblyNames) VersionInfo => ("MLEM", ["MLEM.KNI", "MLEM.Ui.KNI", "MLEM.Extended.KNI"]);

        public void CleanUp()
        {
            try
            {
                var type = Type.GetType("MLEM.Misc.MlemPlatform, MLEM.KNI");
                if (type == null) return;
                var current = type.GetField("Current", BindingFlags.Static | BindingFlags.Public);
                current?.SetValue(null, null);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[XnaFiddle] {Name} cleanup failed: {e}");
            }
        }
    }
}
