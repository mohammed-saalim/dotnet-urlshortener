# AI log 014 — Operational telemetry to Azure Monitor (OpenTelemetry)

| Field | Value |
|---|---|
| Issue | #28 (B7) |
| Branch | `feat/28-telemetry` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | low per the issue; the Bicep part (new resource, secret on the container app) is infra, so high-impact sign-off rules apply to that file |

## Intent
Operational telemetry (requests, failures, dependencies, two custom metrics) exported to Application Insights through the Azure Monitor OpenTelemetry distro, separate from the product analytics in ADR-0004, with local runs and tests exporting nothing.

## Constraints given to the AI
- One new package, `Azure.Monitor.OpenTelemetry.AspNetCore`, verified on nuget.org; nothing else (tests use the BCL `MeterListener`).
- Exporter opt-in: registered only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is non-empty. Meter registration independent of the exporter.
- Two custom metrics on one `Meter` named `UrlShortener`, created through `IMeterFactory`; the dropped-clicks one reads `ClickQueue.Dropped`.
- No raw URLs or client IPs in telemetry; verify request URLs, IP masking, SQL statement capture, default sampling, daily cap.
- Bicep: workspace-based Application Insights on the existing workspace; connection string through a Container Apps secret. Entra-only ingestion evaluated, implemented only if small and with no new package.
- Integration tests in existing classes (no extra SQL Server container); red first for the two metric tests.
- No new ADR (append to ADR-0006). Do not touch `docs/HANDOFF.md`, `.claude/settings.json`, migrations. No commit/push/PR; no `azd up`/`provision`/`deploy`; no `.env`/secrets/`.azure/`.

## Acceptance criteria (runnable)
- Local run without a connection string: app starts, no exporter errors → `dotnet run` check below, `NoExporterWithoutConnectionString`
- After deploy: a redirect shows up in App Insights requests; the two custom metrics are visible → **not verifiable here** (nothing is deployed); what to look for is in "Deferred to the engineer"
- `docs/SETUP.md` says where to look → "Telemetry" paragraph
- Metrics behave → `RateLimitRejectionIsCounted` (built-in rate-limit metric), `DroppedClicksAreObservable` (custom)

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #28 per the brief (decisions 1–8: package, opt-in, two custom metrics `urlshortener.clicks.dropped` and `urlshortener.ratelimit.rejected`, PII/cost checks, Bicep, three tests, docs), argue disagreements in this log but implement.
2. Subagent → coordinator (heads-up after research): OTel naming rule against pluralized namespaces; ASP.NET Core already emits `aspnetcore.rate_limiting.requests`; distro disables `url.query` redaction.
3. Coordinator → subagent: (1) naming change accepted; (3) redaction re-enable accepted as one documented value; (2) **change of decision**: drop the custom rate-limit counter, use the built-in meter via `AddMeter`, no code in `OnRejected` ("CLAUDE.md §2 wins over my brief"); test it with a `MeterListener` scoped to the factory's host; show red or explain and mutate.

## Package verification (nuget.org, 2026-10-10)

| Field | Value |
|---|---|
| Id | `Azure.Monitor.OpenTelemetry.AspNetCore` |
| Version pinned | **1.6.0** (latest stable; published 2026-07-27) |
| Owners | `azure-sdk`, `Microsoft` (verified prefix: yes) |
| Total downloads | 50,129,790 |
| Repo | <https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore> |
| Target frameworks | `net10.0`, `net8.0`, `netstandard2.0` |
| Direct dependencies | `Azure.Core >= 1.60.0`, `Azure.Monitor.OpenTelemetry.Exporter >= 1.8.3`, `OpenTelemetry.Extensions.Hosting >= 1.15.3`, `OpenTelemetry.Instrumentation.AspNetCore >= 1.15.2`, `OpenTelemetry.Instrumentation.Http >= 1.15.1`. The SqlClient instrumentation is vendored (source-included), not a dependency. **`Azure.Identity` is not a dependency.** |

