# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 0 — scaffold (task G0 in `docs/plan.md`). No code yet.
- **Branch:** `main` (scaffold only; all code goes on feature branches).
- **Last checks run:** `git check-ignore -v docs/brief/*.pdf` → ignored.
- **GitHub repo:** not created yet (public, `gh repo create`).

## Decisions made so far
See `docs/plan.md` "Decisions" and `docs/research/reports/URL shortener assignment validation.md`. ADRs 0001-0003 are written in task G6.

## Next step
1. Restart Claude Code from the repo root (rename `Docs` → `docs` needs the cwd outside it; Docker/azd need a fresh PATH).
2. `git add -A && git commit` scaffold on `main`; `gh repo create` public; push.
3. Create labels, milestones (Day 1/2/3) and Day 1 issues (G1-G6) from `docs/plan.md`.
4. Open `feat/01-solution-scaffold` for G1.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
