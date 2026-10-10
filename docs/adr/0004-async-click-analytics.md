# ADR-0004: Record clicks through a bounded in-process channel and a background writer

- Status: accepted (engineer confirmed the analytics shape, no stored IP, and the queue/batch numbers on 2026-10-10)
- Date: 2026-10-10
- Deciders: Mohammed Saalim
- AI assistance: Claude Code drafted options; decision and wording are the engineer's

## Context and problem statement
`GET /{code}` is the hot path: today it is one indexed `SELECT` and a 302 ([`RedirectEndpoint.cs`](../../src/UrlShortener.Api/Redirect/RedirectEndpoint.cs)). Day 2 must record each redirect so `GET /api/links/{code}/stats` can report total clicks, clicks per day and top referrers ([`docs/plan.md`](../plan.md), "API surface"). Recording must not add latency or a new way for the redirect to fail. The service runs as a single replica on Container Apps consumption, scaling to zero, against an Azure SQL free-offer database that must be allowed to auto-pause (CLAUDE.md section 9). How should a click get from the redirect into the database?

## Decision drivers
- The redirect's status, headers and latency stay as they are; a database problem must not turn a 302 into a 500.
- No new Azure resource and no new package (issue #15).
- Same database and the same test style as Day 1: real SQL Server, behaviour asserted over HTTP.
- No connection when idle: probes and idle loops must not keep the database online (CLAUDE.md section 9).
- Privacy: no raw client IP stored (issue #14).

## Considered options
1. Synchronous `INSERT` in the redirect handler.
2. Bounded `System.Threading.Channels` channel plus a `BackgroundService` writer that batch-inserts into a `Clicks` table.
3. Counter column on `Links` (`ClickCount = ClickCount + 1`).
4. External queue (Storage Queue or Service Bus) and a separate consumer.

## Decision outcome
Chosen option: "2. Bounded channel plus background writer", because it keeps the redirect to one read and a non-blocking in-memory write while still producing per-click rows with dimensions, using only the framework and the existing database.

The design B2 (#15) implements:

- **Table `Clicks`**: `Id bigint identity PK`, `LinkId bigint NOT NULL FK → Links(Id)`, `OccurredAt datetimeoffset NOT NULL`, `ReferrerHost nvarchar(253) NULL`, `UserAgent nvarchar(256) NULL`. **No IP column.** Index `(LinkId, OccurredAt)` for the per-link, per-day stats query. `datetimeoffset` matches `Links` (AI log 002); `docs/plan.md` still says `datetime2` and is corrected in B2. The FK keeps the EF default delete behaviour, since links are only soft-deleted today.
- **What the redirect captures**, after a successful lookup only: `LinkId` (the lookup projects `Id` as well as `OriginalUrl`), `OccurredAt` from the injected `TimeProvider`, the **host** of the `Referer` header if it parses as an absolute http/https URL (the raw referrer can carry personal data in its path and query, the same reason only hosts are logged), and `User-Agent` truncated to 256 characters. A referrer host longer than 253 characters is stored as null. Both limits are applied before enqueue, so a hostile header cannot fail a batch on length.
- **`ClickQueue`** (singleton) wraps `Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true }, itemDropped)`. The redirect calls `TryWrite` and never `WriteAsync`. Drops are counted in `itemDropped` (a documented callback) and when `TryWrite` returns `false` (channel already completed during shutdown; the `RedirectStillRedirectsWhenQueueIsFull` test also proves one full-queue drop is counted once, since the docs do not say what `TryWrite` returns under `DropWrite`); the counter is exposed as `ClickQueue.Dropped`.
  - **Capacity 10,000**: worst case about 1.1 KB per item (253 + 256 UTF-16 characters plus a `long` and a `DateTimeOffset`), so about 11 MB at most. At an assumed 1,000 redirects/s it buffers about 10 s of writer stall (a slow insert, a connection retry); B5's k6 run replaces the assumption with a measured rate.
- **`ClickWriterService`** (`BackgroundService`): waits with `WaitToReadAsync`, then `TryRead`s **up to 100** items and inserts them in one `SaveChangesAsync` using a fresh scope (`IServiceScopeFactory.CreateAsyncScope`) and `LinksDbContext`, because a hosted service is a singleton and cannot take a scoped `DbContext` by constructor.
  - **Batch 100, no flush timer**: the writer inserts whatever is queued as soon as it wakes, so stats lag only by queue depth plus one insert (milliseconds at low load), with no timer and no throughput cap; 100 bounds a failed batch's loss and keeps each insert short. A fixed interval was rejected: it delays visibility and caps throughput at batch size divided by the interval.
  - **Failure handling**: each batch is wrapped in `try/catch`; on failure the writer logs the batch size and the exception type and SQL error number only (no field values, no exception message), adds the batch to a failed counter, drops the batch and continues. It never re-queues: a poisoned item would loop. The writer's context inherits `EnableRetryOnFailure` from `Program.cs`, so a transient failure may be retried by the execution strategy before the batch counts as failed (B2 confirms); the queue absorbs redirects meanwhile. No exception may escape `ExecuteAsync`: since .NET 6 that is logged and **by default the host is stopped** ([breaking change](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling); `BackgroundServiceExceptionBehavior.StopHost`). The default is kept rather than switched to `Ignore`, so a real bug in the loop still fails loudly.
  - **Drops are visible**: the writer logs a warning with the number of clicks dropped since its last log whenever `Dropped` has grown (count only). A `System.Diagnostics.Metrics` counter for Log Analytics can be added in B6 if wanted.
  - **Idle means no connection**: the loop waits on the channel, never on a timer, and opens a scope only when it has items. `LiveDoesNotTouchTheDatabase` keeps passing and Azure SQL can auto-pause.
  - **Drain on graceful shutdown**: `StopAsync` first completes the channel writer, then waits for the loop, which reads until the channel is completed and empty. Inserts during the drain use the token passed to `StopAsync` (per the docs it "has a default 30 second timeout to indicate that the shutdown process should no longer be graceful": **30 s**, [hosted services](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)), not `ExecuteAsync`'s `stoppingToken`, which is already cancelled by then.

### Consequences
- Good: the redirect does one read and one non-blocking `TryWrite`; a down database, a full queue or a writer bug cannot change its status or headers.
- Good: per-click rows with referrer host and user agent give `byDay` and `topReferrers` from one table in the same database, and Day 3 can add dimensions without a new store.
- Bad: **delivery is at most once.** Clicks still in memory (up to 10,000) and the batch being written are **lost on a crash, OOM or `SIGKILL`**, because `StopAsync` may not run ([hosted services](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)). A failed batch is dropped, not retried. Clicks are also dropped when the queue is full. Accepted for analytics; stated in the API docs and TESTING.md in B2.
- Bad: stats are **eventually consistent**: a click appears after its batch is inserted (milliseconds normally, longer while the writer is stalled). Tests poll instead of asserting immediately.
- Bad: **one replica assumption.** Each replica has its own queue, which is fine (rows are independent), but scale-in and revisions on Container Apps are shutdowns, so the drain path matters; clicks are lost if the platform kills the container before the drain finishes (the Container Apps termination grace period is not sourced here; B6 checks it).
- Neutral: memory is bounded by capacity × item size (about 11 MB worst case). Retention is not implemented: `Clicks` grows without bound (open question 7 in [the scenario doc](../scenarios/brownfield.md)).
- Neutral / to verify in B2: whether the web server or the writer stops first is not decided here. If the writer completes the channel while requests are still served, those clicks count as dropped; `ShutdownDrainsPendingClicks` covers the drain either way.

### Confirmation
Tests to be written in B2 (#15), all integration tests through `ApiFactory` on real SQL Server; time-based ones poll up to a bound instead of sleeping:
- `RedirectEnqueuesClickAndWriterPersistsIt`: one redirect → one `Clicks` row with the right `LinkId`, `OccurredAt = ApiFactory.Now`, referrer host (not the full URL) and truncated user agent.
- `RedirectStillRedirectsWhenQueueIsFull`: queue capacity 1 and no writer (replaced in `ConfigureTestServices`) → every redirect is still 302 with `no-store`, and `ClickQueue.Dropped` grows. Asserts the counter, not `TryWrite`'s return value (which the docs do not specify for `DropWrite`).
- `ShutdownDrainsPendingClicks`: enqueue N clicks, dispose the factory, assert N rows.
- `WriterSurvivesAFailedBatch`: a batch that violates the FK (a `LinkId` that does not exist) fails, then a valid click is still persisted and the host is still serving.
- `MissDoesNotRecordAClick`: unknown and soft-deleted codes add no rows.
- Existing tests unchanged and green, in particular `KnownCodeRedirectsWithFoundAndNoStore` and `LiveDoesNotTouchTheDatabase`.
- Operational signal: the "clicks dropped" warning log (count only) and `ClickQueue.Dropped`.

## Pros and cons of the options
### 1. Synchronous insert in the redirect handler
- Good: simplest code; no loss on crash; stats immediately consistent.
- Bad: a second database round-trip on every redirect, and an insert failure or timeout fails or slows the redirect unless swallowed, which is the failure mode this ADR exists to avoid.

### 2. Bounded channel plus background writer (chosen)
- Good: redirect latency unaffected; bounded memory; batched inserts; framework-only (`System.Threading.Channels` is in the shared framework since .NET Core 3.0, [Channels](https://learn.microsoft.com/dotnet/core/extensions/channels)).
- Bad: at-most-once delivery and loss on crash; more moving parts to test (drop, drain, failure). The defaults are traps: a bounded channel's default `FullMode` is `Wait`, where `WriteAsync` waits for space ([Channels, bounding strategies](https://learn.microsoft.com/dotnet/core/extensions/channels#bounding-strategies)), and `StopHost` is the default exception behaviour, so both are set or handled explicitly above.

### 3. Counter column on `Links`
- Good: one column, no new table; `totalClicks` is a read.
- Bad: no dimensions, so no `byDay` or `topReferrers`; every redirect becomes a write to the same hot row (contention, [research report](../research/reports/URL%20shortener%20assignment%20validation.md), "Cache and analytics"); still a synchronous write unless queued, which brings back option 2.

### 4. External queue and separate consumer
- Good: durable across crashes; consumers can scale independently; the production shape for large shorteners (Dub and Sink send analytics to a separate engine, research notes Q7).
- Bad: a new Azure resource with its own cost and identity setup, new SDK packages, and a second deployable; the research report calls queue plus OLAP "over-engineering for 2-3 days". The redirect still needs a non-blocking hand-off to the queue client.

## More information
- Research: [report](../research/reports/URL%20shortener%20assignment%20validation.md), "Verdict table" (Bounded Channel + BackgroundService: VERIFIED), "Domain design guidance" (Analytics row), "Risks specific to AI-generated code" (channel and BackgroundService items); [domain notes](../research/notes/URL%20shortener%20assignment%20validation/domain_design_security.md), Q7.
- Official docs confirmed for this ADR (2026-10-10): [Channels](https://learn.microsoft.com/dotnet/core/extensions/channels) (default `Wait`, `DropWrite` "drops the item being written", `itemDropped` callback), [`BoundedChannelFullMode`](https://learn.microsoft.com/dotnet/api/system.threading.channels.boundedchannelfullmode), [`Channel.CreateBounded`](https://learn.microsoft.com/dotnet/api/system.threading.channels.channel.createbounded), [.NET 6 hosting exception handling](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling), [hosted services](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0) (30 s default, scoped services need a scope, `StopAsync` may not run on a crash).
- Impact analysis and open questions: [`docs/scenarios/brownfield.md`](../scenarios/brownfield.md). Privacy rationale for no IP: open question 2 there.
