# Logic, outputs and capabilities (multi-input, multi-output, custom logic)

Index only. Full evidence for each item is in `complex-module-poc-findings.md` (section named below).
Generated from that log; update both when adding items.

| ID | Finding | Status | Needs live system | Logged in |
|---|---|---|---|---|
| P5 | Contract view shows "Internal steps not recorded" and no input-to-output dependencies; needs recorded execu... | Open | No | Gaps |
| P87 | Contract and logic are separate, and only the contract can have many inputs | Open | No | Capability check: inputs, outputs and custom logic (build through #656 |
| P88 | Outputs are values, not proposals | Open | No | Capability check: inputs, outputs and custom logic (build through #656 |
| P89 | Setpoints cannot be inputs | Open | No | Capability check: inputs, outputs and custom logic (build through #656 |
| P90 | No code or expression path for power users | Open | No | Capability check: inputs, outputs and custom logic (build through #656 |
| P99 | The proposal covers inputs only (Current, History, Daily, Latest test, Cards, Setpoint). It does not addres... | Open | No | CL #663 - one source model for module authoring (design doc) |
| P100 | The doc lists setpoints as 'read design pending F3'. Live data (P78-P86) already shows setpoint values arri... | Open | Yes | CL #663 - one source model for module authoring (design doc) |
| P103 | Many newer PRs (#665-#681) add a rules engine, rules drafts and a workbench. These are exactly the multi-in... | Open | No | CL #662 - stop unusable drafts without telling why |
| P107 | The new flow has no outputs or logic step yet ('later steps appear as the backend can run them'), so multi-... | Open | No | CL #666 - first steps of the new authoring flow (live, ForeSite provid |
| P109 | Daily source gives one value per quantity; no window (last N days), aggregation or lag choice, so Idle Time... | Open | No | CL #667 - daily averages source (live, ForeSite provider, Edge) |
| P110 | Function set is min, max, avg, abs, round, clamp, if. Rod Buckling, Target Fillage and Rotaflex logic need ... | Open | No | CL #670 - rule expression engine (build + V2 checks; library only, not |
| P111 | Expressions are single-valued (number, boolean or text) with decimal arithmetic. No multi-output assignment... | Open | No | CL #670 - rule expression engine (build + V2 checks; library only, not |
| P112 | The outline lists only Inputs. There is still no Outputs or Logic part (P107), so the workbench cannot yet ... | Open | No | CL #669 - workbench shell (live, ForeSite, Edge) |
