# Plan: AI-Assisted URL Shortener (interview assignment)

## Context

A 2-3 day take-home: build a runnable URL shortener that demonstrates **engineer-led, AI-accelerated** execution across greenfield, brownfield and ambiguous scenarios, with traceability (generated/edited/rejected + rationale), quality gates, secure AI usage and human sign-off. Graded on judgement and defensibility as much as code. Everything will be questioned in an interview, so every non-obvious choice gets an ADR and every AI-touched task gets a log entry.

Workspace: `C:\Users\moham\Documents\dotnet-urlshortener` (currently only `Docs/` with the brief, `research_notes/`, `reports/`). Not a git repo yet. Research validation report: `reports/URL shortener assignment validation.md`.

**Decisions the user has made (this session):** .NET 10 · Docker + Testcontainers · Bicep via `azd`, cost-conscious · public GitHub repo · `Assisted-by:` trailer · Azure deploy kept ~1 week after reviewer finishes, then `azd down` · GitHub issues board, progressive commits · TDD only where it matters · CLAUDE.md maintained with gotchas · DB choice delegated to me · short-code design delegated to me.

**Decisions I'm making (each gets an ADR):**
- **DB: SQL Server everywhere, single provider.** Testcontainers.MsSql for tests, `docker compose` for local run, Azure SQL free offer in the cloud. No SQLite switch: one migration path, real collation behaviour in tests, and the "no configurability not asked for" rule. Trade-off: reviewer needs Docker Desktop (documented in SETUP.md).
- **Short codes: 7-char Base62 from `RandomNumberGenerator.GetString`, column `varchar(10)` with collation `Latin1_General_100_CS_AS`, unique index, insert-and-retry on unique violation (no check-then-insert).** A Testcontainers test asserting `abc ≠ ABC` on real SQL Server is the artifact that proves the collation trap was caught. Alternative (Crockford base32, case-insensitive) recorded as rejected.
- **302 + `Cache-Control: private, no-store`** on redirects.
- Report questions 6-10 (mutability, 404 vs 410, analytics depth/IP handling, Idempotency-Key, reputation) are **not** pre-decided: they are the raw material of the brownfield and ambiguous scenarios.

---

## Toolchain status (checked 2026-10-10)

| Tool | Status |
|---|---|
| .NET SDK 10.0.101 | present |
| Docker 29.8.2 | installed; **daemon not running yet**; this shell needs a restart to see it on PATH |
| azd 1.35.1 | installed (bundles Bicep); shell restart needed |
| az CLI | not installed; optional (azd handles login/deploy) |
| gh 2.82 | signed in as `mohammed-saalim` |
| git, Node 22 | present |

Prerequisite before Day 1 build tasks: start Docker Desktop; restart Claude Code (or terminal) so `docker`/`azd` resolve.

---

## Repo layout

```
dotnet-urlshortener/
  .claude/settings.json          # deny Read(**/.env, **/secrets.json); Stop hook; attribution.commit
  .github/
    ISSUE_TEMPLATE/task.md       # intent / constraints / acceptance criteria / risk level
    PULL_REQUEST_TEMPLATE.md     # AI-log link, checks run, sign-off checkbox
    workflows/ci.yml             # build, format, test (Testcontainers on ubuntu), vuln audit, coverage
  .gitignore                     # docs/*.pdf, [Dd]ocs/*.pdf, .env, bin/obj, *.user
  CLAUDE.md                      # pasted guidelines + project conventions + gotchas (living)
  README.md                      # what/why, links to all docs
  Directory.Build.props          # TreatWarningsAsErrors, AnalysisLevel, CA5394=error, Nullable
  Directory.Packages.props       # central package versions (pinned)
  .editorconfig
  docker-compose.yml             # SQL Server 2022 container for local run
  azure.yaml                     # azd
  infra/                         # main.bicep, modules (sql, containerapp, log analytics)
  src/UrlShortener.Api/          # Minimal API; feature folders: Links/, Redirect/, Analytics/, Health/
  tests/UrlShortener.UnitTests/
  tests/UrlShortener.IntegrationTests/   # WebApplicationFactory + Testcontainers.MsSql
  perf/redirect-load.js          # k6 script, run via docker image grafana/k6
  docs/
    brief/                       # the PDF (gitignored)
    research/                    # moved research_notes/ + reports/
    adr/0001-....md              # MADR 4.0, hand-written
    ai-log/NNN-slug.md           # one per task
    scenarios/{greenfield,brownfield,ambiguous}.md
    ARCHITECTURE.md              # Mermaid diagrams: components, control flow, redirect + click data flow
    SETUP.md  TESTING.md  HANDOFF.md  ENGINEERING_SUMMARY.md  plan.md (copy of this)
```

