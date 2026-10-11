# AI log 002 — EF Core model, SQL Server migration, docker compose, Testcontainers fixture

| Field | Value |
|---|---|
| Issue | #2 |
| Branch | `feat/02-persistence` |
| Date | 2026-10-10 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent delegated by the coordinator session) |
| Risk level | high-impact (schema) |

## Intent
Persist links in SQL Server with a case-sensitive unique short code, and prove it against a real SQL Server in tests.

## Constraints given to the AI
- TDD: the collation/uniqueness tests are written and seen failing before the mapping is correct.
- No EF InMemory provider, no `System.Random`, `CancellationToken` passed in app code.
- Only first-party Microsoft packages plus Testcontainers; each verified on nuget.org; versions pinned only in `Directory.Packages.props`.
- No password committed; compose fails fast without one. Never read `.env` / `secrets.json`.
- Nothing beyond the issue: no endpoints, no repository abstraction, no caching. No commits.

## Acceptance criteria (runnable)
- `docker compose up -d` then the migration applies cleanly → see "Compose verification"
- Integration test: `abc` then `ABC` both insert; `abc` twice throws a unique violation → see "TDD sequence"
- `dotnet test` green locally (Docker) and in CI → local below; CI runs on the PR

## Prompts (summarised, secrets redacted)
1. Coordinator → subagent: implement #2 per the issue, with deliverables (entity, context, Program wiring, migration via local `dotnet-ef`, compose, Testcontainers fixture, 3 tests), hard rules from CLAUDE.md §6-§9, and the list of checks to paste.
2. Subagent → advisor model: plan reviewed before writing. Advice taken: distinct codes per test (shared DB, order not guaranteed), regenerate the migration by deleting the files rather than `migrations remove`, `!.env.example` in `.gitignore`, `tools18` + `-C` + `$$` in the compose healthcheck, `TrustServerCertificate=True` for the local connection string, record the `datetime2` vs `datetimeoffset` deviation.
3. Subagent → nuget.org search + flat-container APIs: owners, downloads, latest stable versions (below).

## Packages added (all verified on nuget.org 2026-10-10)

