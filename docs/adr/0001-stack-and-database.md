# ADR-0001: Use .NET 10 Minimal APIs, EF Core 10 and a single SQL Server provider

- Status: accepted
- Date: 2026-10-10
- Deciders: Mohammed Saalim
- AI assistance: Claude Code drafted options; decision and wording are the engineer's

## Context and problem statement
The prototype is built by a .NET API developer in 2 to 3 days. A reviewer must be able to run it locally without an Azure subscription, while the engineer has a personal Azure subscription for an optional cloud deploy. .NET 8 LTS goes out of support on 2026-11-10, one month after this work, while .NET 10 LTS is supported to 2028-11-14 ([support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)). Which application stack and primary database should the prototype use?

## Decision drivers
- Supported runtime for the lifetime of the review (.NET 8 EOL 2026-11-10).
- Reviewer can clone and run it; cloud is optional.
- Real behaviour under test: the case-sensitive unique short code (see [ADR-0002](0002-short-codes-and-collation.md)) must be provable on the engine that runs in the cloud.
- One migration path and no configurability nobody asked for (CLAUDE.md section 2).
- Cost: the cloud path must fit free allowances that apply "regardless of subscription type".

## Considered options
1. .NET 10 + Minimal APIs + EF Core 10 + SQL Server only
2. .NET 10 + controllers
3. .NET 8 (any API style)
4. .NET 10 + EF Core with a provider switch: SQLite locally, SQL Server in containers and cloud
5. PostgreSQL (container locally, Flexible Server in the cloud)
6. Cosmos DB
7. Azure Table Storage

## Decision outcome
Chosen option: "1. .NET 10 Minimal APIs + EF Core 10 + SQL Server as the only provider", because it is supported until 2028, uses Microsoft's default API style, and runs the same engine in tests (Testcontainers), locally (`docker compose`) and in the cloud (Azure SQL free offer), so the collation behaviour tested is the collation deployed.

### Consequences
- Good: built-in OpenAPI (.NET 9+), built-in validation (.NET 10+), transitive NuGet auditing on by default for net10.0+ ([research report](../research/reports/URL%20shortener%20assignment%20validation.md), executive summary), available without third-party packages (Scalar UI is the one third-party addition, Development only). The validator was hand-written anyway; see AI log 003.
- Good: one migration path; collation and unique-index behaviour are exercised on a real SQL Server in every integration test class.
- Good: the Azure SQL free offer (100k vCore-seconds, 32 GB, 10 databases, no time limit) and the Container Apps monthly grant apply to an existing subscription ([free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer?view=azuresql), [ACA billing](https://learn.microsoft.com/azure/container-apps/billing#consumption-plan)).
- Bad: **the reviewer needs Docker** (Docker Desktop) to run the API and the integration tests; the first test run pulls a SQL Server image of about 1.5 GB. A reviewer on an older SDK needs the .NET 10 SDK ([`global.json`](../../global.json) pins 10.0.101).
- Bad: **there is no production migration path yet.** Migrations apply at startup in Development only; the options (idempotent migration script run by the pipeline, or a migration bundle) are open for the Azure task (AI log 002).
- Neutral / follow-ups: Azure SQL serverless bills vCore-seconds whenever the database is online, so `/health/live` must not touch the DB (implemented, tested). `UseSqlServer` is used rather than `UseAzureSql`; revisit at deploy time.

### Confirmation
- [`global.json`](../../global.json) pins SDK 10.0.101; `Directory.Packages.props` pins EF Core packages at 10.0.12.
- CI ([`ci.yml`](../../.github/workflows/ci.yml)) installs the SDK from `global.json` and runs the integration tests on `ubuntu-latest`.
- `SqlServerFixture` ([`tests/UrlShortener.IntegrationTests/SqlServerFixture.cs`](../../tests/UrlShortener.IntegrationTests/SqlServerFixture.cs)) starts `mcr.microsoft.com/mssql/server:2022-latest` per test class and applies the migrations.
- `dotnet test` passes (72 tests at the end of Day 1: 47 unit, 25 integration).

## Pros and cons of the options
### 1. .NET 10 + Minimal APIs + EF Core + SQL Server only
- Good: see above; LTS to 2028-11-14; Microsoft's stated default for new projects ([APIs overview](https://learn.microsoft.com/aspnet/core/fundamentals/apis?view=aspnetcore-10.0)).
- Bad: Docker required; heavier than a file database.

### 2. .NET 10 + controllers
- Good: `[ApiController]` conventions, familiar to MVC teams.
- Bad: more ceremony; Microsoft lists controllers only for OData, custom model binders or a team standard. None applies.

### 3. .NET 8
- Good: matches older reviewer environments.
- Bad: unsupported from 2026-11-10; needs Swashbuckle plus hand-rolled validation; would require an EOL disclosure. The research report notes the brief itself was not used to test claims, so a mandated framework could override this (open question 1 in the report).

### 4. Provider switch (SQLite default, SQL Server optional)
- This was the research report's recommendation, not a decision. It was not chosen.
- Good: zero-dependency clone and run; SQLite is case-sensitive by default (BINARY).
- Bad: two migration paths and a provider-conditional collation call (SQL Server collation names fail on SQLite); the default-path tests would not catch the SQL Server case-insensitivity bug, which is the main domain risk. The "no configurability not asked for" rule also argues against it. Trade-off accepted: Docker for the reviewer.

### 5. PostgreSQL
- Good: case-sensitive by default; excellent Npgsql support.
- Bad: the Azure Flexible Server free tier is 12 months and for new Azure customers only ([PG free](https://learn.microsoft.com/azure/postgresql/configure-maintain/how-to-deploy-on-azure-free-account)), so it likely is not free on an existing subscription. The report marks container memory use as UNVERIFIED.

### 6. Cosmos DB
- Good: lifetime free tier (1000 RU/s + 25 GB).
- Bad: no EF migrations; since EF 9, `HasIndex` throws ([Cosmos limitations](https://learn.microsoft.com/ef/core/providers/cosmos/limitations)). Mismatched to a relational uniqueness problem.

### 7. Table Storage
- Good: very cheap; PK+RK uniqueness built in; precedent in AzUrlShortener.
- Bad: no EF; loses the relational design. The report marks its key-comparison semantics and pricing UNVERIFIED.

## More information
- Research: [`docs/research/reports/URL shortener assignment validation.md`](../research/reports/URL%20shortener%20assignment%20validation.md), sections "Stack and storage options" and "Verdict table".
- Plan: [`docs/plan.md`](../plan.md), "Decisions I'm making".
- Implementation: [AI log 001](../ai-log/001-solution-scaffold.md), [AI log 002](../ai-log/002-persistence.md).
