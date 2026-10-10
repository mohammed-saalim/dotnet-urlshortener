# ADR-0002: Generate random Base62 short codes, stored with a case-sensitive collation

- Status: accepted
- Date: 2026-10-10
- Deciders: Mohammed Saalim
- AI assistance: Claude Code drafted options; decision and wording are the engineer's

## Context and problem statement
Every link needs a short, unique code, and users may also choose a custom alias. Base62 codes are case-sensitive (`abc` and `ABC` are different codes), but SQL Server's default collation, `SQL_Latin1_General_CP1_CI_AS`, is case-insensitive, so a plain unique index silently treats them as one value ([EF collations](https://learn.microsoft.com/ef/core/miscellaneous/collations-and-case-sensitivity)). How are codes generated, how is uniqueness guaranteed, and how is the collation trap closed?

## Decision drivers
- Uniqueness must hold under concurrent requests (no check-then-insert race).
- A large keyspace, so collisions stay rare without coordination.
- Codes are not secrets, but they should not be trivially enumerable or decodable.
- The collation bug must be caught by a test on the real engine (SQLite would not catch it).
- Simple to explain: no extra service, no counters.

## Considered options
1. Random 7-character Base62 from `RandomNumberGenerator.GetString`, unique index, insert and retry
2. Sequential: Base-N encoding of the identity column
3. Sqids or Hashids over the identity column
4. Case-insensitive alphabet (Base36 or Crockford base32), lower-cased

## Decision outcome
Chosen option: "1. random 7-character Base62, unique index, insert and retry", because it needs no coordination, has a 3.5e12 keyspace, and uniqueness is enforced by the database rather than by application logic.

Details:
- **Generation:** `RandomNumberGenerator.GetString(Base62, 7)` ([docs](https://learn.microsoft.com/dotnet/api/system.security.cryptography.randomnumbergenerator.getstring?view=net-10.0)). Never `System.Random` (CA5394 is off by default, so it would compile cleanly; here it is an error).
- **Uniqueness:** insert, catch the SQL Server unique violation (error 2601 or 2627), generate a new code and retry, **max 5 attempts**, then fail with 500. Never check-then-insert (TOCTOU).
- **Collation:** column `Code varchar(10) COLLATE Latin1_General_100_CS_AS` with a unique index, set at **column** level. A query-level `COLLATE` would defeat the index.
- **Custom aliases:** 4 to 10 Base62 characters, compared against a small reserved list (`api`, `health`, `openapi`, `scalar`, `admin`) case-insensitively. A taken alias returns 409 and is not retried. Day 3 expands the list.

### Consequences
- Good: the collision maths is comfortable. 62^7 is about 3.52e12 codes. The per-insert collision probability at 1M existing links is about 2.8e-7. The birthday 50% point (some pair collides) is only about 2.2M links, but that does not threaten correctness because every insert is checked by the index and retried ([research report](../research/reports/URL%20shortener%20assignment%20validation.md), "Domain and security", first row). Five consecutive collisions at 1M links is about (2.8e-7)^5 (my arithmetic), effectively never.
- Good: the collation trap is closed by one mapping line plus tests on a real SQL Server; the red run is recorded.
- Bad: needs collation discipline: any future migration or manual DDL that recreates `Code` must keep the collation. The tests exist to catch that.
- Bad: codes are not secret and not authorization; anyone who knows a code can follow it.
- Neutral: **`_CS_AS` is verified** as case-sensitive. The report marks the claim that `Latin1_General_100_BIN2` is the recommended binary collation as UNVERIFIED (no source fetched), so `_BIN2` was neither used nor asserted. Azure SQL's single-database default collation is also UNVERIFIED in the report; the explicit column collation makes that irrelevant.

### Confirmation
- Evidence the trap is real: [AI log 002](../ai-log/002-persistence.md), "Red 2": with `varchar(10)` and a unique index but no collation, `CodesDifferingOnlyByCaseAreDistinctRows` failed with `Cannot insert duplicate key row ... The duplicate key value is (ABC)`, and `CodeColumnUsesCaseSensitiveCollation` read back `SQL_Latin1_General_CP1_CI_AS`. After `.UseCollation("Latin1_General_100_CS_AS")` all three tests passed.
- Persistence: `LinkPersistenceTests.CodesDifferingOnlyByCaseAreDistinctRows`, `CodeColumnUsesCaseSensitiveCollation` and `DuplicateCodeIsRejectedByUniqueIndex` in [`LinkPersistenceTests.cs`](../../tests/UrlShortener.IntegrationTests/Links/LinkPersistenceTests.cs).
- End to end: `RedirectEndpointTests.CodeDifferingOnlyInCaseIsNotFound` in [`RedirectEndpointTests.cs`](../../tests/UrlShortener.IntegrationTests/Redirect/RedirectEndpointTests.cs).
- Retry and conflict behaviour: `CreateLinkEndpointTests.RetriesWithNewCodeWhenGeneratedCodeCollides`, `GivesUpAfterMaxGeneratedCodeCollisions` and `DuplicateCustomAliasReturnsConflict` in [`CreateLinkEndpointTests.cs`](../../tests/UrlShortener.IntegrationTests/Links/CreateLinkEndpointTests.cs). The agent's mutation check (attempt limit set to 1) made the retry test fail (AI log 003).
- Generator: [`ShortCodeTests.cs`](../../tests/UrlShortener.UnitTests/Links/ShortCodeTests.cs) (length 7, Base62 alphabet, all 62 characters appear across 5000 codes).
- Command: `dotnet ef migrations script` output contains `COLLATE Latin1_General_100_CS_AS` (AI log 002).

## Pros and cons of the options
### 1. Random Base62 + unique index + retry
- Good: no shared counter; works across instances; large keyspace; not enumerable by incrementing.
- Bad: collation must be right; retry logic must exist and be tested.

### 2. Sequential Base-N of the identity
- Good: no collisions at all; shortest codes early on.
- Bad: enumerable and leaks volume; couples the public code to the surrogate key. Not researched further.

### 3. Sqids or Hashids over the identity
- Good: no collisions; non-sequential looking.
- Bad: "no encryption of any kind"; IDs decode back to numbers ([sqids FAQ](https://sqids.org/faq)). Adds a dependency that would need NuGet vetting.

### 4. Case-insensitive alphabet (Base36 / Crockford)
- Good: removes the whole collation bug class; tolerates retyping and mixed case.
- Bad: smaller keyspace (Base36 at 7 characters is 36^7, about 7.8e10 per the report; Crockford base32 would be 32^7, about 3.4e10, my arithmetic). Recorded in `docs/plan.md` as the rejected alternative. Still the better choice if codes are ever typed by hand; Base62 trades that away for keyspace.

## More information
- Research: [report](../research/reports/URL%20shortener%20assignment%20validation.md), "Domain design guidance" (Short codes, Collation) and the "Risks specific to AI-generated code" checklist.
- The report cites Kutt's check-then-insert approach as TOCTOU-prone ([kutt utils](https://github.com/thedevs-network/kutt/blob/main/server/utils/utils.js)).
- Implementation: [AI log 002](../ai-log/002-persistence.md), [AI log 003](../ai-log/003-create-link.md).
