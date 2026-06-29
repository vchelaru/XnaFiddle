using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace XnaFiddle
{
    /// <summary>
    /// Fingerprints fiddle source (C# + shader tabs) so unchanged restarts can reuse the
    /// already-loaded <see cref="Type"/> instead of Roslyn + Assembly.Load again. WASM cannot
    /// unload assemblies; redundant loads are the main memory growth on mobile restarts.
    /// </summary>
    public static class CompileFingerprint
    {
        public static string Compute(string csharpSource, IReadOnlyList<ShaderFile> shaders)
        {
            var sb = new StringBuilder();
            sb.Append(csharpSource ?? "");
            if (shaders == null || shaders.Count == 0)
                return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));

            var ordered = new List<ShaderFile>(shaders);
            ordered.Sort((a, b) => string.Compare(a?.Name, b?.Name, StringComparison.Ordinal));
            for (int i = 0; i < ordered.Count; i++)
            {
                ShaderFile s = ordered[i];
                if (s == null)
                    continue;
                sb.Append('\0');
                sb.Append(s.Name ?? "");
                sb.Append('\0');
                sb.Append(s.Source ?? "");
            }
            return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        static string ToHex(byte[] hash) => Convert.ToHexString(hash);
    }
}
