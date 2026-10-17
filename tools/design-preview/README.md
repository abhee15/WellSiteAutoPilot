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
