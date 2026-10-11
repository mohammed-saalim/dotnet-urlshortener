# Testing

```bash
dotnet test -c Release   # unit + integration; integration tests need Docker running
```

## What the tests cover

| Project / class | Kind | Covers |
|---|---|---|
| `UrlShortener.UnitTests/Links/ShortCodeTests` | unit | Short-code length and alphabet (`RandomNumberGenerator`, never `System.Random`) |
| `UrlShortener.UnitTests/Links/CreateLinkValidatorTests` | unit | Destination-URL, alias and expiry validation, including whitespace and control characters |
| `IntegrationTests/Links/LinkPersistenceTests` | integration | Case-sensitive `Code` collation, unique index rejects duplicates |
| `IntegrationTests/Links/CreateLinkEndpointTests` | integration | `POST /api/links`: 201, validation 400s, alias 409, collision retry, malformed JSON |
| `IntegrationTests/Links/LinkEndpointsTests` | integration | Get and soft delete; the create log holds code and host but not the URL |
| `IntegrationTests/Redirect/RedirectEndpointTests` | integration | `302` + `Cache-Control: private, no-store`, misses, case, soft delete, route shadowing |
| `IntegrationTests/Health/HealthEndpointsTests` | integration | `/health/live` never touches the database (unreachable DB still 200); `/health/ready` checks it |
| `IntegrationTests/ErrorPipeline/ProductionErrorPipelineTests` | integration, `Production` environment | ProblemDetails for 400/404/500 without stack traces |
| `IntegrationTests/Analytics/ClickAnalyticsTests` | integration | Click recording (ADR-0004): redirect → row (host-only referrer, truncated user agent), redirect still 302 when the queue is full and drops are counted, shutdown drains the queue, the writer survives a failed batch, misses record nothing |
| `IntegrationTests/RateLimiting/RateLimitingTests` | integration | Rate limit on create (ADR-0005): over-limit POST is 429 with `Retry-After` and a ProblemDetails body; redirect, get and delete are not limited; invalid limits fail startup |

Integration tests run the real app through `WebApplicationFactory` (`ApiFactory`) against a real SQL Server started by Testcontainers (`mcr.microsoft.com/mssql/server:2022-latest`), one container per test class (`SqlServerFixture`). No EF InMemory or SQLite: collation and unique-index behaviour are part of what is tested. Time is fixed through an injected `TimeProvider` (`ApiFactory.Now`). Click tests poll the database (every 50 ms, up to 5 s) because clicks are written asynchronously.

## Known limitations

- **Clicks are at most once.** They are queued in memory and written by a background service. Up to 10,000 queued clicks and the batch being written are **lost on a crash, OOM kill or `SIGKILL`**, since only a graceful shutdown drains the queue (30 s default timeout). A failed batch (up to 100 clicks) is logged and discarded, not retried. Clicks are dropped when the queue is full; drops are counted (`ClickQueue.Dropped`) and logged as a count. See [ADR-0004](adr/0004-async-click-analytics.md).
- **Stats are eventually consistent:** a click is visible once its batch is inserted.
- **No retention:** the `Clicks` table grows without bound (open question 7 in [the brownfield scenario](scenarios/brownfield.md)).
- **One SQL Server container per test class.** Starting several in parallel has flaked on the GitHub runner (CLAUDE.md §9); a shared collection fixture is tracked in issue #21. New click tests were kept in one class for that reason.
- **Rate-limit tests share one partition.** TestServer sets no `RemoteIpAddress`, so every test request uses the `"unknown"` key; per-IP separation and the proxy behaviour behind Container Apps ingress (#9) are not tested.
- The drain test covers a graceful stop of the test host, not the Container Apps termination grace period (checked in B6).

## Performance

To be filled in by B5 (#18): k6 redirect load test, p50/p95 and requests per second, with and without a cache.
