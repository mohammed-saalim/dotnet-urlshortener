# URL Shortener Domain Design, Security, and Reference Implementations (validation notes)

All observations made 2026-10-10 unless stated. Labels: VERIFIED (primary source fetched this session), PARTIALLY VERIFIED (secondary source, partial fetch, or from well-known spec not re-fetched this session), REFUTED, UNVERIFIED.

## Q1. Reference repos: license, stars, stack, storage, design choices

### Takeaway
Every reference repo except Dub and Sink is MIT and safe to learn from. Dub is AGPLv3 plus a proprietary Enterprise Edition (EE) license on its `(ee)` directories, and Sink is AGPL-3.0-only. Reading either for design ideas is fine. Copying code from them would bring copyleft (AGPL) or EE obligations. One correction to the brief: `FBoucher/AzUrlShortener` now resolves to `microsoft/AzUrlShortener`, which was archived on 2026-09-09. Active development moved back to `fboucher/AzUrlShortener`.

### Cited Findings
| Repo | SPDX license | Stars (2026-10-10) | Stack | Storage | Label |
|---|---|---|---|---|---|
| dubinc/dub | GitHub reports `NOASSERTION`. LICENSE.md says "AGPLv3" without stating -only or -or-later, so the exact SPDX id (`AGPL-3.0-only` vs `AGPL-3.0-or-later`) cannot be determined from it. That applies everywhere except `apps/web/app/(ee)` and `apps/web/app/app.dub.co/(dashboard)/[slug]/(ee)`, which use `ee/LICENSE.md` (commercial EE) | 24,869 | TypeScript, Next.js, Prisma, Tailwind, Vercel | MySQL/PlanetScale (repo topics), Tinybird/ClickHouse for analytics, Upstash (Redis) | VERIFIED: [GitHub API repo metadata](https://github.com/dubinc/dub), [LICENSE.md](https://github.com/dubinc/dub/blob/main/LICENSE.md) |
| YOURLS/YOURLS | MIT | 12,259 | PHP | MySQL | VERIFIED: [GitHub](https://github.com/YOURLS/YOURLS) |
| thedevs-network/kutt | MIT | 11,149 | JavaScript (Node) | SQL DB via query layer (Postgres/SQLite/MySQL per project docs; not re-verified) | VERIFIED for license/stars: [GitHub](https://github.com/thedevs-network/kutt) |
| ccbikai/Sink | AGPL-3.0-only | ~7.2k (GitHub UI rounding) | Nuxt 4, Drizzle ORM, Cloudflare Workers | "Cloudflare D1 is the authoritative link store; Workers KV is a write-through read cache"; Workers Analytics Engine; optional R2 | VERIFIED: [GitHub](https://github.com/ccbikai/Sink) (repo-search API returned no hit; the stars came from the page fetch) |
| shlinkio/shlink | MIT | 5,333 | PHP (Mezzio/Laminas), REST API | Relational DB (Doctrine; MySQL/Postgres/MariaDB/SQLite/MSSQL per docs; not re-verified) | VERIFIED: [GitHub](https://github.com/shlinkio/shlink) |
| microsoft/AzUrlShortener (formerly FBoucher/AzUrlShortener) | MIT | 588 | C#, Blazor (FluentUI), Azure Container Apps | Azure Table Storage | VERIFIED: [GitHub](https://github.com/microsoft/AzUrlShortener). ARCHIVED 2026-09-09: "Development continues at fboucher/AzUrlShortener" |
| fboucher/AzUrlShortener (active successor) | MIT (LICENSE file: "MIT License, Copyright (c) 2020 Frank Boucher") | 7 | .NET 10, .NET Aspire, Blazor admin ("TinyBlazorAdmin") | Azure Table Storage | VERIFIED: [LICENSE](https://github.com/fboucher/AzUrlShortener/blob/main/LICENSE); stack PARTIALLY VERIFIED: [GitHub](https://github.com/fboucher/AzUrlShortener) |
| poorna-soysa/url-shortener-app (.NET, >200 stars) | MIT | 265 | .NET 10, .NET Aspire | PostgreSQL | VERIFIED: GitHub search `url shortener language:C# stars:>200`, which returned only 2 repos ([repo](https://github.com/poorna-soysa/url-shortener-app)) |

Notable design choices to learn from (not copy):
- **YOURLS** generates keywords sequentially from an incrementing decimal (`yourls_get_next_decimal()`), converted with `yourls_int2string()` to base 36 or 62 depending on `YOURLS_URL_CONVERT`. Short codes are therefore guessable. Reserved keywords are checked with `yourls_keyword_is_reserved()` (the reserved list, existing pages, and directories). Source: [functions-shorturls.php](https://github.com/YOURLS/YOURLS/blob/master/includes/functions-shorturls.php). VERIFIED.
- **Kutt** generates codes with nanoid `customAlphabet(env.LINK_CUSTOM_ALPHABET, env.LINK_LENGTH)`. Its collision handling is check-then-insert: it runs `query.link.find(...)` and recurses if a row exists. It keeps a `preservedURLs` reserved list (login, logout, admin, api, static, stats, settings, 404, privacy, pricing, css, fonts, images, ...). Redirects use Express `res.redirect(link.target)`, which defaults to 302. It has banned-domain and banned-host validators. Sources: [utils.js](https://github.com/thedevs-network/kutt/blob/main/server/utils/utils.js), [links.handler.js](https://github.com/thedevs-network/kutt/blob/main/server/handlers/links.handler.js). VERIFIED. Lesson: even a popular repo uses a TOCTOU-prone existence check, so the prototype should rely on a DB unique index and catch the violation.
- **Shlink** makes the redirect status configurable: `REDIRECT_STATUS_CODE` accepts 301, 302, 307 or 308 and defaults to 302. `REDIRECT_CACHE_LIFETIME` (default 30 s) applies only to 301/308. `DEFAULT_SHORT_CODES_LENGTH` defaults to 5 (minimum 4). `SHORT_URL_MODE` is `strict` (case-sensitive) or `loose`, default strict. `ANONYMIZE_REMOTE_ADDR` defaults to true. `DISABLE_TRACKING` and `TRACK_ORPHAN_VISITS` control tracking. Source: [Shlink env vars](https://shlink.io/documentation/environment-variables/). VERIFIED.
- **Sink** keeps an authoritative SQL store (D1) behind a write-through KV read cache, and sends analytics to a separate append-only engine. Source: [GitHub](https://github.com/ccbikai/Sink). VERIFIED.
- **Dub** redirects with 302 by default and uses 301 only for root-domain links. Analytics go to a separate OLAP store (Tinybird/ClickHouse). Sources: [Dub blog](https://dub.co/blog/301-vs-302-redirect), [repo topics](https://github.com/dubinc/dub). VERIFIED.

### Inferences
- License flags: Dub (AGPL plus a proprietary EE directory) and Sink (AGPL-3.0-only) both carry copyleft terms. Learning designs from them is fine, but no code should be copied. The MIT repos impose no restriction beyond attribution if code is copied. None of the repos has a non-commercial clause, apart from Dub's EE directories (proprietary, not open source).
- The .NET ecosystem has few well-starred URL shorteners. Only 2 C# repos have more than 200 stars, and the best known is now archived.

### Gaps
- The exact star count for Sink is a rounded UI value ("7.2k"). GitHub repo-search via the API returned no result or errored for ccbikai/Sink (possibly an API indexing or permission quirk), so no exact count is available.
- Storage backends for kutt and shlink are from general knowledge and were not re-fetched.

## Q2. Short code generation and collision math

### Takeaway
VERIFIED. A random 7-character Base62 code from a CSPRNG, with a unique index and retry-on-conflict, is a sound design. Some collisions somewhere in the table are expected past about 2.2M links, but each insert has negligible failure probability, and a few retries make failure practically impossible. Sequential and Sqids-style codes can be decoded or enumerated. The biggest implementation trap is a case-insensitive DB collation: the default on SQL Server en-US is `SQL_Latin1_General_CP1_CI_AS`.

### Cited Findings
- 62^7 = 3,521,614,606,208 (about 3.52e12). Computed this session in Python; reproducible with N = 62^7 using the birthday bound P(any collision among n) = 1 - exp(-n^2/2N), the next-insert collision chance n/N, k consecutive retry failures (n/N)^k, and the 50% point sqrt(2N ln 2):
  - Chance that some collision occurs anywhere among n codes (birthday bound): 1.4e-5 at n=10k; 0.14% at 100k; 13.2% at 1M; about 100% at 10M. The 50% point is about 2.21M codes.
  - Chance that the next insert collides: n/N, which is 2.8e-7 at 1M and 2.8e-5 at 100M. Five consecutive collisions at 100M has probability about 1.9e-23.
  - So "collisions will happen" is TRUE past about 1M links, but retry-on-conflict makes them harmless. For comparison: 62^6 is about 5.68e10, and 36^7 (a case-insensitive alphabet) is about 7.84e10.
- `RandomNumberGenerator.GetString(ReadOnlySpan<char> choices, int length)` "Creates a string populated with characters chosen at random from choices". It is a static CSPRNG method. Source: [MS Learn](https://learn.microsoft.com/dotnet/api/system.security.cryptography.randomnumbergenerator.getstring?view=net-10.0). VERIFIED.
- Analyzer rule CA5394, "Do not use insecure randomness", fires on any `System.Random` call. It is not enabled by default in .NET 10. Source: [CA5394](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca5394). VERIFIED. Microsoft SDL lists System.Random as "Not Recommended" ([SDL crypto recs](https://learn.microsoft.com/security/engineering/cryptographic-recommendations#random-number-generators)). `RNGCryptoServiceProvider` is obsolete (SYSLIB0023) ([MS Learn](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rngcryptoserviceprovider?view=net-10.0)). VERIFIED.
- Sqids: "There is no encryption of any kind". Generated IDs "could be decoded back into numbers", so they are not suited to sensitive data and can reveal user counts. Sqids has a built-in blocklist (case-insensitive substring match for words of 4 or more characters). Hashids is its predecessor and was replaced because it lacked a blocklist and its salt was confusing. Source: [sqids.org/faq](https://sqids.org/faq). VERIFIED.
- Collation: for the en-US OS locale, the default SQL Server setup collation is `SQL_Latin1_General_CP1_CI_AS` (CI = case-insensitive). BIN2 collations do pure code-point comparison. Collation can be set per column. Source: [MS Learn Collation and Unicode support](https://learn.microsoft.com/sql/relational-databases/collations/collation-and-unicode-support?view=sql-server-ver17). VERIFIED. SQLite defaults to BINARY (memcmp, case-sensitive). NOCASE folds only ASCII. Source: [sqlite.org datatype3](https://www.sqlite.org/datatype3.html). VERIFIED.
- Reserved words: YOURLS (`yourls_keyword_is_reserved`) and Kutt (`preservedURLs`: api, admin, static, login, stats, ...) both block aliases that collide with app routes. Sources: [YOURLS](https://github.com/YOURLS/YOURLS/blob/master/includes/functions-shorturls.php), [Kutt utils](https://github.com/thedevs-network/kutt/blob/main/server/utils/utils.js). VERIFIED.

### Inferences
- With SQL Server defaults, codes `aBc1234` and `abc1234` would collide on a unique index (an insert fails, or a lookup returns the wrong row). Declare the code column with a `_CS_AS` or `_BIN2` collation, or use a DB whose default is binary (SQLite, PostgreSQL). Without that, 62^7 is effectively 36^7.
- Sequential or base62(auto-increment) codes (as in YOURLS) let anyone enumerate every link in the system. That matters if links point to private documents. Random codes avoid enumeration but are not secrets, so they should not stand in for authorization.
- Reserved words should be compared case-insensitively against the route table (`api`, `health`, `admin`, `swagger`, `openapi`, `metrics`, `favicon.ico`, `robots.txt`, ...), even if codes themselves are case-sensitive.

### Gaps
- The PostgreSQL default (deterministic, case-sensitive collation) was not fetched this session. It is well known but carries no source here.

## Q3. Redirect semantics, caching, what major shorteners return

### Takeaway
VERIFIED. RFC 9110 makes 301 and 308 heuristically cacheable, while 302 and 307 are not and can only be cached with explicit freshness. A prototype that wants every click observable should send 302 (or 307) with `Cache-Control: private, max-age=0` or `no-store`. Bit.ly and TinyURL actually send 301 but suppress or cap caching through headers. Dub sends 302.

### Cited Findings
- RFC 9110 §15.4.2 (301): "the target resource has been assigned a new permanent URI". The UA "MAY change the request method from POST to GET". "A 301 response is heuristically cacheable". §15.4.3 (302): "resides temporarily under a different URI"; the UA MAY change POST to GET; it is not heuristically cacheable. §15.4.8 (307): temporary; "the user agent MUST NOT change the request method". §15.4.9 (308): permanent, method-preserving, "heuristically cacheable". Source: [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.4). VERIFIED.
- RFC 9111 §3: a cache must not store a response unless it has public, private, Expires, max-age, s-maxage, a cache extension allowing storage, or a heuristically cacheable status code. So 302/307 are stored only with explicit freshness. §4.2.2 gives the heuristic of about 10% of the time since Last-Modified. Source: [RFC 9111](https://www.rfc-editor.org/rfc/rfc9111.html). VERIFIED.
- Observed with curl on 2026-10-10 (a primary observation; this is not vendor documentation):
  - `bit.ly/github`: **301**, `Cache-Control: private, max-age=90`.
  - `tinyurl.com/2unsh` and `tinyurl.com/2fcpre6`: **301**, `Cache-Control: no-store, no-cache, must-revalidate`, `X-Robots-Tag: noindex`.
  - `git.new/dub` (a Dub-hosted custom domain): **302**, `Cache-Control: public, max-age=0, must-revalidate`.
  - `d.to/` (Dub root domain): **301**, `max-age=0`.
  - `dub.sh/*` returned 403/429 to curl because of bot protection.
  - VERIFIED (observed).
- Dub: "By default, Dub uses the 302 status code for link redirects", which lets destinations be updated "without the risk of permanent browser caching". Source: [Dub blog](https://dub.co/blog/301-vs-302-redirect). VERIFIED.
- Shlink defaults to 302 and caches 301/308 for only 30 s. Source: [Shlink env vars](https://shlink.io/documentation/environment-variables/). VERIFIED. Kutt uses Express's default 302 ([links.handler.js](https://github.com/thedevs-network/kutt/blob/main/server/handlers/links.handler.js)). VERIFIED.
- Claim "bit.ly uses 301": VERIFIED (observed). Its old blog-era 302 history appears only in secondary sources ([searchatlas](https://searchatlas.com/blog/link-tracking-with-url-shorteners-pros-and-cons/)), so that history is PARTIALLY VERIFIED.

### Inferences
- "302 keeps clicks observable" is correct, with a nuance. What actually matters is the cache headers: a 301 with `no-store` (TinyURL) is also observable in practice, and a 302 with `max-age=3600` would be cached. Recommendation: 302 plus `Cache-Control: private, no-store` (or `max-age=0`), and optionally `X-Robots-Tag: noindex`. Use 307 only if non-GET methods must be preserved, which a shortener does not need.
- Avoid 301/308 without a short max-age. Browsers would cache them indefinitely, so later destination edits or deletions would not take effect and clicks would go uncounted.

### Gaps
- No official Bitly or TinyURL documentation stating their status codes was retrieved. The Bitly support article returned 403. The values above are observed behavior.

## Q4. Expired or deleted links: 404 vs 410

### Takeaway
VERIFIED for the semantics. 410 means the target is gone and "likely to be permanent". 404 makes no claim either way. Both are heuristically cacheable, so either should carry `Cache-Control: no-store` if a link can be re-enabled. Shorteners in practice return 404 for unknown codes.

### Cited Findings
- RFC 9110 §15.5.5: 404 "does not indicate whether this lack of representation is temporary or permanent" and is heuristically cacheable. §15.5.11: 410 means the condition "is likely to be permanent" and is heuristically cacheable. Source: [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.11). VERIFIED.
- Observed: unknown codes on bit.ly and tinyurl return **404**. TinyURL's 404 carries `Cache-Control: max-age=0, must-revalidate, no-cache, no-store, private`. VERIFIED (observed 2026-10-10).

### Inferences
- A defensible design: 404 for unknown codes, and 410 for known codes that were expired or deleted, sent with no-store because an expiry may be extended. Returning 404 for both avoids revealing that a code once existed. That is a small privacy and enumeration advantage, and the trade-off should be documented.

### Gaps
- How bit.ly, TinyURL or Dub respond for expired links (as opposed to unknown codes) was not observed. Dub has an expired-link redirect setting according to general knowledge, but this is unverified.

## Q5. SSRF, open redirect and destination validation

### Takeaway
PARTIALLY VERIFIED. If the server never fetches destinations, classic SSRF largely disappears. What remains is (a) a stored malicious or abusive destination, which is an open-redirect and phishing-relay risk, and (b) future features that fetch the URL (link previews, OG metadata, reputation crawlers), which bring SSRF back. Validate on create anyway: http/https only, an absolute URL, no userinfo, a length cap, and rejection of self-host loops. Private-IP rejection is cheap defense in depth, but it is meaningful only for literal IPs unless DNS is resolved, and resolve-then-validate is still vulnerable to DNS rebinding if a fetch happens later.

### Cited Findings
- OWASP SSRF Cheat Sheet: prefer allowlists; a denylist is a "Last Resort". Allow only "HTTP or HTTPS". Block private ranges 10/8, 172.16/12 and 192.168/16, plus localhost and link-local. Block `169.254.169.254` and metadata hostnames. For DNS rebinding: "Bind the connection to a validated address", because a second lookup between validation and connection enables rebinding. "Disable the support for the following of the redirection". "Treat parser disagreement as a rejection" (example: `http://example.com\@evil.com`). For .NET, `IPAddress.TryParse` is noted as accepting hex/octal/dword forms. The sheet does not cover the case where the app does not fetch. Source: [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html). VERIFIED.
- OWASP Unvalidated Redirects: the safest pattern is to "have the user provide short name, ID or token which is mapped server-side to a full target URL", which is exactly a shortener. Optionally, "Force all redirects to first go through a page notifying users that they are going off of your site". Weak pattern matching can be bypassed with userinfo and misleading hostnames. Use a maintained parser and compare the parsed scheme, host and port. Source: [OWASP Unvalidated Redirects Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Unvalidated_Redirects_and_Forwards_Cheat_Sheet.html). VERIFIED.
- Reserved ranges named in the brief: 100.64.0.0/10 is CGNAT (RFC 6598), fc00::/7 is ULA (RFC 4193), 127/8 and ::1 are loopback, and 169.254/16 is link-local (RFC 3927). PARTIALLY VERIFIED: these are standard IANA special-purpose registry entries but were not re-fetched this session ([IANA IPv4 special registry](https://www.iana.org/assignments/iana-ipv4-special-registry/)).
- Max URL length: RFC 9110 §4.1 RECOMMENDS supporting URIs of at least 8000 octets. PARTIALLY VERIFIED: the passage was not re-quoted this session ([RFC 9110 §4.1](https://www.rfc-editor.org/rfc/rfc9110.html#section-4.1)). A 2048–8000 character cap is a reasonable implementation choice.

### Inferences
- Without server-side fetching, the remaining risks are: the shortener serving as an open-redirect or phishing relay (abuse and domain reputation); `javascript:`, `data:` and `file:` schemes (blocked by the scheme allowlist; browsers mostly refuse such Location targets anyway); redirect loops to its own host or to another shortener; and log or HTML injection if the URL is rendered in an admin UI or interstitial.
- Validating private IPs by hostname is insufficient once the system fetches, because of DNS rebinding. For a no-fetch prototype, rejecting literal private IPs and `localhost` is mostly hygiene. It protects users from links into their own intranet and is not SSRF protection for the server.
- IDN/punycode homographs: a reasonable approach is to store the IDNA (punycode) form via `System.Globalization.IdnMapping` or `Uri.IdnHost` and display it in admin views. Detecting confusables fully is out of scope for a prototype. No source was fetched on this point (UNVERIFIED as a sourced claim).

### Gaps
- No OWASP page explicitly addresses the "does not fetch" case. The reduced-risk conclusion is an inference.
- The UTS #39 / homograph guidance source was not fetched.

## Q6. URL reputation checks

### Takeaway
VERIFIED for v4. The Safe Browsing v4 docs say the API is non-commercial only, and they mark v4 deprecated in favor of v5. The quote comes from the v4 page; that the same restriction covers v5 is expected but not confirmed. Commercial use must go through Google Cloud Web Risk, whose Lookup API is free up to 100k calls per month. No public SmartScreen URL-lookup API was found. URLhaus covers only malware distribution and requires an Auth-Key. Putting the check behind an `IUrlReputationChecker` interface with a no-op default is defensible for a prototype if it is documented as a known gap.

### Cited Findings
- "The Safe Browsing API is for non-commercial use only. If you need to use APIs to detect malicious URLs for commercial purposes – meaning 'for sale or revenue-generating purposes' – refer to the Web Risk API." The page also says "The Safe Browsing APIs (v4) are deprecated", and v5 docs exist. Source: [Google Safe Browsing v4](https://developers.google.com/safe-browsing/v4). VERIFIED.
- Web Risk Lookup API `uris.search`: free for 1–100,000 calls per month; $0.50 per 1,000 calls from 100,001 to 10M; contact sales above 10M. The Update API `threatLists.computeDiff` is free. Source: [Web Risk pricing](https://cloud.google.com/web-risk/pricing). PARTIALLY VERIFIED: figures come from the search-result extract, because the direct fetch was truncated.
- Microsoft Defender SmartScreen: no public developer URL-reputation query API was found. Microsoft offers demo pages and a submission portal only. Sources: [MS Learn SmartScreen URL reputation demos](https://learn.microsoft.com/en-us/defender-endpoint/defender-endpoint-demonstration-smartscreen-url-reputation), plus search results. PARTIALLY VERIFIED (absence of evidence).
- URLhaus requires an `Auth-Key`. It is free for community fair use, but "Use of the API by companies... with commercial or for-profit needs may require a paid subscription". It covers only URLs "directly being used to distribute malware", not phishing. Source: [URLhaus API](https://urlhaus.abuse.ch/api/). VERIFIED.
- PhishTank: downloadable database updated hourly; registration (an application key) is recommended to avoid rate limits; terms link to Cisco's EULA. Pricing and the current registration status were not explicit. Source: [PhishTank developer info](https://www.phishtank.com/developer_info.php). PARTIALLY VERIFIED.
- Kutt uses only banned-domain and banned-host lists, with no Safe Browsing integration in the link handler. Source: [kutt links.handler.js](https://github.com/thedevs-network/kutt/blob/main/server/handlers/links.handler.js). VERIFIED.

### Inferences
- Using Safe Browsing for a production or commercial shortener would breach its terms. Web Risk is the compliant Google option and its free tier fits a prototype.
- A no-op default behind an interface is a standard seam. It is defensible as long as the README says abuse screening is off by default. A simple local domain denylist is a cheap middle ground (Kutt does this).

### Gaps
- PhishTank's current pricing and registration status were not confirmed.
- The non-commercial restriction was read on the v4 page. The v5 docs and the Safe Browsing ToS were not fetched, so whether v5 carries the same restriction is unconfirmed.

## Q7. Analytics design (async queue, crash loss, privacy, shape)

### Takeaway
PARTIALLY VERIFIED. A bounded `Channel<T>` with a `BackgroundService` consumer is the idiomatic .NET pattern. On a crash or hard kill, every event still in memory is lost, because `StopAsync` may not run. Since .NET 6, an unhandled exception in `ExecuteAsync` stops the host. IPs are personal data under EU law (Breyer), so truncate, hash or anonymize them; Shlink does this by default.

### Cited Findings
- "If the app shuts down unexpectedly (for example, the app's process fails), `StopAsync` might not be called." The default shutdown timeout is 30 s. Source: [MS Learn hosted services](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0). VERIFIED.
- Since .NET 6, an exception thrown from `ExecuteAsync` is logged and "By default, the host is stopped" (`BackgroundServiceExceptionBehavior.StopHost`). Before .NET 6 such failures were silently lost ("zombie"). Source: [MS Learn breaking change](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling). VERIFIED. In .NET 11, `RunAsync` and `StopAsync` also throw, giving a non-zero exit code ([MS Learn .NET 11 change](https://learn.microsoft.com/dotnet/core/compatibility/extensions/11/ihost-runasync-stopasync-throw-backgroundservice-failure)). VERIFIED.
- Shlink: `ANONYMIZE_REMOTE_ADDR` defaults to true ("obfuscated before storing"), and `DISABLE_TRACKING` is available. Source: [Shlink env vars](https://shlink.io/documentation/environment-variables/). VERIFIED.
- CJEU Breyer (C-582/14, 19 Oct 2016): a dynamic IP address is personal data for a website publisher that has legal means to identify the visitor with the ISP's help. Sources: [Bird & Bird](https://twobirds.com/en/insights/2016/global/cjeu-decision-on-dynamic-ip-addresses-touches-fundamental-dp-law-questions), [arXiv analysis](https://arxiv.org/pdf/2511.01751). PARTIALLY VERIFIED (secondary legal commentary).
- Sink sends analytics to an analytics engine separate from the link store, and Dub does the same with Tinybird/ClickHouse. Sources: [Sink](https://github.com/ccbikai/Sink), [dub topics](https://github.com/dubinc/dub). VERIFIED.

### Inferences
- In-memory queue loss covers events still queued plus the batch being written. On graceful shutdown, drain the channel before the 30 s timeout. On kill or OOM, the loss is unavoidable unless writes go synchronously or through an outbox, which is acceptable for analytics if documented.
- Bound the channel and choose `BoundedChannelFullMode.DropWrite` or `DropOldest` with a dropped-events counter, rather than `Wait`, so the redirect hot path never blocks. Inside the consumer loop, catch, log and continue per item, so one bad event does not stop the whole host. Per [MS Learn Channels](https://learn.microsoft.com/dotnet/core/extensions/channels#bounding-strategies) (VERIFIED), `BoundedChannelFullMode.Wait` is the default: `WriteAsync` waits for space and `TryWrite` returns false. `DropNewest`, `DropOldest` and `DropWrite` drop items instead, and `Channel.CreateBounded(options, itemDropped)` accepts a callback for counting drops. So an AI-written `CreateBounded(n)` that calls `await WriteAsync` from the redirect path will block redirects under load.
- Minimal analytics shape: total clicks, clicks per day (UTC), referrer host, and optionally a coarse user-agent family. Store a truncated IP (/24 IPv4, /48 IPv6) or a salted hash with a rotating salt, or nothing. Country requires a GeoIP database, which is optional.

### Gaps
- No primary source for "typical analytics shape" was retrieved, and no EDPB guidance on IP hashing was retrieved.

## Q8. Idempotency-Key on create

### Takeaway
VERIFIED with a correction. `draft-ietf-httpapi-idempotency-key-header` is not an RFC. Its latest revision, -07 dated 2025-10-15, is now marked **Expired**. The semantics in the brief match the draft, with additions: 400 when a required key is missing and 409 when the original request is still in flight. For a 2–3 day prototype it is optional. A cheaper alternative is to make create naturally idempotent, or to implement only the stored-response, 422-on-mismatch path.

### Cited Findings
- Status: draft-ietf-httpapi-idempotency-key-header-07, latest revision 2025-10-15, state "Expired". Source: [IETF Datatracker](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/). VERIFIED.
- Semantics: a missing required key "SHOULD reply with an HTTP 400". Reuse "with a different request payload... SHOULD reply with a HTTP 422". A retry "while the original request is still being processed... SHOULD reply with an HTTP 409". The resource "SHOULD define such expiration policy and publish it". An idempotency fingerprint may be computed from a payload checksum. Source: [draft -07](https://www.ietf.org/archive/id/draft-ietf-httpapi-idempotency-key-header-07.html). VERIFIED.

### Inferences
- Claim "same key + different body → 422": VERIFIED. Claim "the draft is an active standard": REFUTED (it is expired). Calling it "IETF httpapi draft, de facto Stripe-style" is accurate.
- Whether it is over-engineering depends on scope. A minimal version (a table of key, request hash, response and expiry, with a unique index on key) costs about half a day, including the concurrency case. Shipping it without 409 in-flight handling is acceptable if documented.

### Gaps
- Whether the draft was revived or replaced after 2025-10-15 was not checked beyond the datatracker state.

## Q9. Common failure modes of AI-generated code (URL shorteners and small .NET APIs)

### Takeaway
PARTIALLY VERIFIED. Published studies confirm that AI code is frequently insecure and that users become overconfident about it. One study confirms that hallucinated packages are common. Most of the specific URL-shortener failure modes in the brief are not studied directly. They are well-grounded inferences, supported by vendor documentation and by examples found in popular repos.

### Cited Findings
- Veracode 2025 GenAI Code Security Report: 80 tasks, more than 100 LLMs, with vulnerabilities in 45% of cases. C#, Python and JavaScript failed at rates of 38–45% and Java at more than 70%. Models failed to defend against XSS (CWE-80) in 86% of cases and log injection (CWE-117) in 88%. Security performance did not improve with newer models. Source: [Veracode press release](https://www.veracode.com/press-release/ai-generated-code-poses-major-security-risks-in-nearly-half-of-all-development-tasks-veracode-research-reveals/). VERIFIED (vendor study).
- Perry et al. (Stanford), "Do Users Write More Insecure Code with AI Assistants?" (47 participants): participants with AI help wrote significantly less secure code and were more likely to believe it was secure. Those who trusted the AI less and refined their prompts produced fewer vulnerabilities. Source: [arXiv 2211.03622](https://arxiv.org/abs/2211.03622). VERIFIED.
- Spracklen et al., "We Have a Package for You!" (USENIX Security 2025): 576k samples from 16 LLMs. Hallucinated packages averaged at least 5.2% for commercial models and 21.7% for open-source models, with 205,474 unique hallucinated names. The study covered Python and JS, not NuGet. Source: [arXiv 2406.10279](https://arxiv.org/abs/2406.10279). VERIFIED. A 2026 follow-up, "The Range Shrinks, the Threat Remains", re-evaluated frontier models ([listing](https://fugumt.com/fugumt/paper_check/2605.17062v1)). UNVERIFIED (only seen in search results).
- Non-cryptographic RNG: CA5394 flags `System.Random` but is off by default in .NET 10, so AI-written `new Random()` code compiles cleanly. Source: [CA5394](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca5394). VERIFIED. Fix: `RandomNumberGenerator.GetString` ([MS Learn](https://learn.microsoft.com/dotnet/api/system.security.cryptography.randomnumbergenerator.getstring?view=net-10.0)).
- The check-then-insert TOCTOU pattern appears even in Kutt (`find`, then recurse). Source: [kutt utils.js](https://github.com/thedevs-network/kutt/blob/main/server/utils/utils.js). VERIFIED as present in the code. Training data includes this pattern, so models are likely to reproduce it (inference).
- Case-insensitive default collation on SQL Server en-US. Source: [MS Learn](https://learn.microsoft.com/sql/relational-databases/collations/collation-and-unicode-support?view=sql-server-ver17). VERIFIED.
- Swallowed or fatal background exceptions: since .NET 6, `StopHost` is the default, so an uncaught exception in the click consumer stops the whole web app. Code that wraps everything in `catch {}` instead loses events silently. Source: [MS Learn](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling). VERIFIED.

### Inferences
Checklist for review, inferred from the above:
1. `System.Random` used for codes instead of `RandomNumberGenerator`. Enable CA5394 as an error.
2. Uniqueness enforced by "exists?, then insert" instead of a unique index plus catching `DbUpdateException` / unique-violation error 2601/2627 and retrying.
3. Code column with a CI collation.
4. Missing scheme allowlist (`javascript:` accepted), or `Uri.TryCreate` with `UriKind.RelativeOrAbsolute` accepting relative paths.
5. Unbounded `Channel.CreateUnbounded` or `ConcurrentQueue` with no backpressure.
6. `CancellationToken` not passed to EF Core or channel reads, and no drain on shutdown.
7. `catch (Exception) {}` in `BackgroundService`, or the opposite: no per-item catch, so the host stops.
8. Hallucinated or wrong NuGet package names or versions. Verify every package on nuget.org.
9. Connection strings and API keys hard-coded in `appsettings.json` and committed.
10. Tests that verify mocks (`Verify(x => x.Save(...))`) rather than HTTP behavior. Prefer `WebApplicationFactory` integration tests that assert the status code, Location and Cache-Control headers.
11. 301 used by default.
12. Logging raw user-supplied URLs (log injection, CWE-117, the 88% failure figure above).

### Gaps
- No published study specific to URL shorteners or .NET minimal APIs was found.
- GitClear's code-churn/duplication report and Snyk's AI code survey were not retrieved this session.
- No NuGet-specific package hallucination data was found.
