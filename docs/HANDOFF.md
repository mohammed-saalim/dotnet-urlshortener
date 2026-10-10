# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 1 — G1 (#7), G2 (#8), G3 (#10) merged. G4 done by a delegated agent, reviewed, PR open. Next is G5 (#5), then G6 (#6) closes Day 1.
- **Branch:** `feat/04-redirect`. `main` = G3.
- **Last checks run:** build 0/0, format exit 0, 47 unit + 15 integration tests.
- **New issue #9 (Day 2, high-impact):** public base URL / AllowedHosts / forwarded headers before the Azure deploy.
- **Local DB for `dotnet run`:** engineer must set `MSSQL_SA_PASSWORD` (env or `.env`) and `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` — commands in AI log 002.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues #1–#6 = Day 1 tasks G1–G6, milestones Day 1/2/3, labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow changes made by the engineer during G1:** minimal commits (no trailers), a Walkthrough section in every PR, one fresh subagent per issue (CLAUDE.md §6.4, §6.5, §6.12).

## Decisions made so far
See `docs/plan.md` "Decisions" and `docs/research/reports/URL shortener assignment validation.md`. ADRs 0001-0003 are written in task G6.

## Next step
1. Engineer reviews the G4 PR (low risk), merges.
2. `git checkout main && git pull`, branch `feat/05-api-surface`, delegate G5 (#5). #5 now also owns the error pipeline items deferred from the #3 review (ProblemDetails for malformed JSON in all environments, a `UseEnvironment("Production")` test, optional shared factory). Keep G5 minimal: get/delete endpoints, health, Scalar UI, ProblemDetails, safe logging.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
