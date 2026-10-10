# dotnet-urlshortener

A URL shortener built as an **AI-assisted, engineer-led** engineering exercise: the engineer owns every decision and approves every output; Claude Code accelerates implementation, tests, docs and review preparation inside clearly scoped tasks.

> Status: Day 1 complete — the core create/redirect service runs locally. Walkthrough and evidence: `docs/scenarios/greenfield.md`; run it with `docs/SETUP.md`. See `docs/HANDOFF.md` for where work currently stands.

## What this repo demonstrates

| Deliverable | Where |
|---|---|
| Runnable prototype | `src/UrlShortener.Api` (+ `docker compose up`) — see `docs/SETUP.md` |
| Architecture overview | `docs/ARCHITECTURE.md` (Mermaid), decisions in `docs/adr/` |
| Three scenarios: greenfield, brownfield, ambiguous | `docs/scenarios/` — each shows decomposition, execution, validation |
| Setup instructions | `docs/SETUP.md` |
| Testing approach, limitations, trade-offs | `docs/TESTING.md`, `docs/ENGINEERING_SUMMARY.md` |
| AI traceability (generated / edited / rejected + rationale) | `docs/ai-log/` one entry per task; commit trailers `Assisted-by:` and `AI-Log:` |
| Process | GitHub issues → branch per issue → PR with AI log → CI → human sign-off → engineer merges |

## Stack

.NET 10 · ASP.NET Core Minimal APIs · EF Core 10 · SQL Server (Testcontainers for tests, Docker locally, Azure SQL free offer in the cloud) · Azure Container Apps via `azd` + Bicep · xUnit · k6 (perf).

## How AI was used

Every task starts as an issue with intent, constraints, acceptance criteria and risk level. Claude Code works on a branch; each artifact is recorded in `docs/ai-log/NNN-slug.md` as generated, edited (why) or rejected (why), with the checks that were run. High-impact changes (schema, URL validation, rate-limit policy, infra, agent settings) get a security review and an explicit human sign-off before merge. The rules Claude follows live in `CLAUDE.md`.

## Research

The design was validated against primary sources before any code was written: `docs/research/reports/URL shortener assignment validation.md`.

## Plan

`docs/plan.md` — the approved task breakdown, API surface, data model and day-by-day sequencing.
