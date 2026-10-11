namespace UrlShortener.Api.Safety;

/// <summary>Destination safety settings (ADR-0007). Section <c>Safety</c>.</summary>
public sealed class SafetyOptions
{
    public const string SectionName = "Safety";

    /// <summary>
    /// Hosts no new link may point to; each entry also denies its subdomains. Case-insensitive. Empty by default.
    /// Checked at create time only: an entry added later does not block existing links.
    /// </summary>
    public string[] DeniedHosts { get; set; } = [];

    public const string DeniedHostsMessage =
        "Safety:DeniedHosts entries must be plain ASCII host names (no scheme, port, path or wildcard).";

    // ASCII only: destinations are compared by IdnHost (punycode), so a Unicode entry would never match.
    public static bool IsValidHost(string? host) =>
        !string.IsNullOrWhiteSpace(host)
        && host.All(char.IsAscii)
        && Uri.CheckHostName(host.TrimEnd('.')) is UriHostNameType.Dns or UriHostNameType.IPv4;
}
