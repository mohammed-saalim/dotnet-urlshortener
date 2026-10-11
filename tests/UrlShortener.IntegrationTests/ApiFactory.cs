using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UrlShortener.Api.Analytics;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// The real app (Development environment by default, so startup migration runs) against the test class's SQL Server container.
/// Created per test rather than as a class fixture, because it needs the container's connection string and,
/// for the retry tests, a scripted sequence of codes.
/// </summary>
public sealed class ApiFactory(string connectionString, params string[] codes) : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>How many scripted codes the app asked for; lets the retry tests assert the exact attempt count.</summary>
    public int CodesGenerated { get; private set; }

    /// <summary>"Production" covers the production error pipeline and skips startup migration (no database needed).</summary>
    public string EnvironmentName { get; init; } = "Development";

    /// <summary>Replaces the click queue with one of this capacity; null keeps the app's default.</summary>
    public int? ClickQueueCapacity { get; init; }

    /// <summary>False removes the click writer, so queued clicks stay queued (queue-full test).</summary>
    public bool ClickWriterEnabled { get; init; } = true;

    /// <summary>Extra configuration values (e.g. <c>RateLimiting:PermitLimit</c>), applied as host settings.</summary>
    public IDictionary<string, string?> Settings { get; init; } = new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Host setting, so it is visible when Program.cs reads configuration (before the host is built).
        builder.UseSetting("ConnectionStrings:UrlShortener", connectionString);
        builder.UseEnvironment(EnvironmentName);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));

            if (ClickQueueCapacity is int capacity)
            {
                services.RemoveAll<ClickQueue>();
                services.AddSingleton(new ClickQueue(capacity));
            }

            if (!ClickWriterEnabled)
            {
                // Only the writer: the collection also holds the hosted service that runs the web server.
                services.Remove(services.Single(d => d.ImplementationType == typeof(ClickWriterService)));
            }

            if (codes.Length > 0)
            {
                var queue = new Queue<string>(codes);
                services.AddSingleton(new GenerateShortCode(() =>
                {
                    CodesGenerated++;
                    return queue.Dequeue();
                }));
            }
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
