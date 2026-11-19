# Setpoints and live ForeSite data

Index only. Full evidence for each item is in `complex-module-poc-findings.md` (section named below).
Generated from that log; update both when adding items.

| ID | Finding | Status | Needs live system | Logged in |
|---|---|---|---|---|
| P73 | Rod Buckling cannot run on WellPilotRPOC | Open | Yes | Rod Buckling check on usdcforsqlts004 (well "AN   13-14", WellPilotRPO |
| P74 | Setpoint groups with no saved transaction look like errors | Open | Yes | Rod Buckling check on usdcforsqlts004 (well "AN   13-14", WellPilotRPO |
| P75 | Well names are not normalised | Open | Yes | Rod Buckling check on usdcforsqlts004 (well "AN   13-14", WellPilotRPO |
| P76 | Setpoint read on this server hangs rather than failing | Open | Yes | usdcfstappts012 check |
| P77 | Server looks like generated test data | Open | Yes | usdcfstappts012 check |
| P78 | Setpoint values come back as strings with a separate original value | Open | Yes | usdcfstappts010 / 011 check (Rod Buckling setpoints) |
| P79 | Group names differ from the original script | Open | Yes | usdcfstappts010 / 011 check (Rod Buckling setpoints) |
| P80 | Lufkin 2.0 speed-config setpoints are not on any scanned well | Open | Yes | usdcfstappts010 / 011 check (Rod Buckling setpoints) |
| P81 | Device type is a filter, not a property | Open | Yes | usdcfstappts010 - dashboard-guided Lufkin / RPOC scan |
| P82 | Setpoint reads do not survive modest concurrency | Open | Yes | usdcfstappts010 - dashboard-guided Lufkin / RPOC scan |
| P83 | Same elements, different group names across device families | Open | Yes | usdcfstappts010 - dashboard-guided Lufkin / RPOC scan |
| P84 | The same setpoint appears twice and disagrees | Open | Yes | WellPilot (RPOC) logic module check on usdcfstappts010 (wells 776 and  |
| P85 | Time-valued setpoints arrive as text with unit "s" | Open | Yes | WellPilot (RPOC) logic module check on usdcfstappts010 (wells 776 and  |
| P86 | Wells with identical VFD limits make target-fillage logic meaningless | Open | Yes | WellPilot (RPOC) logic module check on usdcfstappts010 (wells 776 and  |
