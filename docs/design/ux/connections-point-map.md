# 5. Connection setup and CygNet point-map / UDC editor

## Purpose
Help an administrator connect a CygNet site, understand whether its data is usable (fresh, mapped, readable), and edit which UDC supplies each quantity. Plan: A2 (freshness diagnostics, done), A4 (per-connection point map and UDC editor), A5 (site-specific mapping, extra quantities).

Backend facts (`docs/implementation/cygnet-adapter.md`): connection settings `domain`, `siteService` (e.g. `CYG_DEMO.UIS`), optional `facilityFilter` wildcard (e.g. `RPOC_00*`), optional `pointMap` as `Quantity=UDC|unit;...`. Default map: PumpFillage `RRLPFILAVG` %, CasingPressure `PRCASXIN` psi, TubingPressure `PRTUBXIN` psi, RodLoad `RRLMXLDDLY` lbf; MotorAmps unmapped (`CYGNET_QUANTITY_UNSUPPORTED`). Some demo points carry old timestamps. All connections use the Gateway's Windows account.

## Who
- View connection, status, map and diagnostics: `connections.read`.
- Create/edit connection, edit map, run diagnostics that read data: `connections.manage` (with `connections.read`).
- Saving a map changes what logic reads, so it appears in the audit trail.

## Entry points
`#connections` > connection > tabs **Overview | Point map | Diagnostics | Source changes**; asset detail > data source chip "CygNet: RPOC_0012"; authoring/preview error "MotorAmps is not mapped" > "Map this quantity" deep link `#connections/{id}/point-map?quantity=MotorAmps`; schedule page "stale data" hint.

## Layout
Reuse the existing Connections page (`ConnectionsPage`, `ConnectionInventory`, `ConnectionImport`, `ConnectionSourceChanges`) and add tabs.

### Setup (Overview tab)
```
Connections › CYG_DEMO                         ● Healthy · checked 12:03   [Test connection]
 Adapter  CygNet     Backend  Live
 Domain            [ 27212          ]
 Site service      [ CYG_DEMO.UIS   ]     Resolved FAC service: CYG_DEMO.FAC
 Facility filter   [ RPOC_00*       ]     Matches 107 facilities (checked 12:03)
 Read account      Gateway Windows account (shared by all connections)
 [Save connection]  [Discard]
```
Live validation: "Test connection" returns Healthy / Degraded / Unavailable with a reason (no CAL, service not found, timeout). The shared read account line is informational; per-connection identity is future (A7).

