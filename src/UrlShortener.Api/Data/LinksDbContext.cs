using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Analytics;
using UrlShortener.Api.Links;

namespace UrlShortener.Api.Data;

public sealed class LinksDbContext(DbContextOptions<LinksDbContext> options) : DbContext(options)
{
    public DbSet<Link> Links => Set<Link>();

    public DbSet<Click> Clicks => Set<Click>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Link>(link =>
        {
            // Base62 codes are case-sensitive; SQL Server's default collation is not ("abc" would collide with "ABC").
            // Set at column level so the unique index itself is case-sensitive (see CLAUDE.md §9).
            link.Property(l => l.Code)
                .HasMaxLength(10)
                .IsUnicode(false)
                .UseCollation("Latin1_General_100_CS_AS");
            link.HasIndex(l => l.Code).IsUnique();

            link.Property(l => l.OriginalUrl).HasMaxLength(2048);
            link.Property(l => l.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<Click>(click =>
        {
            // No navigation properties, so the foreign key is declared here. Default delete behaviour: links are only
            // soft-deleted today.
            click.HasOne<Link>().WithMany().HasForeignKey(c => c.LinkId);
            click.HasIndex(c => new { c.LinkId, c.OccurredAt }); // per-link, per-day stats (B3)

            click.Property(c => c.ReferrerHost).HasMaxLength(Click.ReferrerHostMaxLength);
            click.Property(c => c.UserAgent).HasMaxLength(Click.UserAgentMaxLength);
        });
    }
}
