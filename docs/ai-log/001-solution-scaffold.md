# AI log 001 — solution scaffold, quality gates, agent settings, CI skeleton

| Field | Value |
|---|---|
| Issue | #1 |
| Branch | `feat/01-solution-scaffold` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-fable-5-1` |
| Risk level | high-impact (agent settings, CI) |

## Intent
Create the .NET 10 solution with the quality gates wired in from the first build, so every later task is held to the same bar.

## Constraints given to the AI
- No application logic.
- Only first-party Microsoft packages plus xunit; every version pinned centrally.
- `.claude/settings.json` reviewed line by line by the engineer before merge.
- Verify the `attribution`, `permissions.deny` and `hooks` schemas against the official docs, not memory.

## Acceptance criteria (runnable)
- `dotnet build` clean with TreatWarningsAsErrors, AnalysisLevel=latest-recommended, EnforceCodeStyleInBuild, Nullable, CA5394=error
- `dotnet format --verify-no-changes` passes
- `dotnet test` runs in both test projects
- CI green on the PR
- Claude cannot read `**/.env` or `**/secrets.json`

## Prompts (summarised, secrets redacted)
1. Engineer: "Continue with your work" after approving `docs/plan.md`; task G1.
2. Claude → tools: `dotnet new sln` / `webapi -minimal` / `xunit` ×2, `dotnet sln add`, project references.
3. Claude → docs: fetched code.claude.com settings-reference, permissions and hooks pages for exact schemas.
4. Claude → GitHub advisory API: looked up GHSA-v5pm-xwqc-g5wc patched version after the first build failed.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `UrlShortener.sln`, 3 csproj files | generated (dotnet templates), then edited | Stripped `Version=` attributes (moved to central package management) and duplicated `Nullable`/`ImplicitUsings` (moved to `Directory.Build.props`) |
| `src/UrlShortener.Api/Program.cs` | generated (template), then edited | Removed the weather-forecast sample and `WeatherForecast` record; added `public partial class Program;` so `WebApplicationFactory` can reach it |
| `UnitTest1.cs` ×2, `UrlShortener.Api.http` | rejected (deleted) | Placeholder content with no value; test projects run with zero tests until real ones land |
| `Directory.Build.props` | generated | Quality gates per CLAUDE.md §8; `GenerateDocumentationFile`+`NoWarn CS1591` added after IDE0005 refused to run without it |
| `Directory.Packages.props` | generated | CPM with transitive pinning; versions copied from template output; `Microsoft.OpenApi 2.12.2` pinned to clear GHSA-v5pm-xwqc-g5wc |
| `.editorconfig`, `.gitattributes` | generated | File-scoped namespaces, braces, LF endings; `.gitattributes` stops the CRLF warnings seen on the scaffold commit |
| `.github/workflows/ci.yml` | generated | restore → format → build (Release) → vulnerable-package check → test with coverage → upload results |
| `CLAUDE.md` §6.4, §6.5, §6.12; PR template | edited | Engineer dropped commit trailers in favour of minimal commits (traceability stays in the AI log and PR), added a per-PR Walkthrough section, and adopted one-subagent-per-issue delegation |
| `.claude/settings.json` | generated, then edited twice | (1) Removed a `Read(docs/brief/**)` deny rule: the brief is secret from the repo, not from the agent. (2) Added `set -o pipefail` to the Stop hook: without it `dotnet build \| tail` returned tail's exit code and a failed build passed the gate. `attribution.commit=""` because the docs show no placeholders, so the model id cannot be templated |
| `.env` (local only, gitignored) | generated | Dummy file used to prove the Read deny rule; contains no real secret |

## Checks run
```
$ dotnet build --nologo -v q           (first run)
error NU1903: Package 'Microsoft.OpenApi' 2.0.0 has a known high severity vulnerability (GHSA-v5pm-xwqc-g5wc)
→ pinned Microsoft.OpenApi 2.12.2 (first patched: 2.7.5)

$ dotnet build --nologo -v q           (second run)
error EnableGenerateDocumentationFile: Set 'GenerateDocumentationFile' to 'true' to enable IDE0005
→ added GenerateDocumentationFile=true, NoWarn CS1591

$ dotnet build --nologo -v q           (third run)
0 Warning(s)  0 Error(s)

$ dotnet format --verify-no-changes --no-restore ; echo $?
0

$ dotnet test --no-build --nologo
No test is available in ...UrlShortener.UnitTests.dll          (expected: no tests yet)
No test is available in ...UrlShortener.IntegrationTests.dll   (expected)

$ dotnet list package --vulnerable --include-transitive
UrlShortener.Api / UnitTests / IntegrationTests: no vulnerable packages given the current sources.

$ (set -o pipefail; dotnet build ... && dotnet test tests/UrlShortener.UnitTests ... || exit 2); echo $?
0                                                             (Stop hook simulation)

$ git check-ignore -q .env && echo ignored
ignored
```
Read-deny check (Claude attempted `Read .env` with the dummy file present):
```
<tool_use_error>File is in a directory that is denied by your permission settings.</tool_use_error>
```

## Gotchas encountered
Added to CLAUDE.md §9: vulnerable transitive package in the .NET 10 template; IDE0005 needs GenerateDocumentationFile; `attribution.commit` has no placeholders; Stop-hook pipelines need `pipefail`.

## Sign-off
Reviewed and merged by the engineer in PR #7; I understood the change and accept ownership of it. — Mohammed Saalim, sign-off recorded 2026-10-11
