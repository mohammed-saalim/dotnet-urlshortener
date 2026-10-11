using System.Net;
using System.Net.Sockets;

namespace UrlShortener.Api.Links;

/// <summary>
/// Hand-written rather than DataAnnotations + AddValidation(): every rule here (scheme, userinfo, host, reserved words,
/// "in the future" against an injected clock) would need a custom attribute anyway, and a pure function is testable
/// without a host. Keys are the JSON property names. Messages never echo the submitted value. The destination rules
/// (ADR-0007) look at the URL only: no DNS lookup, so a name that resolves to a private address is accepted.
/// </summary>
public static class CreateLinkValidator
{
    public const int MaxUrlLength = 2048;
    public const int MinAliasLength = 4;
    public const int MaxAliasLength = 10;

    // Top-level paths the app serves (api, health, openapi, scalar) or plausibly will (ADR-0007): an admin UI, sign-in,
    // docs, static files, a status page, metrics. "api" is unreachable since aliases are 4+ characters; kept as a
    // record. A dotted path (robots.txt, favicon.ico) never matches the redirect route, so it needs no entry.
    private static readonly HashSet<string> ReservedAliases = new(
        ["api", "health", "openapi", "scalar", "admin", "docs", "login", "logout", "static", "assets", "status",
            "metrics", "swagger"],
        StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string[]> Validate(CreateLinkRequest request, DateTimeOffset now, string publicHost)
    {
        var errors = new Dictionary<string, string[]>();

        if (UrlError(request.Url, publicHost) is { } urlError)
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

    private static string? UrlError(string? url, string publicHost)
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

        return HostError(uri, publicHost);
    }

    // ADR-0007 threats 2 and 3. Uri normalises shorthand IPv4 (2130706433, 0x7f.1, 127.1) to a dotted quad, so those
    // arrive here as IPv4 (pinned by a unit test). A trailing dot names the same host, so it is trimmed before comparing.
    private static string? HostError(Uri uri, string publicHost)
    {
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            // IdnHost has no brackets around IPv6. A literal that does not parse is rejected rather than let through.
            return !IPAddress.TryParse(uri.IdnHost, out var address) || IsNonPublic(address)
                ? "Must not point to a loopback, private, link-local or unspecified address."
                : null;
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "Must not point to localhost.";
        }

        // Host only, any port or scheme: another port on our host is still us, and a loop is never a valid link.
        if (host.Equals(publicHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
        {
            return "Must not point to this service.";
        }

        return null;
    }

    private static bool IsNonPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] is 0 or 10 or 127 // unspecified 0/8, private 10/8, loopback 127/8
                || (b[0] == 169 && b[1] == 254) // link-local, including the cloud metadata address
                || (b[0] == 172 && b[1] is >= 16 and <= 31) // private 172.16/12
                || (b[0] == 192 && b[1] == 168); // private 192.168/16
        }

        return IPAddress.IsLoopback(address) // ::1
            || address.Equals(IPAddress.IPv6Any) // ::
            || address.IsIPv6LinkLocal // fe80::/10
            || address.IsIPv6UniqueLocal; // fc00::/7
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
