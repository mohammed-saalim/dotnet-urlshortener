# AI log 005 — Get/delete endpoints, health checks, OpenAPI/Scalar, global ProblemDetails, safe logging

| Field | Value |
|---|---|
| Issue | #5 |
| Branch | `feat/05-api-surface` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | low |

## Intent
Round out the core API so a reviewer can inspect, delete and discover endpoints, with health probes that are safe for Azure SQL auto-pause and ProblemDetails for every error.

## Constraints given to the AI
- Keep it minimal: no abstractions beyond what each endpoint needs, no packages except the EF health check and Scalar (each verified), no features beyond the issue.
- `TypedResults`, `CancellationToken` everywhere, never log destination URLs or client IPs.
- Tests after, integration level, through `WebApplicationFactory` + Testcontainers. Extend `ApiFactory` rather than adding a second factory.
- No commits, no PR, no `docs/HANDOFF.md` update, no reading `.env`/secrets.

## Acceptance criteria (runnable)
- Get 200/404, delete 204/404 → `LinkEndpointsTests` (5 tests)
- Both health endpoints; `/health/live` never touches the DB → `HealthEndpointsTests.LiveDoesNotTouchTheDatabase` (unreachable DB, Production), `ReadyIsHealthyWhenDatabaseIsReachable` (+ mutation check below)
- curl walkthrough create → get → redirect → delete → redirect 404 → "curl walkthrough" below
- Log output contains the code but not the destination URL → `CreateLogsCodeAndHostButNotUrl` (+ mutation check)
- Malformed JSON is a ProblemDetails 400 in all environments → `ProductionErrorPipelineTests.MalformedJsonIsBadRequestProblem` (Production), `CreateLinkEndpointTests.RejectsMalformedJsonWithBadRequest` (Development, now also asserts `application/problem+json`)
- One test class runs with `UseEnvironment("Production")` → `ProductionErrorPipelineTests` (malformed JSON 400, `/x` 404, 500 after collisions without a stack trace)
- Shared `ApiFactory` per class: considered, not done (see Decisions)

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #5 per a spec (get/delete, live/ready health with tag filter, Scalar in Development, unconditional `UseExceptionHandler` + `StatusCodeSelector` + `UseStatusCodePages`, two log lines, `LoggerMessage` only if CA1848 fires), hard rules from CLAUDE.md §6-§9, test list, checks to paste.

## Packages (verified on nuget.org 2026-10-10, pinned in `Directory.Packages.props`)

| Package | Version | Owner(s) | Downloads | Repo / licence | Why |
|---|---|---|---|---|---|
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | 10.0.12 | aspnet, dotnetframework, Microsoft (verified prefix) | 149,134,943 | github.com/dotnet/dotnet, MIT | `AddDbContextCheck<LinksDbContext>()`: first-party, same version as EF Core 10.0.12 already pinned; its dependencies (HealthChecks, EF Relational) are already in the graph |
| `Scalar.AspNetCore` | 2.17.14 (latest stable) | Scalar, marclave (verified prefix) | 48,052,142 | github.com/scalar/scalar, MIT | Scalar UI in Development (CLAUDE.md §7); no NuGet dependencies |

