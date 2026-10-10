namespace UrlShortener.Api.Links;

/// <summary>
/// Hand-written rather than DataAnnotations + AddValidation(): every rule here (scheme, userinfo, host, reserved words,
/// "in the future" against an injected clock) would need a custom attribute anyway, and a pure function is testable
/// without a host. Keys are the JSON property names. Messages never echo the submitted value.
/// </summary>
public static class CreateLinkValidator
{
    public const int MaxUrlLength = 2048;
    public const int MinAliasLength = 4;
    public const int MaxAliasLength = 10;

    // Paths the app serves itself or may serve later. Minimal on purpose; the ambiguous scenario (Day 3) expands it.
    private static readonly HashSet<string> ReservedAliases =
        new(["api", "health", "openapi", "scalar", "admin"], StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string[]> Validate(CreateLinkRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();

        if (UrlError(request.Url) is { } urlError)
        {
            errors["url"] = [urlError];
        }

        if (request.CustomAlias is not null && AliasError(request.CustomAlias) is { } aliasError)
        {
            errors["customAlias"] = [aliasError];
        }

        if (request.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            errors["expiresAt"] = ["Must be in the future."];
        }

        return errors;
    }

    private static string? UrlError(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Required.";
        }

        // Checked on the raw string, which is what gets stored (column is nvarchar(2048)).
        if (url.Length > MaxUrlLength)
        {
            return $"Must be at most {MaxUrlLength} characters.";
        }

        // The raw string is what is stored and later sent as the redirect Location header, and Kestrel only writes
        // header-safe (printable ASCII) values. Uri.TryCreate would silently trim or escape spaces, CR/LF and non-ASCII,
        // so the check is on the raw string. Clients percent-encode paths and use punycode for international hosts.
        if (url.Any(c => c is < '!' or > '~'))
        {
            return "Must contain only printable ASCII characters; percent-encode anything else.";
        }

        // Allow-list of schemes: rejects javascript:, data:, file:, ftp: and, on Linux, "/path" (parsed as file://).
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return "Must be an absolute http or https URL.";
        }

        // Credentials in a shared link leak to everyone who clicks it, and enable "https://trusted.com@evil.com" spoofing.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "Must not contain a username or password.";
        }

        return null;
    }

    private static string? AliasError(string alias)
    {
        if (alias.Length is < MinAliasLength or > MaxAliasLength)
        {
            return $"Must be {MinAliasLength} to {MaxAliasLength} characters.";
        }

        if (!alias.All(char.IsAsciiLetterOrDigit))
        {
            return "Must contain only letters A-Z, a-z and digits 0-9.";
        }

        if (ReservedAliases.Contains(alias))
        {
            return "Is reserved.";
        }

        return null;
    }
}
