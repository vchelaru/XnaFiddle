using System.IO;
using System.Reflection;

namespace XnaFiddle
{
    /// <summary>
    /// Built-in content assets any fiddle can reference by name without uploading anything.
    /// See STANDARD_CONTENT_PLAN.md for the mechanism (lazy source-scan registration + the
    /// "std/" name reservation). Mirrors ExampleGallery's embedded-resource loading, but the
    /// assets here are not gated behind a specific example being open.
    /// </summary>
    public static class StandardContentRegistry
    {
        /// <summary>
        /// The append-only catalog of standard content. Name is the permanent key a fiddle
        /// references and that gets registered into InMemoryContentManager (always under the
        /// "std/" prefix — see STANDARD_CONTENT_PLAN.md decision #1); ResourceFile is the bare
        /// filename embedded under XnaFiddle.StandardContent. Name and ResourceFile are kept
        /// separate so the bytes behind a name can be replaced later without breaking fiddles
        /// that already reference it (plan decision #4).
        ///
        /// Once an entry ships, it is permanent: it can never be renamed or removed, only added
        /// to. A shared fiddle/URL may already reference it by name.
        /// </summary>
        public static readonly (string Name, string ResourceFile)[] Items =
        [
            ("std/DroidSans.ttf", "DroidSans.ttf"),
        ];

        public static byte[] Load(string resourceFile)
        {
            Assembly assembly = typeof(StandardContentRegistry).Assembly;
            string resourceName = "XnaFiddle.StandardContent." + resourceFile;
            using Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            byte[] data = new byte[stream.Length];
            stream.Read(data, 0, data.Length);
            return data;
        }
    }
}
