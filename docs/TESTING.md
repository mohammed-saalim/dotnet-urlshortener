# Testing

```bash
dotnet test -c Release   # unit + integration; integration tests need Docker running
```

## What the tests cover

| Project / class | Kind | Covers |
|---|---|---|
| `UrlShortener.UnitTests/Links/ShortCodeTests` | unit | Short-code length and alphabet (`RandomNumberGenerator`, never `System.Random`) |
| `UrlShortener.UnitTests/Links/CreateLinkValidatorTests` | unit | Destination-URL, alias and expiry validation, including whitespace and control characters; ADR-0007 host rules: loopback, private, link-local and unspecified IP literals (IPv4, IPv6, IPv4-mapped, range edges), `localhost`/`*.localhost`, the service's own host (any port), reserved aliases, messages that do not echo the URL; pins that `Uri` normalises `2130706433`, `0x7f.1`, `127.1` to `127.0.0.1` |
| `UrlShortener.UnitTests/Safety/DenylistReputationCheckerTests` | unit | `Safety:DeniedHosts`: exact host and subdomains, case and trailing dot ignored, look-alikes allowed, empty list allows all |
| `IntegrationTests/Links/LinkPersistenceTests` | integration | Case-sensitive `Code` collation, unique index rejects duplicates |
| `IntegrationTests/Links/CreateLinkEndpointTests` | integration | `POST /api/links`: 201, validation 400s (including metadata-address, `localhost` and own-host destinations, and a denylisted host through the reputation seam), alias 409, collision retry, malformed JSON |
| `IntegrationTests/Links/LinkEndpointsTests` | integration | Get (an expired link still 200 with its past `expiresAt`) and soft delete (get 404, redirect 410); the create log holds code and host but not the URL |
| `IntegrationTests/Redirect/RedirectEndpointTests` | integration | `302` + `Cache-Control: private, no-store`, misses (404), case, deleted and expired (`ExpiresAt <= now`) → 410 ProblemDetails + `no-store` (ADR-0007), one second before expiry still 302, route shadowing |
| `IntegrationTests/Health/HealthEndpointsTests` | integration | `/health/live` never touches the database (unreachable DB still 200); `/health/ready` checks it; no OpenTelemetry tracer or meter provider is registered without `APPLICATIONINSIGHTS_CONNECTION_STRING` (#28) |
| `IntegrationTests/ErrorPipeline/ProductionErrorPipelineTests` | integration, `Production` environment | ProblemDetails for 400/404/500 without stack traces |
| `IntegrationTests/Analytics/ClickAnalyticsTests` | integration | Click recording (ADR-0004): redirect → row (host-only referrer, truncated user agent), redirect still 302 when the queue is full and drops are counted, shutdown drains the queue, the writer survives a failed batch, misses and 410s (deleted, expired) record nothing; dropped clicks are reported by the `urlshortener.analytics.dropped_clicks` observable counter (#28) |
| `IntegrationTests/RateLimiting/RateLimitingTests` | integration | Rate limit on create (ADR-0005): over-limit POST is 429 with `Retry-After` and a ProblemDetails body; redirect, get and delete are not limited; invalid limits fail startup; a rejection is counted by the built-in `aspnetcore.rate_limiting.requests` metric with `policy=create`, `result=endpoint_limiter` (#28) |
| `IntegrationTests/PublicUrl/PublicUrlTests` | integration | `shortUrl` from `PublicUrl:BaseUrl`, not the client's `Host` header (create and get); a missing or malformed base URL (padding, user info, path, non-http scheme) fails startup; Production rejects an unknown `Host` (400) while `localhost` passes; `X-Forwarded-For` is ignored by default and gives a rate-limit partition per client when `PublicUrl:TrustForwardedHeaders` is on |
| `IntegrationTests/Analytics/LinkStatsTests` | integration | `GET /api/links/{code}/stats`: total, 7 UTC days zero-filled oldest first (day boundaries at 00:00 UTC), top 5 referrer hosts by count then host (nulls excluded), zero-click link, an expired link still reported (200), 404 problem for unknown and soft-deleted codes. Clicks inserted directly, no polling |

Integration tests run the real app through `WebApplicationFactory` (`ApiFactory`) against a real SQL Server started by Testcontainers (`mcr.microsoft.com/mssql/server:2022-latest`), one container per test class (`SqlServerFixture`). No EF InMemory or SQLite: collation and unique-index behaviour are part of what is tested. Time is fixed through an injected `TimeProvider` (`ApiFactory.Now`). Click tests poll the database (every 50 ms, up to 5 s) because clicks are written asynchronously.

## Known limitations

- **Clicks are at most once.** They are queued in memory and written by a background service. Up to 10,000 queued clicks and the batch being written are **lost on a crash, OOM kill or `SIGKILL`**, since only a graceful shutdown drains the queue (30 s default timeout). A failed batch (up to 100 clicks) is logged and discarded, not retried. Clicks are dropped when the queue is full; drops are counted (`ClickQueue.Dropped`) and logged as a count. See [ADR-0004](adr/0004-async-click-analytics.md).
- **Stats are eventually consistent:** a click is visible once its batch is inserted.
- **No retention:** the `Clicks` table grows without bound (open question 7 in [the brownfield scenario](scenarios/brownfield.md)).
- **One SQL Server container per test class.** Starting several in parallel has flaked on the GitHub runner (CLAUDE.md §9); a shared collection fixture is tracked in issue #21. New click tests were kept in one class for that reason.
- **Rate-limit tests share one partition.** TestServer sets no `RemoteIpAddress`, so every test request uses the `"unknown"` key. Per-IP separation is tested only through `X-Forwarded-For` with `TrustForwardedHeaders` on (`PublicUrlTests`); the real Container Apps ingress is not (B6).
- **Telemetry export is not tested.** `ApiFactory` pins `APPLICATIONINSIGHTS_CONNECTION_STRING` to empty (it outranks an environment variable of the same name), so no test host exports, whatever the machine sets; the metric tests read the instruments in-process with a `MeterListener` filtered to the test host's own `IMeterFactory` (other test classes run in parallel and create meters of the same name). That the exporter picks the meters up (`AddMeter`) is checked only after deploy (AI log 014).
- The drain test covers a graceful stop of the test host, not the Container Apps termination grace period (checked in B6).

## Performance

Measured for B5 (#18) with [`perf/redirect-load.js`](../perf/redirect-load.js) (how to run: [`perf/README.md`](../perf/README.md)). Full output in [AI log 012](ai-log/012-perf-measure.md).

**Setup.** One laptop: Intel Core i7-10610U (4 cores / 8 threads), 32 GB, Windows 11. The API ran in Release (`dotnet run -c Release`, `Development` environment for the startup migration) against SQL Server 2022 in Docker Desktop (WSL2), with k6 2.3.0 in a container on the same machine, so load generator, API and database share the CPU. One link, so every request hits the same hot code: the best case for any cache. The click writer (ADR-0004) was inserting a row per redirect throughout. `/health/live` (no database) is the floor: the same pipeline without the lookup.

| Run | Requests/s | p50 | p95 | p99 | Errors |
|---|---|---|---|---|---|
| Redirect, 1 VU, 15 s (unloaded) | 194 | 4.54 ms | 6.91 ms | 10.98 ms | 0 % |
| `/health/live`, 1 VU, 15 s (floor) | 788 | 1.04 ms | 1.56 ms | 2.11 ms | 0 % |
| Redirect, 20 VUs, 30 s, run 1 (as shipped) | 1,143 | 14.68 ms | 31.61 ms | 65.23 ms | 0 % |
| Redirect, 20 VUs, 30 s, run 2 (as shipped) | 1,080 | 16.29 ms | 31.12 ms | 46.54 ms | 0 % |
| Redirect, 20 VUs, 30 s, EF Core logging at `Warning` | 1,014 | 17.00 ms | 36.17 ms | 52.49 ms | 0 % |
| `/health/live`, 20 VUs, 30 s (floor) | 3,223 to 4,034 | 4.26 to 4.86 ms | 8.37 to 12.31 ms | 12.52 to 24.4 ms | 0 % |

- **Reading it:** 20 VUs with no think time is a saturation test, so its latency is mostly queueing (Little's law: 20 VUs / 1,080 rps ≈ 18.5 ms, the measured average was 18.13 ms). The intrinsic cost of the lookup is the unloaded difference: about **3.5 ms at p50** (4.54 − 1.04), including EF Core, SqlClient, the round trip into the Docker VM and the click enqueue. At saturation the lookup costs about three quarters of the capacity (about 1,100 vs 3,200 to 4,000 rps). During run 2 SQL Server used about 165 % CPU and the API process about 167 % of one core; neither was pegged, the shared 8 threads were.
- **Logging is not the bottleneck:** as shipped, EF Core logs every executed command at `Information` (39,134 `Executed DbCommand` lines in run 1). Turning it down to `Warning` did not make the redirect faster (within run-to-run noise).
- **Analytics under load:** every redirect was stored. Clicks for the run's link equalled k6 iterations in all three 20-VU runs (34,648, 32,572 and 30,594; also confirmed by `totalClicks` from the stats endpoint), with no "clicks dropped" or "Discarded a batch" warnings, so the 10,000-item queue (ADR-0004) kept up at about 1,100 redirects/s.

**Decision: no cache in B5.** The research guidance is that an in-process cache pays off when the database lookup dominates latency *and* traffic repeats hot codes. Unloaded, a redirect takes 4.5 ms at p50 and 6.9 ms at p95, of which the lookup is about 3.5 ms; at saturation the service still serves about 1,100 redirects/s on one shared laptop, orders of magnitude above what a single-replica prototype will see. A cache would buy throughput headroom that is not needed, and it brings staleness that must be handled everywhere a link changes: explicit invalidation on delete (`ExecuteUpdateAsync` tracks nothing), the expiry check after a cache hit (Day 3), and no invalidation across replicas. Whether real traffic repeats hot codes is unknown; this test assumes it does.

**What would change the decision** (and the next step, already scoped in [the brownfield impact table](scenarios/brownfield.md), B5 row): the Azure SQL round trip from Container Apps measured in B6 (the database share there will be larger than a local container's, and every read costs vCore-seconds on the free offer); more than one replica; or measured traffic with hot-code skew and sustained load. The cache would then be `IMemoryCache` (shared framework, enough for one replica; HybridCache only if stampede protection or a Redis L2 is wanted), keyed by the case-sensitive code, short TTL, storing `Id` as well as the URL for click recording, with an explicit `Remove` in `DeleteLinkEndpoint` and the tests `DeletedLinkStopsRedirectingWhenCached` and `CacheKeysAreCaseSensitive`, then this table re-run for before/after.

**Limits of this measurement:** one machine with the generator co-located; a local container, not Azure SQL; one hot code; runs of 15 to 30 s. Treat the numbers as relative (lookup vs floor), not as capacity figures for Azure.

### On Azure (the B6 trigger check)

The first trigger above, measured on 2026-10-11 against the deployed app (centralus; one replica of 0.25 vCPU / 0.5 GiB; Azure SQL free offer, serverless 0.5–1 vCore). k6 2.3.0 ran from the same laptop over the internet, **1 VU for 30 s only**: one small replica needs no saturation test. Server-side numbers are from Application Insights, which keeps every trace since #33. Full output: [AI log 017](ai-log/017-azure-perf.md).

| Measured | p50 | p95 | p99 | Source |
|---|---|---|---|---|
| Redirect, client side (385 requests, 11.8/s) | 94.4 ms | 105.5 ms | 201.8 ms | k6 |
| `/health/live`, client side (floor, 1,228 requests) | 22.0 ms | 34.7 ms | 70.2 ms | k6 |
| Redirect, server side | 6.2 ms | 80.8 ms | 85.8 ms | `requests.duration` |
| SQL lookup inside the redirect | **1.9 ms** | 77.0 ms | 81.3 ms | `dependencies.duration` |

- **The Azure SQL round trip is not the cost.** The lookup takes about 1.6–1.9 ms when nothing interferes (249 of 386 calls under 10 ms, p50 1.6 ms), against 3.5 ms for the whole lookup locally (B5).
- **The tail is CPU, not the database.** Server-side durations are bimodal: 53 % of redirects finish under 10 ms (mostly 3–4 ms) and 33 % take 70–100 ms. The slow ones arrive about 100 ms apart, and the container peaked at 90 % of its 0.25 vCPU limit during the run (`CpuPercentage`, against 2–3 % idle). That matches Linux CFS quota throttling: 0.25 vCPU is 25 ms of CPU per 100 ms period, after which the process waits out the rest (~75 ms). The SQL span shows the stall only because it is open when the freeze happens. This is an inference from the pattern and the CPU metric; the container's throttle counters were not read.
- **Cold start, measured:** the first `/health/ready` after idle took **22.1 s** (database resume from auto-pause), and the first create took 1.6 s server-side. SETUP's "tens of seconds" holds.

**Decision unchanged: no cache.** A cache would remove about 2 ms of database time per redirect. At this size the lever for the tail is CPU (0.5 vCPU, or less work per request), and only under sustained load: one closed-loop client at 12 requests/s is already far above prototype traffic. Every lookup does wake the database and spend free vCore-seconds, but one wake costs at least the 60-minute auto-pause delay (ADR-0006), so a cache would not prevent wakes at low traffic. **Next triggers:** more than one replica, sustained real traffic with hot-code skew, or the tail mattering to a user.