Housekeeping before `git init`: rename `Docs` → `docs/brief`, move `research_notes/` and `reports/` to `docs/research/`. Verify with `git check-ignore -v docs/brief/*.pdf` before the first commit.

---

## Workflow (the "process" deliverable)

1. **Issue first.** Every task is a GitHub issue using the template (intent, constraints, acceptance criteria, technical context, risk level). Labels: `scenario:greenfield|brownfield|ambiguous`, `type:feature|refactor|bug|docs|infra`, `risk:high-impact`. Milestones: Day 1/2/3. A GitHub Project board (Todo / In progress / In review / Done) if `gh` has the `project` scope; otherwise labels + milestones suffice.
2. **Branch per issue:** `feat/NN-slug`, `fix/NN-slug`, `docs/NN-slug`. Only the initial scaffold commit (gitignore, README, CLAUDE.md, docs skeleton — no code) lands on `main` directly.
3. **AI log per task** (`docs/ai-log/NNN-slug.md`): tool+model, intent, constraints, acceptance criteria, prompts (summarised, redacted), outcome per artifact (**generated / edited-why / rejected-why**), checks run (commands, exit codes, test counts), risk level, human sign-off line.
4. **Commit trailers** (human-written messages): `Assisted-by: Claude Code (<model>)` and `AI-Log: docs/ai-log/NNN-slug.md`. No `Co-Authored-By`; no `Signed-off-by` from AI. Configure via `attribution.commit` in `.claude/settings.json` (verify exact schema with the `update-config` skill at implementation).
5. **PR per branch**, template filled, CI green. **High-impact changes** (schema/migrations, URL validation, rate-limit policy, infra, settings/hooks) get `/security-review` + a fresh-context `/code-review`, and the user signs off in the PR before merging. **The user merges; the agent never merges.**
6. **Evidence not claims:** every task closes with pasted command output (test counts, curl responses).
7. **CLAUDE.md gotchas:** when a mistake or surprise costs time, append it to the Gotchas section in the same PR (`claude-md-management:revise-claude-md` at session end).
8. **Handoff:** `docs/HANDOFF.md` updated at the end of each session: current issue, branch, last checks run, next step, open questions — so a different model (Opus 5.5) can resume cold.

**TDD scope (tests first):** short-code generation + uniqueness/collation; destination URL validation; redirect status + headers; rate-limiter rejection (429 + Retry-After); expiry → 410. Everything else: tests after, integration-level via `WebApplicationFactory`.

