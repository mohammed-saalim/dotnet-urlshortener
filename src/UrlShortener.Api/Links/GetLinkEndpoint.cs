using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;

namespace UrlShortener.Api.Links;

public static class GetLinkEndpoint
{
    public static async Task<Results<Ok<LinkResponse>, NotFound>> HandleAsync(
        string code,
        LinksDbContext db,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        var link = await db.Links
            .AsNoTracking()
            .SingleOrDefaultAsync(l => l.Code == code && l.DeletedAt == null, cancellationToken);

        if (link is null)
        {
            return TypedResults.NotFound(); // body written by UseStatusCodePages as ProblemDetails
        }

        // Same shortUrl construction as CreateLinkEndpoint; #9 replaces both with a configured public base URL.
        return TypedResults.Ok(new LinkResponse(
            link.Code,
            $"{httpRequest.Scheme}://{httpRequest.Host}/{link.Code}",
            link.OriginalUrl,
            link.CreatedAt,
            link.ExpiresAt));
    }
}
