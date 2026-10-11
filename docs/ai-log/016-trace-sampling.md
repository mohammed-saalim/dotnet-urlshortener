# AI log 016 — trace-sampling

| Field | Value |
|---|---|
| Issue | #33 |
| Branch | `infra/33-trace-sampling` |
| Date | 2026-10-11 |
| Tool / model | Claude Code / `claude-opus-5-5` (coordinator, no subagent: two configuration values with a known cause) |
| Risk level | high-impact (infra) |

## Intent
A `traceId` the API returns to a caller must always be findable in Application Insights.

## Constraints given to the AI
No code change if configuration does it; stay under the 0.5 GB/day workspace cap; don't over-engineer (engineer: show the real support problem, keep it small).

## Acceptance criteria (runnable)
- Bicep sets `OTEL_TRACES_SAMPLER=microsoft.fixed_percentage` and `OTEL_TRACES_SAMPLER_ARG=1.0`.
- Live: the same burst; every 429's `traceId` is found in `requests`.
- SETUP gains a "find a request by its traceId" query; ADR-0006 records the change and its cost trigger.
- The click write's missing link to its redirect is documented as a known limitation, not built.

## Prompts (summarised, secrets redacted)
1. Engineer asked whether requests are tracked end to end ("correlation id", a workbook). The coordinator queried the live App Insights (AI log 014, "Live checks"): correlation works within a request; two gaps found.
2. Engineer: "Do whatever is right and doesn't overengineer ... I would really like to show the real support problem."

## The problem, as observed
11 POSTs in one burst against the live app (rate limit 10 per 60 s), then one more. The 429 body: `{"title":"Too many requests","status":429,"traceId":"00-8e8d47521d781c8beb9eb58dfbdbe06a-1592fb3366c1bd30-00"}`. Query `requests | where operation_Id == '8e8d47521d781c8beb9eb58dfbdbe06a'` returned no rows. The rate-limit metric counted 2 rejections, while `requests` held one 429 row. Cause: the distro's default rate-limited sampler (5 traces per second since 1.5.0) dropped one of them. The distro also drops the logs of a sampled-out request by default (`EnableTraceBasedLogsSampler`).

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `infra/resources.bicep` | edited | Two env vars next to the existing OTEL one. The environment-variable form is in the distro 1.6.0 README ("Customizing Sampling Behavior", option 3: `microsoft.fixed_percentage`, arg = ratio). Chosen over `options.SamplingRatio = 1F; options.TracesPerSecond = null` in `Program.cs` because sampling is a deployment and cost decision, sitting with the cap it trades against; tests run without an exporter either way. |
| `docs/adr/0006-azure-hosting-and-migrations.md` | edited | Sampling bullet rewritten (what was seen, why a quoted id must be findable, cost, trigger to revisit); new "Known limitation" bullet for the click write. |
| `docs/SETUP.md` | edited | "Find a request from the `traceId` a caller quotes": KQL by `operation_Id` + the portal's Transaction search. |
| `CLAUDE.md` §9 | edited | Gotcha: default sampling makes a returned `traceId` unfindable. |
| `docs/ai-log/014-telemetry.md`, `docs/HANDOFF.md` | edited | B7 live-check results recorded (also posted on PR #30); handoff brought up to the deployed state. |
| (proposal) link the click insert to its redirect (`ActivityLink`) | rejected for now | About 15 lines plus a test. No support question needs it, because clicks are counted, not investigated one by one. Documented with its trigger instead. |
| (proposal) custom workbook | rejected | The portal's end-to-end transaction view and Application map already show a request with its dependencies and logs; already listed under "Not built, deliberately". |

## Checks run
```
$ ~/.azd/bin/bicep lint infra/main.bicep; echo $?
0   (no diagnostics; bicep 0.47.16)
```
No application code changed.

### Live re-check (2026-10-11, after `azd provision` from this branch)
New revision `ca-api-...--0000001` (100 % traffic) carries `OTEL_TRACES_SAMPLER=microsoft.fixed_percentage` and `OTEL_TRACES_SAMPLER_ARG=1.0`. The image is unchanged (`...azurecr.io/.../api-urlshortener:azd-deploy-1791728993`), so `infra/api-image.bicep` kept the deployed image on re-provision.

The burst at 14:56:15 UTC: 13 POSTs → 10 × 201, 3 × 429. The trace ids from the three 429 bodies:
```
requests | where operation_Id in ('185a899f...','782fd4a3...','b4230a90...')
2026-10-11T14:56:19.73Z | POST /api/links/ | 429 | 185a899f3b3b73792488654f60bb980f
2026-10-11T14:56:19.94Z | POST /api/links/ | 429 | 782fd4a342953c876689aee60e307edf
2026-10-11T14:56:20.08Z | POST /api/links/ | 429 | b4230a9020ba757e3cbefe52216fa1ca
requests | where timestamp > 14:56 and name startswith 'POST' | summarize by resultCode
201 | 10
429 | 3
```
**Before:** 1 of 2 429s stored, and the one quoted to the client was missing. **After:** 3 of 3 found by the id the client received, and 13 of 13 requests stored.

## Gotchas encountered
Default trace sampling (CLAUDE.md §9).

## Sign-off
Reviewed the full diff, understood it, accept ownership. — <name>, YYYY-MM-DD
