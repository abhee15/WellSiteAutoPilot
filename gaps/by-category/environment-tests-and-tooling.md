# Environment, tests and tooling

Index only. Full evidence for each item is in `complex-module-poc-findings.md` (section named below).
Generated from that log; update both when adding items.

| ID | Finding | Status | Needs live system | Logged in |
|---|---|---|---|---|
| P21 | the repo samples (three CygNet checks, authoring dotnet and python) are V1 manifests with entry points and ... | Open | No | Breadth testing of #638 (CL 1108675): input kinds, value types, asset  |
| P22 | all 126 assets searched belong to the ForeSite connection. No CygNet or SJVKRP assets were found, so the Cy... | Open | Yes | Breadth testing of #638 (CL 1108675): input kinds, value types, asset  |
| P63 | The e2e harness cannot run against the local stack | Open | No | CL 1108783 (#654, e2e: guided fillage case through an imported ForeSit |
| P67 | The pressure e2e case was dropped | Open | No | CL 1108784 (#655, provider required, canonical and immutable on guided |
| P72 | Simulator-only CygNet path cannot be exercised on this box | Open | Yes | CL 1108788 (#657, CygNet stand-in for current values) |
| P96 | Not verified live: the adapter tests skip the live check (FORESITE_BASE_URL unset), so Uncertain quality on... | Open | Yes | CL #660 - stored cards report Uncertain |
| P97 | Live preview not confirmed: POST production-days-preview with a seeded asset (18_5) returned 503 'The integ... | Open | Yes | CL #661 - any discovered daily quantity in the production-day preview |
| P98 | Build, adapter checks, V2 contract tests, tsc, eslint, vitest 13/13 and 379 guards pass; the generated clie... | Open | No | CL #661 - any discovered daily quantity in the production-day preview |
| P101 | Verified by unit tests only (V2 and manifest checks pass). Not seen end to end: a run needs an input whose ... | Open | Yes | CL #664 - wrong unit reported separately from wrong quantity |
| P102 | Not run live: build, V2 checks, tsc, eslint, vitest 13/13 + 10/10 and guards pass, but the stack is still o... | Open | Yes | CL #662 - stop unusable drafts without telling why |
| P104 | Verified by build, V2 checks, tsc, eslint, vitest 13/13 + 10/10 and guards only; not exercised live. Markin... | Open | Yes | CL #665 - inputs read from the primary provider; external exceptions |
