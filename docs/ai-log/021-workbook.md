# AI log 021 — workbook

| Field | Value |
|---|---|
| Issue | #44 |
| Branch | `infra/44-workbook` |
| Date | 2026-10-11 |
| Tool / model | Claude Code / `claude-opus-5-5` (subagent, briefed by the coordinator) |
| Risk level | high-impact (infra/Bicep) |

## Intent
A small Application Insights workbook, defined in Bicep, that shows the main support scenarios in one place, starting with "find the request behind a `traceId` a caller quotes".

## Constraints given to the AI
Infra only (no app code, no packages, no change to existing resources); four panels only; workbook JSON in its own file; deterministic resource name; every panel's KQL run against the live App Insights before it is embedded; no `azd provision`/`up`/`deploy` (the engineer provisions); stop if rendering costs more than about an hour.

## Acceptance criteria (runnable)
- [x] `bicep build` and `bicep lint` clean (no warnings); provision preview shows only the workbook added. → "Checks run" 1-3. azd's own preview cannot show a workbook at all, so an ARM what-if was used for "only the workbook added".
- [x] Every panel's KQL returns rows against the live App Insights before it is embedded. → "Checks run" 4.
- [ ] After `azd provision`, the workbook exists and each panel shows data; a `traceId` from a live 429 or 410 finds its request. → engineer, after provisioning.
- [ ] A second `azd provision` updates the same workbook (deterministic name), no duplicate. → engineer; the name is `guid(resourceGroup().id, 'urlshortener-workbook')`, the same on every run (the what-if shows the same GUID).
- [x] `docs/SETUP.md` says where the workbook is and what each panel answers.

## Prompts (summarised, secrets redacted)
1. Coordinator brief: issue #44 text; live request names and metric names already observed; gotchas to handle (GUID name, braces are workbook parameter syntax, `loadJsonContent`); pre-flight every query with a helper script that prints only query results; record no ids, tokens or connection strings.

## Design decisions

- **Resource shape** from the Learn template reference for `Microsoft.Insights/workbooks` (latest API `2023-06-01`; `kind: 'shared'` is the only valid value; `version` "like 'Notebook/1.0', which should match the workbook in serializedData") and "Programmatically manage workbooks" (`category: 'workbook'` puts it in the Application Insights Workbooks gallery; `sourceId` is the resource it is listed under). `tags: tags` like every other resource.
- **No resource id in `workbook.json`.** The Learn page warns that exported `serializedData` embeds subscription and resource-group ids, and shows the fix: parse, `union` in a `fallbackResourceIds` array, re-serialize. Bicep does the same in one line: `string(union(loadJsonContent('workbook.json'), { fallbackResourceIds: [appInsights.id] }))`. `loadJsonContent` also fails `bicep build` on malformed JSON.
- **Query items** use `queryType: 0` and `resourceType: "microsoft.insights/components"`, copied from Microsoft's own App Insights workbook (`Workbooks/Performance/Apdex/Apdex.workbook` in `microsoft/Application-Insights-Workbooks`), not guessed.
- **Hidden until a traceId is entered:** `conditionalVisibility: { parameterName: "TraceId", comparison: "isNotEqualTo" }` with no `value`, the form Microsoft's own workbooks use for "parameter is set" (`Workbooks/Azure Resources/Alerts/Alerts.workbook`, `Documentation/LegacyAI/LegacyWorkbookConversion.workbook`). It is not optional decoration: with an empty value the query matches every log line that has no operation (25 startup/shutdown lines, check 4).
- **Braces.** `{...}` is workbook parameter syntax, and the redirect route's own name is ``GET /{code:regex(^[A-Za-z0-9]{{4,10}}$)}``. The redirect filter is `name startswith "GET /" and name contains "code:regex"`: no braces, and it matched exactly the redirect route's 302/404/410 rows, not `GET /api/links/{code}` (check 4, P2 names). Header texts contain no braces either. The only braces in the file are the intended parameters `{TraceId}` and `{TimeRange:start|end|grain}`.
- **traceId input:** both the full ProblemDetails value and the bare 32-hex id work, with surrounding whitespace trimmed first (code review: a pasted space returned no rows, silently): `let raw = trim(@"\s+", "{TraceId}"); iff(raw startswith "00-", tostring(split(raw, "-")[1]), raw)`; then the SETUP.md query unchanged. Both forms returned the same rows (check 4).
- **traceId lookup ignores the time picker** and always searches the last 30 days (`timeContext.durationMs = 2592000000`, the workspace retention). A caller may quote an id from days ago; tying the lookup to a 24-hour default would show "no rows" for a request that exists.
- **Zero-filled series** for panels 2 and 3 (`make-series ... default = 0 ... from {TimeRange:start} to {TimeRange:end} step {TimeRange:grain}`, the form on the Learn "Workbook time parameters" page). 429s and 410s are rare; without zero bins a line chart joins two spikes hours apart. Without `from`/`to`, `make-series` gives each `resultCode` its own range (seen: 410 had 2 bins, 404 had 1).
- **Dropped clicks: `sum(valueSum)`, not `max`.** The exporter uses delta temporality (AI log 014, "Delta temporality", `AzureMonitorExporterExtensions.cs`), so each row is the drops in that export interval; summing gives the total in the range. `max` would report only the worst minute. Live rows: one per minute, all 0.
- **5xx counts all routes**, not just the redirect: a 5xx anywhere is a health signal. The dropped-clicks value is `coalesce(..., 0.0)` because an idle app (scaled to zero) exports no metric rows.
- **Text parameter injection.** A workbook text parameter "is replaced directly in the query, with no escaping or quoting" (Learn, "Use a text parameter"). Someone typing `"` into the traceId box can change the query. Accepted: the query runs with the viewer's own permissions on this App Insights resource, so it can read nothing they could not already query in Logs.

