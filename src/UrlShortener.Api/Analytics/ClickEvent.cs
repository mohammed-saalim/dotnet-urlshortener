namespace UrlShortener.Api.Analytics;

/// <summary>What the redirect hands to <see cref="ClickQueue"/>; values are already length-limited.</summary>
public sealed record ClickEvent(long LinkId, DateTimeOffset OccurredAt, string? ReferrerHost, string? UserAgent);
