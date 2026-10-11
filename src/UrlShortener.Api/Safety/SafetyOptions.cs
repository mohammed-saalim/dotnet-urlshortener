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
}
