# AI log 009 — GET /api/links/{code}/stats

| Field | Value |
|---|---|
| Issue | #16 |
| Branch | `feat/09-stats` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | low |

## Intent
Expose the click analytics recorded by B2: total clicks, clicks per day for the last 7 days, top 5 referrer hosts (issue #16, `docs/plan.md` "API surface").

## Constraints given to the AI
- Minimal: one endpoint file, one test class, no caching, no pagination, no new packages, no hand-written SQL.
- Touch `Program.cs` only for the one `MapGet` in the `/api/links` group; do not touch `ApiFactory.cs`, `CreateLinkEndpoint.cs` or other test files (B4 is being built in parallel).
- `TypedResults`, `CancellationToken`, ProblemDetails 404 for unknown or soft-deleted codes (brownfield decision 4). Day boundaries UTC from the injected `TimeProvider`.
- No commit/push/PR; no reading `.env`/secrets/`.azure/`.

## Acceptance criteria (runnable)
- Integration test on aggregates with a fixed `TimeProvider` → `StatsAggregatesClicksByDayAndReferrer` (`ApiFactory.Now`)
- 404 for unknown code → `StatsForUnknownCodeIsNotFound` (problem+json)
- 404 for a soft-deleted code (brownfield decision 4) → `StatsForDeletedLinkIsNotFound` (red run below)
- Zero clicks → 200, total 0, seven zero days, no referrers → `StatsForLinkWithoutClicksIsZeroFilled`
- Top 5 cut-off and tie order → `StatsKeepsTopFiveReferrersByCountThenHost`
- Existing tests unchanged: 77 → 82, no existing test edited

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #16 with the response shape `LinkStatsResponse(Code, TotalClicks, ByDay, TopReferrers)`, the three-query approach, the UTC day rule, the named tests and data set, the docs to update, the checks to paste, and the file restrictions above.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `src/UrlShortener.Api/Analytics/GetStatsEndpoint.cs` | generated | Response records and handler. File name from the B3 row of the brownfield impact table. Four queries: link id, `COUNT_BIG`, by-day, top referrers (SQL below) |
| `src/UrlShortener.Api/Program.cs` | edited (+1 statement, 2 lines) | `links.MapGet("/{code}/stats", ...)` with `GetLinkStats` and the summary from the brief, wrapped like the redirect mapping |
| `tests/UrlShortener.IntegrationTests/Analytics/LinkStatsTests.cs` | generated | Five tests; links and clicks inserted through `fixture.CreateDbContext()`, so no polling |
| `docs/TESTING.md` | edited | One row in the coverage table |
| (temp) `tests/.../Analytics/ZzStatsProbe.cs` | generated then deleted | Captured the SQL EF sent for one request (below) |
| (option) group by `new { Year, Month, Day }` or group in memory | rejected | Not needed: `GroupBy(c => c.OccurredAt.Date)` translates to `CONVERT(date, ...)` on the server |
| (option) one combined query for everything | rejected | The brief allowed small queries; three readable LINQ queries beat one clever one. All are covered by the `(LinkId, OccurredAt)` index or a `LinkId` seek on it |

## Decisions (for the engineer to question)
- **UTC days via `CONVERT(date, OccurredAt)`.** SQL Server's `CONVERT(date, datetimeoffset)` returns the date *in the stored offset*, not in UTC. It is the UTC date here only because every `OccurredAt` comes from `TimeProvider.GetUtcNow()` (offset `+00:00`). A row written with another offset would be bucketed by its local date. Stated in a code comment. If that ever matters, group on `SWITCHOFFSET` (not available in LINQ without a custom function) or normalise on write. Not a §9 gotcha yet, because nothing writes non-zero offsets.
- **`TotalClicks` and `TopReferrers` are all-time; only `ByDay` is the 7-day window.** That is how the brief and `plan.md` read (`{totalClicks, byDay[7], topReferrers[5]}`). The aggregate test has one click eight days ago with a referrer to pin this down. If "top referrers in the last 7 days" was meant, it is a one-line `Where`.
- **The window has no upper bound.** A click dated after today (clock skew) is in the total but not in `ByDay`, because the zero-fill only looks up the seven days. Not worth a clause.
- **Tie order follows the column collation**, `nvarchar` with the database default (case-insensitive), not C# ordinal. Hosts come from `Uri.Host`, which is lower-case, so the two agree in practice.
- **Fourth query for the link id.** The brief's "three small EF queries" are the aggregates; the link lookup is a separate `SELECT TOP(2) [Id]`, matching `GetLinkEndpoint`. A join would save a round trip but needs the 404 decided from an empty result, which is less clear.
- **Fifth test beyond the brief** (`StatsKeepsTopFiveReferrersByCountThenHost`): the brief's data set has two hosts, so neither `Take(5)` nor the tie-break by host would be caught otherwise. This is why integration is 35, not 34.
- **Aggregate test uses boundary instants** inside the brief's counts: one click at exactly 00:00 UTC today and one at the last tick of yesterday, so an off-by-one on the day boundary fails the test.
- **OpenAPI summary** carries the at-most-once note asked for in AI log 008's open items, pointing at `docs/TESTING.md`. Day boundaries being UTC is in the code comment and `TESTING.md`, not in the summary (kept to the brief's text).
- **No logging** in the handler: a 404 here is not interesting enough to log.