**Quality gates:** `Directory.Build.props` (`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, CA5394 as error, `Nullable`), `.editorconfig`, `dotnet format --verify-no-changes`, `dotnet list package --vulnerable` (NuGetAudit `all` is default on net10), coverlet in CI, Stop hook running `dotnet build` + unit tests locally. Every NuGet package verified on nuget.org before adding, listed in the AI log.

**Secure AI usage:** committed deny rules `Read(**/.env)`, `Read(**/secrets.json)`; `dotnet user-secrets` for local SQL password; `Active Directory Default` (passwordless) for Azure SQL; no connection strings in prompts or appsettings.

---

## API surface

| Method | Route | Response |
|---|---|---|
| POST | `/api/links` `{url, customAlias?, expiresAt?}` | 201 `{code, shortUrl, url, createdAt, expiresAt}`; 400 ProblemDetails (validation); 409 alias taken; 429 (rate-limited) |
| GET | `/{code}` | 302 + `Location` + `Cache-Control: private, no-store`; 404 unknown; 410 expired/deleted (ambiguous scenario decides) |
| GET | `/api/links/{code}` | 200 metadata; 404 |
| DELETE | `/api/links/{code}` | 204 (soft delete); 404 |
| GET | `/api/links/{code}/stats` | 200 `{totalClicks, byDay[7], topReferrers[5]}` (brownfield) |
| GET | `/health/live`, `/health/ready` | liveness never touches DB (Azure SQL auto-pause); readiness checks DB, used by ACA probes sparingly |
| GET | `/openapi/v1.json` | built-in OpenAPI (.NET 10), Scalar UI in Development |

No auth (recorded assumption: internal/demo; API-key seam noted as limitation).

## Data model

- `Links`: `Id bigint identity PK`, `Code varchar(10) COLLATE Latin1_General_100_CS_AS UNIQUE`, `OriginalUrl nvarchar(2048)`, `IsCustomAlias bit`, `CreatedAt datetimeoffset`, `ExpiresAt datetimeoffset?`, `DeletedAt datetimeoffset?`. Index on `Code`. (Changed from `datetime2` in G2: `datetimeoffset` keeps the UTC offset, which matters for expiry semantics; see AI log 002.)
- `Clicks` (brownfield): `Id bigint identity`, `LinkId FK`, `OccurredAt datetimeoffset` (matches `Links`; corrected in B2), `ReferrerHost nvarchar(253)?` (host only), `UserAgent nvarchar(256)?` (truncated). **No IP column** (ADR-0004; truncated/hashed IP listed as a future option). Index `(LinkId, OccurredAt)`.

---

## Task breakdown by day (issue numbers assigned at creation)

### Day 1 — Greenfield: core create/redirect service (runnable by end of day)
Scenario doc opens with the decomposition + dependency graph (Mermaid).

| # | Task | Depends on | Verify |
|---|---|---|---|
| G0 | Repo scaffold on `main`: gitignore, README, CLAUDE.md, docs skeleton, issue/PR templates, labels, milestones | — | `git check-ignore` PDF; `gh issue list` |
| G1 | Solution + API project + test projects, `Directory.Build.props`, central packages, `.editorconfig`, `.claude/settings.json`, CI workflow skeleton | G0 | `dotnet build` warnings-as-errors clean; CI green on empty tests |
| G2 | EF Core model + `LinksDbContext`, migration, `docker-compose.yml`, Testcontainers fixture | G1 | migration applies in container; `abc ≠ ABC` collation test passes (TDD) |
| G3 | Short-code generator + create endpoint with validation (absolute http/https, length, no userinfo) + retry-on-conflict + custom alias | G2 | unit tests (TDD) + integration: 201/400/409 |
| G4 | Redirect endpoint: 302 + headers, 404 | G3 | integration tests assert status, `Location`, `Cache-Control` |
| G5 | Get + Delete endpoints, health, OpenAPI/Scalar, ProblemDetails, logging (no raw URLs in logs) | G4 | curl walkthrough pasted in AI log |
| G6 | SETUP.md + greenfield scenario doc + ADR-0001 (stack/DB), ADR-0002 (short codes/collation), ADR-0003 (redirect semantics) | G5 | reviewer can clone → `docker compose up` → `dotnet run` → curl |

### Day 2 — Brownfield: analytics + rate limiting + Azure
Opens with an **impact analysis** (`docs/scenarios/brownfield.md`): modules, data flows, risk per change.

| # | Task | Depends on | Verify |
|---|---|---|---|
| B1 | Impact analysis doc + ADR-0004 (async analytics design: bounded channel `DropWrite`, dropped counter, drain on shutdown, loss-on-crash accepted) | G6 | user sign-off (high-impact: schema) |
| B2 | `Clicks` table + migration; `ClickQueue` (bounded `Channel`) + `ClickWriterService` (`BackgroundService`, per-item catch, cancellation) ; redirect enqueues | B1 | integration test: redirect → click row within N ms; shutdown drains; queue full → redirect still 302 |
| B3 | Stats endpoint | B2 | integration test on aggregates |
| B4 | Rate limiting on `POST /api/links`: fixed-window per client, **429 + Retry-After** (TDD), `ForwardedHeaders` with `KnownNetworks` noted for ACA; ADR-0005 | G5 | test asserts 429 + header; user sign-off (policy) |
| B5 | Redirect cache: HybridCache L1 (stampede protection), invalidate on delete; measure before/after | B2 | perf numbers in AI log |
| B6 | Infra: `azure.yaml`, Bicep (Container Apps consumption, Azure SQL **free offer** serverless w/ auto-pause, Log Analytics, managed identity, passwordless SQL), `azd up`; cost guardrails: budget alert, liveness probe off DB, `Free amount remaining` alert; GitHub Container Registry or ACR Basic noted | G6 | `azd up` succeeds; live curl; user sign-off (infra); **teardown reminder: `azd down` ~1 week after review** |
| B7 | Refactor/bug-fix found during review (only if real; never fabricated) | — | tests before/after |

### Day 3 — Ambiguous: "links should expire and be safe" + docs + summary
| # | Task | Depends on | Verify |
|---|---|---|---|
| A1 | Clarification doc: open questions (what is "expire": TTL default? hard delete? 404 vs 410; what is "safe": private IPs/localhost/self-loop, reserved aliases, reputation, length caps, homographs; mutability) → **assumptions in ADR-0007** (recorded as assumptions, reversible) | B6 | user reviews questions |
| A2 | Smallest reversible implementation: expiry → 410 + `no-store`; validation hardening (private/loopback/link-local/metadata IPs, `localhost`, self-host loop, reserved-word blocklist, 2048 cap); `IUrlReputationChecker` seam with no-op default + denylist | A1 | TDD tests for each rule; `/security-review`; user sign-off (validation) |
| A3 | Perf gate: k6 redirect load test via Docker, numbers recorded (p50/p95, RPS) with cache on/off | B5 | results in TESTING.md |
| A4 | ARCHITECTURE.md (Mermaid), TESTING.md, limitations/trade-offs, ENGINEERING_SUMMARY.md, final CLAUDE.md gotchas pass, HANDOFF.md | all | docs review; README links resolve |

Azure deploy (B6) is the task most likely to slip to Day 3; everything else on Day 2 stays local.

---

## CLAUDE.md contents (hand-written, not `/init` — `/init` generates from existing code and the repo is empty; use `claude-md-management:revise-claude-md` for updates)

1. The pasted behavioural guidelines (verbatim, sections 1-4).
2. Project: purpose, stack (.NET 10, EF Core 10, SQL Server/Testcontainers/Azure SQL), repo map, commands (`dotnet build/test`, `docker compose up`, `azd up/down`).
3. Workflow rules: issue → branch → AI log → PR → user merges; trailers; high-impact list requiring sign-off; never merge, never push to main, never add packages without approval, never read secrets, no employer references.
4. Conventions: feature folders, `TypedResults`, ProblemDetails, no `System.Random`, no check-then-insert, no raw URLs/IPs in logs, cancellation tokens everywhere, tests via `WebApplicationFactory` not mocks-of-mocks.
5. TDD scope list.
6. **Gotchas** (living): seeded with: SQL Server CI collation; rate limiter 503 default; `BoundedChannelFullMode.Wait` default; BackgroundService exception stops host; `.claudeignore` no-op; SqlClient 6+ needs `Microsoft.Data.SqlClient.Extensions.Azure` for Entra auth; Azure SQL free offer burns on probes; Docker/azd need PATH refresh after install.
7. Model handoff: pointer to `docs/HANDOFF.md` and `docs/plan.md`.

---

## Verification (end-to-end)

- Fresh clone → `docker compose up -d` → `dotnet run --project src/UrlShortener.Api` → `curl -X POST /api/links` → `curl -i /{code}` shows 302 + headers → `/stats` shows the click → `dotnet test` green (unit + Testcontainers) → CI green on PR.
- Collation proof: integration test creating `abc` then `ABC` both succeed on SQL Server.
- Rate limit proof: N+1 POSTs → 429 with `Retry-After`.
- Expiry proof: link with past `expiresAt` → 410.
- Azure: `azd up` → live URL curl → budget/alerts visible → `azd down` scheduled.
- Perf: k6 output recorded.
- Traceability: every merged PR links an AI log; `git log --format=%B | grep Assisted-by` covers every non-scaffold commit.

## First actions after approval
1. Start Docker Desktop; restart Claude Code for PATH.
2. Housekeeping moves (Docs → docs/brief, research → docs/research), `.gitignore`, `git init`, verify PDF ignored.
3. Write CLAUDE.md, README, docs skeleton, templates; copy this plan to `docs/plan.md`; initial commit on `main`.
4. `gh repo create` (public), push, create labels/milestones/issues for Day 1; open `feat/01-...`.
