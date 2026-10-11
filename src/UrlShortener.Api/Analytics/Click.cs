namespace UrlShortener.Api.Analytics;

/// <summary>One redirect. No client IP is stored (ADR-0004).</summary>
public sealed class Click
{
    // The redirect truncates or nulls longer values before enqueueing, so a hostile header cannot fail a batch.
    public const int ReferrerHostMaxLength = 253; // the longest DNS name
    public const int UserAgentMaxLength = 256;

    public long Id { get; set; }

    public long LinkId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? ReferrerHost { get; set; }

    public string? UserAgent { get; set; }
}