Source: `https://azuresearch-usnc.nuget.org/query?q=packageid:Azure.Monitor.OpenTelemetry.AspNetCore` and the 1.6.0 registration/catalog entry on `api.nuget.org`.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `Directory.Packages.props`, `src/UrlShortener.Api/UrlShortener.Api.csproj` | edited | Pin `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 with the usual "verified on nuget.org" comment; reference from the Api only. No transitive pin needed (vulnerability scan clean) |
| `src/UrlShortener.Api/Telemetry/AppMetrics.cs` | generated | `MeterName = "UrlShortener"`; ctor `(IMeterFactory, ClickQueue)` creates the meter and the observable counter `urlshortener.analytics.dropped_clicks` (`{click}`), which reads `ClickQueue.Dropped`. Stores no fields: the factory owns and disposes the meter, and a `Meter` field would make the class `IDisposable` (CA1001) for nothing |
| `src/UrlShortener.Api/Program.cs` | edited | (1) `AddSingleton<AppMetrics>()`; (2) `AddOpenTelemetry().UseAzureMonitor().WithMetrics(m => m.AddMeter(AppMetrics.MeterName, "Microsoft.AspNetCore.RateLimiting"))` only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is non-empty; (3) `_ = app.Services.GetRequiredService<AppMetrics>();` right after `Build()`. Two `using`s. `OnRejected` unchanged |
| `infra/resources.bicep` | edited | `Microsoft.Insights/components@2020-02-02` (`appi-<token>`, kind/`Application_Type` web, `WorkspaceResourceId: logs.id`, `IngestionMode: LogAnalytics`, same tags); `configuration.secrets` with the connection string; env `APPLICATIONINSIGHTS_CONNECTION_STRING` via `secretRef`; env `OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION=false`. No new output |
| `tests/.../RateLimiting/RateLimitingTests.cs` | edited (new method only) | `RateLimitRejectionIsCounted` |
| `tests/.../Analytics/ClickAnalyticsTests.cs` | edited (new method only) | `DroppedClicksAreObservable` (own link `Click07`) |
| `tests/.../ApiFactory.cs` | edited (one line) | `UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "")`, same shape as the existing `PublicUrl:BaseUrl` line. Added after the experiment in Checks: with the variable set in the environment, `NoExporterWithoutConnectionString` failed (the exporter was registered in every test host); with the pin it passes. Tests export nothing regardless of the machine |
| `tests/.../Health/HealthEndpointsTests.cs` | edited (new method only) | `NoExporterWithoutConnectionString`: Production + unreachable DB (no container round trip); asserts no `TracerProvider` / `MeterProvider` in DI |
| `docs/SETUP.md` | edited | "What gets created" lists App Insights; "Logs" paragraph corrected for `azd monitor`; new "Telemetry" subsection (where to look, metric names, KQL for rejections, sampling, log duplication, cap) |
| `docs/TESTING.md` | edited | Three coverage rows extended; new limitation "telemetry export is not tested" |
| `docs/adr/0006-azure-hosting-and-migrations.md` | edited (appended) | "Telemetry (B7, #28)" section. One edit above it: the "More information" bullet "Follow-up: issue #28 … not part of this ADR" now points at the new section, since the append made it false |
| `CLAUDE.md` §9 | edited | `azd monitor` gotcha corrected; two new gotchas (distro disables query redaction; observable instrument needs its owner resolved) |
| (brief) custom `Counter<long>` `urlshortener.ratelimit.rejected` incremented in `OnRejected` with tag `policy` | **rejected (coordinator's change of decision)** | Brief said custom counter; built-in used: zero code, standard name, tags already include policy and result. Cost, found afterwards: Metrics Explorer stores custom metrics without dimensions by default, so rejections are not a one-click chart and need a `customMetrics` query (in SETUP). A rejections-only custom counter would have charted directly |
| (brief) names `urlshortener.clicks.dropped`, `urlshortener.ratelimit.rejected` | **changed** (accepted by the coordinator) | OTel naming: "Metric namespaces SHOULD NOT be pluralized"; a count is pluralized in the last segment when the unit is an annotation like `{click}` (cf. `kestrel.rejected_connections`, `aspnetcore.rate_limiting.requests`). `urlshortener.analytics.dropped_clicks` follows that; the rate-limit metric now has the built-in name |
| (brief) `.WithMetrics(m => m.AddMeter(...))` | kept | The README shows `ConfigureOpenTelemetryMeterProvider(... AddMeter)`. `WithMetrics` on the same `OpenTelemetryBuilder` configures the same single `MeterProvider` and keeps the opt-in in one block |
| (proposal) enable the OTel SDK in tests to assert exported data | rejected | Needs an exporter in tests (constraint: no telemetry in tests) or `Microsoft.Extensions.Diagnostics.Testing` (excluded). `MeterListener` reads the instruments; export is a deploy-time check |
| (proposal) Entra-only ingestion now | not implemented | See "Decisions" |
| (proposal) component daily cap (`pricingPlans`) | not added | The effective cap is already the workspace's 0.5 GB/day (minimum of both) |
| (proposal) `APPLICATIONINSIGHTS_NAME` output | not added | azd discovers the component by resource type (source below) |
| (proposal) set sampling explicitly | not added | Default is already rate-limited (5 traces/s) |
| (proposal) disable Live Metrics / standard metrics / SDK stats | not done | Live Metrics "incurs no additional billing" (option XML doc), with overhead only while the portal blade is open; SDK stats are exporter health counters. Not needed for cost or PII |

## Docs and source confirmed
All fetched on 2026-10-10/11. Source links are pinned to the release tags actually used.
- **Distro README 1.6.0** (<https://github.com/Azure/azure-sdk-for-net/blob/Azure.Monitor.OpenTelemetry.AspNetCore_1.6.0/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore/README.md>, same text on Learn: <https://learn.microsoft.com/dotnet/api/overview/azure/monitor.opentelemetry.aspnetcore-readme>): `UseAzureMonitor()` + `APPLICATIONINSIGHTS_CONNECTION_STRING`; vendored SqlClient instrumentation; meters `Microsoft.AspNetCore.Hosting` and `System.Net.Http` on .NET 8+; "rate-limited sampling by default, collecting up to 5.0 traces per second"; custom meter via `ConfigureOpenTelemetryMeterProvider(... AddMeter(...))`; `Credential` option for Entra auth.
- **Distro source 1.6.0**, `src/OpenTelemetryBuilderExtensions.cs`: reads `config["APPLICATIONINSIGHTS_CONNECTION_STRING"]` (any configuration source works, the same one the gate in `Program.cs` reads); adds only the Hosting and Http meters; **sets `OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION` (and the HttpClient equivalent) to `true` when the key is null** (comment: workaround for GHSA-vh2m-22xx-q94f). The override is absent in the 1.1.0 tag and present from **1.1.1** (2024-04-26, PR #43432, the OpenTelemetry 1.8.1 / CVE-2024-32028 update); the changelog does not mention it.
- **`AzureMonitorOptions.cs` 1.6.0**: `TracesPerSecond = 5.0`, `SamplingRatio = 1.0F` (ignored while `TracesPerSecond` is set), `EnableTraceBasedLogsSampler = true` ("only logs associated with sampled traces are exported. Logs without trace context are always exported"), `EnableLiveMetrics = true` ("incurs no additional billing").
- **Distro CHANGELOG**: 1.5.0 "Default Sampler Changed … to `RateLimitedSampler` with 5.0 traces per second"; 1.6.0 brings exporter 1.8.3 with "customer SDK stats enabled by default (opt out with `APPLICATIONINSIGHTS_SDKSTATS_DISABLED=true`)". SDK stats are the exporter's own success/drop counters ([SDK Stats](https://learn.microsoft.com/azure/azure-monitor/app/sdk-stats)), not request data, so they were left on.
- **Exporter source 1.8.3**, `src/Internals/AzureMonitorTransmitter.cs:79`: `throw new InvalidOperationException("A connection string was not found. Please set your connection string.")`. Confirmed empirically by mutation M3 below.
- **Vendored SqlClient instrumentation** (`src/Vendoring/OpenTelemetry.Instrumentation.SqlClient/SqlClientTraceInstrumentationOptions.cs`, `Implementation/SqlClientDiagnosticListener.cs`): `SetDbStatementForText` defaults to **false** and is the only path that tags text commands with `db.statement`. `SetDbStatementForStoredProcedure` defaults to true, but EF Core issues no stored procedures. `EnableConnectionLevelAttributes` is false (the data source is recorded as `peer.service`).
- **OTel ASP.NET Core instrumentation README** (<https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.AspNetCore/README.md>): default attributes include `url.path`, `url.query` ("values in the query component are replaced with the text `Redacted`"), `http.route`, `user_agent.original`; **no `client.address`**. Source at `Instrumentation.AspNetCore-1.15.2`: `AspNetCoreTraceInstrumentationOptions(IConfiguration)` reads the redaction key from `IConfiguration`, so the environment variable set in Bicep is honoured, and the distro leaves an existing key alone.
- **IP handling** (<https://learn.microsoft.com/azure/azure-monitor/app/ip-collection>): "By default, IP addresses are temporarily collected but not stored … `0.0.0.0` is written to the `client_IP` field"; storing them requires `DisableIpMasking: true`, which is not set.
- **Daily cap** (<https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap#application-insights>): "For workspace-based Application Insights resources, the effective daily cap is the minimum of the two settings." Billing (<https://learn.microsoft.com/azure/azure-monitor/app/create-workspace-resource>): "Data ingestion and retention for workspace-based Application Insights resources are billed through the Log Analytics workspace."
- **Custom metrics in App Insights** (<https://learn.microsoft.com/azure/azure-monitor/app/metrics-overview#metrics-preaggregation>): OTel metrics land in `customMetrics` and in Metrics Explorer under the namespace *azure.applicationinsights*; "the preaggregated version of the metric is stored by default with no dimensions"; dimensions are a paid opt-in.
- **ASP.NET Core rate-limiting metrics** (<https://learn.microsoft.com/aspnet/core/metrics/http?view=aspnetcore-10.0>): `aspnetcore.rate_limiting.requests`, Counter, `{request}`, attributes `aspnetcore.rate_limiting.policy` and `aspnetcore.rate_limiting.result`. The value `endpoint_limiter` for a policy rejection was observed in the test (the doc lists only `acquired` and `request_canceled` as examples).
- **OTel naming** (<https://github.com/open-telemetry/semantic-conventions/blob/main/docs/general/naming.md>): lowercase, dot-delimited namespaces, "Metric namespaces SHOULD NOT be pluralized", pluralize the name only for annotation units. Units (<https://github.com/open-telemetry/semantic-conventions/blob/main/docs/general/metrics.md>): singular `{click}`-style annotations.
- **Entra auth** (<https://learn.microsoft.com/azure/azure-monitor/app/azure-ad-authentication>): `DisableLocalAuth: true` + `Monitoring Metrics Publisher` on the component for the sending identity.
- **azd monitor** (`cli/azd/cmd/monitor.go` at tag `azure-dev-cli_1.35.1`, the installed version): no flag → `monitorOverview = true`; components are found by listing the environment's resource groups for type `Microsoft.Insights/components`; `--overview` needs a `Microsoft.Portal/dashboards` resource ("no Application Insights dashboard found"); `--live` and `--logs` need only the component. Learn page: <https://learn.microsoft.com/azure/developer/azure-developer-cli/monitor-your-app>.

## Decisions (for the engineer to question)
- **Built-in rate-limit metric instead of a custom counter** (coordinator's change of decision). Brief said custom counter; built-in used: zero code, standard name, tags already include policy and result. Trade-off found while writing SETUP: without paid custom-metric dimensions, Metrics Explorer shows accepted and rejected attempts as one series, so rejections need the KQL in SETUP. The 429s also appear in Performance/Failures as `resultCode == 429`.
- **The opt-in gate reads `builder.Configuration`**, the same source the distro reads, so an environment variable, user secret or appsettings value all work. It uses `IsNullOrWhiteSpace` rather than a null check, so an empty value cannot crash startup.
- **`AppMetrics` is resolved eagerly after `Build()`.** An observable instrument exists only once its meter does, and with nothing depending on `AppMetrics` it would never be created (mutation M1). The test deliberately does not resolve it, so it catches that regression.
- **Test isolation.** `MeterListener` is process-wide, and xUnit runs test classes in parallel, each with its own host and a `Meter` of the same name. Meters created through `IMeterFactory` have `Meter.Scope` set to that factory, so the listeners enable only instruments where `ReferenceEquals(instrument.Meter.Scope, factory.Services.GetRequiredService<IMeterFactory>())`. A name-only filter would let another class's drops or 429s leak in. ASP.NET Core's rate-limiting meter and `AppMetrics` both come from the host's `IMeterFactory`, so one filter works for both (the green run shows it does for the built-in one).
- **Red step.** `DroppedClicksAreObservable` and `NoExporterWithoutConnectionString` were red (compile errors) before the code existed. `RateLimitRejectionIsCounted` cannot be made red through production code: the metric is ASP.NET Core's and is emitted in-process whether or not `AddMeter` is called (`AddMeter` changes only what is exported). Its sensitivity is shown by mutation instead (M2). It is therefore a contract test for the names and tag values the SETUP query relies on, not a test of `AddMeter`; export is checked after deploy.
- **Connection string as a Container Apps secret.** Documented as non-secret, but it authorizes ingestion into the resource; a secret keeps it out of the portal's env-var view. It is still readable by anyone allowed to list the app's secrets or read the component.
- **Entra-only ingestion: not implemented; it is the production option.** It needs `DisableLocalAuth: true`, a `Monitoring Metrics Publisher` role assignment for the user-assigned identity on the component (about 10 lines of Bicep), the identity's client id as a setting, and `options.Credential = new ManagedIdentityCredential(clientId)` in code. `Azure.Identity` is **not** a distro dependency, so it needs a direct `PackageReference`. It is already in the graph transitively (1.17.1, via Microsoft.Data.SqlClient), so the cost is a reference and a pin, not a new download, but the brief's no-new-package rule excludes it. With it, a leaked connection string would be useless.
- **Only the ASP.NET Core query redaction is re-enabled; the HttpClient one stays off (the distro's choice).** The app's only outbound HTTP is the managed-identity token request (query `resource`, `client_id`, `api-version`: neither personal nor secret); SQL is not HTTP. If an outbound HTTP call with data in the query is ever added, set `OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION=false` too.
- **The rate-limiting meter exports five instruments**, not only `aspnetcore.rate_limiting.requests` (also `active_request_leases`, `queued_requests`, `request_lease.duration`, `request.time_in_queue`). Small, pre-aggregated volume; the README's `AddView(..., MetricStreamConfiguration.Drop)` would drop the four unused ones (recorded, not done).
- **Delta temporality.** The exporter registers its `PeriodicExportingMetricReader` with `TemporalityPreference = MetricReaderTemporalityPreference.Delta` (`AzureMonitorExporterExtensions.cs:167-168` at `Azure.Monitor.OpenTelemetry.Exporter_1.8.3`), so the dropped-clicks series shows new drops per interval; SETUP says to use Sum for a total.
- **Query redaction is re-enabled in Bicep, not appsettings.json.** JSON offers no place to explain an `OTEL_...EXPERIMENTAL...` key, and the exporter runs only where Bicep supplies the connection string. Consequence: a local run with a hand-set connection string (not a documented path) would not redact.
- **Logs are exported too** (the distro's `ILogger` provider), so a log line can be ingested twice (console table + `traces`). Accepted: our logs carry codes and hosts only, the cap bounds the cost, and switching the logger off is not "one documented line" in this distro.
- **No sampling override.** 5 traces/s is far above prototype traffic, so effectively everything is kept, and a load test is capped. Metrics stay exact.

## Deferred to the engineer (needs `azd up`; nothing was deployed)
After `azd provision --preview` (expect one new `Microsoft.Insights/components` and an updated container app) and `azd up`:
1. Container app → **Settings > Secrets** lists `appinsights-connection-string`; **Containers > Environment variables** shows `APPLICATIONINSIGHTS_CONNECTION_STRING` as a secret reference, not a value.
2. Create a link, follow the redirect, wait 2 to 5 minutes. App Insights → **Performance**: `POST /api/links` and the redirect operation appear (exact operation name not verified; expected to be the route template). **Failures**: no unexpected 5xx. The redirect's SQL call appears as a dependency **with no query text** (open one and check its command/`data` field).
3. Exceed the create limit (11 POSTs within 60 s), then run the SETUP KQL: `rejected` > 0. Not verified: that the exporter keeps the attribute names `aspnetcore.rate_limiting.result` / `.policy` as `customDimensions` keys unchanged.
4. **Metrics** → namespace *azure.applicationinsights* → `urlshortener.analytics.dropped_clicks` exists (0 is expected).
5. In `requests`, a redirect called with `?x=secret` shows `x=Redacted` in `url`, and `client_IP` is `0.0.0.0`.
6. Application Map / dependencies also show the managed-identity token request (an HTTP dependency to the identity endpoint) next to SQL: expected, not a leak.
7. Container app **Log stream**: no exporter errors at startup. `azd monitor --logs` opens the component; bare `azd monitor` is expected to fail (no dashboard).
8. In `requests`, no `/health/live` rows (the trace filter drops them), while `http.server.request.duration` in Metrics still counts them. Added by the coordinator after the code review.

## Checks run
Red, before any production change (tests written first):
```
$ dotnet build tests/UrlShortener.IntegrationTests -c Release
ClickAnalyticsTests.cs(10,24): error CS0234: The type or namespace name 'Telemetry' does not exist in the namespace 'UrlShortener.Api'
HealthEndpointsTests.cs(3,7): error CS0246: The type or namespace name 'OpenTelemetry' could not be found
HealthEndpointsTests.cs(4,7): error CS0246: The type or namespace name 'OpenTelemetry' could not be found
    0 Warning(s)
    3 Error(s)
