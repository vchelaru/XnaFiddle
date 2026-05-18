using XnaFiddle;

namespace XnaFiddle.Tests;

public class GitHubUrlNormalizerTests
{
    // ── Rewrites ──────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_RewritesBlobUrl()
    {
        const string input  = "https://github.com/AristurtleDev/xna-fiddle-resources/blob/develop/fonts/JetBrainsMono-Regular.xnb";
        const string expect = "https://raw.githubusercontent.com/AristurtleDev/xna-fiddle-resources/develop/fonts/JetBrainsMono-Regular.xnb";
        Assert.Equal(expect, GitHubUrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_RewritesRawUrl()
    {
        const string input  = "https://github.com/user/repo/raw/main/path/to/file.png";
        const string expect = "https://raw.githubusercontent.com/user/repo/main/path/to/file.png";
        Assert.Equal(expect, GitHubUrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_HostIsCaseInsensitive()
    {
        const string input  = "https://GitHub.com/user/repo/blob/main/file.txt";
        const string expect = "https://raw.githubusercontent.com/user/repo/main/file.txt";
        Assert.Equal(expect, GitHubUrlNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_HandlesDeepPaths()
    {
        const string input  = "https://github.com/u/r/blob/main/a/b/c/d/e/f.txt";
        const string expect = "https://raw.githubusercontent.com/u/r/main/a/b/c/d/e/f.txt";
        Assert.Equal(expect, GitHubUrlNormalizer.Normalize(input));
    }

    // ── Pass-through (no rewrite) ─────────────────────────────────────────────

    [Fact]
    public void Normalize_LeavesRawGitHubUserContentUnchanged()
    {
        const string url = "https://raw.githubusercontent.com/user/repo/main/file.txt";
        Assert.Equal(url, GitHubUrlNormalizer.Normalize(url));
    }

    [Fact]
    public void Normalize_LeavesNonGitHubHostUnchanged()
    {
        const string url = "https://example.com/user/repo/blob/main/file.txt";
        Assert.Equal(url, GitHubUrlNormalizer.Normalize(url));
    }

    [Theory]
    [InlineData("https://github.com/user/repo")]
    [InlineData("https://github.com/user/repo/releases/tag/v1.0")]
    [InlineData("https://github.com/user/repo/tree/main/src")]
    [InlineData("https://github.com/user/repo/pull/42")]
    public void Normalize_LeavesNonBlobNonRawGitHubUrlsUnchanged(string url)
    {
        Assert.Equal(url, GitHubUrlNormalizer.Normalize(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/user/repo/blob/main/file.txt")] // relative — TryCreate Absolute fails
    public void Normalize_PassesThroughInvalidOrRelativeInputs(string? input)
    {
        Assert.Equal(input, GitHubUrlNormalizer.Normalize(input!));
    }
}