## Artifacts

| Artifact | Outcome | Rationale |
|---|---|---|
| `infra/workbook.json` | generated | Four panels: one-line markdown header each, a parameter step (`TimeRange`, default 24 h; `TraceId`, not required, so no "required parameter" banner on open), six query items. Generated from a small script in the scratchpad so the KQL is JSON-escaped correctly; the JSON file is the reviewed artifact. |
| `infra/resources.bicep` | edited | One `Microsoft.Insights/workbooks@2023-06-01` resource after `appInsights`, with a comment on the GUID name and the id injection. |
| `docs/SETUP.md` | edited | "Support workbook" paragraph after the traceId query: where it is, one line per panel. The existing query text is unchanged. |
| `CLAUDE.md` §9 | edited | Two gotchas that bit in this task: azd's preview hides workbooks; a KQL `let` name shadowed by a column. |
| (first draft) `let id = ...` in the traceId query | rejected | `requests` and `dependencies` have a column `id`; `where operation_Id == id` compared with that column, not the variable, and returned the same 25 startup log lines for every input, including an empty one. Renamed to `wanted`; the real id then returned the 410 request and its SQL call. |
| (option) filter panel 2 to `resultCode in ("302","404","410")` | rejected | Unfiltered shows the same three codes today and would also show any unexpected code on the redirect route instead of hiding it. |
| (option) separate charts for 5xx and dropped clicks over time | rejected | Two more charts for values that are normally 0; one row of totals answers "any?". Duration stays a time chart because its shape matters. |
| (option) `crossComponentResources` / a resource picker | rejected | The workbook opens from its source resource, and `fallbackResourceIds` covers opening it from the gallery; a picker is a parameter the issue did not ask for. |
| (proposal, AI log 016) "custom workbook: rejected" | superseded | The engineer opened #44 to have the support scenarios on one screen. ADR-0006 listed workbooks under "Not built, deliberately"; the coordinator added a dated update line there rather than rewriting the signed-off text. |

## Checks run

**1. Bicep build** (bicep 0.47.16):
```
$ ~/.azd/bin/bicep.exe build infra/main.bicep --stdout > /dev/null; echo "build exit=$?"
build exit=0
```
Compiled resource (excerpt): `"type": "Microsoft.Insights/workbooks", "apiVersion": "2023-06-01", "name": "[guid(resourceGroup().id, 'urlshortener-workbook')]", "kind": "shared"`, `serializedData` = `[string(union(variables('$fxv#0'), createObject('fallbackResourceIds', ...)))]`. No string in the loaded JSON starts with `[`, so ARM does not read any of it as an expression (checked by walking the compiled variable).

