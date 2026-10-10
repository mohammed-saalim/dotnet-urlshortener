# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 1 — G1 (#7), G2 (#8), G3 (#10), G4 (#11) merged. G5 done by a delegated agent, reviewed, PR open. Next is G6 (#6, docs + ADRs 0001-0003), which closes Day 1.
- **Branch:** `feat/05-api-surface`. `main` = G4.
- **Last checks run:** build 0/0, format exit 0, 47 unit + 25 integration tests, no vulnerable packages.
- **API now complete for Day 1:** POST/GET/DELETE `/api/links`, `GET /{code}`, `/health/live|ready`, `/openapi/v1.json`, `/scalar` (Development).
- **New issue #9 (Day 2, high-impact):** public base URL / AllowedHosts / forwarded headers before the Azure deploy.
- **Local DB for `dotnet run`:** engineer must set `MSSQL_SA_PASSWORD` (env or `.env`) and `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` — commands in AI log 002.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues #1–#6 = Day 1 tasks G1–G6, milestones Day 1/2/3, labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow changes made by the engineer during G1:** minimal commits (no trailers), a Walkthrough section in every PR, one fresh subagent per issue (CLAUDE.md §6.4, §6.5, §6.12).

## Decisions made so far
See `docs/plan.md` "Decisions" and `docs/research/reports/URL shortener assignment validation.md`. ADRs 0001-0003 are written in task G6.

## Next step
1. Engineer reviews the G5 PR, merges.
2. `git checkout main && git pull`, branch `docs/06-day1-docs`, delegate G6 (#6): `docs/SETUP.md`, `docs/scenarios/greenfield.md` (decomposition with a Mermaid dependency graph, execution summary pointing at AI logs 001-005, validation evidence), ADR-0001 stack/DB, ADR-0002 short codes/collation, ADR-0003 redirect semantics. Sources: `docs/plan.md`, `docs/research/reports/*.md`, the AI logs. Docs only, no code.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
