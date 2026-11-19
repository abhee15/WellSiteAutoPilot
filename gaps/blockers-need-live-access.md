# Blockers GitHub cannot verify without live ForeSite or CygNet access

Each item needs something only the live environment can supply. Sorted by what to ask for. Answer in place or add redacted
samples under `gaps/samples/`; see also `live-environment-clarifications.md` (F-items).

| ID | Needs | Category |
|---|---|---|
| P1 | stored cards on a real ForeSite well; compatibility for structured inputs | Source model and provider catalogue |
| P7 | quality reported for real stored cards | Validation and contract rules |
| P20 | which real assets answer GUIDED_LIVE_SOURCE_UNAVAILABLE and why | Source model and provider catalogue |
| P22 | a CygNet connection and domain with assets (none on this box beyond the stand-in) | Environment, tests and tooling |
| P27 | quality reported for real stored cards (fixed in code by #660, not yet seen live) | Validation and contract rules |
| P72 | a CygNet service reachable from CI, or fixtures captured from one | Environment, tests and tooling |
| P73 | a Lufkin 2.0 / WellPilotRPOC well with the real Sec*ScaSpd and GRP_VSD_SPEED_CFG setpoints | Setpoints and live ForeSite data |
| P74 | which setpoint groups a real controller leaves unsaved | Setpoints and live ForeSite data |
| P75 | real well-name normalisation rules (whitespace in names) | Setpoints and live ForeSite data |
| P76 | why setpoint reads time out on usdcfstappts012 | Setpoints and live ForeSite data |
| P77 | whether usdcfstappts012 is generated data | Setpoints and live ForeSite data |
| P78 | real setpoint payloads (string values, OriginalValue, UnitKey, ranges) | Setpoints and live ForeSite data |
| P79 | group names per device family | Setpoints and live ForeSite data |
| P80 | a LufkinSam well that holds the speed-config setpoints | Setpoints and live ForeSite data |
| P81 | authoritative device type per well | Setpoints and live ForeSite data |
| P82 | server concurrency limits for setpoint reads | Setpoints and live ForeSite data |
| P83 | element to group mapping across device families | Setpoints and live ForeSite data |
| P84 | which of two duplicate setpoints is authoritative | Setpoints and live ForeSite data |
| P85 | time-valued setpoint formats | Setpoints and live ForeSite data |
| P86 | applicability rules for unconfigured wells | Setpoints and live ForeSite data |
| P96 | run the adapter live check with FORESITE_BASE_URL set against a ForeSite with a stored card library | Environment, tests and tooling |
| P97 | production-days preview with a quantity against a reachable ForeSite (503 on the local box) | Environment, tests and tooling |
| P100 | setpoint value shapes (see P78-P86) | Logic, outputs and capabilities (multi-input, multi-output, custom logic) |
| P101 | an input whose declared unit differs from the provider unit, end to end | Environment, tests and tooling |
| P102 | sample range and Submit gate checked in the UI | Environment, tests and tooling |
| P104 | a ForeSite well model file for WAMI, and an External input checked against a real asset | Environment, tests and tooling |
