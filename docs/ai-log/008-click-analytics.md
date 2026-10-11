# AI log 008 — Clicks table, bounded channel and background writer; redirect enqueues

| Field | Value |
|---|---|
| Issue | #15 |
| Branch | `feat/08-click-analytics` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | high-impact (schema) |

## Intent
Record every redirect as a click row without adding latency or failure modes to the redirect path (ADR-0004, "Decision outcome").

## Constraints given to the AI
- Implement ADR-0004 exactly; no redesign, no over-engineering. No new packages.
- `CancellationToken` everywhere; `[LoggerMessage]` (CA1848) with arguments computed into locals (CA1873); log counts, exception type and SQL error number only.
- TDD for queue-full behaviour and shutdown drain. One new test class (`ClickAnalyticsTests`) to limit parallel SQL Server containers (#21).
- Do not touch the `Links` mapping; existing 72 tests unchanged and green.
- No commit/push/PR; no reading `.env`/secrets/`.azure/`.

## Acceptance criteria (runnable)
- Redirect → click row within N ms → `RedirectEnqueuesClickAndWriterPersistsIt` (polls every 50 ms, up to 5 s)
- Queue full → redirect 302 and dropped counter increments → `RedirectStillRedirectsWhenQueueIsFull`
- Host shutdown drains pending clicks → `ShutdownDrainsPendingClicks` (red → green below)
- Writer survives a failed batch → `WriterSurvivesAFailedBatch`
- Misses record nothing → `MissDoesNotRecordAClick`
- Migration applies (startup `MigrateAsync` in every Development test factory and in `SqlServerFixture`); script below shows only `Clicks` DDL
- Existing tests unchanged: 72 → 77, no existing test edited

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #15 per ADR-0004 (entity, queue, writer, redirect enqueue, registrations, `ApiFactory` seams, `TESTING.md`, `plan.md` fix), the five named tests, the hard rules above, the checks to paste, and the three "to verify in B2" items.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `src/UrlShortener.Api/Analytics/Click.cs` | generated | Entity per ADR-0004; length limits as constants shared by the mapping and the redirect's truncation |
| `src/UrlShortener.Api/Analytics/ClickEvent.cs` | generated | Record handed to the queue |
| `src/UrlShortener.Api/Analytics/ClickQueue.cs` | generated | `CreateBounded` with `DropWrite`, `SingleReader = true`, `SingleWriter = false`; drops counted in `itemDropped` and on a `false` `TryWrite`; `Dropped`, `Reader`, `Complete()`; capacity parameter (default 10,000) as the test seam. `[SuppressMessage]` for CA1711 (see Decisions) |
| `src/UrlShortener.Api/Analytics/ClickWriterService.cs` | generated | `BackgroundService`: batch up to 100, scope per batch, per-batch catch, `FailedBatches`, drop-delta warning, drain in `StopAsync`. **Deviates from the brief's loop token** (see Decisions) |
| `src/UrlShortener.Api/Data/LinksDbContext.cs` | edited | `DbSet<Click>`, `Click` mapping (explicit FK since there are no navigations, composite index, max lengths). `Links` mapping untouched |
| `src/UrlShortener.Api/Migrations/20261011001449_AddClicks*.cs`, snapshot | generated (`dotnet ef migrations add AddClicks`) | Not hand-edited. Snapshot diff has no removed lines |
| `src/UrlShortener.Api/Redirect/RedirectEndpoint.cs` | edited | Projects `Id` + `OriginalUrl`; after a hit, `TryWrite(ClickEvent(...))` with host-only referrer and truncated UA; same 302 + headers. Injects `ClickQueue`, `TimeProvider`, `HttpRequest`. `WithSummary` not touched |
| `src/UrlShortener.Api/Program.cs` | edited | `AddSingleton(new ClickQueue())` (instance, so DI never resolves the `int` parameter), `AddHostedService<ClickWriterService>()` |
| `tests/.../ApiFactory.cs` | edited | `ClickQueueCapacity` (init, null = default) replaces the singleton; `ClickWriterEnabled` (init, default true) removes only the `ClickWriterService` descriptor (an unfiltered `RemoveAll<IHostedService>()` would also remove the web server's hosted service) |
| `tests/.../Analytics/ClickAnalyticsTests.cs` | generated | The five ADR-0004 tests |
| `docs/TESTING.md` | generated | Coverage table, known limitations (at-most-once, loss on crash, no retention, per-class containers / #21), Performance placeholder for B5 |
| `docs/plan.md` | edited | `Clicks` line: `datetime2` → `datetimeoffset`, host-only referrer, no IP column |
| `CLAUDE.md` §9 | edited | Three gotchas hit in this task (below) |
| (proposal) loop on `WaitToReadAsync(stoppingToken)` as in the brief | rejected | Loses every queued click at shutdown, measured below (0 of 250 stored) |
| (proposal) shutdown test enqueueing via redirects | rejected | The writer keeps up with sequential redirects, so the test passed with or without a drain; written straight to the queue instead (see Decisions) |
| (proposal) rename `ClickQueue` to satisfy CA1711 | rejected | ADR-0004, the impact analysis and the issue all use the name; a scoped suppression is one attribute |
| (temp) `tests/.../ZzProbe.cs` | generated then deleted | Observations for the three "to verify" items below |

## "To verify in B2" items from ADR-0004 (observed)

1. **What `TryWrite` returns under `DropWrite`.** Probe on a raw `Channel.CreateBounded<int>(capacity 1, DropWrite)` with a counting callback:
   ```
   TryWrite empty=True full=True completed=False callbacks=1
   ```
   A full channel returns **`true`** and calls `itemDropped`; `false` only after completion. Confirmed in the app too: with the callback's increment removed, `RedirectStillRedirectsWhenQueueIsFull` saw `Dropped == 0` (mutation below), so no full-queue drop came through the `false` path. Each drop is therefore counted exactly once (the test asserts `Dropped == 2` for 3 redirects into capacity 1). After shutdown the app's `TryWrite` returned `False dropped=1`.
2. **Whether the web server or the writer stops first.** `factory.Services.GetServices<IHostedService>()` in registration order:
   ```
   Hosted services (registration order): HealthCheckPublisherHostedService, ClickWriterService, GenericWebHostService
   ```
   With minimal hosting the web server's `GenericWebHostService` is registered at `Build()`, after `Program.cs`'s services, and the host stops hosted services in reverse order, so **the web server stops first, then the writer**. Also observed: when `ApplicationStopping` fired, the queue was not yet completed (`queue completed? False`). Requests finishing during shutdown therefore still enqueue before the writer completes the channel. Caveat: observed through `WebApplicationFactory`/TestServer (same generic host and same registration path as Kestrel, but Kestrel itself was not run).
3. **Whether `EnableRetryOnFailure` retried the FK failure.** No. Probe with a capturing logger, one click with `LinkId = -1`:
   ```
   Failed batch after 304 ms
   LOG: [Information Microsoft.EntityFrameworkCore.Database.Command] Executed DbCommand (19ms) [Parameters=[@p0='?' (DbType = Int64), @p1='?' (DbType = DateTimeOffset), @p2='?' (Size = 253), @p3='?' (Size = 256)], ...]
   LOG: [Error Microsoft.EntityFrameworkCore.Update] An exception occurred in the database while saving changes for context type 'UrlShortener.Api.Data.LinksDbContext'.
   LOG: [Warning UrlShortener.Api.Analytics.ClickWriterService] Discarded a batch of 1 clicks: DbUpdateException (SQL error 547)
   Any log contains secret: False
   ```
   One `INSERT` into `Clicks`, the exception is `DbUpdateException` (not `RetryLimitExceededException`), and the batch failed in ~0.3 s including first-connection setup. SQL 547 is not transient, so the execution strategy does not retry. Parameter values are not logged (`?`), and the test referrer/UA strings ("secret") appear in no log line. Note: EF Core itself logs an Error with the exception for every failed save; its FK message names the constraint, table and column, not values.

## Decisions (for the engineer to question)
- **Writer loop token (deviation from the brief, not from the ADR).** The brief said `while (await reader.WaitToReadAsync(stoppingToken))` plus `StopAsync` → `Complete()` → base. `BackgroundService.StopAsync` cancels `stoppingToken` before it awaits `ExecuteAsync`, so the next `WaitToReadAsync`/`SaveChangesAsync` throws and the rest of the queue is lost. Measured with exactly that shape: `Expected: 250, Actual: 0`, three runs out of three. Shipped: a private `CancellationTokenSource` cancelled only when `StopAsync`'s token fires (the 30 s graceful-shutdown bound the ADR names); the loop waits and inserts on it and ends normally when the completed channel is empty. `stoppingToken` is unused. Disposed in `Dispose()` (CA1001).
- **`ShutdownDrainsPendingClicks` writes 250 events straight to the queue** (via the DI singleton) and disposes immediately, instead of N redirects. With redirects the writer races ahead and the test cannot fail; 250 is more than two batches, so a writer that stops reading at shutdown is caught (red output below). The redirect → queue path is covered by the other tests.
- **`FailedBatches` is public on the writer.** `WriterSurvivesAFailedBatch` polls it so the bad click and the valid one can never land in the same batch (which would fail both and flake). The ADR's "failed counter" made it a natural read-only property; it is not exposed anywhere else.
- **FK is `ON DELETE CASCADE`.** The ADR says "EF default delete behaviour"; for a required FK that default is cascade. Links are only soft-deleted today, so it never fires; a future hard delete would delete the link's clicks too. Change to `Restrict` if clicks should block hard deletes.
- **CA1711 suppression on `ClickQueue`** (`*Queue` must derive from `Queue`), with a justification, rather than renaming away from the ADR.
- **Referrer:** `Uri.TryCreate(..., Absolute)` + scheme http/https, `uri.Host` (lower-cased by `Uri`), null if over 253 or missing. A Unicode (IDN) host is stored as `Uri.Host` returns it; not tested.
- **User-Agent:** multiple header values are joined with commas by `StringValues.ToString()` before truncation; an empty header is stored as null. Truncation can split a surrogate pair at character 256 (`nvarchar` stores it).
- **Drop warning is emitted after each batch** when `Dropped` grew. If drops happen and then traffic stops entirely, the warning waits for the next batch (no timer, by design). The warning was not exercised by any test or probe (the queue-full test disables the writer).
- **Shutdown-timeout branch not exercised.** If the 30 s token fires mid-insert, SqlClient may surface it as a `SqlException` inside `DbUpdateException` rather than an `OperationCanceledException`; the writer then logs one "Discarded a batch" warning, and the next `WaitToReadAsync` throws on the cancelled token and the loop exits cleanly. Expected, not a bug.
- **API docs:** ADR-0004 says at-most-once delivery is "stated in the API docs and TESTING.md in B2". Done in `TESTING.md`; the OpenAPI text was not changed (redirect `WithSummary` untouched). B3's stats endpoint is the natural place for an OpenAPI description saying counts are at-most-once.

## Checks run

**Red 1: tests written first, nothing implemented** (`dotnet build -c Release`):
```
ClickAnalyticsTests.cs(7,24): error CS0234: The type or namespace name 'Analytics' does not exist in the namespace 'UrlShortener.Api' (are you missing an assembly reference?)
ClickAnalyticsTests.cs(135,29): error CS0246: The type or namespace name 'Click' could not be found (are you missing a using directive or an assembly reference?)
```

**Green: first run of the five new tests:**
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 2 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Red 2: drain mutations** (restored afterwards). (a) Loop on `stoppingToken` and no `Complete()` in `StopAsync`; (b) the brief's shape, `Complete()` kept but loop on `stoppingToken`, run three times:
```
(a)  Failed UrlShortener.IntegrationTests.Analytics.ClickAnalyticsTests.ShutdownDrainsPendingClicks [1 s]
     Expected: 250
     Actual:   0
(b)  Expected: 250 / Actual: 0   (x3)
     Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Red 3: queue-full mutation** (`itemDropped` callback no longer increments; restored afterwards):
```
  Failed UrlShortener.IntegrationTests.Analytics.ClickAnalyticsTests.RedirectStillRedirectsWhenQueueIsFull [1 s]
Expected: 2
Actual:   0
Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2, Duration: 1 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Migration** (`dotnet ef migrations script 20261010213557_InitialCreate AddClicks --project src/UrlShortener.Api`, without `--no-build`):
```
BEGIN TRANSACTION;
CREATE TABLE [Clicks] (
    [Id] bigint NOT NULL IDENTITY,
    [LinkId] bigint NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [ReferrerHost] nvarchar(253) NULL,
    [UserAgent] nvarchar(256) NULL,
    CONSTRAINT [PK_Clicks] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Clicks_Links_LinkId] FOREIGN KEY ([LinkId]) REFERENCES [Links] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_Clicks_LinkId_OccurredAt] ON [Clicks] ([LinkId], [OccurredAt]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261011001449_AddClicks', N'10.0.12');

COMMIT;
GO
```
Nothing on `Links`; no separate `IX_Clicks_LinkId` (the composite index covers the FK). `git diff` of `LinksDbContextModelSnapshot.cs` has no removed lines.

**Baseline before any change:** 47 unit + 25 integration passed.

**Final gates:**
```
$ dotnet build -c Release --nologo -v q
    0 Warning(s)
    0 Error(s)
$ dotnet format --verify-no-changes --no-restore; echo $?
0
$ dotnet test -c Release --nologo
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 175 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    30, Skipped:     0, Total:    30, Duration: 3 s - UrlShortener.IntegrationTests.dll (net10.0)
$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.
$ git status --short
 M CLAUDE.md
 M docs/plan.md
 M src/UrlShortener.Api/Data/LinksDbContext.cs
 M src/UrlShortener.Api/Migrations/LinksDbContextModelSnapshot.cs
 M src/UrlShortener.Api/Program.cs
 M src/UrlShortener.Api/Redirect/RedirectEndpoint.cs
 M tests/UrlShortener.IntegrationTests/ApiFactory.cs
?? docs/TESTING.md
?? src/UrlShortener.Api/Analytics/
?? src/UrlShortener.Api/Migrations/20261011001449_AddClicks.Designer.cs
?? src/UrlShortener.Api/Migrations/20261011001449_AddClicks.cs
?? tests/UrlShortener.IntegrationTests/Analytics/
```
(Taken before this log was written; it also shows in the final status.) Integration suite re-run three more times: 30/30 each time.

Tests: 72 → 77 (+5 integration). No existing test edited; `KnownCodeRedirectsWithFoundAndNoStore` and `LiveDoesNotTouchTheDatabase` pass unchanged (the writer is running and idle in the latter, against an unreachable database).

## Gotchas encountered
Added to CLAUDE.md §9: `BackgroundService.StopAsync` cancels `stoppingToken` before awaiting `ExecuteAsync` (drain lost 0/250); `TryWrite` returns `true` when `DropWrite` drops; CA1711 on `*Queue` type names.

## Open items
- High-impact (schema): `/security-review` and a fresh-context `/code-review` before merge (CLAUDE.md §6.6); not run by the subagent.
- Engineer: cascade vs restrict on `FK_Clicks_Links_LinkId`.
- B3: OpenAPI wording for at-most-once counts on the stats endpoint.
- B6: Container Apps termination grace period vs the 30 s drain.
- `docs/HANDOFF.md` not updated by the subagent (coordinator's end-of-session step).

## Sign-off
(pending engineer review of the PR)

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`); gates re-run independently: build -c Release 0/0, format exit 0, 47 unit + 30 integration, no vulnerable packages. High-impact (schema), so both review tools ran.

**`/security-review` (fresh Opus agent): no HIGH/MEDIUM findings.** Traced `Referer` and `User-Agent` from request to database to logs: EF parameterises everything; both values are bounded before enqueue to the column lengths; no IP or full referrer stored; the only new logs carry counts, exception type and SQL error number; `Uri.TryCreate` cannot throw; `TryWrite` never waits; nothing escapes `ExecuteAsync`; the cascade FK cannot be reached from user input (soft delete only).

**`/code-review medium` (fresh context): 5 findings, triaged:**

| Finding | Outcome | What changed |
|---|---|---|
| Drops counted after the last batch (e.g. `TryWrite` after `Complete()` during shutdown) were never logged | **fixed** | `LogNewDrops()` now also runs in a `finally` after the loop |
| `ReferrerHost` could store `""` for `Referer: http:///path` (empty host passes the length check) | **fixed** | `!string.IsNullOrEmpty(uri.Host)` guard, mirroring `CreateLinkValidator` |
| `ShutdownDrainsPendingClicks` leaks the host if an assertion fails; may pass on a fast machine for a weaker regression | **partly fixed** | `await using` added (explicit dispose kept as the shutdown under test). The "wrong token only in `WaitToReadAsync`" weakness is accepted: the recorded red run (0 of 250 rows) shows the test does catch the realistic regression |
| `ClickEvent` is a twin of `Click`; enqueue the entity directly | declined | One record + one copy loop is the cost of keeping the queue message independent of the EF entity (no `Id`, no tracking concerns). Revisit only if a dimension is added |
| Shutdown mechanism could be shorter (`ExecuteTask.WaitAsync`) at the cost of not cancelling an in-flight insert on timeout | declined | The agent's design was tested (drain red → green); cancelling a hung insert at the 30 s limit is the safer property. Trade-off noted here |

Agent decisions accepted: own CTS for the drain (the `stoppingToken` red run is the evidence), `ClickWriterEnabled`/`ClickQueueCapacity` test seams, `FailedBatches` public for the FK test, CA1711 suppression with justification, cascade FK left at EF's default (unreachable while deletes are soft; switch to `Restrict` if a hard delete is ever added).

```
$ dotnet build -c Release --nologo -v q
    0 Warning(s)   0 Error(s)
$ dotnet test -c Release --no-build --nologo
Passed!  - 47 unit, 30 integration
```
