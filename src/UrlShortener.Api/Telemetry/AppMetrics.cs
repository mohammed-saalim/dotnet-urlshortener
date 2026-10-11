using System.Diagnostics.Metrics;
using UrlShortener.Api.Analytics;

namespace UrlShortener.Api.Telemetry;

/// <summary>
/// The app's own operational metrics (issue #28), separate from the click analytics stored in the database (ADR-0004).
/// The meter comes from <see cref="IMeterFactory"/>, so each host (and each test host) has its own instance and the
/// factory disposes it. Rate-limit rejections need no instrument here: ASP.NET Core's built-in
/// <c>aspnetcore.rate_limiting.requests</c> already counts them by policy and result.
/// </summary>
public sealed class AppMetrics
{
    /// <summary>Meter name; the exporter is told to collect it in <c>Program.cs</c>.</summary>
    public const string MeterName = "UrlShortener";

    /// <summary>Creates the meter and its instruments; resolved once at startup (Program.cs) so they exist before the first export.</summary>
    public AppMetrics(IMeterFactory meterFactory, ClickQueue clickQueue)
    {
        var meter = meterFactory.Create(MeterName);
        // Observable: reads the counter ClickQueue already keeps, so a drop is counted in exactly one place.
        meter.CreateObservableCounter(
            "urlshortener.analytics.dropped_clicks",
            () => clickQueue.Dropped,
            unit: "{click}",
            description: "Clicks dropped because the click queue was full or already completed.");
    }
}
