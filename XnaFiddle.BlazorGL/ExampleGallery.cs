using System.IO;
using System.Reflection;

namespace XnaFiddle
{
    public static class ExampleGallery
    {
        public static readonly string[] Names = GetExampleNames();

        private static string[] GetExampleNames()
        {
            Assembly assembly = typeof(ExampleGallery).Assembly;
            string prefix = "XnaFiddle.Examples.";
            string suffix = ".cs";
            string[] resources = assembly.GetManifestResourceNames();

            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < resources.Length; i++)
            {
                if (resources[i].StartsWith(prefix) && resources[i].EndsWith(suffix))
                {
                    string name = resources[i].Substring(prefix.Length, resources[i].Length - prefix.Length - suffix.Length);
                    names.Add(name);
                }
            }
            names.Sort();
            return names.ToArray();
        }

        public static string Load(string name)
        {
            Assembly assembly = typeof(ExampleGallery).Assembly;
            string resourceName = "XnaFiddle.Examples." + name + ".cs";
            using Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}
