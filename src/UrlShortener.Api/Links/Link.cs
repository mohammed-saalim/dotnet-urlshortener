namespace UrlShortener.Api.Links;

public sealed class Link
{
    public long Id { get; set; }

    public required string Code { get; set; }

    public required string OriginalUrl { get; set; }

    public bool IsCustomAlias { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
