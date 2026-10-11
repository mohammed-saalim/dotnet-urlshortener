# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 2 (brownfield). B1 (#14) PR #20 open (needs merge). B2 (#15) click analytics done by a delegated agent, both reviews run (3 fixes), PR open and stacked on #20. Next is B3 (#16) stats endpoint; B4 (#17) can run in parallel.
- **Branch:** `feat/08-click-analytics` (based on `docs/07-brownfield-impact`). `main` = Day 1, 72 tests; this branch 77.
- **Resolved in B2:** `TryWrite` under `DropWrite` returns true and fires `itemDropped`; the web host stops before the writer (TestServer); `EnableRetryOnFailure` does not retry an FK failure. Writer drains on its own token (looping on `stoppingToken` lost every queued click in the red run).
- **Day 2 issues:** #14 B1, #15 B2, #16 B3, #17 B4, #18 B5 (measure first, cache only if justified), #19 B6, #9 base URL. Order: B1 → B2 → B3; B4 independent; B5 after B2; #9 before B6; B6 last.
- **Decisions taken for B2:** see the table in AI log 007 (shape = timestamp + referrer host + truncated UA; no IP; capacity 10,000; batch 100 no timer; stats 404 on deleted; `datetimeoffset`).
- **API now complete for Day 1:** POST/GET/DELETE `/api/links`, `GET /{code}`, `/health/live|ready`, `/openapi/v1.json`, `/scalar` (Development).
- **New issue #9 (Day 2, high-impact):** public base URL / AllowedHosts / forwarded headers before the Azure deploy.
- **Local DB for `dotnet run`:** engineer must set `MSSQL_SA_PASSWORD` (env or `.env`) and `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` — commands in AI log 002.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues #1–#6 = Day 1 tasks G1–G6, milestones Day 1/2/3, labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow changes made by the engineer during G1:** minimal commits (no trailers), a Walkthrough section in every PR, one fresh subagent per issue (CLAUDE.md §6.4, §6.5, §6.12).

## Decisions made so far
See `docs/plan.md` "Decisions" and `docs/research/reports/URL shortener assignment validation.md`. ADRs 0001-0003 are written in task G6.

## Next step
1. Engineer merges PR #20 (B1), then reviews and merges the B2 PR (schema = high-impact sign-off).
2. `git checkout main && git pull`, branch `feat/09-stats`, delegate B3 (#16): one grouped query over `Clicks`, fixed `TimeProvider`, 404 for unknown/deleted, state at-most-once in the endpoint summary. Keep to one new test class (#21 flake risk; now six classes).
3. B4 (#17) is independent of B3 and can be delegated in parallel on its own branch.
3. Still open from Day 1: production migration path (decide in B6), public base URL (#9), optional down-DB readiness test, optional shared `ApiFactory`.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
