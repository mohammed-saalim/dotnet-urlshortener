# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 1 complete once the G6 docs PR merges: G1 (#7), G2 (#8), G3 (#10), G4 (#11), G5 (#12) merged; G6 (docs: SETUP, greenfield scenario, ADR-0001..0003) written by a Sonnet agent, reviewed, PR open. Next is Day 2 (brownfield): B1 impact analysis + ADR-0004, then B2 click analytics.
- **Branch:** `docs/06-day1-docs`. `main` = G5.
- **Last checks run:** build -c Release 0/0, format exit 0, 47 unit + 25 integration tests, no vulnerable packages; SETUP.md cold-run in a scratch clone (result in AI log 006).
- **API now complete for Day 1:** POST/GET/DELETE `/api/links`, `GET /{code}`, `/health/live|ready`, `/openapi/v1.json`, `/scalar` (Development).
- **New issue #9 (Day 2, high-impact):** public base URL / AllowedHosts / forwarded headers before the Azure deploy.
- **Local DB for `dotnet run`:** engineer must set `MSSQL_SA_PASSWORD` (env or `.env`) and `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` — commands in AI log 002.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues #1–#6 = Day 1 tasks G1–G6, milestones Day 1/2/3, labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow changes made by the engineer during G1:** minimal commits (no trailers), a Walkthrough section in every PR, one fresh subagent per issue (CLAUDE.md §6.4, §6.5, §6.12).

## Decisions made so far
See `docs/plan.md` "Decisions" and `docs/research/reports/URL shortener assignment validation.md`. ADRs 0001-0003 are written in task G6.

## Next step
1. Engineer reviews the G6 docs PR, merges. Day 1 done.
2. Create the Day 2 issues from `docs/plan.md` (B1-B7 plus existing #9), milestone "Day 2 - Brownfield + Azure". B1 (impact analysis doc + ADR-0004) is docs-first and high-impact (schema follows in B2): do it before any Day 2 code.
3. Open questions carried into Day 2: production migration path (ADR-0001 consequence), public base URL (#9), whether `/health/ready` needs a down-DB test, shared `ApiFactory` per class (optional).

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
