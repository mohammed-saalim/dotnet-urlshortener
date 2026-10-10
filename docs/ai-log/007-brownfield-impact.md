# AI log 007 — Brownfield impact analysis and ADR-0004

| Field | Value |
|---|---|
| Issue | #14 |
| Branch | `docs/07-brownfield-impact` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | high-impact (the schema in ADR-0004 is what B2 migrates) |

## Intent
Before any Day 2 code, identify the modules, APIs, data flows and tests the Day 2 changes touch, and decide the click analytics design so B2 implements an agreed shape.

## Constraints given to the AI
- Docs only: no code, tests, csproj, settings or workflow changes; no commits, pushes or PRs.
- Precise and short, not exhaustive (scenario doc under ~180 lines).
- Every technical claim traces to the code, the research files, or an official docs page actually fetched.
- No raw client IP stored; analytics must never slow or fail the redirect.
- Do not read `.env`, `secrets.json`, `.azure/` or `docs/brief/*`.

## Acceptance criteria (runnable)
- [x] Impact table: component → change → risk → test that protects it (`docs/scenarios/brownfield.md`, "Impact analysis")
- [x] Data-flow diagram (Mermaid) for redirect → queue → writer → Clicks ("Data flow after Day 2")
- [x] ADR-0004 with the four options, decision, consequences, confirmation tests for B2
- [x] Open questions for the engineer (analytics shape, IP handling, retention, plus partitioning, deleted-link stats, capacity, batch, `OccurredAt` type)