```
Green, the three new tests:
```
$ dotnet test tests/UrlShortener.IntegrationTests -c Release --no-build --filter "...RateLimitRejectionIsCounted|...DroppedClicksAreObservable|...NoExporterWithoutConnectionString"
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3
```
Mutations (each applied alone to `Program.cs`, then restored; `diff` against the backup was empty afterwards):
```
== M1: remove `_ = app.Services.GetRequiredService<AppMetrics>();`  → DroppedClicksAreObservable
   Assert.Equal() Failure: Values differ  Expected: 2  Actual: null
Failed!  - Failed:     1
== M2: remove `.RequireRateLimiting(RateLimitOptions.CreatePolicy)`  → RateLimitRejectionIsCounted
   Assert.Equal() Failure: Values differ  Expected: TooManyRequests  Actual: Created
Failed!  - Failed:     1
== M3: exporter gate replaced by `if (true)`  → NoExporterWithoutConnectionString
   System.InvalidOperationException : A connection string was not found. Please set your connection string.
Failed!  - Failed:     1
```
Gates:
```
$ dotnet build -c Release
    0 Warning(s)
    0 Error(s)

$ dotnet format --verify-no-changes        (first run: ENDOFLINE on the files edited with Python; see Gotchas)
format exit 0

$ dotnet test -c Release --no-build
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47 - UrlShortener.UnitTests.dll
Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54 - UrlShortener.IntegrationTests.dll
```
Integration baseline on `HEAD` is 51 cases (46 `[Fact]` + one `[Theory]` with 5 `InlineData`), so 51 + 3 new = 54. The brief expected 53 + 1, which assumed a baseline of 53.
```
$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.

