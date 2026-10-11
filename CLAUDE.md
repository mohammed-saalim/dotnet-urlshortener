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

Repo map: `src/UrlShortener.Api` (feature folders `Links/`, `Redirect/`, `Analytics/`, `Safety/`, `PublicUrl/`, `RateLimiting/`, `Telemetry/`, plus `Data/` and `Migrations/`; health checks are mapped in `Program.cs`), `tests/UrlShortener.UnitTests`, `tests/UrlShortener.IntegrationTests`, `infra/` (Bicep), `perf/` (k6), `docs/` (adr, ai-log, scenarios, research, ARCHITECTURE, SETUP, TESTING, HANDOFF, ENGINEERING_SUMMARY).

Commands:
```
docker compose up -d                       # local SQL Server
dotnet build -c Release                    # warnings are errors; Release, because some analyzers only fire there
dotnet test                                # unit + integration (needs Docker)
dotnet format --verify-no-changes
dotnet list package --vulnerable
dotnet run --project src/UrlShortener.Api
azd up / azd down                          # cloud deploy / teardown
```

## 6. Workflow (non-negotiable)

1. **Issue first.** Every task is a GitHub issue (template: intent, constraints, acceptance criteria, technical context, risk level); small follow-ups may skip it (engineer, 2026-10-11), and the PR says so. Labels `scenario:*`, `type:*`, `risk:high-impact`.
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
- Redirects: `302` + `Cache-Control: private, no-store`. Expired/deleted: `410` + `no-store` (decision in ADR-0007).
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
- **Microsoft.Data.SqlClient 7+ (not 6) needs `Microsoft.Data.SqlClient.Extensions.Azure`** for `Active Directory Default`, or it fails at runtime with "Cannot find an authentication provider". EF Core 10.0.12 brings SqlClient 6.1.6, which still depends on `Azure.Identity` and contains `ActiveDirectoryAuthenticationProvider`, so no package is needed today; every `Extensions.Azure` version pairs with SqlClient 7.x. A Learn breaking-change page says "starting in 6.0"; the nuspecs and the SqlClient 7.0.0 release notes say 7.0 (AI log 013, ADR-0006). Add the package when SqlClient 7 arrives.
- **Azure SQL free offer bills vCore-seconds whenever the DB is online.** Health probes that touch the DB prevent auto-pause and can burn the month in days. `/health/live` must not touch the DB.
- **Windows is case-insensitive:** `Docs` and `docs` are one folder, and a folder can't be renamed while a shell's cwd is inside it. Start Claude Code from the repo root.
- **Newly installed CLIs (docker, azd) are not on PATH in an already-open shell.** Restart the terminal/Claude Code.
- **Configuration keys with dots can't be `export`ed in bash** (`Logging__LogLevel__Microsoft.EntityFrameworkCore` is not a valid shell name). Use `env 'Name.With.Dots=Warning' dotnet run ...`.
- **`.NET 8` goes out of support on 2026-11-10.** This project targets .NET 10 (LTS to 2028-11-14).
- **The .NET 10.0.101 `webapi` template ships a vulnerable transitive package.** `Microsoft.AspNetCore.OpenApi 10.0.1` depends on `Microsoft.OpenApi >= 2.0.0` (GHSA-v5pm-xwqc-g5wc, high). With `NuGetAuditMode=all` + warnings-as-errors the first build fails. Fix: pin the transitive version in `Directory.Packages.props` (`CentralPackageTransitivePinningEnabled`).
- **`IDE0005` (unused usings) only runs at build with `GenerateDocumentationFile=true`**, which then raises CS1591 on every public member. Both are set in `Directory.Build.props`; don't re-add them per project.
- **`attribution.commit` has no placeholders.** It's a literal string, so the model id can't be templated; it is set to `""`, so no trailer is added (commits carry none, §6.4).
- **Stop-hook pipelines need `set -o pipefail`**, otherwise `dotnet build | tail` reports tail's exit code and a failed build passes the gate.
- **`dotnet ef migrations add` output fails the build.** The migration `.cs` (unlike `.Designer.cs`) has no `<auto-generated />` header, so `EnforceCodeStyleInBuild` raises IDE0161/IDE0005 on it. `.editorconfig` marks `src/UrlShortener.Api/Migrations/*.cs` as `generated_code = true`; never hand-edit migrations to satisfy style rules.
- **`dotnet ef migrations script --no-build` right after `migrations add` scripts the stale DLL** (the new migration is missing from the output). Run it without `--no-build`.
- **Testcontainers 4.x: `new MsSqlBuilder()` is obsolete** (CS0618, an error here). Pass the image to the constructor: `new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")`.
- **Test method names are PascalCase.** `latest-recommended` enables CA1707 (no underscores), so `Method_condition_result` names fail the build.
- **`.gitignore` `.env.*` also ignores `.env.example`**; it is re-included with `!.env.example`.
- **`Read(...)` deny rules do not cover Bash.** `cat .env` in a shell still works, so the deny rules prevent accidental reads by the file tools, not deliberate ones. Real protection is: secrets never exist in the repo (user-secrets, env vars, managed identity).
- **Git Bash rewrites container paths** in `docker compose exec ... /opt/mssql-tools18/bin/sqlcmd` to `C:/Program Files/Git/opt/...`. Prefix with `MSYS_NO_PATHCONV=1`.
- **`Uri.TryCreate(s, UriKind.Absolute, ...)` accepts leading/trailing spaces, tabs and embedded CR/LF**, silently trimming or percent-encoding them in the parsed `Uri`. The raw string is what gets stored and later becomes the redirect `Location` header, so the validator checks the raw string and accepts printable ASCII only (which excludes whitespace and control characters).
- **An unconditional `app.UseExceptionHandler()` breaks Development 400s** unless it has a `StatusCodeSelector`. It sits inside the Developer Exception Page, catches `BadHttpRequestException` (malformed JSON, thrown because `ThrowOnBadRequest` is on in Development only) and returns 500. Production never throws there: it writes a bare 400 with no body, which only `UseStatusCodePages()` turns into ProblemDetails. Both are needed for ProblemDetails everywhere (AI log 005).
- **One SQL Server container per test class flakes on the GitHub runner.** Five classes start five `mssql/server` containers in parallel on a 7 GB runner; on PR #20 one exited at startup (code 255, `CoInitializeSecurity failure`) and its whole class failed while the rest passed. A rerun is the immediate fix; the structural fix is a shared collection fixture (issue #21).
- **Analyzers differ between SDK versions, so local and CI must use the same SDK.** CA1873 (expensive logging argument) passed locally on SDK 10.0.101 and failed twice on CI, which had picked up 10.0.401 via `dotnet-version: 10.0.x` (PR #12). My first diagnosis ("only fires in Release") was wrong. Fix: `global.json` pins the SDK and CI reads it (`global-json-file`). Keep `dotnet build -c Release` as the local gate anyway, matching CI. For CA1873: compute log arguments into locals inside an `IsEnabled` guard.
- **`CA1848` is an error here** (`latest-recommended` + `TreatWarningsAsErrors`): `logger.LogInformation(...)` does not build. Use `[LoggerMessage]` partial methods. Static endpoint classes cannot be `ILogger<T>` type arguments; inject `ILoggerFactory` and `CreateLogger(typeof(...))`.
- **`BackgroundService.StopAsync` cancels `stoppingToken` before it waits for `ExecuteAsync`.** A writer loop on `WaitToReadAsync(stoppingToken)` therefore abandons everything still queued at shutdown, even when `StopAsync` completes the channel first (`ShutdownDrainsPendingClicks` stored 0 of 250). `ClickWriterService` waits and inserts on its own token, cancelled only when `StopAsync`'s graceful-shutdown token fires; the loop ends when the completed channel is empty (AI log 008).
- **Under `BoundedChannelFullMode.DropWrite`, `TryWrite` on a full channel returns `true`** and calls the `itemDropped` callback; it returns `false` only once the channel is completed. Count drops in the callback, not from the return value (observed in AI log 008).
- **CA1711 rejects type names ending in `Queue`** (also `Collection`, `Dictionary`, `EventHandler`...) unless the type derives from the matching base. `ClickQueue` keeps the ADR-0004 name with a justified `[SuppressMessage]`.
- **TestServer leaves `HttpContext.Connection.RemoteIpAddress` null** (observed on .NET 10, AI log 010). Every `WebApplicationFactory` request therefore lands in the rate limiter's `"unknown"` fallback partition, so integration tests cannot show per-IP separation; they only show the limit itself. A null peer also makes the forwarded-headers middleware skip its known-proxy check, so a forwarded-headers test passes whether or not `KnownIPNetworks`/`KnownProxies` are cleared; `PublicUrlTests` sets a non-loopback peer with a startup filter to make the lists matter (AI log 011).
- **`ForwardedHeadersOptions.KnownNetworks` is obsolete in .NET 10** (`ASPDEPR005`, an error under `TreatWarningsAsErrors`). Use `KnownIPNetworks` (`System.Net.IPNetwork`). The proxy/load-balancer docs page still shows `KnownNetworks.Clear()`; the breaking-change page has the new name (AI log 011).
- **`[Url]` is a prefix check** (`http://`, `https://`, `ftp://`), not URL validation, and `Uri.TryCreate` trims whitespace (above). Any option that is later joined into a URL as a raw string must be validated on the raw string (`PublicUrlOptions.IsValidBaseUrl`, caught by code review in AI log 011).
- **`bicep lint` exits 0 when it reports warnings** (bicep 0.47.16, AI log 013). CI greps its output for `: Warning ` / `: Error ` to fail the job.
- **Bare `azd monitor` means `--overview`, which needs a portal dashboard** (`Microsoft.Portal/dashboards`), not just Application Insights; this template creates none, so it fails. `azd monitor --logs` / `--live` work since B7: azd finds the App Insights component by resource type in the environment's resource group, no Bicep output needed (azd 1.35.1 `cmd/monitor.go`, AI log 014).
- **The Azure Monitor distro turns OpenTelemetry's `url.query` redaction off** (since 1.1.1; it sets `OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION=true` unless the key already exists). Bicep sets it to `false`, so query-string values in request telemetry are stored as `Redacted`.
- **An observable instrument exists only once the object that creates it does.** `AppMetrics` is a DI singleton nothing depends on, so `Program.cs` resolves it right after `Build()`; without that line `urlshortener.analytics.dropped_clicks` is never published (mutation in AI log 014: the test observed `null` instead of 2).
- **A free-offer Azure SQL database with `freeLimitExhaustionBehavior: AutoPause` accepts only the default `autoPauseDelay` (60 min).** Setting the documented minimum (15) passes `bicep lint` and `azd provision --preview`, then fails at deploy with `ProvisioningDisabled: Only default value for auto pause delay is allowed...`. Leave the property out (#31).
- **Azure SQL server creation can be refused per region and subscription** (`RegionDoesNotAllowProvisioning`; eastus2 did, centralus worked). The "already exists or conflicting state" line azd prints above it is a generic suggestion, not the cause. `azd provision --preview` does not catch it.
- **azd's preflight warns that `AllowAllWindowsAzureIps` contains the reserved word "WINDOWS"** and says the deployment will fail. It is a false positive: that is the documented name of the "Allow Azure services" rule, and ARM created it. Answer Yes.
- **An `existing` reference to a resource in the same template that declares it is a circular dependency in ARM**, even behind `if (...)` and even though `bicep build` emits no self-`dependsOn`. It only fails once the condition is true: the first `azd up` (app absent, `apiExists` false) passed validation, the second failed with `InvalidTemplate: Circular dependency detected`. Read the current image in a module (`infra/api-image.bicep`, #31).
- **The Azure Monitor distro samples traces at 5 per second by default** (since 1.5.0), and drops a sampled-out request's logs with it. A `traceId` returned in a ProblemDetails body can therefore be missing from App Insights (seen live: a 429 in an 11-request burst, #33). Bicep sets `OTEL_TRACES_SAMPLER=microsoft.fixed_percentage` / `OTEL_TRACES_SAMPLER_ARG=1.0`.
- **A trailing dot turns an IPv4 literal into a DNS name for `System.Uri`** (`http://127.0.0.1./` → `HostNameType.Dns`, host `127.0.0.1.`), while browsers (WHATWG URL) drop the dot and go to the address. A rule gated on `HostNameType` alone lets `http://169.254.169.254./` through; `CreateLinkValidator` re-parses the host without the dot (found by the security review of #38, AI log 019). `IPAddress.ScopeId` throws on IPv4; check `AddressFamily` first.
- **`System.Uri` normalises shorthand IPv4 hosts** (`http://2130706433/`, `http://0x7f.1/`, `http://127.1/`) to the dotted quad `127.0.0.1` with `HostNameType == IPv4` (observed on .NET 10, AI log 019, pinned by `UriNormalisesShorthandIpv4ToDottedQuad`). The destination IP rule therefore sees them; don't hand-parse. Also observed: for IPv6, `Host` keeps the brackets (`[::1]`) and drops a zone ID, while `IdnHost` has no brackets and keeps the zone (`fe80::1%25eth0`), so parse `IdnHost`; a trailing dot stays in `Host` (`localhost.`), so trim it before comparing names.
- **`azd provision --preview --no-prompt` needs `AZURE_SUBSCRIPTION_ID`** in the azd environment; without it the preview stops at "prompt required" (after downloading bicep to `~/.azd/bin`, which is then usable for local `bicep lint`).
- **`azd provision --preview` does not list every resource.** It drops every change whose resource type has no display name in `azapi.GetResourceTypeDisplayName` (`pkg/infra/provisioning/manager.go`, azd 1.35.1), so the workbook, the SQL database, the identity, the role assignment, the firewall rule, the action group, the alert and the budget never appear, even with `--output json`. It cannot show that a new workbook is the only addition; an ARM what-if on the compiled template does (`Create` for the workbook, `Deploy` for everything else; found by the code review, AI log 021).
- **A KQL `let` name can be shadowed by a column of the same name.** `let id = ...; union requests, dependencies, traces, exceptions | where operation_Id == id` compared against the `id` column, not the variable, and returned every startup log line (empty `operation_Id`) for any input. Use a name no table has (`wanted`; AI log 021).
