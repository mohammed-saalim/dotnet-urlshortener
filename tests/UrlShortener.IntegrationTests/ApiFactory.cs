using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Links;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// The real app (Development environment, so startup migration runs) against the test class's SQL Server container.
/// Created per test rather than as a class fixture, because it needs the container's connection string and,
/// for the retry tests, a scripted sequence of codes.
/// </summary>
public sealed class ApiFactory(string connectionString, params string[] codes) : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>How many scripted codes the app asked for; lets the retry tests assert the exact attempt count.</summary>
    public int CodesGenerated { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Host setting, so it is visible when Program.cs reads configuration (before the host is built).
        builder.UseSetting("ConnectionStrings:UrlShortener", connectionString);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));

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
