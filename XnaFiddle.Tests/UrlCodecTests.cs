using XnaFiddle;

namespace XnaFiddle.Tests;

public class UrlCodecTests
{
    // ── Encode / Decode round-trips ──────────────────────────────────────────

    [Fact]
    public void RoundTrip_ShortString()
    {
        const string original = "Hello, World!";
        Assert.Equal(original, UrlCodec.Decode(UrlCodec.Encode(original)));
    }

    [Fact]
    public void RoundTrip_EmptyString()
    {
        Assert.Equal("", UrlCodec.Decode(UrlCodec.Encode("")));
    }

    [Fact]
    public void RoundTrip_TypicalCSharpCode()
    {
        const string code = """
            using Microsoft.Xna.Framework;

            public class MyGame : Game
            {
                protected override void Draw(GameTime gt)
                {
                    GraphicsDevice.Clear(Color.CornflowerBlue);
                }
            }
            """;
        Assert.Equal(code, UrlCodec.Decode(UrlCodec.Encode(code)));
    }

    [Fact]
    public void RoundTrip_Unicode()
    {
        const string original = "// 日本語コメント\nvar x = \"émoji: 🎮\";";
        Assert.Equal(original, UrlCodec.Decode(UrlCodec.Encode(original)));
    }

    // ── Encoded output is URL-safe ────────────────────────────────────────────

    [Fact]
    public void Encode_ProducesNoBase64Padding()
    {
        string encoded = UrlCodec.Encode("test");
        Assert.DoesNotContain("=", encoded);
    }

    [Fact]
    public void Encode_ProducesUrlSafeChars()
    {
        // Encode a large string to ensure + and / would appear in raw Base64
        string encoded = UrlCodec.Encode(new string('A', 1000));
        Assert.DoesNotContain("+", encoded);
        Assert.DoesNotContain("/", encoded);
    }

    // ── ParseQueryParam ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("?example=ColorCycle", "example", "ColorCycle")]
    [InlineData("?foo=1&example=Bounce&bar=2", "example", "Bounce")]
    [InlineData("example=NoLeadingQuestion", "example", "NoLeadingQuestion")]
    public void ParseQueryParam_ReturnsValue(string search, string key, string expected)
    {
        Assert.Equal(expected, UrlCodec.ParseQueryParam(search, key));
    }

    [Theory]
    [InlineData("?other=1", "example")]
    [InlineData("", "example")]
    [InlineData(null, "example")]
    public void ParseQueryParam_ReturnsNull_WhenKeyMissing(string search, string key)
    {
        Assert.Null(UrlCodec.ParseQueryParam(search, key));
    }

    [Fact]
    public void ParseQueryParam_DecodesPercentEncoding()
    {
        Assert.Equal("My Game", UrlCodec.ParseQueryParam("?example=My%20Game", "example"));
    }

    [Fact]
    public void ParseQueryParam_DoesNotMatchSubstring()
    {
        // "notexample=X" should not match key "example"
        Assert.Null(UrlCodec.ParseQueryParam("?notexample=X", "example"));
    }
}
