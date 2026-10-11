# AI log 015 — sql-autopause-default

| Field | Value |
|---|---|
| Issue | #31 |
| Branch | `infra/31-sql-autopause-default` |
| Date | 2026-10-11 |
| Tool / model | Claude Code / `claude-opus-5-5` (coordinator, no subagent: one-line fix with a known cause) |
| Risk level | high-impact (infra) |

## Intent
Make `azd up` provision the free-offer database, and record the two other first-deploy surprises.

## Constraints given to the AI
Keep the free offer and `freeLimitExhaustionBehavior: AutoPause`. No other infra change.

## Acceptance criteria (runnable)
- `azd up` provisions the database and deploys the app; live curls pasted in the PR (engineer).
- ADR-0006, SETUP and CLAUDE.md §9 match the deployed configuration.

## Prompts (summarised, secrets redacted)
1. Engineer pasted the failed `azd up` output (centralus): `ProvisioningDisabled: Only default value for auto pause delay is allowed for Free Limit database with auto pause exhaustion behavior`.

## Deploy history (first `azd up`)
1. **eastus2:** `RegionDoesNotAllowProvisioning` for the SQL server. The read-only ARM capabilities check (`Microsoft.Sql/locations/{region}/capabilities`) showed eastus2, eastus, northcentralus, southcentralus and westus2 restricted for this subscription, centralus, westus, westus3 and canadacentral available. `azd down --force --purge`, then `azd env set AZURE_LOCATION centralus`.
2. **centralus:** azd preflight warned that `AllowAllWindowsAzureIps` contains the reserved word "WINDOWS" and "the deployment will fail"; the engineer answered Yes. Registry, Log Analytics, App Insights, SQL server and the Container Apps environment (13 min 31 s) were created; the database failed with the error above.
3. Read-only ARM check after the failure: firewall rules `[('AllowAllWindowsAzureIps', '0.0.0.0')]`, databases `[('master', 'Online')]`. The firewall warning was a false positive; only the app database is missing.
4. **centralus, from this branch:** validation failed with `InvalidTemplate: Deployment template validation failed: 'Circular dependency detected on resource ...'`. The container app had been created during attempt 2, so azd now passed `apiExists=true` and the inline `existing` lookup of the app (AI log 013, "not verified" on a real deployment) became active. Fix: the lookup moved to `infra/api-image.bicep`, the module the B6 handoff named for this case.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `infra/resources.bicep` | edited | `autoPauseDelay: 15` removed so ARM applies the default (60 min). Omitting it rather than writing `60`: the error says "default value", and the property absent is the default by definition. |
| `docs/adr/0006-azure-hosting-and-migrations.md` | edited | Delay corrected; cost of the default stated (about 1,800 vCore-seconds per wake, ~55 wakes a month). |
| `docs/SETUP.md` | edited | Cold-start text (60 min); region-restriction recovery steps. |
| `infra/api-image.bicep` | generated | Reads the app's current image, or returns the placeholder; a module so ARM evaluates it as a separate deployment. Same shape as azd's `fetch-container-image.bicep` template module. |
| `infra/resources.bicep` (image) | edited | Inline `existingApi` + `apiImage` var replaced by the module; `image: apiImage.outputs.image`. |
| `CLAUDE.md` §9 | edited | Four gotchas: default-only delay, regional SQL restriction, azd reserved-word false positive, `existing` self-reference circular dependency. |
| (alternative) `BillOverUsage` to keep 15 minutes | rejected | Removes the hard cost stop and cannot be reverted (ADR-0006). |

Docs source for the default: [serverless default settings](https://learn.microsoft.com/azure/azure-sql/database/serverless-tier-overview?view=azuresql#default-settings) ("Auto-pause delay for General Purpose ... Minimum: 15 minutes ... 60 minutes" default). The free-offer restriction is not on that page or the free-offer page; the evidence is the ARM error.

## Checks run
```
$ ~/.azd/bin/bicep lint infra/main.bicep; echo "exit $?"
exit 0   (no diagnostics; bicep 0.47.16)
$ ~/.azd/bin/bicep build infra/main.bicep --stdout   -> valid ARM JSON (after the module change; lint re-run, exit 0)
```
No application code changed, so build and tests are unaffected (CI runs them).

## Gotchas encountered
The three §9 entries above.

## Sign-off
Reviewed the full diff, understood it, accept ownership. — <name>, YYYY-MM-DD
