# .NET 8 expires a month after submission

*Research validation as of 2026-10-10. Scope: prior claims for a 2-3 day, AI-assisted, engineer-led URL shortener assignment (greenfield, brownfield, ambiguous scenarios).*

**The prior research mostly holds up, but its biggest premise is out of date: .NET 8 LTS (and with it ASP.NET Core 8 and EF Core 8) goes out of support on 2026-11-10, one month from now, while .NET 10 LTS is supported to 2028-11-14** ([.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)). That one fact makes several .NET 8 workarounds unnecessary: built-in OpenAPI (.NET 9+), built-in Minimal API validation (.NET 10+), `UseAzureSql` (EF Core 9+), and transitive NuGet auditing by default (net10.0+). Seven other prior claims are wrong or outdated. The Linux kernel no longer puts the model name in `Assisted-by:`. Spec Kit's phases now end in `converge` and its commands use hyphens. Dub runs on PlanetScale/MySQL, not Postgres. The Idempotency-Key "standard" is an expired IETF draft. `.claudeignore` has no effect. Azure Cache for Redis is being retired. Google Safe Browsing is non-commercial only. The Anthropic workflow guidance, the rate-limiter defaults, the EF Core Cosmos limitations, the Azure SQL free offer, RFC 9110 redirect semantics and the collision math all check out against primary sources. The domain risk that matters most is quiet: **SQL Server's default collation is case-insensitive**, so a plain unique index treats `abc` and `ABC` as the same code, and SQLite-based tests will not catch it. A relational design (EF Core, a SQLite or SQL Server container locally, the Azure SQL free offer in the cloud) is the recommended fit for a .NET API developer who wants local runs without a subscription. Nothing below is decided until the engineer decides it.

---

## Verdict table

Labels: **V** = VERIFIED, **PV** = PARTIALLY VERIFIED, **R** = REFUTED, **O** = OUTDATED, **U** = UNVERIFIED.

### AI workflow and traceability

