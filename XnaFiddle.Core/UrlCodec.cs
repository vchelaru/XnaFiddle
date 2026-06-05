using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace XnaFiddle
{
    public static class UrlCodec
    {
        /// <summary>
        /// GZip-compresses and Base64url-encodes a string for use in a URL fragment.
        /// </summary>
        public static string Encode(string code)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(code);
                gzip.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToBase64String(output.ToArray())
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// Decodes a Base64url + GZip string produced by <see cref="Encode"/>.
        /// </summary>
        public static string Decode(string encoded)
        {
            string base64 = encoded.Replace('-', '+').Replace('_', '/')
                + new string('=', (4 - encoded.Length % 4) % 4);
            byte[] compressed = Convert.FromBase64String(base64);

            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Extracts a single named value from a URL query string (e.g. "?foo=bar&amp;baz=1").
        /// Returns null if the key is not found.
        /// </summary>
        public static string ParseQueryParam(string search, string key)
        {
            if (string.IsNullOrEmpty(search) || !search.Contains(key + "="))
                return null;
            string s = search.StartsWith("?") ? search.Substring(1) : search;
            foreach (var part in s.Split('&'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0] == key)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }
    }
}
