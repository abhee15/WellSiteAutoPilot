# Design preview

Renders the built frontend against representative mock API data and captures one
screenshot per section, so a design change can be reviewed visually before and after.
Local aid only; it is not part of CI and never talks to a real server or database.

```bash
cd src/web
npx vite build --outDir /tmp/wsa-preview --emptyOutDir      # no generated API client needed
node ../../tools/design-preview/mock-server.mjs /tmp/wsa-preview 5199 &
python3 ../../tools/design-preview/capture.py /tmp/wsa-after http://127.0.0.1:5199
```

`capture.py` needs Python Playwright and a Chromium (set `CHROMIUM_PATH` if it is not at
`/opt/pw-browsers/chromium`). Edit the mock data in `mock-server.mjs` when a view needs more
states (empty, failed, long lists). Compare against the prototype under `acl/src` described in
`docs/development/design-tokens.md`.

## Live updates

By default the mock has no `/hubs/changes`, so the page shows the polling fallback ("Checking every 10 s").
Start it with `MOCK_HUB=1` to serve a minimal long-polling SignalR hub; `POST /__mock/change?area=assets`
then pushes a change signal and the page shows "Live updates on". Real WebSocket and Windows Negotiate
behaviour are not simulated.

## Roles

The mock can act as any built-in role. Set a `persona` cookie (`Admin`, `Engineer`, `Operator`, or `None` for a user with
no access); the permissions come straight from the server's `RolePermissionMap.cs`, and the mock answers 403 where the real
server would. `role-audit.py` drives the app as each role, page by page and tab by tab, and prints what each one is offered:

```bash
python3 ../../tools/design-preview/role-audit.py http://127.0.0.1:5199 /tmp/role-audit Operator Engineer Admin
```

The automated checks live in `src/web/tests/interaction/role-matrix.test.tsx` (every role against every section and the
main controls) and `src/web/tests/permission-catalog.test.mjs` (every permission the web app checks exists on the server).

## Cascade audit (priority conflicts)

jsdom ignores CSS, so unit tests cannot see a rule that loses to another at equal specificity. This audit runs in a real browser.
From `src/web`: `npx vite build -c ../../tools/design-preview/cascade/vite.unminified.mjs --outDir /tmp/wsa-cascade`,
then `node ../../tools/design-preview/cascade/split.mjs "$PWD/src/" /tmp/wsa-cascade/assets/index-*.css /tmp/segments.json`,
start the mock server, and `python3 ../../tools/design-preview/cascade/cascade-audit.py /tmp/segments.json`.
It reinstalls each source file as its own stylesheet in bundle order, walks every page, tab and the Filters panel at 1440 and 420 px,
and lists each element/property where two files tie on specificity and differ in value. Fix by raising the intended rule's specificity (e.g. `.a.b`), not by reordering imports.

## Shared native forms

The existing visual-review workflow builds `tests/visual/shared-forms.html` using the isolated
`shared-forms.vite.mjs` config, then runs `capture-shared-forms.py`. The fixture build has no API proxy
and does not enter the production bundle. The script serves it locally, checks computed typography
against the actual tokens, contrast, native control/choice sizes, focus and overflow at four widths,
and captures fields and notices. Optional local before/after mock URLs also exercise the recommendation
decision and execution flows without sending commands. The JSON audits and screenshots are uploaded
in the visual-review artifact.

From `src/web` (Python Playwright and Chromium required):

```bash
npx vite build --config ../../tools/design-preview/shared-forms.vite.mjs --outDir /tmp/wsa-form-fixture --emptyOutDir
python ../../tools/design-preview/capture-shared-forms.py /tmp/wsa-form-fixture /tmp/wsa-form-evidence
```

Set `CHROMIUM_PATH` only when using an existing local browser instead of Playwright's installed Chromium.

## Recommendations workspace

`capture-recommendations.py OUTPUT BEFORE_URL AFTER_URL` reviews local mock previews at
1440/900/420/320px plus a short desktop viewport. It captures pending, expired, approved,
read-only, unconfirmed-command and settings tasks, measures scroll ownership and overflow,
and checks detail/return focus, metadata disclosure and browser Back. It sends no commands;
the shared-form audit separately covers delayed decisions, retained input and execution refusals.
The visual-review workflow runs both and uploads their screenshots and JSON audits.

`capture-typography.py OUTPUT BEFORE_URL AFTER_URL` reviews all eight primary pages and
clear/unavailable/restricted Overview states at four widths. It compares computed section,
metric, compact-filter and supporting-text roles with tokens, checks loaded font faces,
responsive Overview grouping and horizontal overflow, and publishes screenshots with JSON evidence.

`capture-command-scroll.py OUTPUT BEFORE_URL AFTER_URL` checks a 30-command queue and expanded
resolution/release forms at seven viewport/text-scale combinations. It uses wheel, keyboard End
and Tab, intersects target bounds with every clipping ancestor and verifies the final controls are
reachable. Hidden panels cannot be a programmatic-scroll workaround. The workflow captures
before/after screenshots and geometry; the audit never submits a command or changes the kill switch.
