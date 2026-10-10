# CLAUDE.md

Project memory for the AI-assisted URL shortener (interview assignment). Read fully before any task.
Model handoff: see `docs/HANDOFF.md` for where work stopped, and `docs/plan.md` for the approved plan.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

---

## 5. Project

URL shortener prototype that must show **engineer-led execution accelerated by AI**: the engineer (Mohammed) owns every decision and approves every output. Everything here will be questioned in an interview, so every non-obvious choice needs a written reason.

- Stack: .NET 10 LTS, ASP.NET Core Minimal APIs, EF Core 10, SQL Server (Testcontainers in tests, `docker compose` locally, Azure SQL free offer in the cloud), Azure Container Apps via `azd` + Bicep.
- Personal environment only: no employer code, data, URLs, naming, tenants or credentials anywhere, including prompts.
- The brief in `docs/brief/` is employer-classified: gitignored, quote at most a few words.

Repo map: `src/UrlShortener.Api` (feature folders `Links/`, `Redirect/`, `Analytics/`, `Health/`), `tests/UrlShortener.UnitTests`, `tests/UrlShortener.IntegrationTests`, `infra/` (Bicep), `perf/` (k6), `docs/` (adr, ai-log, scenarios, research, ARCHITECTURE, SETUP, TESTING, HANDOFF, ENGINEERING_SUMMARY).

Commands:
```
docker compose up -d                       # local SQL Server
dotnet build                               # warnings are errors
dotnet test                                # unit + integration (needs Docker)
dotnet format --verify-no-changes
dotnet list package --vulnerable
dotnet run --project src/UrlShortener.Api
azd up / azd down                          # cloud deploy / teardown
```

## 6. Workflow (non-negotiable)

1. **Issue first.** Every task is a GitHub issue (template: intent, constraints, acceptance criteria, technical context, risk level). Labels `scenario:*`, `type:*`, `risk:high-impact`.
2. **Branch per issue:** `feat/NN-slug`, `fix/NN-slug`, `docs/NN-slug`, `infra/NN-slug`. Never commit code to `main`.
3. **AI log per task:** `docs/ai-log/NNN-slug.md` from `docs/ai-log/TEMPLATE.md`. Record every artifact as generated / edited (why) / rejected (why), the checks run with real output, and the sign-off line. Fill it as you go, not at the end.
4. **Commits are minimal:** a one-line subject, `Closes #N` in the body, nothing else. No attribution trailers of any kind (`Assisted-by`, `Co-Authored-By`, `Signed-off-by`). Traceability lives in the AI log and the PR, not in commit messages.
5. **PR per branch** using the template. CI must be green. **The engineer merges; Claude never merges, never pushes to `main`, never force-pushes.**
   Every PR ends with a **Walkthrough** section: the files to read in order (with one line on what each does), and the exact commands to see the change working locally (`gh pr checkout N`, `docker compose up -d`, `dotnet run`, `curl ...`). The engineer reads the walkthrough before reviewing the diff.
6. **High-impact changes need explicit sign-off in the PR before merge:** schema/migrations, destination-URL validation, rate-limit policy, infra/Bicep, `.claude/settings.json`/hooks, anything touching secrets or auth. Run `/security-review` and a fresh-context `/code-review` on these first.
7. **Evidence, not claims.** Close every task with pasted command output (test counts, curl responses, exit codes).
8. **Packages:** verify each NuGet package on nuget.org (owner, downloads, repo) before adding; ask before adding; pin the version in `Directory.Packages.props`; list it in the AI log.
9. **Secrets:** never read `.env`/`secrets.json`; use `dotnet user-secrets` locally and passwordless `Active Directory Default` on Azure. No connection strings in prompts, appsettings or logs.
10. **Gotchas:** when something costs time or surprises you, add it to section 9 in the same PR.
11. **Handoff:** update `docs/HANDOFF.md` at the end of every session (current issue, branch, last checks, next step, open questions).
12. **Delegation:** one issue = one fresh subagent (`general-purpose`, Opus) working on the issue's branch, briefed with the issue text, this file and the AI-log template. The coordinator session reviews the diff and the AI log, runs the gates itself, commits, and opens the PR. Subagents never commit, push or open PRs. Decisions that need the engineer are reported back, not guessed.

## 7. Conventions

- Minimal APIs grouped with `MapGroup`, `TypedResults`, ProblemDetails for all errors, built-in OpenAPI (`/openapi/v1.json`), Scalar UI in Development only.
- Options pattern with `ValidateDataAnnotations().ValidateOnStart()`; `CancellationToken` passed everywhere.
- Short codes: `RandomNumberGenerator.GetString`, never `System.Random`. Uniqueness by **unique index + catch violation + retry**, never check-then-insert.
- Redirects: `302` + `Cache-Control: private, no-store`. Expired/deleted: `410` + `no-store` (decision in ADR-0006).
- Logging: never log raw destination URLs or client IPs; log the short code and the host only.
- Tests: behaviour through `WebApplicationFactory` + Testcontainers (real SQL Server). No EF InMemory provider. No tests that only assert a mock was called.
- Docs: MADR 4.0 ADRs in `docs/adr/`, Mermaid diagrams in Markdown (GitHub renders them). No generated-diagram tools.

**TDD scope (test first):** short-code generation and uniqueness/collation; destination-URL validation; redirect status + headers; rate limiter 429 + `Retry-After`; expiry → 410. Everything else: tests after, integration-level.

## 8. Quality gates

