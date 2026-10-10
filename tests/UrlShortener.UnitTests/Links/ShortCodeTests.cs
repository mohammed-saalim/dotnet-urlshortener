using UrlShortener.Api.Links;

namespace UrlShortener.UnitTests.Links;

public sealed class ShortCodeTests
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void GeneratedCodeIsSevenBase62Characters()
    {
        var code = ShortCode.Generate();

        Assert.Equal(7, code.Length);
        Assert.All(code, c => Assert.Contains(c, Base62));
    }

    [Fact]
    public void ThousandGeneratedCodesAreDistinct()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => ShortCode.Generate()).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryAlphabetCharacterAppearsAcrossManyCodes()
    {
        // Distribution sanity: 5000 codes = 35000 characters, ~565 expected per character.
        // A biased or truncated alphabet (e.g. digits only, or no lowercase) fails; the chance of a fair
        // generator missing any one character is negligible (62 * (61/62)^35000).
        var seen = new HashSet<char>(Enumerable.Range(0, 5000).SelectMany(_ => ShortCode.Generate()));

        Assert.Equal(Base62.Length, seen.Count);
        Assert.All(Base62, c => Assert.Contains(c, seen));
    }
}
