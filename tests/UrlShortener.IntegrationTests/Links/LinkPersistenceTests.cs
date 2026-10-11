using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests.Links;

// Each test uses its own codes: the database is shared across the class and test order is not guaranteed.
public sealed class LinkPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task CodesDifferingOnlyByCaseAreDistinctRows()
    {
        await using (var db = fixture.CreateDbContext())
        {
            db.Links.Add(NewLink("abc"));
            db.Links.Add(NewLink("ABC"));
            await db.SaveChangesAsync();
        }

        await using var verify = fixture.CreateDbContext();
        var codes = await verify.Links
            .Where(l => l.Code == "abc" || l.Code == "ABC")
            .Select(l => l.Code)
            .ToListAsync();
        Assert.Equal(["ABC", "abc"], codes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProductionMigratesAtStartupWhenSwitchedOn()
    {
        // ADR-0006: the Azure deployment runs as Production with Database:MigrateOnStartup=true. A database that does
        // not exist yet on the class's container, so the app itself must create it and apply every migration.
        var connectionString = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = "MigrateOnStartup" }.ConnectionString;
        using var factory = new ApiFactory(connectionString)
        {
            EnvironmentName = "Production",
            Settings = { ["Database:MigrateOnStartup"] = "true" },
        };

        using var client = factory.CreateClient(); // builds the host, which migrates before it listens

        await using var db = new LinksDbContext(new DbContextOptionsBuilder<LinksDbContext>().UseSqlServer(connectionString).Options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task DuplicateCodeIsRejectedByUniqueIndex()
    {
        await using (var db = fixture.CreateDbContext())
        {
            db.Links.Add(NewLink("dup"));
            await db.SaveChangesAsync();
        }

        await using var second = fixture.CreateDbContext();
        second.Links.Add(NewLink("dup"));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        var sql = Assert.IsType<SqlException>(ex.InnerException);
        Assert.True(sql.Number is 2601 or 2627, $"Expected unique violation 2601/2627, got {sql.Number}");
    }

    [Fact]
    public async Task CodeColumnUsesCaseSensitiveCollation()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Links' AND COLUMN_NAME = 'Code'",
            connection);

        var collation = await command.ExecuteScalarAsync();

        Assert.Equal("Latin1_General_100_CS_AS", collation);
    }

    private static Link NewLink(string code) => new()
    {
        Code = code,
        OriginalUrl = "https://example.com/",
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