Custom `IHealthCheck` with `CanConnectAsync` was the alternative; rejected because the first-party check does the same call (`CanConnectAsync` by default) with zero code to own.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `src/UrlShortener.Api/Links/GetLinkEndpoint.cs` | generated | One `AsNoTracking` query filtered on `DeletedAt == null`; reuses `LinkResponse`; `shortUrl` built as in create (#9 replaces both) |
| `src/UrlShortener.Api/Links/DeleteLinkEndpoint.cs` | generated | `ExecuteUpdateAsync` soft delete, `0 rows → 404` (see Decisions: deviates from the spec's "query + save") |
| `src/UrlShortener.Api/Program.cs` | edited | Health checks, Scalar in Development, `WithName`/`WithSummary`, error pipeline (unconditional handler + selector + status code pages); old comment on the conditional handler removed (orphaned) |
| `src/UrlShortener.Api/Links/CreateLinkEndpoint.cs` | edited | `[LoggerMessage]` Information log: code + host. Class made `partial` |
| `src/UrlShortener.Api/Redirect/RedirectEndpoint.cs` | edited | `Problem(404)` → `NotFound()` (status code pages now writes the body); `[LoggerMessage]` Debug log on miss (code only). Class made `partial` |
| `Directory.Packages.props`, `UrlShortener.Api.csproj` | edited | Two packages above |
| `tests/.../ApiFactory.cs` | edited | `EnvironmentName { get; init; } = "Development"` + `UseEnvironment`. Init property, not a ctor parameter: an optional `string` before `params string[] codes` would silently reinterpret `new ApiFactory(cs, "Clash01", "Fresh01")` |
| `tests/.../Links/LinkEndpointsTests.cs` | generated | 5 tests incl. the log-capture test (list-backed `ILoggerProvider` added through `WithWebHostBuilder`) |
| `tests/.../Health/HealthEndpointsTests.cs` | generated | 2 tests |
| `tests/.../ErrorPipeline/ProductionErrorPipelineTests.cs` | generated | 3 tests, `EnvironmentName = "Production"` |
| `tests/.../Links/CreateLinkEndpointTests.cs` | edited | Malformed-JSON test also asserts `application/problem+json` (Development side of "all environments") |
| `tests/.../Redirect/RedirectEndpointTests.cs` | edited | Shadowing test renamed and extended with `/health/live` (200) and `/scalar` (Scalar's own 302 to `scalar/`; our endpoint would have returned 404) |
| `CLAUDE.md` §9 | edited | Rewrote the `UseExceptionHandler` gotcha (its advice "outside Development only" no longer matches the code); added CA1848 |
| (proposal) plain `logger.LogInformation(...)` | rejected | CA1848 fails the build (output below) |
| (proposal) `ILogger<CreateLinkEndpoint>` parameter | rejected | Static classes cannot be type arguments (CS0718); `ILoggerFactory.CreateLogger(typeof(...))` keeps the same category name |
| (proposal) keep `UseExceptionHandler` Development-conditional + add `UseStatusCodePages` | rejected for now, engineer to decide | Works too and keeps the Developer Exception Page details in Development (experiment D below), but the issue asks to remove the guard, and without the selector a Production `BadHttpRequestException` (e.g. 413 body too large) would become 500 |
| (proposal) shared `ApiFactory` per test class | not done | Optional in the issue; current suite runs in ~2 s, and per-test factories match the existing classes |
| (temp) `tests/.../ZzProbe.cs` | generated then deleted | Dumped status/content-type/body per pipeline variant for the experiment below |

## Error pipeline experiment (Developer Exception Page vs `UseExceptionHandler`)
Probe: malformed JSON, `GET /x`, and the 500-after-collisions case, in Development and Production. Bodies truncated.

| Variant | Env | malformed JSON | `/x` | 500 |
|---|---|---|---|---|
| A: baseline (handler outside Dev only) | Prod | 400, **empty** | 404, **empty** | 500 problem+json, generic |
| | Dev | 400 problem+json **with exception details** (Developer Exception Page) | 404, **empty** | 500 problem+json with stack trace |
| B: unconditional handler + selector | Prod | 400, **empty** | 404, **empty** | 500 problem+json, generic |
| | Dev | 400 problem+json, generic `"Bad Request"` | 404, **empty** | 500 problem+json, generic |
| **C: B + `UseStatusCodePages` (shipped)** | Prod | 400 problem+json | 404 problem+json | 500 problem+json, generic |
| | Dev | 400 problem+json, generic | 404 problem+json | 500 problem+json, generic |
| D: A + `UseStatusCodePages` | Prod | same as C | same as C | same as C |
| | Dev | 400 problem+json with exception details | 404 problem+json | 500 problem+json with stack trace |

Findings:
- In Production, Minimal APIs never throw for a bad body (`RouteHandlerOptions.ThrowOnBadRequest` is on only in Development). They write a bare 400, so the `StatusCodeSelector` changes nothing there; `UseStatusCodePages` is what fixes Production (B vs C).
- The Developer Exception Page does not interfere. The reverse happens: an unconditional `UseExceptionHandler` sits inside it and handles every exception first, so in Development the response no longer carries exception details (still logged to the console). If the engineer wants them back, the simplest fix is variant D.

## curl walkthrough (real output)
The app was run against a throwaway `mcr.microsoft.com/mssql/server:2022-latest` container started for this purpose, with an invented `sa` password passed as the `ConnectionStrings__UrlShortener` environment variable (not printed, never written to a file in the repo; container removed afterwards). No `.env` or user-secrets were read. Headers trimmed only by `tr -d '\r'`.

Development (`ASPNETCORE_ENVIRONMENT=Development`, startup migration ran):
```
$ curl -i -X POST http://localhost:5091/api/links -H "Content-Type: application/json" -d '{"url":"https://example.com/docs?ref=walkthrough"}'
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/links/W3gVUGr

{"code":"W3gVUGr","shortUrl":"http://localhost:5091/W3gVUGr","url":"https://example.com/docs?ref=walkthrough","createdAt":"2026-10-10T23:00:28.2018269+00:00","expiresAt":null}

$ curl -i http://localhost:5091/api/links/W3gVUGr
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"code":"W3gVUGr","shortUrl":"http://localhost:5091/W3gVUGr","url":"https://example.com/docs?ref=walkthrough","createdAt":"2026-10-10T23:00:28.2018269+00:00","expiresAt":null}

$ curl -i http://localhost:5091/W3gVUGr
HTTP/1.1 302 Found
Content-Length: 0
Cache-Control: private, no-store
Location: https://example.com/docs?ref=walkthrough

$ curl -i -X DELETE http://localhost:5091/api/links/W3gVUGr
HTTP/1.1 204 No Content

$ curl -i http://localhost:5091/W3gVUGr
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":"00-64dc86bc69ee1528edbd3ebdc15955a5-6fe08432aae54ae3-00"}

$ curl -i -X DELETE http://localhost:5091/api/links/W3gVUGr
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":"00-9f28aff55841eaf59ce213b0fb02c700-e30b7eb71cd85bd0-00"}

$ curl -i http://localhost:5091/health/live
HTTP/1.1 200 OK
Content-Type: text/plain
Cache-Control: no-store, no-cache

Healthy

$ curl -i http://localhost:5091/health/ready
HTTP/1.1 200 OK
Content-Type: text/plain
Cache-Control: no-store, no-cache

Healthy

$ curl -s http://localhost:5091/openapi/v1.json   (operationId / summary lines)
"summary": "Redirect to the link's destination."   "operationId": "RedirectToLink"
"summary": "Create a short link."                  "operationId": "CreateLink"
"summary": "Get a short link's metadata."          "operationId": "GetLink"
"summary": "Soft-delete a short link."             "operationId": "DeleteLink"

GET /scalar/ -> 200 text/html
```
(`Date`, `Server`, `Transfer-Encoding` headers omitted above.) App log lines matching `example.com` for the whole session:
```
      Created short link W3gVUGr for host example.com
```

Production (`ASPNETCORE_ENVIRONMENT=Production`, same database):
```
$ curl -i -X POST http://localhost:5092/api/links -H "Content-Type: application/json" -d '{not json'
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
Date: Sat, 10 Oct 2026 23:00:46 GMT
Server: Kestrel
Transfer-Encoding: chunked

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"00-7ed6e1f61b4a1818114818b32579f6d6-d721596b9cd05f7e-00"}

$ curl -i http://localhost:5092/x
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":"00-cd154614992d0b4f7ec9f3ab31cf7902-ff960a1f9da99711-00"}

GET /scalar/ -> 404
GET /openapi/v1.json -> 404
```

## Decisions (for the engineer to question)
- **DELETE uses `ExecuteUpdateAsync`, not load + `SaveChanges`** (the spec said "one query + save"). One `UPDATE ... WHERE Code = @code AND DeletedAt IS NULL` is one round trip, and concurrent deletes cannot both return 204: exactly one sees the row. Same line count. Easy to revert if the tracked version is preferred.
- **Error pipeline is variant C** (see experiment): ProblemDetails everywhere, but Development loses the Developer Exception Page's exception details in responses. Variant D keeps them; choose D if that matters more than "Development behaves like Production".
- **404 bodies are now the generic `"title":"Not Found"`** (status code pages) instead of the custom "Short link not found." from #4. `NotFound()` in redirect, get and delete. The OpenAPI document describes these 404s without a body schema.
- **`/health/ready` is not covered for the failure case** (unreachable DB → 503). Not in the spec, and with `EnableRetryOnFailure` it may be slow; the mutation check shows the DB check returns 503 within ~1 s against port 1, so it would be cheap to add if wanted.
- **Health response bodies are the default plain text (`Healthy`)**, with ASP.NET's `no-store, no-cache`. Not ProblemDetails for 503; ACA probes only read the status.
- **Log-capture test covers all categories**, including EF Core's command logging, so it also shows that EF does not log the URL parameter (no `EnableSensitiveDataLogging`). It would catch someone enabling that (negative assertion mutation-checked below; `EnableSensitiveDataLogging` itself was not tried).
- **`GET /api/links/{code}` has no route constraint** (unlike the redirect): any string reaches the parameterised query and returns 404. Expired links still return 200 metadata (Day 3 decides expiry semantics).
- **Shared factory per class not done** (optional item from the #3 review).

## Checks run

**CA1848 with plain `LogInformation`/`LogDebug`:**
```
$ dotnet build --nologo -v q
src\UrlShortener.Api\Links\CreateLinkEndpoint.cs(71,9): error CA1848: For improved performance, use the LoggerMessage delegates instead of calling 'LoggerExtensions.LogInformation(ILogger, string?, params ...
src\UrlShortener.Api\Redirect\RedirectEndpoint.cs(30,13): error CA1848: For improved performance, use the LoggerMessage delegates instead of calling 'LoggerExtensions.LogDebug(ILogger, string?, params obje...
    0 Warning(s)
    2 Error(s)
```

**Red: Production tests before the pipeline change:**
```
  Failed ...ProductionErrorPipelineTests.UnknownRouteIsNotFoundProblem [42 ms]
Expected: "application/problem+json"
Actual:   null
  Failed ...ProductionErrorPipelineTests.MalformedJsonIsBadRequestProblem [17 ms]
Expected: "application/problem+json"
Actual:   null
Failed!  - Failed:     2, Passed:     1, Skipped:     0, Total:     3, Duration: 858 ms - UrlShortener.IntegrationTests.dll (net10.0)
```

**Mutation checks:** `/health/live` predicate `_ => false` → `_ => true`, and the create log given `link.OriginalUrl` instead of the host; both restored afterwards:
```
  Failed UrlShortener.IntegrationTests.Links.LinkEndpointsTests.CreateLogsCodeAndHostButNotUrl [815 ms]
   Assert.Contains() Failure: Sub-string not found
  Failed UrlShortener.IntegrationTests.Health.HealthEndpointsTests.LiveDoesNotTouchTheDatabase [1 s]
Expected: OK
Actual:   ServiceUnavailable
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```
The first log mutation breaks the positive `Contains` before the negative assertions run, so a second one keeps the host and adds the full URL as a third message parameter (`... for host {Host} ({Url})`); restored afterwards:
```
  Failed UrlShortener.IntegrationTests.Links.LinkEndpointsTests.CreateLogsCodeAndHostButNotUrl [815 ms]
   Assert.DoesNotContain() Failure: Sub-string found
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 815 ms - UrlShortener.IntegrationTests.dll (net10.0)
```

**Final gates:**
```
$ dotnet build --nologo -v q
    0 Warning(s)
    0 Error(s)

$ dotnet format --verify-no-changes --no-restore; echo $?
0

$ dotnet test --nologo; echo $?
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 428 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    25, Skipped:     0, Total:    25, Duration: 2 s - UrlShortener.IntegrationTests.dll (net10.0)
0

$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.

$ git status --short
 M Directory.Packages.props
 M src/UrlShortener.Api/Links/CreateLinkEndpoint.cs
 M src/UrlShortener.Api/Program.cs
 M src/UrlShortener.Api/Redirect/RedirectEndpoint.cs
 M src/UrlShortener.Api/UrlShortener.Api.csproj
 M tests/UrlShortener.IntegrationTests/ApiFactory.cs
 M tests/UrlShortener.IntegrationTests/Links/CreateLinkEndpointTests.cs
 M tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs
?? src/UrlShortener.Api/Links/DeleteLinkEndpoint.cs
?? src/UrlShortener.Api/Links/GetLinkEndpoint.cs
?? tests/UrlShortener.IntegrationTests/ErrorPipeline/
?? tests/UrlShortener.IntegrationTests/Health/
?? tests/UrlShortener.IntegrationTests/Links/LinkEndpointsTests.cs
```
(Taken before `CLAUDE.md` and this log were written; both also show in the final status.)

Integration tests: 15 → 25 (+5 links, +2 health, +3 Production pipeline; 2 existing tests extended).

## Gotchas encountered
Added to CLAUDE.md §9: rewrote the `UseExceptionHandler` entry (selector + `UseStatusCodePages`, Production writes a bare 400), and CA1848 / static-class loggers.

## Open items
- Engineer: variant C vs D for Development error responses.
- #9: `shortUrl` from a configured base URL (two copies of the interpolation now: create and get).
- Day 3: expiry → 404 vs 410; 404/410 cache headers.
- `docs/HANDOFF.md` not updated by the subagent (coordinator's end-of-session step).

## Sign-off
(pending engineer review of the PR)

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`); gates re-run independently: build 0/0, format exit 0, 47 unit + 25 integration passed, no vulnerable packages. Not high-impact per CLAUDE.md §6.6 (no schema, validation, rate-limit policy, infra or agent-settings change), so no `/security-review` or `/code-review`.

| Agent decision | Outcome | Rationale |
|---|---|---|
| `DELETE` via one `ExecuteUpdateAsync` instead of load + save | accepted | One round trip; of two concurrent deletes exactly one gets 204 without any extra code. Simpler than the brief |
| Unconditional `UseExceptionHandler` + `StatusCodeSelector` + `UseStatusCodePages` (Development loses exception details in the response body) | accepted | ProblemDetails is identical in every environment and the Production pipeline is covered by tests. Exception details are still in the logs. The alternative (handler outside Development only) would make Development malformed-JSON responses differ from Production; consistency wins for a reviewer-facing API |
| Generic "Not Found" titles instead of "Short link not found." | accepted | Standard ProblemDetails from one place beats hand-written titles per endpoint |
| `ILoggerFactory` + `[LoggerMessage]` because CA1848 is an error | accepted | Correct under `latest-recommended`; gotcha recorded |
| Rewrote the earlier `UseExceptionHandler` gotcha | accepted | The old advice contradicted the new code |
| Skipped shared factory per class and a `/health/ready` 503 test | accepted | Suite runs in ~2 s; a down-DB readiness test can come with B6 if useful |

### CI failure after the first push (coordinator)
CI's Release build failed with **CA1873** at the two new log calls (`loggerFactory.CreateLogger(...)` and `new Uri(...).Host` as log arguments); the local Debug build had not reported it. Fix: hoist the logger into a local and guard each call with `logger.IsEnabled(...)`. Prevention: the local gate and the Stop hook now build `-c Release` (`.claude/settings.json`, CLAUDE.md §5/§9). This PR is therefore marked high-impact (agent settings changed).
```
$ dotnet build -c Release --nologo -v q
    0 Warning(s)   0 Error(s)
$ dotnet test -c Release --no-build --nologo
Passed!  - 47 unit, 25 integration
```

### Second CI failure and the actual cause (coordinator)
The `IsEnabled` guard was not enough: CI still reported CA1873 at `new Uri(link.OriginalUrl).Host` inside the guard, while the same code built clean locally in Release. The CI log shows `actions/setup-dotnet` resolved `10.0.x` to **SDK 10.0.401**; the local SDK is **10.0.101**. The newer SDK ships stricter analyzers, so "Release-only" was a wrong diagnosis (corrected in CLAUDE.md §9).

Fixes: the host is computed into a local before the log call; `global.json` pins SDK `10.0.101` (`rollForward: latestPatch`) and `ci.yml` installs from it (`global-json-file`). Local and CI now compile with the same analyzers.
```
$ dotnet build -c Release --nologo -v q
    0 Error(s)
$ dotnet test -c Release --no-build --nologo
Passed!  - 47 unit, 25 integration
```
