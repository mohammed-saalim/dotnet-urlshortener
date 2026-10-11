using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.RateLimiting;

/// <summary>Fixed-window limit on <c>POST /api/links</c>, per client IP (ADR-0005). Section <c>RateLimiting</c>.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public const string CreatePolicy = "create";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 10;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}