$ ~/.azd/bin/bicep --version && ~/.azd/bin/bicep lint infra/main.bicep
Bicep CLI version 0.47.16 (3f73e1a234)
lint exit 0          (no diagnostics)
```
New transitive packages (from `dotnet list src/UrlShortener.Api package --include-transitive`): `Azure.Monitor.OpenTelemetry.Exporter` 1.8.3, `OpenTelemetry` / `.Api` / `.Api.ProviderBuilderExtensions` / `.Extensions.Hosting` 1.15.3, `OpenTelemetry.Instrumentation.AspNetCore` 1.15.2, `OpenTelemetry.Instrumentation.Http` 1.15.1, `OpenTelemetry.PersistentStorage.Abstractions` / `.FileSystem` 1.0.3 (the exporter's offline retry buffer). `Azure.Core` 1.60.0 and `Azure.Identity` 1.17.1 were already present via Microsoft.Data.SqlClient.

Local run, no connection string, startup migration off so no database or user secret is involved (the secret store was not read):
```
$ env | grep -c '^APPLICATIONINSIGHTS'
0
$ Database__MigrateOnStartup=false dotnet run -c Release --no-build --project src/UrlShortener.Api --launch-profile http
info: Microsoft.Hosting.Lifetime[14]  Now listening on: http://localhost:5078
info: Microsoft.Hosting.Lifetime[0]   Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]   Hosting environment: Development
warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]  Failed to determine the https port for redirect.
$ curl -i http://localhost:5078/health/live
HTTP/1.1 200 OK
Healthy
```
No OpenTelemetry or exporter line in the log. The one warning is the pre-existing HTTPS-port message of the `http` launch profile.

Environment isolation experiment (before and after the `ApiFactory` pin):
```
$ APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:1/" \
    dotnet test tests/UrlShortener.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~NoExporterWithoutConnectionString"
   Assert.Null() Failure: Value is not null
