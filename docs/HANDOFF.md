# Handoff

Read this first when resuming (any model, any session). Keep it to one screen; update at the end of every session.

## Where we are
- **Phase:** Day 2 (brownfield) complete and **deployed**. Merged: B1–B7 (#20, #22, #23, #24, #26, #29, #30), #9 (#27), #31 (#32: first-deploy fixes). **Live** in centralus (azd env `urlshortener`): create 201, redirect 302 + `private, no-store`, stats, readiness all verified (AI log 015); B7 checks 7/8 pass, 1 not observed (AI log 014). **#33 (trace sampling) is PR-ready on `infra/33-trace-sampling`**: deployed from the branch and verified live (3 of 3 quoted 429 trace ids found, AI log 016).
- **Coordinator model:** Opus 5.5 since 2026-10-10 (before B7).
- **Tests:** 101 (47 unit + 54 integration).
- **Deploy facts:** subscription refuses new SQL servers in eastus2 (centralus works); free-offer DB accepts only the default 60-min auto-pause delay; azd's `AllowAllWindowsAzureIps` reserved-word warning is a false positive; the image lookup lives in `infra/api-image.bicep` (CLAUDE.md §9).
- **ADR numbering:** ADR-0006 = Azure hosting + migrations (+ telemetry section). Day 3 expiry/safety ADR is **ADR-0007**. ADR-0005 still "proposed" (sign-off on 10 per 60 s not noted).
- **Local DB for `dotnet run`:** `MSSQL_SA_PASSWORD` (env or `.env`) + `dotnet user-secrets set "ConnectionStrings:UrlShortener" "..."` (AI log 002). Development sets `PublicUrl:BaseUrl` and `Database:MigrateOnStartup` in `appsettings.Development.json`.
- **GitHub repo:** https://github.com/mohammed-saalim/dotnet-urlshortener (public). Open: #21 shared test container (deferred), #33. Labels `scenario:*`, `type:*`, `risk:high-impact`.
- **Workflow (engineer's rules):** minimal commits (no trailers), Walkthrough in every PR, one fresh subagent per issue (small known-cause fixes done by the coordinator, said in the PR); coordinator reviews, runs gates and both reviews on high-impact work, commits, opens the PR; engineer merges.
- **Teardown:** the engineer will remind the coordinator about one week after the reviewer finishes; then `azd down --force --purge`.

## Decisions made so far
`docs/plan.md` "Decisions", ADRs 0001–0006, AI logs 001–013 (each with a coordinator review table).

## Next step
1. Engineer merges #34 (#33, verified live).
2. Done: Azure SQL round trip measured (lookup p50 1.9 ms; the tail is 0.25 vCPU throttling); still no cache (TESTING.md "On Azure", AI log 017).
3. Day 3: A1 clarification doc + ADR-0007 (expiry → 410 vs 404, "safe"), A2 implementation, A3 perf gate (reuse `perf/redirect-load.js`), A4 ARCHITECTURE.md, ENGINEERING_SUMMARY.md, final CLAUDE.md pass.
4. Housekeeping: `.claude/worktrees/agent-aced098cab8bdaeeb` (locked leftover directory) and the `worktree-agent-*` branches can be deleted.

## Open questions for the engineer
- Azure teardown date: ~1 week after the reviewer finishes (set a reminder when deploying).
- GitHub Project board needs `gh auth refresh -s project` if the scope is missing; labels + milestones are the fallback.

## Session log
| Date | Model | What happened |
|---|---|---|
| 2026-10-10 | Fable 5.1 (planning) / Opus 5.5 (research) | Read brief; four-topic research validated; plan approved; scaffold files written. |
| 2026-10-10 | Fable 5.1 (coordinator) / Opus 5.5 (subagents) | Day 1 G1–G6 and Day 2 B1–B4 merged; B5 and #9 built in parallel worktrees and merged; B6 built, PR opened pending the engineer's `azd up`. |
| 2026-10-11 | Opus 5.5 (coordinator + subagents) | B6 merged (not deployed); B7 telemetry built, reviewed, PR opened. |
| 2026-10-11 | Opus 5.5 (coordinator) | First deploy: three failures fixed in #31 (region, auto-pause delay, circular image lookup); live evidence and B7 checks recorded; #33 trace sampling PR opened. |
