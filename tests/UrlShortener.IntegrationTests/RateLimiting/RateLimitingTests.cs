using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Links;
using UrlShortener.Api.RateLimiting;

namespace UrlShortener.IntegrationTests.RateLimiting;

// Each test has its own factory, so its own limiter state. Links are created through the API (generated codes), so
// tests never share rows in the class's database.
public sealed class RateLimitingTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task CreateOverLimitIsTooManyRequestsWithRetryAfter()
    {
        using var factory = CreateFactory(permitLimit: 2);
        using var client = factory.CreateClient();

        var first = await CreateAsync(client);
        var second = await CreateAsync(client);
        var third = await CreateAsync(client);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        var retryAfter = third.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        var window = factory.Services.GetRequiredService<IOptions<RateLimitOptions>>().Value.WindowSeconds;
        Assert.InRange(retryAfter.Value.TotalSeconds, 1, window); // the configured window, not a literal
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        var problem = await third.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(429, problem?.Status);
        Assert.Equal("Too many requests", problem?.Title);
    }

    [Fact]
    public async Task RedirectIsNotRateLimited()
    {
        using var factory = CreateFactory(permitLimit: 1);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var link = await (await CreateAsync(client)).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(link);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateAsync(client)).StatusCode); // create permit used up

        var firstRedirect = await client.GetAsync($"/{link.Code}");
        var secondRedirect = await client.GetAsync($"/{link.Code}");

        Assert.Equal(HttpStatusCode.Found, firstRedirect.StatusCode);
        Assert.Equal(HttpStatusCode.Found, secondRedirect.StatusCode);
    }

    [Fact]
    public async Task GetAndDeleteAreNotRateLimited()
    {
        using var factory = CreateFactory(permitLimit: 1);
        using var client = factory.CreateClient();
        var link = await (await CreateAsync(client)).Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(link);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateAsync(client)).StatusCode); // create permit used up

        var get = await client.GetAsync($"/api/links/{link.Code}");
        var delete = await client.DeleteAsync($"/api/links/{link.Code}");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task RateLimitRejectionIsCounted()
    {
        using var factory = CreateFactory(permitLimit: 1);
        using var client = factory.CreateClient();
        // Built-in ASP.NET Core meter (exported in Azure, issue #28). MeterListener is process-wide and test classes
        // run in parallel, so only instruments whose meter came from this host's IMeterFactory are listened to.
        var meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        var measurements = new ConcurrentQueue<(long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, meterFactory)
                    && instrument.Meter.Name == "Microsoft.AspNetCore.RateLimiting"
                    && instrument.Name == "aspnetcore.rate_limiting.requests")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Enqueue((value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();

        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateAsync(client)).StatusCode);

        var rejected = Assert.Single(measurements, m => !Equals(m.Tags["aspnetcore.rate_limiting.result"], "acquired"));
        Assert.Equal(1, rejected.Value);
        Assert.Equal("endpoint_limiter", rejected.Tags["aspnetcore.rate_limiting.result"]);
        Assert.Equal(RateLimitOptions.CreatePolicy, rejected.Tags["aspnetcore.rate_limiting.policy"]);
    }

    [Fact]
    public void OptionsAreValidatedOnStart()
    {
        // Production: no startup migration, so this test needs no database round trip.
        using var factory = CreateFactory(permitLimit: 0, environment: "Production");

        var ex = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("PermitLimit", ex.Message, StringComparison.Ordinal);
    }

    private ApiFactory CreateFactory(int permitLimit, string environment = "Development") => new(fixture.ConnectionString)
    {
        EnvironmentName = environment,
        Settings = { ["RateLimiting:PermitLimit"] = permitLimit.ToString(CultureInfo.InvariantCulture) },
    };

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/links", new { url = "https://example.com/" });
}
