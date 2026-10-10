using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using UrlShortener.Api.Data;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// One real SQL Server container per test class, with the app's migrations applied.
/// Real SQL Server (not SQLite or InMemory) because collation and unique-index behaviour are what is under test.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>Connection string to a dedicated database (the container's default points at master).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public LinksDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LinksDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "UrlShortener",
        }.ConnectionString;

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
