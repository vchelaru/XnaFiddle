using System;

namespace XnaFiddle
{
    public static class GitHubUrlNormalizer
    {
        /// <summary>
        /// Rewrites github.com /blob/ and /raw/ URLs to raw.githubusercontent.com so the
        /// browser fetch is CORS-allowed and returns file bytes instead of an HTML page.
        /// Returns the input unchanged when no rewrite applies.
        /// </summary>
        public static string Normalize(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return url;

            string[] segs = uri.AbsolutePath.Trim('/').Split('/');
            if (segs.Length < 5) return url;
            if (segs[2] != "blob" && segs[2] != "raw") return url;

            string user = segs[0];
            string repo = segs[1];
            string rest = string.Join("/", segs, 3, segs.Length - 3);
            return $"https://raw.githubusercontent.com/{user}/{repo}/{rest}";
        }
    }
}