## Prompts (summarised, secrets redacted)
1. Coordinator: implement #14 per the issue; read CLAUDE.md, plan, ADR template and ADR-0001, greenfield doc, issues #9, #14-#19, the whole codebase and the analytics sections of the research; write the scenario doc, ADR-0004 and this log; verify links and test names.
2. Mid-task review (advisor) before writing: cite only what the fetched pages state, count drops with the documented `itemDropped` callback, add the impact rows the code reading supports (`LiveDoesNotTouchTheDatabase`, `ApiFactory` blast radius for #9, null `RemoteIpAddress` in TestServer, explicit cache invalidation because delete uses `ExecuteUpdateAsync`), drain with the `StopAsync` token, surface the `datetime2`/`datetimeoffset` conflict.

## Sources relied on
Code (read in full): `src/UrlShortener.Api/Program.cs`, `Data/LinksDbContext.cs`, `Links/*.cs`, `Redirect/RedirectEndpoint.cs`, `Migrations/20261010213557_InitialCreate.cs`, `tests/UrlShortener.IntegrationTests/**/*.cs` (fixture, factory, all 25 tests), `.github/workflows/ci.yml`, `docker-compose.yml`. Docs: `CLAUDE.md`, `docs/plan.md` (API surface, Data model, Day 2), ADR template, ADR-0001, `docs/scenarios/greenfield.md`, `docs/HANDOFF.md`, issues #9, #14, #15, #16, #17, #18, #19.

Research: report sections "Verdict table" (Bounded Channel + BackgroundService row, Breyer row), "Cache and analytics", "Domain design guidance" (Analytics row), "Risks specific to AI-generated code"; `domain_design_security.md` Q7 and the StopHost item.

Official pages **fetched and confirmed** this session (2026-10-10):

| Page | What it confirmed |
|---|---|
| https://learn.microsoft.com/dotnet/core/extensions/channels | Bounded default is `Wait` ("Calls to `WriteAsync` wait ... `TryWrite` return `false` immediately"); `DropWrite` "Drops the item being written"; `itemDropped` callback; channels in the shared framework since .NET Core 3.0 |
| https://learn.microsoft.com/dotnet/api/system.threading.channels.boundedchannelfullmode | Enum member descriptions (same wording) |
| https://learn.microsoft.com/dotnet/api/system.threading.channels.channel.createbounded | `CreateBounded(options, Action<T>? itemDropped)` overload |
| https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling | Since .NET 6 an exception from `ExecuteAsync` is logged and "By default, the host is stopped"; opt-out is `BackgroundServiceExceptionBehavior.Ignore` |
| https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0 | "the default 30 second shutdown timeout"; the `StopAsync` token "has a default 30 second timeout to indicate that the shutdown process should no longer be graceful"; `StopAsync` might not be called on a crash; a `BackgroundService` needs a scope for a `DbContext` |
| https://learn.microsoft.com/dotnet/api/microsoft.extensions.hosting.hostoptions.shutdowntimeout | Fetched; **does not state the default**, so it is not cited for the 30 s figure |
| https://learn.microsoft.com/dotnet/core/extensions/generic-host | Fetched; says the server shutdown timeout is "default 30s"; not cited (the hosted-services page is) |

**Not confirmed by any fetched page:** what `TryWrite` returns under `DropWrite`. The ADR counts drops via `itemDropped` and has `RedirectStillRedirectsWhenQueueIsFull` assert the counter and that one drop is counted once. Also not sourced: whether `EnableRetryOnFailure`'s execution strategy retries the writer's `SaveChangesAsync` (written as "may"); the order in which the web server and the writer stop (marked "to verify in B2"), and whether TestServer sets `RemoteIpAddress` (marked "probably", for B4 to pin). The 1,000 redirects/s figure behind the capacity is an assumption, stated as one; B5 measures it.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `docs/scenarios/brownfield.md` | generated | Requirement, current-system diagrams, impact table (7 rows), post-Day-2 data flow, dependency graph, 8 open questions (110 lines) |
| `docs/adr/0004-async-click-analytics.md` | generated | MADR; option 2 chosen; schema, capacity 10,000, batch 100 with no timer, drain via `StopAsync` token |
| `docs/ai-log/007-brownfield-impact.md` | generated | this log |
| (proposal) fixed flush interval for the writer | rejected | delays stats and caps throughput at batch/interval; waking on `WaitToReadAsync` and taking up to 100 needs no timer |
| (proposal) count drops from `TryWrite`'s return value | rejected | not documented for `DropWrite`; the `itemDropped` callback is |
| (proposal) set `BackgroundServiceExceptionBehavior.Ignore` | rejected | hides real loop bugs; per-batch catch plus the default `StopHost` keeps failures loud |
| (proposal) re-queue a failed batch | rejected | a poisoned item would loop; at-most-once is stated instead |
| (proposal) store the raw referrer and parse the host in the writer | rejected | the raw referrer would sit in memory; host is extracted in the request, matching the "host only" logging rule |

## Checks run
```
$ git status --short
?? docs/adr/0004-async-click-analytics.md
?? docs/ai-log/007-brownfield-impact.md
?? docs/scenarios/brownfield.md

$ # every relative link: test -f on the %20-decoded target, from the doc's directory
OK   docs/scenarios/brownfield.md -> ../adr/0004-async-click-analytics.md
OK   docs/scenarios/brownfield.md -> ../research/notes/URL%20shortener%20assignment%20validation/domain_design_security.md
OK   docs/adr/0004-async-click-analytics.md -> ../../src/UrlShortener.Api/Redirect/RedirectEndpoint.cs
OK   docs/adr/0004-async-click-analytics.md -> ../plan.md
OK   docs/adr/0004-async-click-analytics.md -> ../research/notes/URL%20shortener%20assignment%20validation/domain_design_security.md
OK   docs/adr/0004-async-click-analytics.md -> ../research/reports/URL%20shortener%20assignment%20validation.md
OK   docs/adr/0004-async-click-analytics.md -> ../scenarios/brownfield.md

$ # existing test names cited: grep -rn "Task <name>(" tests
tests/UrlShortener.IntegrationTests/Links/LinkPersistenceTests.cs:46: public async Task CodeColumnUsesCaseSensitiveCollation()
tests/UrlShortener.IntegrationTests/Links/LinkPersistenceTests.cs:11: public async Task CodesDifferingOnlyByCaseAreDistinctRows()
tests/UrlShortener.IntegrationTests/Links/LinkPersistenceTests.cs:29: public async Task DuplicateCodeIsRejectedByUniqueIndex()
tests/UrlShortener.IntegrationTests/Health/HealthEndpointsTests.cs:12: public async Task LiveDoesNotTouchTheDatabase()
tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs:14: public async Task KnownCodeRedirectsWithFoundAndNoStore()
tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs:42: public async Task UnknownCodeIsNotFoundProblem()
tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs:54: public async Task SoftDeletedCodeIsNotFound()
tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs:30: public async Task CodeDifferingOnlyInCaseIsNotFound()
tests/UrlShortener.IntegrationTests/Links/LinkEndpointsTests.cs:45: public async Task DeleteSoftDeletesSoGetAndRedirectAreNotFound()
tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs:66: public async Task RedirectRouteDoesNotShadowApiOpenApiHealthOrScalar()
tests/UrlShortener.IntegrationTests/Links/LinkEndpointsTests.cs:16: public async Task GetReturnsLinkMetadata()
tests/UrlShortener.IntegrationTests/Links/CreateLinkEndpointTests.cs:102: public async Task RetriesWithNewCodeWhenGeneratedCodeCollides()
tests/UrlShortener.IntegrationTests/Links/CreateLinkEndpointTests.cs:14: public async Task CreatesLinkWithGeneratedCode()
tests/UrlShortener.IntegrationTests/Health/HealthEndpointsTests.cs:25: public async Task ReadyIsHealthyWhenDatabaseIsReachable()
(also cited by class: ProductionErrorPipelineTests (3 tests), CreateLinkEndpointTests (7 tests))

$ grep -n "PostAsJsonAsync\|PostAsync\|ShortUrl" tests/UrlShortener.IntegrationTests/Links/*.cs   (abridged)
CreateLinkEndpointTests.cs:19:  var response = await client.PostAsJsonAsync("/api/links", new { url = "https://example.com/path?q=1" });
CreateLinkEndpointTests.cs:26:  Assert.Equal($"http://localhost/{body.Code}", body.ShortUrl);
CreateLinkEndpointTests.cs:93:  var first = await client.PostAsJsonAsync("/api/links", request);
CreateLinkEndpointTests.cs:94:  var second = await client.PostAsJsonAsync("/api/links", request);
(every other test POSTs at most once; only CreatesLinkWithGeneratedCode asserts ShortUrl)

$ # [Fact]/[Theory] counts per integration test file
ProductionErrorPipelineTests 3, HealthEndpointsTests 2, CreateLinkEndpointTests 7, LinkEndpointsTests 5, LinkPersistenceTests 3, RedirectEndpointTests 5  => 25
```

New test names cited, **to be written** (each has 0 matches in `tests` and `src` today):

| Task | Tests |
|---|---|
| B2 (#15) | `RedirectEnqueuesClickAndWriterPersistsIt`, `RedirectStillRedirectsWhenQueueIsFull`, `ShutdownDrainsPendingClicks`, `WriterSurvivesAFailedBatch`, `MissDoesNotRecordAClick` |
| B3 (#16) | `StatsAggregatesClicksByDayAndReferrer`, `StatsForUnknownCodeIsNotFound`, `StatsForDeletedLinkIsNotFound` |
| B4 (#17) | `CreateOverLimitIsTooManyRequestsWithRetryAfter`, `RedirectIsNotRateLimited` |
| B5 (#18, only if a cache is added) | `DeletedLinkStopsRedirectingWhenCached` |
| #9 | `ShortUrlIgnoresClientHostHeader` |

Build and tests were not run: no code changed.

## Decisions the engineer should question
1. **Analytics dimensions (blocks B2).** Referrer host and truncated UA, no geo, no UA parsing. This fixes the migration.
2. **No IP at all.** The Breyer rationale is secondary legal commentary; "none" also rules out unique-visitor counts.
3. **Capacity 10,000** rests on an assumed 1,000 redirects/s, not a measurement. Smaller (1,000) would be just as defensible for a demo.
4. **Batch 100, no timer.** At low load this is one insert per click, which wakes the database for every redirect; the redirect's own `SELECT` already does, so no extra auto-pause cost, but it is more round-trips than a timed flush.
5. **Keeping `StopHost`.** A bug that escapes the per-batch catch takes the web app down with it. Chosen to fail loudly; `Ignore` is the alternative.
6. **Dropping a failed batch** after the execution strategy's retries, instead of retrying it item by item.
7. **FK with EF's default delete behaviour.** Harmless while links are only soft-deleted; revisit if Day 3 adds hard deletes or purging of expired links.
8. **The impact table's "probably no `RemoteIpAddress` in TestServer"** is not verified; B4 must pin it.

## Gotchas encountered
None added to CLAUDE.md section 9. One documentation gap noted above (`TryWrite` under `DropWrite`) is a candidate for section 9 if B2's test shows a surprise.

## Sign-off
Pending: engineer review of the scenario doc, ADR-0004 and open question 1 before B2 starts. — Mohammed Saalim, YYYY-MM-DD

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`): read both documents in full. Docs only, no code gates. The eight open questions were put to the engineer; the three that shape the design were asked explicitly and the other five accepted on the agent's recommendation.

| Question | Engineer's decision |
|---|---|
| 1 Analytics shape | (b) timestamp + referrer host + user agent truncated to 256 chars; no UA parsing, no geo |
| 2 IP in `Clicks` | none stored; the rate limiter may use the IP in memory only |
| 3 Rate-limit partition before #9 | per-IP fixed window, constant fallback when `RemoteIpAddress` is null, proxy caveat in ADR-0005 |
| 4 Stats for a deleted link | 404 |
| 5 Queue capacity | 10,000 |
| 6 Batch / flush | 100, no timer |
| 7 Retention | none; known gap |
| 8 `OccurredAt` type | `datetimeoffset`; `docs/plan.md` corrected in B2 |

ADR-0004 status set to accepted. Agent points accepted as written: the `RedirectStillRedirectsWhenQueueIsFull` test needs the capacity injectable (a small, justified seam), `docs/TESTING.md` will be created in B2, and the unsourced items (what `TryWrite` returns under `DropWrite`, shutdown ordering, TestServer `RemoteIpAddress`, ACA grace period) stay marked "to verify" in the tasks that own them.