| Package | Version | Owner(s) on nuget.org | Downloads | Why |
|---|---|---|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.12 | Microsoft, EntityFramework, aspnet, dotnetframework (verified prefix) | 1.04 B | EF Core provider for SQL Server / Azure SQL. Latest stable 10.0.x. Brings `Microsoft.Data.SqlClient` 6.1.6 transitively |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.12 | same | 1.07 B | Required by `dotnet ef migrations add`. `PrivateAssets=all`, so it is a build-time dependency only |
| `dotnet-ef` (local tool, `.config/dotnet-tools.json`) | 10.0.12 | Microsoft, aspnet, dotnetframework | 129 M | Generates migrations reproducibly: `dotnet tool restore` gives every machine the same version as the runtime packages |
| `Testcontainers.MsSql` | 4.16.0 | Testcontainers, HofmeisterAn (verified prefix) | 36 M | Real SQL Server per test class. Transitively brings `Testcontainers` 4.16.0 and `Docker.DotNet.Enhanced` 4.3.3 (Testcontainers' maintained Docker client fork) |

`dotnet list package --vulnerable --include-transitive` reports none (below), so no extra transitive pins were needed.

## Decisions (for the engineer to question)
- **`Data/LinksDbContext.cs`, not `Links/`.** The context will also own `Clicks` (Analytics feature, B2), so it is shared infrastructure rather than part of one feature folder.
- **`UseSqlServer`, not `UseAzureSql`.** `UseAzureSql(connectionString, ...)` exists in the 10.0.12 package (confirmed in its XML docs, `SqlServerDbContextOptionsExtensions`); per the EF 9 release notes it targets Azure SQL-specific behaviour (compatibility level, Azure-only features), not verified further here. Local dev and tests run on boxed SQL Server 2022, so `UseSqlServer` is the common denominator today; revisit in B6 (Azure). `EnableRetryOnFailure()` uses the default policy (6 retries, max 30 s delay), which matters for Azure SQL serverless auto-resume.
- **`datetimeoffset`, not `datetime2`.** `docs/plan.md` "Data model" says `datetime2`; the coordinator's task says `datetimeoffset`, which is EF's default mapping for `DateTimeOffset`. Followed the task. Trade-off: `datetimeoffset` is 10 bytes vs 8 and keeps the offset (unambiguous instants); `datetime2` needs a UTC-only convention. **Engineer to confirm; the plan should then be updated to match.**
- **Development-only `MigrateAsync()` at startup** rather than "run `dotnet ef database update`". It is a few lines and makes the plan's G6 reviewer flow (`docker compose up -d` → `dotnet run` → curl) work without installing tools. EF Core 9+ takes a migration lock (`sp_getapplock`, visible in the run log) and throws on pending model changes, so repeated runs are safe. **Production migration path is an open item** (options for B6: `dotnet ef migrations script --idempotent` applied by the pipeline, or a migration bundle). `dotnet ef database update` also works and is shown below.
- **Missing connection string is not validated at startup.** `GetConnectionString` returns null and `UseSqlServer(null)` is allowed (that is what lets `dotnet ef migrations add` run without a database). The failure surfaces on first DB use. Fail-fast validation can come with the options work in G5 if wanted.
- **Fixture uses a dedicated `UrlShortener` database** in the container (`SqlConnectionStringBuilder.InitialCatalog`) rather than `master`, where the Testcontainers connection string points by default.
- **Tests have no `CancellationToken`.** xUnit 2.9 has no per-test token (that is xUnit v3's `TestContext.Current.CancellationToken`); app code passes tokens, test code uses the token-less overloads.
- **Startup `MigrateAsync()` has no `CancellationToken`** (deliberate exception to CLAUDE.md §7): before `app.Run()` there is no meaningful token; `app.Lifetime.ApplicationStopping` is not wired to Ctrl+C until the host starts.
- **Heads-up for G3:** `WebApplicationFactory` defaults to the Development environment, so the startup migrate will run in every WAF-based test. The G3 factory must supply the Testcontainers connection string (`UseSetting("ConnectionStrings:UrlShortener", ...)`) before the host builds, or startup throws on the null connection string.
- **Migrations folder marked `generated_code = true`** in `.editorconfig` instead of hand-editing tool output to satisfy IDE0161/IDE0005.

## Local connection string (no secret in the repo)
`dotnet user-secrets init` was run on the API project (adds `UserSecretsId` to the csproj, safe to commit; the agent created no secrets file). Engineer's local setup:
```
export MSSQL_SA_PASSWORD='<your password>'        # or put it in the gitignored .env (see .env.example)
docker compose up -d --wait
dotnet user-secrets set --project src/UrlShortener.Api "ConnectionStrings:UrlShortener" \
  "Server=localhost,1433;Database=UrlShortener;User Id=sa;Password=<your password>;TrustServerCertificate=True"
dotnet run --project src/UrlShortener.Api        # Development: applies migrations at startup
# or explicitly: dotnet tool restore && dotnet ef database update --project src/UrlShortener.Api
```
`TrustServerCertificate=True` is needed only because the dev container uses a self-signed certificate (SqlClient encrypts by default). The SA password is fixed when the volume is first created; after changing it, `docker compose down -v`.

For verification the agent generated an ephemeral random password into the session scratchpad, passed the connection string as the `ConnectionStrings__UrlShortener` environment variable (same key as the user secret), never printed either, then ran `docker compose down -v` and deleted the password file.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `tests/UrlShortener.IntegrationTests/SqlServerFixture.cs` | generated (before the mapping), then edited | `IClassFixture` + `IAsyncLifetime`: starts `mcr.microsoft.com/mssql/server:2022-latest` and applies migrations. Edited: `new MsSqlBuilder()` → `new MsSqlBuilder(image)` after CS0618 (obsolete in Testcontainers 4.x) |
| `tests/UrlShortener.IntegrationTests/Links/LinkPersistenceTests.cs` | generated (before the mapping), then edited | 3 tests per the issue. Edited: underscore names → PascalCase after CA1707; `Assert.Contains(n, new[]{...})` → `Assert.True(n is 2601 or 2627)` after CA1861. The duplicate test uses `dup`, not `abc`, so tests don't depend on each other's rows |
| `src/UrlShortener.Api/Links/Link.cs` | generated | Entity per the issue; `required` on `Code`/`OriginalUrl` |
| `src/UrlShortener.Api/Data/LinksDbContext.cs` | generated, then edited | First version deliberately without `UseCollation` (TDD red); collation added after the red run |
| `src/UrlShortener.Api/Migrations/*_InitialCreate*.cs`, `LinksDbContextModelSnapshot.cs` | generated (`dotnet ef migrations add InitialCreate`) twice | First generation without collation (red). All three files deleted and regenerated after adding the collation, so there is one clean initial migration. Not hand-edited |
| `src/UrlShortener.Api/Program.cs` | edited | `AddDbContext` + `UseSqlServer(GetConnectionString("UrlShortener"), EnableRetryOnFailure)`; Development-only `MigrateAsync` |
| `src/UrlShortener.Api/UrlShortener.Api.csproj` | edited | EF SqlServer + Design references; `UserSecretsId` (from `dotnet user-secrets init`, which also inserted a UTF-8 BOM, removed to keep the diff minimal) |
| `tests/UrlShortener.IntegrationTests/UrlShortener.IntegrationTests.csproj` | edited | `Testcontainers.MsSql` reference |
| `Directory.Packages.props` | edited | 3 package versions with a dated comment |
| `.config/dotnet-tools.json` | generated (`dotnet new tool-manifest`, `dotnet tool install dotnet-ef --version 10.0.12`) | Reproducible EF tooling |
| `docker-compose.yml` | generated | SQL Server 2022, `MSSQL_SA_PASSWORD` required (`:?`), port 1433, `sqlcmd` healthcheck (`tools18`, `-C`, `$$`), named volume |
| `.env.example` | generated | Variable name, complexity rule, "fixed at volume creation" note; no value |
| `.gitignore` | edited | `!.env.example`: the existing `.env.*` rule ignored it (verified with `git check-ignore`) |
| `.editorconfig` | edited | `generated_code = true` for `src/UrlShortener.Api/Migrations/*.cs` |
| `CLAUDE.md` §9 | edited | Gotchas hit in this task (below) |
| `.github/workflows/ci.yml` | not changed | `ubuntu-latest` has Docker; Testcontainers needs nothing else. CI does not run `dotnet ef`, so no `dotnet tool restore` step |
| (proposal) `IDesignTimeDbContextFactory` | rejected | Not needed: `migrations add` works through the host with a null connection string |
| (proposal) `dotnet ef migrations remove` to redo the migration | rejected | Needs a reachable database; deleting the three uncommitted files is equivalent |

## TDD sequence (real output)

**Red 1: tests and fixture written first, no entity or context yet**
```
$ dotnet build --nologo -v q
LinkPersistenceTests.cs(3,20): error CS0234: The type or namespace name 'Api' does not exist in the namespace 'UrlShortener'
LinkPersistenceTests.cs(59,20): error CS0246: The type or namespace name 'Link' could not be found
SqlServerFixture.cs(21,12): error CS0246: The type or namespace name 'LinksDbContext' could not be found
SqlServerFixture.cs(4,20): error CS0234: The type or namespace name 'Api' does not exist in the namespace 'UrlShortener'
    0 Warning(s)
    4 Error(s)
```

**Red 2: entity + context with varchar(10) and the unique index, but no collation; migration generated**
```
$ dotnet test --nologo tests/UrlShortener.IntegrationTests
  Failed UrlShortener.IntegrationTests.Links.LinkPersistenceTests.CodeColumnUsesCaseSensitiveCollation [411 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: Latin1_General_100_CS_AS
Actual:   SQL_Latin1_General_CP1_CI_AS
  Failed UrlShortener.IntegrationTests.Links.LinkPersistenceTests.CodesDifferingOnlyByCaseAreDistinctRows [356 ms]
  Error Message:
   Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details.
---- Microsoft.Data.SqlClient.SqlException : Cannot insert duplicate key row in object 'dbo.Links' with unique index 'IX_Links_Code'. The duplicate key value is (ABC).
Failed!  - Failed:     2, Passed:     1, Skipped:     0, Total:     3, Duration: 906 ms - UrlShortener.IntegrationTests.dll (net10.0)
```
This is the bug the test exists for: under the server default collation, `ABC` is a duplicate of `abc`.

**Green: `.UseCollation("Latin1_General_100_CS_AS")` added, migration files deleted and regenerated**
```
$ dotnet test --nologo tests/UrlShortener.IntegrationTests
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 972 ms - UrlShortener.IntegrationTests.dll (net10.0)
```

## Migration SQL
```
$ dotnet ef migrations script --project src/UrlShortener.Api
CREATE TABLE [Links] (
    [Id] bigint NOT NULL IDENTITY,
    [Code] varchar(10) COLLATE Latin1_General_100_CS_AS NOT NULL,
    [OriginalUrl] nvarchar(2048) NOT NULL,
    [IsCustomAlias] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [ExpiresAt] datetimeoffset NULL,
    [DeletedAt] datetimeoffset NULL,
    CONSTRAINT [PK_Links] PRIMARY KEY ([Id])
);

CREATE UNIQUE INDEX [IX_Links_Code] ON [Links] ([Code]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261010213557_InitialCreate', N'10.0.12');
```
(The first attempt with `--no-build` printed only the history-table DDL because it used the DLL built before `migrations add`; see gotchas.)

## Compose verification
```
$ env -u MSSQL_SA_PASSWORD docker compose config
error while interpolating services.sqlserver.environment.MSSQL_SA_PASSWORD: required variable MSSQL_SA_PASSWORD is missing a value: set MSSQL_SA_PASSWORD

$ docker compose up -d --wait            (MSSQL_SA_PASSWORD exported, ephemeral)
 Container dotnet-urlshortener-sqlserver-1 Healthy
$ docker compose ps
dotnet-urlshortener-sqlserver-1 Up 14 seconds (healthy)

$ ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/UrlShortener.Api --no-launch-profile --urls http://localhost:5199
      Applying migration '20261010213557_InitialCreate'.
      Now listening on: http://localhost:5199
      Hosting environment: Development
(password occurrences in the run log: 0)

$ dotnet ef database update --project src/UrlShortener.Api
No migrations were applied. The database is already up to date.
Done.

$ MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -C ... -d UrlShortener \
    -Q "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Links'; SELECT MigrationId FROM __EFMigrationsHistory;"
Id bigint NULL NULL
Code varchar 10 Latin1_General_100_CS_AS
OriginalUrl nvarchar 2048 SQL_Latin1_General_CP1_CI_AS
IsCustomAlias bit NULL NULL
CreatedAt datetimeoffset NULL NULL
ExpiresAt datetimeoffset NULL NULL
DeletedAt datetimeoffset NULL NULL
20261010213557_InitialCreate

$ docker compose down -v
 Volume dotnet-urlshortener_sqlserver-data Removed
```

## Checks run
```
$ dotnet build --nologo -v q
    0 Warning(s)
    0 Error(s)

$ dotnet build -c Release --nologo -v q
    0 Error(s)

$ dotnet format --verify-no-changes --no-restore ; echo $?
0

$ dotnet test --nologo ; echo $?
No test is available in ...UrlShortener.UnitTests.dll   (expected: no unit tests until G3)
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 994 ms - UrlShortener.IntegrationTests.dll (net10.0)
0

$ dotnet list package --vulnerable --include-transitive
The given project `UrlShortener.Api` has no vulnerable packages given the current sources.
The given project `UrlShortener.UnitTests` has no vulnerable packages given the current sources.
The given project `UrlShortener.IntegrationTests` has no vulnerable packages given the current sources.

$ git check-ignore -v .env.example
.gitignore:8:!.env.example	.env.example      (negation matched: not ignored)
$ git check-ignore -q .env && echo ".env still ignored"
.env still ignored
```

## Gotchas encountered
Added to CLAUDE.md §9: generated migration `.cs` fails `EnforceCodeStyleInBuild` (fixed with `generated_code = true`); `migrations script --no-build` after `migrations add` scripts a stale DLL; Testcontainers 4.x parameterless `MsSqlBuilder()` is obsolete; CA1707 forbids underscored test names; `.env.*` swallowed `.env.example`; Git Bash rewrites container paths in `docker compose exec`.

## Open items
- Engineer to confirm `datetimeoffset` vs the plan's `datetime2`.
- Production migration path (B6).
- `/security-review` and a fresh-context `/code-review` before merge (high-impact: schema).

## Sign-off
Reviewed and merged by the engineer in PR #8; I understood the change and accept ownership of it. — Mohammed Saalim, sign-off recorded 2026-10-11

## Coordinator review (before commit)

Reviewed by the coordinator session (Claude Code, `claude-fable-5-1`) with the engineer's rules; the engineer signs off in the PR.

| Item | Outcome | Rationale |
|---|---|---|
| Whole diff re-read; gates re-run | accepted | build 0/0, format exit 0, 3/3 integration tests, no vulnerable packages |
| `datetimeoffset` vs plan's `datetime2` | accepted, plan updated | offset-preserving timestamps matter for expiry semantics across regions |
| Development-only startup migration | accepted | reviewer flow `docker compose up` + `dotnet run` works with no extra step; production path stays open for B6 |
| Missing-connection-string behaviour | **edited** | `/code-review` (fresh context) flagged that `UseSqlServer(null)` fails deep in `MigrateAsync` with a vague SqlClient error. Added an explicit `InvalidOperationException` in the Development block. Verified: `dotnet run` with no secrets prints the new message; `dotnet ef migrations script` still works without a database |
| `/security-review` | no findings | no user input yet; connection string from config; compose password required from env; migration only in Development. Noted (not a finding): compose publishes 1433 on all interfaces, a dev convenience |
| `Read` deny rules | gotcha added | they don't cover Bash `cat`; protection is "secrets never in the repo", not the deny list |

```
$ dotnet run (Development, no connection string configured)
Unhandled exception. System.InvalidOperationException: ConnectionStrings:UrlShortener is not configured. Set it with `dotnet user-secrets set` (see docs/SETUP.md).
$ dotnet ef migrations script --no-build | grep -c "COLLATE Latin1_General_100_CS_AS"
1
$ dotnet test --no-build
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3
```
