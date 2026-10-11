# ADR-0007: Expired and deleted links answer 410; "safe" means a small set of create-time rules and a reputation seam

- Status: accepted (engineer sign-off 2026-10-11; decisions in PR #37, implementation in PR #39)
- Date: 2026-10-11
- Deciders: Mohammed Saalim
- AI assistance: Claude Code drafted the questions, options and threat model; the four behavioural answers are the engineer's (2026-10-11, [ambiguous scenario](../scenarios/ambiguous.md))

## Context and problem statement
The requirement is only that links "should expire and be safe". Neither word is defined. The code already accepts an optional `expiresAt` (validated as "in the future") but never checks it: `RedirectEndpoint` says "Day 3 decides 404 vs 410". Deleted links (soft delete, `DeletedAt`) answer 404 today. Destination validation (`CreateLinkValidator`) covers length, printable ASCII, http/https only, a host, and no user info. What does "expired" mean to a caller, and what is "safe" protecting against?

## Decision drivers
- Smallest change that answers the requirement and can be reversed without migrating data.
- No new packages, no network calls on the create path, no schema change.
- Every rule testable without a network (TDD scope in CLAUDE.md §7).
- An honest threat model: the service never fetches a destination, so server-side request forgery is not a risk here. The risk is what a short link hides from the person who clicks it.

## Decision: expiry

| Question | Decision | Reversible by |
|---|---|---|
| When is a link expired? | `ExpiresAt <= now`, with `now` from the injected `TimeProvider`. This is the same boundary the validator uses for "must be in the future". | Changing one comparison. |
| What does the redirect answer? | **410 Gone**, ProblemDetails body, `Cache-Control: no-store`. RFC 9110 §15.5.11 defines 410 as "no longer available ... likely to be permanent". 410 is cacheable by default, so `no-store` keeps a browser or proxy from remembering it, should a link ever be restored. | Status code in one place. |
| And a deleted link? | **410 Gone as well** (engineer's answer). Both mean "this existed and is permanently gone". The cost is that a deleted code is distinguishable from one that never existed. That is accepted, because codes are public anyway. Two existing tests that assert 404 change, with the reason in the AI log. | Same place. |
| `GET /api/links/{code}` and `/stats`? | **Expired:** still 200, with the past `expiresAt` visible, because the owner may want its history. **Deleted:** 404, unchanged; the management API treats a deleted resource as gone. | Endpoint filters. |
| Default TTL when `expiresAt` is omitted? | **None** (engineer's answer). Links live until deleted. | A default can be added later without changing existing links. |
| Are expired rows removed? | **No.** Rows are kept: no purge job, no hard delete. Codes are never reused, so an old link can never start pointing somewhere new. | A purge job later; codes must still not be reused. |
| Does a redirect to an expired link record a click? | **No** (engineer's answer). Nothing reached the destination, so stats count real redirects only. | Enqueue before the check. |
| Can expiry be changed after creation? | **No.** Links are immutable; there is no `PATCH`. Extending a link means creating a new one. | A `PATCH` endpoint later. |

## Decision: safety

Threat model. A short link hides its destination, so the risks are to the **person who clicks**:
1. The destination is malicious (phishing, malware).
2. The destination is a host on the clicker's own network: a router admin page, `localhost` services, the link-local metadata address on a cloud VM. The browser can reach these even though the internet cannot.
3. The destination is the shortener itself, which makes redirect loops and chains that hide the final destination.
4. A custom alias takes a path the service serves.

| Rule (create time, `POST /api/links` → 400) | Covers | Reversible by |
|---|---|---|
| Reject IP-literal hosts that are loopback (127.0.0.0/8, ::1), private (10/8, 172.16/12, 192.168/16, fc00::/7), link-local (169.254/16 including the metadata address, fe80::/10) or unspecified (0.0.0.0/8, ::). IPv4-mapped IPv6 is checked as IPv4. | 2 | Rule list in the validator. |
| Reject the host names `localhost` and `*.localhost`. | 2 | Same. |
| Reject destinations whose host is this service's public host (`PublicUrl:BaseUrl`). | 3 | Same. |
| Extend the reserved aliases to the paths the service serves or plausibly will. | 4 | List in the validator. |
| `IUrlReputationChecker` seam, called on create. Default implementation: a configurable host denylist (`Safety:DeniedHosts`, exact host or subdomain match), empty by default. | 1 | Swap the implementation; no caller changes. |

Engineer's answer: **minimal rules plus the seam**. Not built, with reasons:
- **DNS resolution at create time.** A host that resolves to a private address today can change tomorrow (time of check vs time of use), and it puts a network call and a failure mode on every create, and into the tests. Literal IPs and `localhost` are the cases a URL alone can show.
- **An external reputation API** (for example Google Safe Browsing). It needs an API key, an HTTP client and a policy for when the service is down. The seam is where it plugs in.
- **Homograph detection.** The validator already accepts only printable ASCII, so an international host must arrive as punycode (`xn--...`) and is visible as such.
- **Checks at redirect time.** Rules apply at create time only. A denylist entry added later does not block existing links; re-checking on redirect is the obvious extension.
- **Other shorteners as destinations** (chains). Possible later with the denylist.
- **Address ranges outside the list above:** carrier-grade NAT 100.64/10, broadcast, multicast, and IPv6 forms that embed an IPv4 address (IPv4-compatible `::127.0.0.1`, NAT64 `64:ff9b::/96`, 6to4 `2002::/16`). These are accepted today. Each would be one more line in `IsNonPublic`; none appeared in the threat model's examples (code review of #38).

## Considered options
- **Expired: 404 vs 410.** 404 hides that the code ever existed but tells the clicker nothing true. 410 is the defined status for this. Chosen: 410.
- **Deleted: 404 vs 410.** Discussed above; the engineer chose consistency (410).
- **Safety: minimal rules + seam / plus DNS / plus external API.** The engineer chose minimal + seam. The other two each add a network dependency to the create path.

## Consequences
- Good: every rule is a pure function or one comparison, testable without a network. No schema change. Every decision is reversible in one place.
- Good: a caller can tell "never existed" (404) from "existed and is gone" (410).
- Bad: two existing tests and the SETUP walkthrough change (deleted → 410). The AI log for A2 says why.
- Bad: a host name that resolves to a private address is accepted, and a host added to the denylist after a link was created keeps redirecting. Both are stated as limitations.
- Neutral: `CreateLinkValidator` needs the public host for the self-link rule, so it gets a parameter. It stays a pure function, with no options injected into the static class.

## Follow-ups
- A2 (issue to follow): implementation, TDD for each rule, `/security-review` and a fresh `/code-review`, engineer sign-off.
