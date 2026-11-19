# Source model and provider catalogue

Index only. Full evidence for each item is in `complex-module-poc-findings.md` (section named below).
Generated from that log; update both when adding items.

| ID | Finding | Status | Needs live system | Logged in |
|---|---|---|---|---|
| P1 | asset-compatibility returns Unsupported / GUIDED_LIVE_INPUT_UNSUPPORTED for structured inputs (cards, calib... | Open | Yes | Gaps |
| P2 | quantity-discovery returns 400 for card-only contracts (no scalar source); no explanatory UX. | Open | No | Gaps |
| P10 | new inputs default to capability structured.current, which adapter read rules cannot serve (addressed by #6... | Open | No | Live verification of #636 (CL 1108673), Edge visible, 2026-11-17 |
| P12 | the registry lists only quantities a configured adapter registers; structured inputs (cards, calibration) h... | Open | No | Live verification of #637 (CL 1108674), Edge visible, 2026-11-17 |
| P15 | the author cannot choose or pin the provider or the operation (surveillance, daily averages, well trend, we... | Open | No | Breadth testing of #638 (CL 1108675): input kinds, value types, asset  |
| P16 | the registry has 6 quantities. The three reference modules need 10+ more (see summary). Discovery returns S... | Open | No | Breadth testing of #638 (CL 1108675): input kinds, value types, asset  |
| P19 | Unsupported does not say whether the quantity is unregistered, the asset's connection does not provide it, ... | Open | No | Breadth testing of #638 (CL 1108675): input kinds, value types, asset  |
| P20 | 4 of 72 (then 9 of 126) assets answer 404 GUIDED_LIVE_SOURCE_UNAVAILABLE on compatibility (assets with no p... | Open | Yes | Breadth testing of #638 (CL 1108675): input kinds, value types, asset  |
| P30 | Order is not sorted: TotalFluidRateInferred is first, then the rest alphabetical, so the list looks inconsi... | Open | No | CL 1108758 (#647, offer every populated daily quantity with a provider |
| P31 | Daily averages: no way to read the value of a discovered quantity (user-reported) | Fixed on develop | No | CL 1108758 (#647, offer every populated daily quantity with a provider |
| P32 | Quantity source labels expose provider operation names (user-reported) | Fixed on develop | No | CL 1108758 (#647, offer every populated daily quantity with a provider |
| P33 | Well-specific wording limits the authoring model to wells (user-reported) | Open | No | CL 1108758 (#647, offer every populated daily quantity with a provider |
| P34 | Surface cards should be a quantity source, and are RRL-only (user observation, design open) | Open | No | CL 1108758 (#647, offer every populated daily quantity with a provider |
| P36 | Source values are listed but never shown | Open | No | SME walkthrough of the preview panel (visible Edge, P4 tree with #647; |
| P37 | Contradictory availability messages for the same input | Open | No | SME walkthrough of the preview panel (visible Edge, P4 tree with #647; |
| P38 | Applicability is not asset-type aware | Open | No | SME walkthrough of the preview panel (visible Edge, P4 tree with #647; |
| P40 | Quantity catalogues differ by source with no explanation | Open | No | SME walkthrough of the preview panel (visible Edge, P4 tree with #647; |
| P44 | History result contradicts the compatibility result | Open | No | CL 1108759 (#648: history preview for numeric inputs, no-source messag |
| P46 | The operation list offers operations that cannot serve the input | No longer applies (provider-first) | No | CL 1108780 (#650, source hints) - live verification and SME review |
| P47 | Providers with no usable operation are still offered | No longer applies (provider-first) | No | CL 1108780 (#650, source hints) - live verification and SME review |
| P48 | The mismatch reason does not name the providers | No longer applies (provider-first) | No | CL 1108780 (#650, source hints) - live verification and SME review |
| P50 | Hint parameters cannot be authored | No longer applies (provider-first) | No | CL 1108780 (#650, source hints) - live verification and SME review |
| P51 | Provider names in a portable module | No longer applies (provider-first) | No | CL 1108780 (#650, source hints) - live verification and SME review |
| P57 | Daily field names are provider-specific | Open | No | CL 1108781 (#651, provider daily field as scalar input) - live verific |
| P62 | Quantity names are provider-specific | Open | No | CL 1108782 (#652, add a discovered production-day field as a daily inp |
| P65 | New module has no provider choice yet | Fixed on develop | No | CL 1108784 (#655, provider required, canonical and immutable on guided |
| P106 | The only Source offered is 'Current values', and the ForeSite catalogue holds two readings (AveragePumpFill... | Open | No | CL #666 - first steps of the new authoring flow (live, ForeSite provid |
| P108 | The Well select is an "example well" used only to discover which daily quantities exist; the picker returns... | Open | No | CL #667 - daily averages source (live, ForeSite provider, Edge) |
| P114 | The "Current values" list offers only PumpFillage and AveragePumpFillage because the ForeSite adapter hard-... | Open | No | Surveillance catalogue limited to two readings (root cause found on th |
| P115 | Asset-based discovery now makes one ForeSite call per catalogued quantity (43), because `GuidedLivePreviewS... | Open | No | Surveillance catalogue limited to two readings (root cause found on th |
