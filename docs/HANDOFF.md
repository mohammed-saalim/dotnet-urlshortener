# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 2 (brownfield). Day 1 fully merged (#7, #8, #10, #11, #12, #13). B1 (#14: impact analysis + ADR-0004) written, engineer answered the open questions, PR open. Next is B2 (#15) click analytics.
- **Branch:** `docs/07-brownfield-impact`. `main` = Day 1 complete, 72 tests.
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
1. Engineer reviews the B1 PR (ADR-0004 is the decision record for the schema B2 adds), merges.
2. `git checkout main && git pull`, branch `feat/08-click-analytics`, delegate B2 (#15) with ADR-0004 as the spec and its five named tests as the acceptance criteria; B2 also creates `docs/TESTING.md` (loss-on-crash statement) and corrects `docs/plan.md` (`datetime2` → `datetimeoffset` for `Clicks`).
3. Still open from Day 1: production migration path (decide in B6), public base URL (#9), optional down-DB readiness test, optional shared `ApiFactory`.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
