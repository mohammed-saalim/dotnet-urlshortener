using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;

namespace UrlShortener.Api.Links;

public static class DeleteLinkEndpoint
{
    public static async Task<Results<NoContent, NotFound>> HandleAsync(
        string code,
        LinksDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Soft delete in one UPDATE: of two concurrent deletes exactly one sees a row, so a repeat is always 404.
        var deleted = await db.Links
            .Where(l => l.Code == code && l.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.DeletedAt, now), cancellationToken);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}
