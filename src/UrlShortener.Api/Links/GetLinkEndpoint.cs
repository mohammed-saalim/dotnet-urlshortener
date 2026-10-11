using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Data;
using UrlShortener.Api.PublicUrl;

namespace UrlShortener.Api.Links;

public static class GetLinkEndpoint
{
    public static async Task<Results<Ok<LinkResponse>, NotFound>> HandleAsync(
        string code,
        LinksDbContext db,
        IOptions<PublicUrlOptions> publicUrl,
        CancellationToken cancellationToken)
    {
        var link = await db.Links
            .AsNoTracking()
            .SingleOrDefaultAsync(l => l.Code == code && l.DeletedAt == null, cancellationToken);

        if (link is null)
        {
            return TypedResults.NotFound(); // body written by UseStatusCodePages as ProblemDetails
        }

        // shortUrl from the configured public base URL, not the client-controlled Host header (#9).
        return TypedResults.Ok(new LinkResponse(
            link.Code,
            publicUrl.Value.ShortUrl(link.Code),
            link.OriginalUrl,
            link.CreatedAt,
            link.ExpiresAt));
    }
}