Failed!  - Failed:     1, Passed:     0          (without the pin)
Passed!  - Failed:     0, Passed:     1          (with the pin)
```

## Gotchas encountered
- Added to CLAUDE.md §9: the distro disables OTel query redaction (found in source); an observable instrument needs its owning singleton resolved (mutation M1); the `azd monitor` gotcha corrected (bare command needs a dashboard).
- Not added (tooling, not project): Python's text mode on Windows writes CRLF, which `.editorconfig`'s `end_of_line = lf` turns into `ENDOFLINE` errors in `dotnet format`; Git Bash's `grep` also hides the CR, so `grep $'\r'` did not detect it. Rewritten in binary mode.

## Coordinator review
Opus 5.5 (coordinator session; the engineer switched the coordinator from Fable 5.1 to Opus 5.5 before this task), after the subagent's hand-back. `.claude/settings.json` confirmed unchanged. Gates re-run by the coordinator before the reviews: build 0/0, format exit 0, `bicep lint` (0.47.16) no diagnostics, no vulnerable packages, 47 unit + 54 integration passed.

**Mid-task change of decision (coordinator):** the brief asked for a custom rate-limit counter; the subagent reported that ASP.NET Core already emits `aspnetcore.rate_limiting.requests` with policy and result tags. Switched to the built-in meter (CLAUDE.md §2: no code where the framework already does it). Naming change to `urlshortener.analytics.dropped_clicks` and re-enabling `url.query` redaction accepted.

**`/security-review` (fresh Opus agent, committed diff): no findings.** Examined every `[LoggerMessage]` call (codes, hosts, counts, exception type and SQL number only), exception messages that could quote values, SQL dependency telemetry (no `db.statement` for text commands), request telemetry (path is the code, query redacted, client IP stored as `0.0.0.0`, bodies never captured), the managed-identity token request (no headers or bodies recorded; its query holds no secret), connection-string handling (secret, no output), the test-side pin, and metric tags (policy and result only, never the partition key).

**Fresh-context `/code-review`: 9 findings → 2 fixed, 1 confirmed as already evidenced, 6 deferred with reasons.**

| # | Finding | Outcome |
|---|---|---|
| 3 | Liveness probes are exported as request telemetry: on an idle app they would be most of the request rows (cost against the cap, noise in Performance/Failures). | **Fixed:** `AspNetCoreTraceInstrumentationOptions.Filter` drops `/health/*` (inside the exporter block, so nothing changes without a connection string). Not covered by a test: tests run without an exporter. Live check added to the engineer's list. |
| 1 | `ILogger` export means EF Core's Error log for a failed `SaveChanges` (with the full `SqlException` message) is now kept in App Insights; the ADR's "same lines the console gets" understated that. | **Fixed (docs):** ADR-0006 now says these lines are kept for the retention period and why the value-quoting SQL errors cannot occur for our data (click values and the destination URL are bounded to their column lengths before insert). Residual risk accepted rather than filtering EF logs out of the exporter, which would hide real failures. |
| 2 | "No query text" claim not checked against distro 1.6.0. | **Already evidenced:** AI log 014 cites the vendored `SqlClientTraceInstrumentationOptions.cs` in 1.6.0 (`SetDbStatementForText` defaults to false). Live check stays on the engineer's list. |
| 4 | Gate reads only `APPLICATIONINSIGHTS_CONNECTION_STRING`; `AzureMonitor:ConnectionString` would silently disable export. | **Deferred:** one documented key, set only by our Bicep; supporting two spellings is configurability nobody asked for. |
| 5 | Resolving `AppMetrics` by hand after `Build()` looks like dead code. | **Deferred:** the line carries a comment and is guarded by `DroppedClicksAreObservable` (mutation M1 fails it). Moving the instrument into `ClickQueue` would change its constructor in `Program.cs` and the tests for no behaviour gain. |
| 6 | Drops are reported twice (the existing warning log and the new metric). | **Deferred:** the warning predates B7 and is the only signal locally, where nothing exports; the metric is the queryable one in Azure. Revisit if both are kept in production. |
| 7 | `AddMeter("Microsoft.AspNetCore.RateLimiting")` exports all five instruments. | **Deferred:** metrics are pre-aggregated per interval, so four extra streams on one replica are negligible against the cap; the one-line `AddView(..., Drop)` fix is already named in ADR-0006. |
| 8 | Ingestion uses the instrumentation-key connection string while the rest is passwordless. | **Deferred:** already the documented production option in ADR-0006 (needs a direct `Azure.Identity` reference, `DisableLocalAuth`, a role). Worst case is injected telemetry against a capped workspace for one week. |
| 9 | Only the negative path of the export gate is tested. | **Deferred:** a positive test needs a dummy connection string and would start a background exporter in the test host; the live check after `azd up` covers it. |

Tests after the fixes: 47 unit + 54 integration (no change in count).

## Sign-off
Reviewed the full diff, understood it, accept ownership. — <name>, YYYY-MM-DD
