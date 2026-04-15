using XnaFiddle.Api.Slugs;

namespace XnaFiddle.Api.Tests;

public class SlugGeneratorTests
{
    private const string Base62Alphabet =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private readonly SlugGenerator _sut = new();

    [Fact]
    public void Generate_ReturnsSevenCharacterString()
    {
        _sut.Generate().Length.ShouldBe(7);
    }

    [Fact]
    public void Generate_OnlyContainsBase62Characters()
    {
        for (var i = 0; i < 100; i++)
        {
            var slug = _sut.Generate();
            slug.ShouldAllBe(c => Base62Alphabet.Contains(c));
        }
    }

    [Fact]
    public void Generate_ProducesDistinctValuesAcrossManyCalls()
    {
        var slugs = Enumerable.Range(0, 1_000).Select(_ => _sut.Generate()).ToList();
        slugs.Distinct().Count().ShouldBe(slugs.Count);
    }
}
