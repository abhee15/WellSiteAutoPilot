# Validation and contract rules

Index only. Full evidence for each item is in `complex-module-poc-findings.md` (section named below).
Generated from that log; update both when adding items.

| ID | Finding | Status | Needs live system | Logged in |
|---|---|---|---|---|
| P7 | Card quality reported "Unknown" for stored cards while the input declares allowUncertainQuality=No; unclear... | Open | Yes | Gaps |
| P8 | the blank starter and any draft with 0 inputs or 0 outputs cannot be saved. POST /guided-module-drafts -> 4... | Fixed on develop | No | Live verification of #636 (CL 1108673), Edge visible, 2026-11-17 |
| P17 | the server accepts what the UI blocks: unregistered quantity, wrong unit (kg for a % quantity), text value ... | Open | No | Breadth testing of #638 (CL 1108675): input kinds, value types, asset  |
| P18 | history-preview rejects any input that has a valueSchema (GUIDED_HISTORY_INPUT_UNSUPPORTED), and every UI-c... | Open | No | Breadth testing of #638 (CL 1108675): input kinds, value types, asset  |
| P24 | Sample-case reading accepts values outside 0-100 for a percentage (250 saved and evaluated). | Fixed on develop | No | CL 1108717 (#641, named sample cases) - live check 2026-11-18 |
| P25 | "Submit for review" is enabled while a case has a failing assertion; refusal happens only server-side. Not ... | Fixed on develop | No | CL 1108717 (#641, named sample cases) - live check 2026-11-18 |
| P26 | A port whose quantity is not registered (e.g. SPM, NotARegisteredQuantity) gets no capability or unit, and ... | Fixed on develop | No | CL 1108729 (#642, unsupported-input reasons, empty new ports) - live c |
| P27 | Cards on RPOC_0413 (stored cards report quality Unknown) are rejected by quality, and "Allow uncertain qual... | Open | Yes | CL 1108755 (#643, structured inputs from registered provider schemas)  |
| P28 | Missing expectedRevision returns 409 conflict, not a validation error (minor). | Open | No | CL 1108756 (#644, delete unsubmitted drafts) - live check 2026-11-18 |
| P52 | A wrong unit is reported as a quantity mismatch | Fixed on develop | No | CL 1108781 (#651, provider daily field as scalar input) - live verific |
| P53 | Missing field does not say why | Open | No | CL 1108781 (#651, provider daily field as scalar input) - live verific |
| P54 | Freshness for a daily value is expressed in seconds | Open | No | CL 1108781 (#651, provider daily field as scalar input) - live verific |
| P55 | Daily readings need uncertain quality, but the default rejects them | Open | No | CL 1108781 (#651, provider daily field as scalar input) - live verific |
| P56 | A contract with no unit accepts any unit | Open | No | CL 1108781 (#651, provider daily field as scalar input) - live verific |
| P64 | Migration deletes every draft and submission (intended, closed) | Fixed on develop | No | CL 1108784 (#655, provider required, canonical and immutable on guided |
| P66 | Provider errors only give a code in the manifest field | Open | No | CL 1108784 (#655, provider required, canonical and immutable on guided |