### Point map tab
```
Quantity         UDC          Unit   Sample (facility)         Freshness           Source
PumpFillage      RRLPFILAVG   %      99.7 % RPOC_0012          ● Fresh 4 s          Default
CasingPressure   PRCASXIN     psi    221 psi                   ● Fresh 4 s          Default
TubingPressure   PRTUBXIN     psi    104 psi                   ⚠ Stale 3 d          Default
RodLoad          RRLMXLDDLY   lbf    31 200 lbf                ● Fresh 4 s          Default
MotorAmps        [ choose… ]  [A ▾]  —                         ○ Not mapped         [Map this quantity]
[Add quantity ▾]     Sample facility [ RPOC_0012 ▾ ]            [Reset to defaults]  [Save map]
```
- Each row: quantity (fixed list from the adapter), UDC (text with validation and a "Browse UDCs" combobox listing UDCs discovered on the sample facility), unit (from the quantity's allowed units), sample value and freshness read live from the chosen sample facility, source tag (Default or Site override).
- **Edit flow:** change a UDC, the row shows "Unsaved", "Check" reads the point from the sample facility and displays value, quality, timestamp and age in the row before saving. Save is enabled when the syntax validates; a read failure warns but does not block (UDC may not exist on the sample).
- Validation (A3): UDC format, duplicates (two quantities to the same UDC warn), unknown quantity, unit conflicts. Messages in plain words with the reason code in technical text.
- **Raw view toggle:** shows the `Quantity=UDC|unit;...` string for copy/paste and import, read/write, with parse errors per segment.
- Impact note before save: "3 configurations read PumpFillage from this connection" with a link list (uses configuration usage data); changes apply on the next run.

### Diagnostics tab (freshness)
```
Freshness diagnostics         Sample facility [RPOC_0012 ▾]   [Re-check]  Last check 12:03
Facility        Quantity       Value     Point time          Age       Verdict
RPOC_0012       PumpFillage    99.7 %    12:03:17            4 s       ● Fresh
RPOC_0012       TubingPress.   104 psi   Oct 28 09:10        3 d 3 h   ⚠ Stale (limit 15 min)
RPOC_0014       RodLoad        —         —                   —         ✕ No value (NO_VALUE)
Summary: 38 of 40 checked points fresh · 1 stale · 1 no value
```
- Verdicts follow the adapter's reason codes (no value, non-numeric, busy, quantity unsupported, point stale, point-map invalid) as sentences.
- Allowed age limit is shown per row (the module's `maximumAgeSeconds` is separate; the diagnostics use a configurable "Fresh if younger than" field, default 15 min) and a note: "Some demo points carry old timestamps; modules need a larger allowed age for them."
- The check is read-only, capped (for example 50 points), rate-limited and shows "Checking 12 of 40…" progress with Cancel.

## States
| State | Behaviour |
|---|---|
| Loading | `LoadingIndicator`; map table shows skeleton rows |
| Empty connections | "No connections yet. Create a connection to start importing assets." (existing) |
| Backend unconfigured | "CygNet is not configured on this server. No data is shown." (no invented data) |
| Simulated backend | persistent info badge "Simulated data" so nobody mistakes demo wells for a real site |
| Unavailable | `role="alert"` with reason and [Retry]; last known map remains, marked stale |
| Map unsaved | row-level "Unsaved" and page-level dirty warning |
| Save conflict | connection revision mismatch: show who changed it, [Reload] |
| Discovery truncated | "Showing the first 500 UDCs. Enter a longer name to narrow." |
| No permission | read-only table; edit controls absent with a note |

## Real-time
- `connections`: refetch connection status, point map and source changes when a connection or its map changes (including edits by other users) and after imports.
- `assets`: facility and asset discovery counts refresh.
- `configured-logic`: usage counts in the impact note.
- Freshness diagnostics are on-demand (a read of the live site); not triggered by signals, and not polled, to protect the CygNet call gate. Results older than 5 min show "Result is old. Re-check."

## Copy
"Test connection", "Point map", "Map this quantity", "Check this point", "Reset to defaults", "Save point map", "Fresh", "Stale", "Not mapped", "No value", "Fresh if younger than", "Simulated data". Errors: "MotorAmps is not mapped for this connection. Choose the UDC that holds motor current." / "This UDC has no value on the sample facility."

## Accessibility
- Point map is a real editable table with headers; each input labelled "UDC for PumpFillage"; row actions as buttons with row context in the accessible name.
- Freshness uses icon + word + age; ages as text, not colour.
- Combobox for UDC browsing follows ARIA combobox pattern with result count announced.
- Progress for diagnostics uses `role="progressbar"` with text; completion announced in a status region.
- Raw string editor labelled, with a line-by-line error list.

## Open questions
1. Are point-map edits instant, or versioned with the connection revision and approval (recommended for audit)?
2. Per-facility overrides (a well with different UDCs) or per connection only (as the adapter supports now)?
3. A5 extra quantities: fixed adapter list, or may admins define new quantity names the modules can request?
4. Should freshness limits for diagnostics come from the adapter, the connection, or the module?
5. Can the UI browse UDCs on the sample facility (needs a discovery endpoint) or is typing the UDC enough for the PoC?
6. Credentials display: show the Windows account in use, or hide until A7 per-connection identity exists?
