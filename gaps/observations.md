# Observations from Sri's own testing

Log each observation here as O1, O2 and so on, with the screen or step, what was seen, and what was expected.
Each one is triaged into a gap in `complex-module-poc-findings.md` or fixed on `feature/module-authoring-build`.

| # | Date | Where | Observation | Expected | Status |
|---|---|---|---|---|---|

- **O1** Once the new module authoring page covers the agreed functionality, remove the old authoring components (the legacy threshold editor and its sample cases, live preview wiring and the dropdown-based draft list) so there is one path to maintain. Before removal the new page needs: a drafts list (P116), the review queue and approval, persisted scenarios (P117), and live preview on a real well. Tracked as step 5 of the build plan.

- **O2** Product focus for the ForeSite adapter: logic modules exist to adjust setpoints. Anything beyond that (calculating other values and pushing them back to ForeSite) is a later expansion, considered only if it proves valuable to users. Sri will share customer scripts to inform that future work. Until then every step is judged by whether it serves the setpoint workflow (steps 3 and 4 of the build plan).
