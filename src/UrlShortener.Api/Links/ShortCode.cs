using System.Security.Cryptography;

namespace UrlShortener.Api.Links;

/// <summary>Produces the code for a link; a delegate so integration tests can force a collision.</summary>
public delegate string GenerateShortCode();

public static class ShortCode
{
    public const int Length = 7;

    // Base62: URL-safe without escaping. 62^7 ≈ 3.5e12 codes. Case matters, hence the CS collation on the column.
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Cryptographically random (unguessable codes); GetString picks each character without modulo bias.</summary>
    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}
