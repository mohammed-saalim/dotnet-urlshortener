# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 1 — G1 merged (#7). G2 done by a delegated agent, reviewed, PR open. Next is G3 (#3).
- **Branch:** `feat/02-persistence`. `main` = G1.
- **Last checks run:** build 0/0, format exit 0, 3/3 integration tests (Testcontainers), no vulnerable packages, `/code-review` + `/security-review` run (one finding fixed).
- **Local DB for `dotnet run`:** engineer must set `MSSQL_SA_PASSWORD` (env or `.env`) and `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` — commands in AI log 002.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues #1–#6 = Day 1 tasks G1–G6, milestones Day 1/2/3, labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow changes made by the engineer during G1:** minimal commits (no trailers), a Walkthrough section in every PR, one fresh subagent per issue (CLAUDE.md §6.4, §6.5, §6.12).

## Decisions made so far
See `docs/plan.md` "Decisions" and `docs/research/reports/URL shortener assignment validation.md`. ADRs 0001-0003 are written in task G6.

## Next step
1. Engineer reviews the G2 PR (schema = high-impact), signs off, merges.
2. `git checkout main && git pull`, branch `feat/03-create-link`, delegate G3 (#3). Heads-up for G3: `WebApplicationFactory` runs in Development, so the factory must inject the Testcontainers connection string via `UseSetting("ConnectionStrings:UrlShortener", ...)` before the host builds.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
