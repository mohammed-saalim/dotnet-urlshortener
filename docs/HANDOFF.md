# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 2 (brownfield). Merged: B1 (#20), B2 (#22), B3 (#23), B4 (#24). Built in parallel in isolated worktrees and now in PRs: **B5 (#18, `feat/12-perf-measure`, docs + k6 only, no cache: see `docs/TESTING.md` Performance)** and **#9 (`feat/11-public-base-url`, high-impact: `PublicUrl:BaseUrl` option, `TrustForwardedHeaders` switch, `appsettings.Production.json` with `AllowedHosts: localhost`)**. Merge B5 first, then #9 after sign-off; whichever lands second may need GitHub's "update branch" (both touch `CLAUDE.md` §9 and `docs/TESTING.md`, non-adjacent hunks).
- **Tests:** `main` 86 (47 unit + 39 integration); #9 adds 5 integration (`PublicUrlTests`, one more SQL Server container per run; if CI flakes like PR #20, that is the trigger for #21).
- **Found in B5, not fixed:** EF Core logs every SQL command at `Information` in every environment (issue #25; fold into B6).
- **Decisions open for the engineer in PR #9:** `AllowedHosts` fail-closed placeholder vs deriving the host from `PublicUrl:BaseUrl`; `TrustForwardedHeaders=true` trusts any peer (safe only when the Container Apps ingress is the sole path in); `ForwardLimit` 1.
- **ADR-0005** stays "proposed" until the engineer notes sign-off on 10 per 60 s (PR #24 merged without that line being updated).
- **Local DB for `dotnet run`:** engineer must set `MSSQL_SA_PASSWORD` (env or `.env`) and `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` — commands in AI log 002. Development now also needs `PublicUrl:BaseUrl`, set in `appsettings.Development.json` (`http://localhost:5078`).
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Issues: #1–#6 Day 1, #14–#19 Day 2 (B1–B6), #9 base URL, #21 shared test container (deferred), #25 EF logging. Labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow (engineer's rules):** minimal commits (no trailers), Walkthrough in every PR, one fresh subagent per issue, parallel issues in `isolation: "worktree"`; coordinator reviews, runs gates, commits, opens the PR; engineer merges.

## Decisions made so far
`docs/plan.md` "Decisions", ADRs 0001–0005, AI logs 001–012 (each with a coordinator review table). B5: no cache, triggers to revisit written in `docs/TESTING.md`.

## Next step
1. Engineer merges B5 (#18), then #9 after signing off the three decisions above.
2. **B6 (#19)**: azd + Bicep (Container Apps consumption, Azure SQL free offer, Log Analytics, managed identity, passwordless SQL), production migration decision, the telemetry add-on (OpenTelemetry → App Insights, noted on #19), cost guardrails, teardown reminder (~1 week after review). Checklist from #9 and B5: set `PublicUrl__BaseUrl`, `AllowedHosts` and `PublicUrl__TrustForwardedHeaders=true`; check which `Host` the Container Apps probes send (host filtering applies to them); confirm the ingress appends exactly one `X-Forwarded-For` entry; fix #25; measure the Azure SQL round trip from the container (B5's first cache trigger).
3. Day 3: A1 clarification doc + ADR-0006 (expiry → 410 vs 404, "safe"), A2 implementation, A3 perf gate (reuse `perf/redirect-load.js`), A4 ARCHITECTURE.md, ENGINEERING_SUMMARY.md, final CLAUDE.md pass.
4. Housekeeping: `git worktree remove .claude/worktrees/<name>` + `git worktree prune` once branches merge; `agent-aced098cab8bdaeeb` is a locked leftover directory that needs deleting by hand.
5. Still open from Day 1: production migration path (B6), optional down-DB readiness test, optional shared `ApiFactory`.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
| 2026-10-10 | Fable 5.1 (coordinator) / Opus 5.5 (subagents) | Day 1 G1–G6 and Day 2 B1–B4 merged; B5 and #9 built in parallel worktrees and opened as PRs. |
