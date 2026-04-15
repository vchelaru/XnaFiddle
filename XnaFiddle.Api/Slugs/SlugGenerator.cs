using System.Security.Cryptography;

namespace XnaFiddle.Api.Slugs;

public class SlugGenerator : ISlugGenerator
{
    private const string Alphabet =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Length = 7;

    public string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}
