using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace UrlShortener.IntegrationTests.Health;

public sealed class HealthEndpointsTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    // Valid syntax, nothing listening on port 1: any attempt to use the database would fail.
    private const string UnreachableDatabase =
        "Server=127.0.0.1,1;Database=Nowhere;User Id=nobody;Password=unused;TrustServerCertificate=True;Connect Timeout=1";

    [Fact]
    public async Task LiveDoesNotTouchTheDatabase()
    {
        // Production: startup migration is skipped, so the app starts without a reachable database.
        using var factory = new ApiFactory(UnreachableDatabase) { EnvironmentName = "Production" };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadyIsHealthyWhenDatabaseIsReachable()
    {
        using var factory = new ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void NoExporterWithoutConnectionString()
    {
        // No APPLICATIONINSIGHTS_CONNECTION_STRING: the OpenTelemetry SDK is not registered at all (issue #28). Without
        // the gate in Program.cs, resolving a provider would throw "A connection string was not found".
        using var factory = new ApiFactory(UnreachableDatabase) { EnvironmentName = "Production" };

        Assert.Null(factory.Services.GetService<TracerProvider>());
        Assert.Null(factory.Services.GetService<MeterProvider>());
    }
}