**2. Bicep lint:**
```
$ ~/.azd/bin/bicep.exe lint infra/main.bicep; echo "lint exit=$?"
lint exit=0
```
(no diagnostics printed)

**3. Provision preview.** `azd provision --preview --no-prompt` (azd 1.35.1) gives the same list with and without this change (the change was stashed for the baseline run, then restored):
```
  Skip     : Resource group             : rg-urlshortener
  Modify   : Container App              : ca-api-...      (7 property deltas, identical in the baseline)
  Modify   : Container Apps Environment : cae-...         (identical in the baseline)
  Modify   : Container Registry         : cr...           (identical in the baseline)
  Modify   : Application Insights       : appi-...        (+ Flow_Type, + Request_Source: identical in the baseline)
  Skip     : Log Analytics workspace    : log-...
  Skip     : Portal dashboard           : <guid>-dashboard
  Skip     : Azure SQL Server           : sql-...
```
The workbook is not in it, and neither are the database, identity, role assignment, firewall rule, action group, alert or budget: `--output json` gives the same eight rows. The cause (found by the code review, confirmed in the source at tag `azure-dev-cli_1.35.1`): `pkg/infra/provisioning/manager.go` drops every change whose type `azapi.GetResourceTypeDisplayName` does not name (`if mappingName == "" { // ignore; continue }`), and the eight missing types are unmapped. This log first said the renderer "filters nothing"; that looked one layer too low. The `Modify` lines are what-if noise that predates this change. To see every resource, an ARM what-if (read-only, `whatIf` REST call with the compiled `main.json`, `resultFormat: ResourceIdOnly`; `alertEmail` set to a placeholder because its real value lives only in the azd environment's config):
```
Deploy     /subscriptions/resourceGroups                     rg-urlshortener
Deploy     Microsoft.App/containerApps                       ca-api-...
Deploy     Microsoft.App/managedEnvironments                 cae-...
Deploy     Microsoft.Consumption/budgets                     budget-urlshortener
Deploy     Microsoft.ContainerRegistry/registries            cr...
Deploy     Microsoft.Authorization/roleAssignments           <guid>
Deploy     Microsoft.Insights/actionGroups                   ag-...
Ignore     microsoft.insights/actiongroups                   Application Insights Smart Detection
Deploy     Microsoft.Insights/components                     appi-...
Deploy     Microsoft.Insights/metricAlerts                   alert-sql-free-amount-...
Create     Microsoft.Insights/workbooks                      1046121b-9cc3-576f-a00e-24f3b693bd99
Deploy     Microsoft.ManagedIdentity/userAssignedIdentities  id-...
Deploy     Microsoft.OperationalInsights/workspaces          log-...
Ignore     Microsoft.Portal/dashboards                       <guid>-dashboard
Deploy     Microsoft.Sql/servers                             sql-...
Ignore     Microsoft.Sql/servers/databases                   master
Deploy     Microsoft.Sql/servers/databases                   urlshortener
Deploy     Microsoft.Sql/servers/firewallRules               AllowAllWindowsAzureIps
```
`Create` appears once: the workbook. `Deploy` (ResourceIdOnly format) means "already exists, redeployed"; their property-level diff is the azd preview above, unchanged by this task. `Ignore` = in the resource group but not in the template (Smart Detection's action group, a portal dashboard, `master`).

**4. Pre-flight of every panel's KQL** against the live App Insights (timespan P3D; `{TimeRange:grain}` → `1h`, `{TimeRange:start}`/`{TimeRange:end}` → `ago(3d)`/`now()`; extra `project` lines only to count bins). Sample ids found with `requests | where resultCode in ("429","410") | project timestamp, operation_Id, name, resultCode | take 3`.

P1, `{TraceId}` = `46229490dde823ad0ae55a9c887b8359` (a live 410) and = `00-46229490dde823ad0ae55a9c887b8359-0000000000000000-00`, same output for both:
```
timestamp | itemType | name | resultCode | message
2026-10-11T17:35:58.9140488Z | request | GET /{code:regex(^[A-Za-z0-9]{{4,10}}$)} | 410 |
2026-10-11T17:35:58.9163933Z | dependency | SQL: urlshortener |  |
```
P1, `{TraceId}` = `a304290fec977ecc8e5cae43f8fcf119` (a live 429):
```
2026-10-11T14:41:01.4725553Z | request | POST /api/links/ | 429 |
```
P1 with an empty value: 25 rows (startup/shutdown log lines, empty `operation_Id`), which is why the item is hidden until a value is entered. The SETUP.md query with the literal id returns the same 2 rows as P1.

P2 (redirect outcomes): 3 series, 72 hourly bins each:
```
resultCode | bins | total
302 | 72 | 392
410 | 72 | 4
404 | 72 | 1
```
Names matched by the filter: only `GET /{code:regex(^[A-Za-z0-9]{{4,10}}$)}` (302: 392, 410: 4, 404: 1).

P3 (429s on create): `bins 72 | total 4`.

P4 duration (4 rows):
```
timestamp | p50 | p95
2026-10-11T14:00:00Z | 4.6909 | 1255.2996
2026-10-11T15:00:00Z | 6.250561111111111 | 80.8055
2026-10-11T16:00:00Z | 4.363 | 983.7271
2026-10-11T17:00:00Z | 28.6647 | 168.4158
```
P4 errors and drops (1 row):
```
5xx responses (all routes) | dropped clicks
0 | 0
```
`customMetrics` for `urlshortener.analytics.dropped_clicks` over the same window: 64 rows (one per minute while the app ran), `sum(valueSum)` 0.

No application code changed, so `dotnet build`/`dotnet test` were not run by this subagent (the coordinator runs the gates).

## Reviews (coordinator)

**`/security-review`:** no findings (none reached confidence 8). Checked: KQL injection through `TraceId` (runs with the viewer's own read rights, nothing escalated), `kind: 'shared'` (an RBAC-controlled resource, not public), ids or secrets in the committed files (none; the workbook GUID is a hash of the resource group id), PII in the panels (none beyond what Logs already shows).

**Fresh `/code-review`:** Bicep shape, parameter items, conditional visibility, time contexts and every query verified against Learn and Microsoft's own workbooks. Three findings, all taken:

| Finding | Outcome |
|---|---|
| The azd preview gotcha said the cause was unknown, and this log said azd "filters nothing"; azd's `manager.go` drops unmapped types | fixed in CLAUDE.md §9 and check 3 above, after reading the source at the 1.35.1 tag |
| A traceId pasted with a space returns no rows, silently | fixed: `trim(@"\s+", ...)` first; live check below |
| This log said ADR-0006 was not edited, but the coordinator edited it | fixed (artifact table) |

Live check of the trimmed query (P3D), each returning the 410 request and its SQL call:
```
[ 46229490dde823ad0ae55a9c887b8359 ]                       -> 2 rows (request 410, dependency SQL)
[ 00-46229490dde823ad0ae55a9c887b8359-0000000000000000-00] -> 2 rows
[46229490dde823ad0ae55a9c887b8359]                         -> 2 rows
```
While applying the fix, a wrongly escaped backslash in `workbook.json` failed `bicep build` (`BCP186: Unable to parse literal JSON value`): the `loadJsonContent` safety net working as intended. After the fix: `bicep build` exit 0, `bicep lint` exit 0 with no diagnostics.

## Portal check (engineer, after `azd provision`)

- **Listed and rendered:** "URL shortener support" appears under the App Insights resource's Workbooks; exactly one workbook in the resource group (ARM list: `1046121b-...`, `sourceId` the component, `fallbackResourceIds` injected).
- **Every panel failed in the engineer's normal browser** with "Query failed: could not connect to server", while the Logs blade in the same tab returned rows. The workbook's App Insights queries go to `api.applicationinsights.io`; Logs goes to the workspace. In an InPrivate window (no extensions) the panels loaded, so a blocker extension was the cause. Recorded in SETUP.md and CLAUDE.md §9.
- **Panel 1, end to end:** a link was created, deleted and opened on Azure:
  ```
  HTTP/1.1 410 Gone
  cache-control: no-store
  {"title":"This short link has expired or been deleted.","status":410,"traceId":"00-d3a1e06c953d045c73bdf55e9a6df33f-42f50ba8d6a46cb8-01"}
  ```
  Pasting that full value showed the 410 request and its SQL call. The docs said the id ends in `-00`; on Azure it ends in `-01` (the sampled flag), so the text now says `<flags>`. The parse takes the second segment, so either works.
- **Edited after the check (engineer asked whether the panel gives valuable info):** it showed that the request happened, not which link or where the time went. Added `duration` and `url`. Live, the same trace:
  ```
  timestamp                    | itemType   | name                    | resultCode | duration | url
  2026-10-11T20:51:46.5221102Z | request    | GET /{code:regex(...)}  | 410        | 482.6788 | http://<app>/7XcwRgi
  2026-10-11T20:51:46.8137683Z | dependency | SQL: urlshortener       |            | 3.6247   |
  ```
  The request took 483 ms, the SQL call 3.6 ms, starting about 290 ms in: the time went before the query (the first request after the app woke), not to the database.

- **Panels 2-4 (engineer's screenshots, Incognito, last 24 hours):**
  - Panel 2: 302 = 392, 410 = 5, 404 = 1. The 302 spike near 10 AM local is the k6 run of the Azure performance check (386 requests, 15:06 UTC, AI log 017); the fifth 410 is the end-to-end check above.
  - Panel 3: 4 rejections near 9:40-10 AM local: one 429 from the first burst (14:41 UTC) and three from the re-check after #33 (14:56 UTC, AI log 016). The fourth 429 of the first burst was never stored (sampled out before #33), which is the gap #33 fixed.
  - Panel 4: the p50/p95 chart and the table (5xx 0, dropped clicks 0) render. **Defect found:** the summary numbers under the chart read "p50 (Sum) 527" and "p95 (Sum) 2.97K": the chart legend sums each series over its bins, which is right for counts (panels 2-3) and meaningless for percentiles. Learn ("Chart visualizations", Legend: "the aggregation function to use for the legend") confirms the setting; its numeric values are not documented. First fix, **rejected after the portal check:** `"chartSettings": { "showLegend": true }` (copied from `Workbooks/AKS/Node GPU/Node GPU.workbook`), on the guess that it shows names without numbers. It was deployed (ARM showed it, modified 21:28 UTC) and the legend still read "p50 (Sum) 608 / p95 (Sum) 3.3K", so it controls something else; the commit subject of 4d32151 describes that guess, not a fix. Second fix: instead of guessing the number, the engineer picked **Legend → Max** in the portal's edit mode and pasted the JSON the portal wrote, `"aggregation": 2`; that value replaces `chartSettings`. Max is a meaningful summary for a percentile series ("the worst p95 in the range"); the sum is not.
- **Warm-up, measured:** the slow first request is the app, not SQL. The same 410 opened three times straight after a cold start (query as panel 1, joined to its SQL call):
  ```
  request_ms | sql_ms | ms_before_sql
  326.5      | 24.7   | 247.6
  81.7       | 1.7    | 79.1
  5.4        | 1.7    | 3
  ```

## Not verified here (needs the engineer's `azd provision`)
- Rendering, the gallery listing and the visibility rule: verified by the engineer (above). Still open: that the panel 4 legend reads "(Max)" after the next `azd provision` (the portal's own edit showed it, unsaved), and that a second provision keeps one workbook (the ARM list after the first re-provision showed one).

## Gotchas encountered
Added to CLAUDE.md §9: azd's provision preview does not list every resource (the workbook included); a KQL `let` variable named like a column (`id`) is silently shadowed by the column.

## Sign-off
Pending: the engineer's review of the diff and the portal check after `azd provision`.