## Checks run

**Generated SQL** (temporary probe: one request through `ApiFactory` with a capturing logger, `Executed DbCommand` messages):
```
SELECT TOP(2) [l].[Id]
FROM [Links] AS [l]
WHERE [l].[Code] = @code AND [l].[DeletedAt] IS NULL

SELECT COUNT_BIG(*)
FROM [Clicks] AS [c]
WHERE [c].[LinkId] = @id

SELECT [c0].[Key] AS [Day], COUNT_BIG(*) AS [Clicks]
FROM (
    SELECT CONVERT(date, [c].[OccurredAt]) AS [Key]
    FROM [Clicks] AS [c]
    WHERE [c].[LinkId] = @id AND [c].[OccurredAt] >= @start
) AS [c0]
GROUP BY [c0].[Key]

SELECT TOP(@p) [c].[ReferrerHost], COUNT_BIG(*)
FROM [Clicks] AS [c]
WHERE [c].[LinkId] = @id AND [c].[ReferrerHost] IS NOT NULL
GROUP BY [c].[ReferrerHost]
ORDER BY COUNT_BIG(*) DESC, [c].[ReferrerHost]
```
Everything is evaluated on the server; no client evaluation, no warning. Parameter values are logged as `?`. Probe response body for a link with no clicks:
```
200 {"code":"Probe01","totalClicks":0,"byDay":[{"date":"2026-10-04","clicks":0},...,{"date":"2026-10-10","clicks":0}],"topReferrers":[]}
```

**Green, first run of the new class:**
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 2 s - UrlShortener.IntegrationTests.dll (net10.0)
```

**Red: soft-delete mutation** (`&& l.DeletedAt == null` removed from the link lookup; restored afterwards):
```
  Failed UrlShortener.IntegrationTests.Analytics.LinkStatsTests.StatsForDeletedLinkIsNotFound [99 ms]
Expected: NotFound
Actual:   OK
Failed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5, Duration: 1 s
```

**Final gates:**
```
$ dotnet build -c Release --nologo -v q
    0 Warning(s)
    0 Error(s)
$ dotnet format --verify-no-changes --no-restore; echo $?
0
$ dotnet test -c Release --nologo
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 501 ms - UrlShortener.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    35, Skipped:     0, Total:    35, Duration: 4 s - UrlShortener.IntegrationTests.dll (net10.0)
$ git status --short
 M docs/TESTING.md
 M src/UrlShortener.Api/Program.cs
?? src/UrlShortener.Api/Analytics/GetStatsEndpoint.cs
?? tests/UrlShortener.IntegrationTests/Analytics/LinkStatsTests.cs
$ git diff --stat
 docs/TESTING.md                 | 1 +
 src/UrlShortener.Api/Program.cs | 2 ++
 2 files changed, 3 insertions(+)
```
(Taken before this log was written; the log is the fifth new file.) Tests: 77 → 82 (+5 integration), no existing test edited.

## Gotchas encountered
None added to CLAUDE.md §9. `DateTimeOffset.Date` translated without trouble; the offset caveat is recorded under Decisions.

## Open items
- `docs/HANDOFF.md` not updated by the subagent (coordinator's end-of-session step).
- Low risk, so `/security-review` is not required; a fresh-context `/code-review` is the coordinator's call.

## Sign-off
(pending engineer review of the PR)

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`); gates re-run independently in the agent's worktree: build -c Release 0/0, format exit 0, 47 unit + 35 integration. Low risk per CLAUDE.md §6.6, so no `/security-review` or `/code-review`. Built in parallel with B4 (#17) in a separate worktree; `Program.cs` touched by two added lines only.

| Agent decision | Outcome | Rationale |
|---|---|---|
| Fifth test for the top-5 cut-off and host tie-break | accepted | The brief's data could not exercise either |
| Four queries (id lookup + three aggregates) | accepted | Mirrors `GetLinkEndpoint`'s 404 decision; all parameterised, all indexed by `(LinkId, OccurredAt)` |
| Days are UTC because stored offsets are +00:00 | accepted, documented | A non-zero stored offset would shift days silently; the writer only ever stores `GetUtcNow()` |
| `TotalClicks`/`TopReferrers` all-time, `ByDay` 7 days | accepted | Matches the planned response shape; a 7-day referrer window is a one-line change if wanted |
| Referrer ties ordered by DB collation | accepted | `Uri.Host` is lower-case, so no visible effect |
| No upper bound on the day window | accepted | Future-dated clicks cannot occur (writer uses the injected clock) |
