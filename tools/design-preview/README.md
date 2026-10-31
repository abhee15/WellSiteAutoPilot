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