| Prior claim | Verdict | Correction / note | Source |
|---|---|---|---|
| Anthropic workflow: explore → plan → implement → commit | V | Also: skip planning if the diff fits in one sentence; write the spec to SPEC.md after an interview | [Best practices](https://code.claude.com/docs/en/best-practices) |
| Give Claude a runnable check (tests/build) | V | Newer guidance adds "show evidence rather than asserting success" and Stop-hook gates | [Best practices](https://code.claude.com/docs/en/best-practices) |
| Hooks are deterministic; CLAUDE.md is advisory | V | `PreToolUse` can block a tool call | [Hooks](https://code.claude.com/docs/en/hooks) |
| Security-reviewer subagent example | V | A built-in `/security-review` command also exists | [Security](https://code.claude.com/docs/en/security) |
| Spec Kit: MIT, specify → plan → tasks → implement | O | Now constitution + specify → plan → tasks → implement → **converge**; commands are hyphenated (`/speckit-specify`); v1.1.3 released 2026-10-09 | [spec-kit](https://github.com/github/spec-kit) |
| Kernel trailer `Assisted-by: AGENT_NAME:MODEL_VERSION` | O | The Aug 2026 change removed the model name. Current form: `Assisted-by: LLM [TOOL...]` | [coding-assistants.rst](https://docs.kernel.org/process/coding-assistants.html) |
| AI must not add `Signed-off-by` (kernel) | V | Only humans can certify the DCO | [coding-assistants.rst](https://docs.kernel.org/process/coding-assistants.html) |
| Fedora uses `Assisted-by:` | PV | Proposal text verified; the final policy page sits behind a bot wall (snippet only) | [Fedora proposal](https://discussion.fedoraproject.org/t/council-policy-proposal-policy-on-ai-assisted-contributions/165092) |
| LLVM recommends `Assisted-by:` | V | Also bans autonomous agents acting without human approval | [LLVM policy](https://llvm.org/docs/AIToolPolicy.html) |
| QEMU / Gentoo ban AI content | V / PV | QEMU primary verified; Gentoo only via secondary coverage | [QEMU](https://www.qemu.org/docs/master/devel/code-provenance.html), [LWN](https://lwn.net/Articles/970072/) |
| An industry-standard per-task AI log template exists | U | None found. Build a composite from MADR + evidence + disclosure fields | [MADR template](https://raw.githubusercontent.com/adr/madr/develop/template/adr-template.md) |
| MADR 4.0 is the current ADR template | V | 4.0.0, 2024-09-17; adr-tools/log4brains are dormant (last pushes 2024) | [MADR releases](https://api.github.com/repos/adr/madr/releases/latest) |
| Archify is a real, safe MIT agent skill | PV | Real, MIT. It **runs Node scripts** and makes a network update check by default (disable with `ARCHIFY_UPDATE_CHECK_DISABLED=1`) | [archify](https://github.com/tt-a1i/archify) |
| GitHub renders Mermaid natively | V | Works in Markdown files, issues, PRs and wikis | [GitHub docs](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/creating-diagrams) |
| `.claudeignore` protects secrets | R | It "has no effect". Use `Read(./.env)` deny rules | [Permissions](https://code.claude.com/docs/en/permissions) |
| LLMs hallucinate packages (OWASP LLM09) | V | Spracklen: ≥5.2% commercial, 21.7% open-source models, 205,474 names (Python/JS only). The 19.7% aggregate is **PV** (secondary sources) | [OWASP LLM09](https://genai.owasp.org/llmrisk/llm092025-misinformation/), [arXiv 2406.10279](https://arxiv.org/abs/2406.10279) |

### .NET / EF Core / Azure

| Prior claim | Verdict | Correction / note | Source |
|---|---|---|---|
| .NET 8 is a defensible target | O | EOL 2026-11-10. Target .NET 10 (LTS to 2028-11-14). .NET 11 is RC1, with GA due Nov 2026 | [Support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) |
| Rate limiter rejects with 503 and sends no Retry-After by default | V | Set `RejectionStatusCode = 429`; add Retry-After in `OnRejected` from `MetadataName.RetryAfter` (not available for the concurrency limiter) | [Rate limit](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-8.0) |
| Partitioning by IP needs ForwardedHeaders | V | `ForwardedHeaders` defaults to None; KnownProxies covers loopback only; the docs warn of spoofing DoS risk | [Proxy config](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) |
| EF Core Cosmos: no migrations, indexes ignored | V | Update: since EF 9, `HasIndex` **throws** and sync I/O throws; EF 11 adds transactional batches | [Cosmos limitations](https://learn.microsoft.com/ef/core/providers/cosmos/limitations), [EF9 breaking](https://learn.microsoft.com/ef/core/what-is-new/ef-core-9.0/breaking-changes#azure-cosmos-db-breaking-changes) |
| `UseAzureSql()` arrived in EF Core 9/10 | PV | It arrived in **EF Core 9**. On EF 8 use `UseSqlServer(..., o => o.EnableRetryOnFailure())` | [SQL Server provider](https://learn.microsoft.com/ef/core/providers/sql-server/#usage-and-configuration) |
| HybridCache is GA and works on .NET 8 | V | GA with 9.3.0 on 2025-03-11; it is a NuGet package, not part of the shared framework | [GA blog](https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga/) |
| `Active Directory Default` works locally and in Azure | V | Caveats: slow first connect; DB user must be created; **SqlClient 6+/7+ needs `Microsoft.Data.SqlClient.Extensions.Azure`** | [SqlClient Entra](https://learn.microsoft.com/sql/connect/ado-net/sql/azure-active-directory-authentication?view=sql-server-ver17) |
| WebApplicationFactory + Testcontainers; SQLite ≠ SQL Server | V | EF team: avoid the InMemory provider, which does not enforce unique indexes | [Testing strategy](https://learn.microsoft.com/ef/core/testing/choosing-a-testing-strategy) |
| NuGet Audit is on by default | V | Nuance: `NuGetAuditMode` = `direct` for net9 and below, `all` for net10+ | [Auditing packages](https://learn.microsoft.com/nuget/concepts/auditing-packages) |
| Bounded Channel + BackgroundService for clicks | V | Default `FullMode` is `Wait`. An unhandled exception stops the host (`StopHost`). Shutdown timeout is 30 s | [Channels](https://learn.microsoft.com/dotnet/core/extensions/channels#bounding-strategies), [Hosted services](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0) |
| Built-in OpenAPI and validation for Minimal APIs | PV | OpenAPI is built in from .NET 9 (no UI); `AddValidation` from .NET 10. .NET 8 needs Swashbuckle plus filters | [ASP.NET Core 10 new](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0#minimal-apis) |
| azd-starter-bicep supports Container Apps | PV | The starter's README names appservice/function/aks; you author the `containerapp` host yourself | [azd + ACA](https://learn.microsoft.com/azure/developer/azure-developer-cli/container-apps-workflows) |
| Azure SQL free offer | V | 100k vCore-s, 32 GB, 10 DBs, no time limit, "regardless of subscription type"; GA Jan 2025 | [Free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer?view=azuresql) |
| Idempotency-Key is an IETF standard | R | Expired draft -07 (2025-10-15). Its semantics (400/422/409) are fine to follow "per the draft" | [Datatracker](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/) |

### Stack and storage

| Prior claim | Verdict | Correction / note | Source |
|---|---|---|---|
| Microsoft recommends Minimal APIs for new projects | V | Use controllers only for OData, custom model binders, or a team standard | [APIs overview](https://learn.microsoft.com/aspnet/core/fundamentals/apis?view=aspnetcore-10.0) |
| SQL Server / LocalDB default to a CI collation | V | `SQL_Latin1_General_CP1_CI_AS`; LocalDB's collation cannot be changed. Azure SQL DB single-DB default is **U** | [EF collations](https://learn.microsoft.com/ef/core/miscellaneous/collations-and-case-sensitivity) |
| `Latin1_General_100_BIN2` is the recommended binary collation | U | No source fetched; `_CS_AS` is verified as case-sensitive | no source; check learn.microsoft.com/sql/relational-databases/collations |
| SQLite and PostgreSQL are case-sensitive by default | V | SQLite BINARY = memcmp; NOCASE folds ASCII only | [SQLite](https://www.sqlite.org/datatype3.html), [Npgsql](https://www.npgsql.org/efcore/misc/collations-and-case-sensitivity.html) |
| Cosmos free tier is 1000 RU/s + 25 GB | V | One account per subscription; not available for serverless | [Cosmos free tier](https://learn.microsoft.com/azure/cosmos-db/free-tier) |
| Postgres Flexible Server is free | PV | Free for **12 months** only (B1MS 750 h), for new free-account customers only | [PG free](https://learn.microsoft.com/azure/postgresql/configure-maintain/how-to-deploy-on-azure-free-account) |
| Azure Cache for Redis as the cache | O | Basic/Standard/Premium retire 2028-09-30; Azure Managed Redis replaces it (no free tier: **U**) | [Retirement FAQ](https://learn.microsoft.com/azure/azure-cache-for-redis/retirement-faq) |
| Container Apps scales to zero with a free grant | V | 180k vCPU-s, 360k GiB-s and 2M requests per month | [ACA billing](https://learn.microsoft.com/azure/container-apps/billing#consumption-plan) |
| Dub uses Postgres / Tinybird / Redis | R | Not Postgres: PlanetScale (MySQL) + Prisma. Tinybird and Upstash Redis are confirmed | [dubinc/dub](https://github.com/dubinc/dub) |
| YOURLS uses MySQL | PV | Repo page confirms; requirements page returned 404 | [YOURLS](https://github.com/YOURLS/YOURLS) |
| AzUrlShortener (FBoucher) uses Table Storage | PV | `FBoucher/` redirects to `microsoft/AzUrlShortener`, **archived 2026-09-09**; the active fork is `fboucher/AzUrlShortener` (.NET 10, Aspire) | [microsoft/AzUrlShortener](https://github.com/microsoft/AzUrlShortener) |
| Health checks / Options validation / OpenTelemetry are standard | U | Standard practice, but no page was fetched | no source; check learn.microsoft.com health-checks and OTel pages |

### Domain and security

| Prior claim | Verdict | Correction / note | Source |
|---|---|---|---|
| 7-char Base62 + unique index + retry is sound | V | 62^7 ≈ 3.52e12. The birthday 50% point is ~2.2M links, but each insert's collision chance stays tiny (2.8e-7 at 1M) | computed; [GetString](https://learn.microsoft.com/dotnet/api/system.security.cryptography.randomnumbergenerator.getstring?view=net-10.0) |
| `System.Random` is unsafe for codes | V | CA5394 is **off by default**, so AI code using it compiles cleanly | [CA5394](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca5394) |
| Sqids/Hashids are not secure | V | "No encryption of any kind"; IDs decode to numbers | [sqids FAQ](https://sqids.org/faq) |
| 302 keeps clicks observable; 301 is cached | V | RFC 9110: 301/308 are heuristically cacheable. **Cache headers decide in practice** | [RFC 9110 §15.4](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.4) |
| bit.ly uses 301 | V | Observed with the researcher's curl: 301 + `private, max-age=90`; TinyURL 301 + `no-store`; Dub 302. Observations, not vendor docs | [Dub blog](https://dub.co/blog/301-vs-302-redirect) |
| 404 vs 410 for dead links | V | 410 means "likely permanent"; both are heuristically cacheable, so send `no-store` | [RFC 9110 §15.5](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.11) |
| SSRF is a core risk | PV | Only if the server fetches URLs. With no fetch, the main risk is open-redirect/phishing relay | [OWASP SSRF](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html) |
| Use Google Safe Browsing for reputation | R | For commercial use: it is non-commercial only, and v4 is deprecated. Web Risk Lookup is free to 100k calls/month (**PV**) | [Safe Browsing v4](https://developers.google.com/safe-browsing/v4), [Web Risk pricing](https://cloud.google.com/web-risk/pricing) |
| SmartScreen offers a URL lookup API | U | No public developer API found (likely false) | [SmartScreen demos](https://learn.microsoft.com/en-us/defender-endpoint/defender-endpoint-demonstration-smartscreen-url-reputation) |
| IP addresses are personal data (GDPR) | PV | CJEU Breyer, via secondary legal commentary | [Bird & Bird](https://twobirds.com/en/insights/2016/global/cjeu-decision-on-dynamic-ip-addresses-touches-fundamental-dp-law-questions) |
| AI code is often insecure | V | Veracode (vendor study): 45% vulnerable; Perry et al.: AI users were more insecure and more overconfident | [Veracode](https://www.veracode.com/press-release/ai-generated-code-poses-major-security-risks-in-nearly-half-of-all-development-tasks-veracode-research-reveals/), [arXiv 2211.03622](https://arxiv.org/abs/2211.03622) |

---

## Stack and storage options

**Context:** the engineer is a .NET API developer with a personal Azure subscription, and the reviewer must be able to run the app locally with no subscription. Both of the relevant Azure free amounts apply "regardless of subscription type": the Azure SQL free offer ([free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer?view=azuresql)) and the Container Apps monthly grant ([ACA billing](https://learn.microsoft.com/azure/container-apps/billing#consumption-plan)). The Cosmos lifetime free tier also applies. The 12-month free services (PostgreSQL B1MS, Container Registry) are for "new Azure customers only" ([Azure account](https://azure.microsoft.com/en-us/pricing/purchase-options/azure-account)), so they likely do **not** apply to an existing subscription. That strengthens the case for Azure SQL.

### Application stack

| Option | Pros | Cons |
|---|---|---|
| **.NET 10 + Minimal APIs** (feature-grouped `MapGroup`, `TypedResults`) | LTS to 2028; built-in OpenAPI, `AddValidation`, ProblemDetails, no third-party packages needed; Microsoft's stated default | A reviewer stuck on an older SDK needs the .NET 10 SDK installed |
| .NET 10 + controllers | `[ApiController]` conventions; familiar to MVC teams | More ceremony; Microsoft lists it only for specific needs |
| .NET 8 (any style) | Matches older reviewer environments | **Unsupported in a month**; needs Swashbuckle plus hand-rolled validation; must state EOL in README and set `CheckSdkVulnerabilities` |
| Node / Go / Python | All viable in 2-3 days | Throws away the candidate's .NET signal (comparison unsourced) |

### Primary storage

| Option | Local run without subscription | Case-sensitive unique code | EF Core | Cloud free path | Fit |
|---|---|---|---|---|---|
| **SQLite** (file) | Zero dependencies | Yes (BINARY default) | Good; no DB-generated concurrency tokens; rebuild-style migrations | None (not a server) | Best "clone and run"; weak production story |
| **SQL Server Linux container** | Docker Desktop + ≥2 GB RAM, x86-64 only | Only with explicit `_CS_AS`/`_BIN2` column collation | First-party, full migrations | **Azure SQL free offer**, no time limit | Highest fidelity to the cloud target |
| SQL Server LocalDB | Windows only, no Docker (**U**) | Collation fixed at CI; column override is mandatory | First-party | Same as above | Windows-only fallback |
| PostgreSQL container | Light container (memory **U**) | Yes by default | Npgsql, excellent | Flexible Server free for 12 months only | Strong, but the cloud free tier expires |
| Cosmos DB (emulator) | Windows installer or Linux vNext (NoSQL-only, no auth) | Unique keys per partition, immutable; **EF ignores or throws on `HasIndex`** | No migrations | 1000 RU/s + 25 GB lifetime | Mismatched to the problem's relational shape |
| Table Storage (Azurite) | npm/Docker/VS bundled | PK+RK uniqueness built in (ordinal semantics **U**) | None (SDK only) | Very cheap (pricing **U**) | AzUrlShortener precedent; loses the EF/relational talking points |

### Cache and analytics

| Concern | Options | Trade-off |
|---|---|---|
| Redirect cache | HybridCache L1-only / IMemoryCache / Redis | Both in-process options are enough for one replica ([caching guidance](https://learn.microsoft.com/dotnet/core/extensions/caching#hybrid-caching)). HybridCache adds stampede protection, and a Redis L2 can be added later without changing callers. Other replicas' L1 is not invalidated, and ACA scale-to-zero leaves the cache cold |
| Click analytics | Append-only `Clicks` table in the same DB via a bounded channel / counter column / Azurite table / queue + OLAP | The same-DB append table keeps one dependency. The counter has hot-row contention and no dimensions. Queue + OLAP is the dub/Sink production shape and over-engineering for 2-3 days |

### Hosting (optional demo)

| Option | Idle cost | Caveat |
|---|---|---|
| Container Apps Consumption + `azd` | $0 compute when scaled to zero | ACR's free window is new-customer only, so the registry is likely a monthly cost (price **U**) unless you use GitHub Container Registry; Log Analytics ingestion beyond free is **U** |
| App Service F1 | $0 | 60 CPU-min/day, 32-bit, shared ([limits](https://learn.microsoft.com/azure/azure-resource-manager/management/azure-subscription-service-limits#azure-app-service-limits)) |
| Functions Flex Consumption | Near $0 | Changes the programming model; needs a Storage account |

**Azure SQL free-offer trap:** serverless bills vCore-seconds whenever the DB is online. Open pooled connections or health probes that hit the DB block auto-pause. One user burned the monthly 100k vCore-seconds in 2 days at ~0.3% CPU ([Microsoft Q&A](https://learn.microsoft.com/answers/a/12568728)). By arithmetic, 100k s ≈ 27.8 vCore-hours. Keep health checks off the DB and set an alert on "Free amount remaining".

### Recommendation (not a decision)

> **.NET 10 Minimal APIs + EF Core**, with a **provider switch**: SQLite as the zero-dependency default so the reviewer can clone and run, and SQL Server via `docker compose` (or Testcontainers in tests) as the high-fidelity path. Azure SQL free offer + Container Apps via `azd` are optional, documented but not required. Add **HybridCache (L1 only)** on the redirect path and an **append-only `Clicks` table** fed by a bounded channel with `DropWrite`. Constraints this choice brings: keep separate migrations per provider (or `EnsureCreated` for SQLite tests only), and make the SQL Server collation call provider-conditional, because SQL Server collation names fail on SQLite. Ruled out for a 2-3 day prototype: Cosmos (no migrations, EF uniqueness gaps) and Redis as a primary store.

---

## Domain design guidance

| Topic | Guidance | Evidence |
|---|---|---|
| **Short codes** | `RandomNumberGenerator.GetString(alphabet, 7)`; insert, catch the unique violation (SQL 2601/2627), retry up to N times. **Never check-then-insert** (Kutt does, and it is TOCTOU-prone). Reject reserved words (`api`, `health`, `openapi`, `admin`, `robots.txt`...) case-insensitively. Codes are not secrets, so do not use them as authorization | [GetString](https://learn.microsoft.com/dotnet/api/system.security.cryptography.randomnumbergenerator.getstring?view=net-10.0), [kutt utils](https://github.com/thedevs-network/kutt/blob/main/server/utils/utils.js) |
| **Collation** | On SQL Server: `HasMaxLength(10).IsUnicode(false)` + `.UseCollation("..._CS_AS" or BIN2)`, set at column level (query-level `Collate` defeats the index). Alternative: a Base36/Crockford alphabet, lower-cased (36^7 ≈ 7.8e10 codes) | [EF collations](https://learn.microsoft.com/ef/core/miscellaneous/collations-and-case-sensitivity) |
| **Redirects** | **302 + `Cache-Control: private, no-store`** (optionally `X-Robots-Tag: noindex`). 301/308 are heuristically cacheable, so later edits, deletes and click counts stop working. Shlink and Kutt default to 302 | [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.4), [Shlink](https://shlink.io/documentation/environment-variables/) |
| **404 / 410** | 404 for unknown codes; 410 for expired/deleted, with `no-store`. Or 404 for everything, which hides that a code ever existed. Document the choice | [RFC 9110 §15.5](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.11) |
| **Destination validation / SSRF** | Absolute URL; http/https only; no userinfo; length cap (2048-8000); reject self-host loops and literal private IPs and `localhost`; treat parser disagreement as a rejection. If a feature ever fetches URLs (previews), add real SSRF controls (bind to the validated IP, no redirect following) | [OWASP SSRF](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html), [OWASP redirects](https://cheatsheetseries.owasp.org/cheatsheets/Unvalidated_Redirects_and_Forwards_Cheat_Sheet.html) |
| **Abuse / rate limiting** | Built-in rate limiter on `POST /links`, set to 429 + Retry-After. If partitioning by IP, call `UseForwardedHeaders` first with `KnownNetworks` set, and bound partition keys | [Rate limit](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-8.0) |
| **Reputation checks** | `IUrlReputationChecker` seam: no-op default + local domain denylist (Kutt's approach). Web Risk is the compliant Google option; Safe Browsing is non-commercial only; URLhaus covers malware only and needs an Auth-Key | [Safe Browsing](https://developers.google.com/safe-browsing/v4), [URLhaus](https://urlhaus.abuse.ch/api/) |
| **Analytics** | Bounded channel with `DropWrite` + a dropped-events counter; per-item try/catch; drain on shutdown within 30 s; best-effort and lost on crash (document it). Store a truncated IP (/24, /48), a salted hash, or nothing (Shlink anonymizes by default) | [Channels](https://learn.microsoft.com/dotnet/core/extensions/channels#bounding-strategies), [Shlink](https://shlink.io/documentation/environment-variables/) |
| **Idempotency** | Optional. If built: a table of key, request hash, response and expiry; 422 on a different payload, 409 while in flight. Describe it as "per expired IETF draft -07". Minimum: document it as a known gap | [draft -07](https://www.ietf.org/archive/id/draft-ietf-httpapi-idempotency-key-header-07.html) |

---

## AI traceability and quality gates

**Workflow per scenario.** Explore (plan mode) → human-approved plan → implement with tests → fresh-context review → human commit ([best practices](https://code.claude.com/docs/en/best-practices)). Borrow Spec Kit's *artifact shapes* (`constitution.md`, `spec.md`, `plan.md`, `tasks.md`) rather than installing its Python/uv toolchain. Greenfield maps to constitution → spec; brownfield maps to the existing-project guide plus the bug workflow; ambiguous requirements map to clarify or its "go / clarify / stop" assessment ([spec-kit](https://github.com/github/spec-kit)).

**Commit trailers.** Use `Assisted-by: Claude Code (<model>)` in human-written commits. Keeping the model name departs from the kernel's current rule but serves traceability in a take-home. Add an optional custom `AI-Log: docs/ai-log/NNN.md` pointer (invented here, not a standard). `Signed-off-by` is for humans only. Claude Code adds a `Co-Authored-By` trailer by default; change it with `attribution.commit` (`includeCoAuthoredBy` is deprecated) ([settings](https://code.claude.com/docs/en/settings-reference)). `Assisted-by` is more defensible than `Co-authored-by`, which implies accountability an AI cannot hold.

**Per-task log fields** (`docs/ai-log/NNN-slug.md`, MADR-like; no standard exists):
- Task ID, date, tool and model
- Intent and constraints (no new packages without approval, no secrets)
- Runnable acceptance criteria
- Prompts (verbatim or summarized, redacted)
- Outcome per artifact: **generated / edited (why) / rejected (why)**
- **Checks run**: commands, exit codes, test counts (this maps to MADR's "Confirmation" section)
- Risk level (low / high-impact)
- **Human sign-off**: name, date, "reviewed diff, understood, accept"

**ADRs and diagrams.** Hand-written MADR 4.0 files in `docs/adr/NNNN-title.md`, with Mermaid in Markdown as the diffable source of truth (GitHub renders it natively) ([GitHub docs](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/creating-diagrams)).

**Archify verdict.** Optional at most. It is real, MIT, ~81.6k stars, but it is third-party executable Node code the agent runs with your permissions, plus a phone-home update check and a bundled zip ([archify](https://github.com/tt-a1i/archify)). If you use it: pin the version, read the scripts, set `ARCHIFY_UPDATE_CHECK_DISABLED=1`, generate one HTML file from the Mermaid source as a *derived* artifact, and log it.

**Secure AI usage.**
- Checked-in `.claude/settings.json` deny rules such as `Read(**/.env)` and user-secrets paths (`.claudeignore` does nothing) ([permissions](https://code.claude.com/docs/en/permissions)).
- Use `dotnet user-secrets` or environment variables. Never paste connection strings into prompts. Use passwordless `Active Directory Default` for Azure SQL.
- Verify every NuGet package on nuget.org (owner, downloads, repo), pin it, and log it. Run `dotnet list package --vulnerable`.
- Run `/security-review` or the security-reviewer subagent on high-impact diffs. **A human makes the final call**, as Fedora and LLVM require.

**Hooks and gates.** CLAUDE.md is advisory; hooks are deterministic ([best practices](https://code.claude.com/docs/en/best-practices)). Commit a PostToolUse/Stop hook that runs `dotnet build` + `dotnet test`. Directory.Build.props: `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, CA5394 as error, and `NuGetAuditMode=all` if on net8. Add `dotnet format --verify-no-changes` and coverlet coverage in CI ([quality props](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#code-analysis-properties), [NuGet audit](https://learn.microsoft.com/nuget/concepts/auditing-packages)).

---

## Risks specific to AI-generated code in this domain

Review checklist; tick each before human sign-off:

- [ ] `System.Random` used for codes (CA5394 is off by default); must be `RandomNumberGenerator`
- [ ] Uniqueness enforced by an "exists?, then insert" check instead of a unique index plus catching the violation and retrying
- [ ] Short-code column left on a CI collation; no `abc` vs `ABC` test run against **SQL Server** (SQLite passes it, SQL Server fails it)
- [ ] Tests on the EF InMemory provider (does not enforce unique indexes)
- [ ] `Uri.TryCreate(..., UriKind.RelativeOrAbsolute)`, or no scheme allowlist (`javascript:`, `data:`, `file:` accepted)
- [ ] 301 used by default, or a 302 with a long `max-age`
- [ ] `Channel.CreateUnbounded`, or a bounded channel with default `Wait` awaited from the redirect path
- [ ] `catch (Exception) {}` swallowing everything in `BackgroundService`, or no per-item catch at all (`StopHost` kills the web app)
- [ ] `CancellationToken` not passed to EF Core or channel reads; no drain on shutdown
- [ ] Rate limiter left at 503 with no Retry-After; IP partitioning without `UseForwardedHeaders`/`KnownNetworks`; unbounded partition keys
- [ ] `HasIndex(...).IsUnique()` on Cosmos (ignored in EF 8, throws in EF 9+)
- [ ] SqlClient 6+/7+ pinned without `Microsoft.Data.SqlClient.Extensions.Azure` (fails at runtime with "Cannot find an authentication provider")
- [ ] Hallucinated or wrong NuGet names or versions; unvetted new dependencies
- [ ] Secrets or connection strings in `appsettings*.json` or in prompts
- [ ] Raw user-supplied URLs written to logs (log injection, CWE-117; Veracode reports 88% model failure on it)
- [ ] Raw client IPs stored in the clicks table
- [ ] Tests that assert mock calls rather than HTTP behavior (status, `Location`, `Cache-Control`) via `WebApplicationFactory`
- [ ] Shared migrations folder across SQLite and SQL Server; SQL Server collation name applied unconditionally

---

## Open questions for the engineer

1. **Target framework.** (a) .NET 10: supported to 2028, fewer packages; reviewer needs the .NET 10 SDK. (b) .NET 8: matches older environments but is unsupported from 2026-11-10; README must disclose it, and it needs Swashbuckle plus custom validation.
2. **API style.** (a) Minimal APIs: Microsoft's default for new projects, less code. (b) Controllers: familiar conventions, more ceremony. Pick controllers only if the reviewer's team standardizes on them.
3. **Default local database.** (a) SQLite: clone-and-run, but weak fidelity (case sensitivity, migrations). (b) SQL Server container: real engine, but the reviewer needs Docker and 2 GB RAM. (c) Both, via a provider switch: best of both, at the cost of two migration paths.
4. **Cloud target, if any.** (a) None (local only, `azd`/Bicep included but not run): zero cost, weaker "deployed" story. (b) Azure SQL free + Container Apps: $0 idle, but watch the vCore-second burn. (c) PostgreSQL Flexible: the 12-month free tier is for new customers only, so it is likely paid from day one on your existing subscription.
5. **Short-code alphabet.** (a) Base62 + explicit CS/BIN2 collation: larger keyspace, needs collation discipline and tests. (b) Base36/Crockford, case-insensitive: removes the whole collation bug class and tolerates retyping, with a smaller keyspace.
6. **Are links mutable or deletable?** Yes means cache invalidation matters and 301 is unsafe. No (write-once) simplifies caching and analytics.
7. **Expired/deleted response.** (a) 404 + 410: clearer semantics. (b) 404 for all: hides existence, less informative.
8. **Analytics depth.** (a) Append-only clicks table: dimensions, one dependency. (b) Counter column only: simplest, hot-row contention. (c) None: least scope, but drops an expected feature. Also decide whether IPs are dropped, truncated or hashed.
9. **Idempotency-Key in scope?** Yes costs about half a day including the 409 in-flight case. No: document it as a known gap.
10. **Reputation check.** (a) No-op seam + denylist: cheap, documented gap. (b) Web Risk: real screening, needs a Google Cloud account and API key in secrets.
11. **Integration tests.** (a) Testcontainers.MsSql: exercises real collation and UNIQUE; needs Docker locally and in CI. (b) SQLite only: fast, but misses the case-sensitivity bug.
12. **Commit attribution.** (a) `Assisted-by` + `attribution.commit` configured: defensible accountability story. (b) Claude's default `Co-Authored-By`: zero effort, but implies co-authorship; explain the choice in the README.
13. **Gate strength.** (a) Committed Stop/PostToolUse hooks + CI: visible, deterministic evidence. (b) CLAUDE.md instructions only: advisory, weaker evidence for graders.
14. **Archify.** (a) Skip: no supply-chain question to answer. (b) One-off derived HTML with the update check disabled: visual polish, but needs an audit note.

---

## Gaps in this research

- **The assignment brief was not used to test claims.** It is employer-classified, so grading weights and any mandated framework (e.g. .NET 8) are unconfirmed and could override questions 1-3.
- UNVERIFIED, with no primary source: BIN2 as the recommended binary collation; EF's default `nvarchar(max)` string mapping; the Azure SQL single-DB default collation; LocalDB-as-fallback details; PostgreSQL container memory; Table Storage key comparison semantics; health checks / Options / OpenTelemetry pages; Aspire + `azd` integration.
- Prices not fetched: Azure Managed Redis smallest SKU, ACR Basic, Container Apps beyond the grant, Log Analytics, PostgreSQL B1ms after 12 months, Service Bus/Storage Queues. Web Risk pricing comes from a truncated fetch.
- Conflicts left open: the Azure SQL serverless auto-pause minimum delay (60 min in the docs vs 20 min in a Q&A answer); HybridCache's NuGet page listed "earliest GA 10.0.0" (treated as a parser artifact; 9.3.0 GA is authoritative).
- Weaker evidence: Fedora's final policy text (behind a bot wall), Gentoo (secondary), the Spracklen 19.7% aggregate (secondary), Breyer (legal commentary), bit.ly/TinyURL/Dub status codes (curl observations, not vendor docs), Veracode (vendor study).
- Not researched: Container Apps / App Service SIGTERM grace periods; xUnit v3 compatibility; GitHub Copilot content-exclusion equivalents; a primary-source PR template with AI disclosure; whether the Idempotency-Key draft was revived after its April 2026 expiry; whether Safe Browsing v5 keeps the non-commercial clause; Archify's full script audit (e.g. `visual-check.mjs` launching a headless browser).
- No published study covers AI failure modes specific to URL shorteners or .NET Minimal APIs. The risk checklist is inferred from general studies, vendor docs and reference repos.
