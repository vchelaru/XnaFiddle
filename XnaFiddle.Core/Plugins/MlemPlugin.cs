using System;
using System.Collections.Generic;
using System.Reflection;

namespace XnaFiddle.Plugins
{
    public class MlemPlugin : ILibraryPlugin, IExportableLibrary
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

        public bool IsUsedInSource(string source) => source.Contains("MLEM");

        public List<ExportPackage> GetExportPackages(ExportTarget target, string source)
        {
            bool isKni = target.IsKni();
            var packages = new List<ExportPackage>
            {
                new() { Id = isKni ? "MLEM.KNI" : "MLEM", Version = PackageVersions.Mlem }
            };

            if (source.Contains("MLEM.Ui"))
                packages.Add(new ExportPackage { Id = isKni ? "MLEM.Ui.KNI" : "MLEM.Ui", Version = PackageVersions.Mlem });

            if (source.Contains("MLEM.Extended"))
                packages.Add(new ExportPackage { Id = isKni ? "MLEM.Extended.KNI" : "MLEM.Extended", Version = PackageVersions.Mlem });

            return packages;
        }
    }
}