`Directory.Build.props`: `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, `Nullable=enable`, CA5394 as error. `.editorconfig` committed. CI: build, `dotnet format --verify-no-changes`, tests (Testcontainers on `ubuntu-latest`), `dotnet list package --vulnerable`, coverage. A Stop hook runs `dotnet build` + unit tests locally.

## 9. Gotchas (living list; add in the same PR that hit them)

- **SQL Server default collation is case-insensitive** (`SQL_Latin1_General_CP1_CI_AS`). Base62 codes `abc` and `ABC` collide silently. The `Code` column must use `Latin1_General_100_CS_AS`, set at column level (a query-level `COLLATE` defeats the index). SQLite would not catch this, which is why tests run on real SQL Server.
- **ASP.NET rate limiter rejects with 503 and no `Retry-After` by default.** Set `RejectionStatusCode = 429` and write `Retry-After` in `OnRejected` from the lease metadata.
- **`Channel.CreateBounded` defaults to `FullMode = Wait`**, which would block the redirect path. Use `DropWrite` and count drops.
- **An unhandled exception in a `BackgroundService` stops the whole host** (`BackgroundServiceExceptionBehavior.StopHost`). Catch per item.
- **`.claudeignore` does nothing.** Secrets are protected by `Read(...)` deny rules in `.claude/settings.json`.
- **Microsoft.Data.SqlClient 6+ needs `Microsoft.Data.SqlClient.Extensions.Azure`** for `Active Directory Default`, or it fails at runtime with "Cannot find an authentication provider".
- **Azure SQL free offer bills vCore-seconds whenever the DB is online.** Health probes that touch the DB prevent auto-pause and can burn the month in days. `/health/live` must not touch the DB.
- **Windows is case-insensitive:** `Docs` and `docs` are one folder, and a folder can't be renamed while a shell's cwd is inside it. Start Claude Code from the repo root.
- **Newly installed CLIs (docker, azd) are not on PATH in an already-open shell.** Restart the terminal/Claude Code.
- **`.NET 8` goes out of support on 2026-11-10.** This project targets .NET 10 (LTS to 2028-11-14).
- **The .NET 10.0.101 `webapi` template ships a vulnerable transitive package.** `Microsoft.AspNetCore.OpenApi 10.0.1` depends on `Microsoft.OpenApi >= 2.0.0` (GHSA-v5pm-xwqc-g5wc, high). With `NuGetAuditMode=all` + warnings-as-errors the first build fails. Fix: pin the transitive version in `Directory.Packages.props` (`CentralPackageTransitivePinningEnabled`).
- **`IDE0005` (unused usings) only runs at build with `GenerateDocumentationFile=true`**, which then raises CS1591 on every public member. Both are set in `Directory.Build.props`; don't re-add them per project.
- **`attribution.commit` has no placeholders.** It's a literal string, so the model id can't be templated; it is set to `""` and trailers are written by hand per commit.
- **Stop-hook pipelines need `set -o pipefail`**, otherwise `dotnet build | tail` reports tail's exit code and a failed build passes the gate.
- **`dotnet ef migrations add` output fails the build.** The migration `.cs` (unlike `.Designer.cs`) has no `<auto-generated />` header, so `EnforceCodeStyleInBuild` raises IDE0161/IDE0005 on it. `.editorconfig` marks `src/UrlShortener.Api/Migrations/*.cs` as `generated_code = true`; never hand-edit migrations to satisfy style rules.
- **`dotnet ef migrations script --no-build` right after `migrations add` scripts the stale DLL** (the new migration is missing from the output). Run it without `--no-build`.
- **Testcontainers 4.x: `new MsSqlBuilder()` is obsolete** (CS0618, an error here). Pass the image to the constructor: `new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")`.
- **Test method names are PascalCase.** `latest-recommended` enables CA1707 (no underscores), so `Method_condition_result` names fail the build.
- **`.gitignore` `.env.*` also ignores `.env.example`**; it is re-included with `!.env.example`.
- **`Read(...)` deny rules do not cover Bash.** `cat .env` in a shell still works, so the deny rules prevent accidental reads by the file tools, not deliberate ones. Real protection is: secrets never exist in the repo (user-secrets, env vars, managed identity).
- **Git Bash rewrites container paths** in `docker compose exec ... /opt/mssql-tools18/bin/sqlcmd` to `C:/Program Files/Git/opt/...`. Prefix with `MSYS_NO_PATHCONV=1`.
- **`Uri.TryCreate(s, UriKind.Absolute, ...)` accepts leading/trailing spaces, tabs and embedded CR/LF**, silently trimming or percent-encoding them in the parsed `Uri`. The raw string is what gets stored and later becomes the redirect `Location` header, so the validator rejects whitespace and control characters explicitly.
- **An unconditional `app.UseExceptionHandler()` breaks Development 400s** unless it has a `StatusCodeSelector`. It sits inside the Developer Exception Page, catches `BadHttpRequestException` (malformed JSON, thrown because `ThrowOnBadRequest` is on in Development only) and returns 500. Production never throws there: it writes a bare 400 with no body, which only `UseStatusCodePages()` turns into ProblemDetails. Both are needed for ProblemDetails everywhere (AI log 005).
- **`CA1848` is an error here** (`latest-recommended` + `TreatWarningsAsErrors`): `logger.LogInformation(...)` does not build. Use `[LoggerMessage]` partial methods. Static endpoint classes cannot be `ILogger<T>` type arguments; inject `ILoggerFactory` and `CreateLogger(typeof(...))`.
