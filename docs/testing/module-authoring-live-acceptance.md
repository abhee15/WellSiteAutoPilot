# Module Authoring live acceptance

Run on a local installation connected to authorized test assets. Use **Shadow** for configured runs and read-only provider access for previews. Record the AutoPilot build and provider version, test case and observed result, source timestamps and units, and a run ID when applicable. Use sanitized labels in shared results.

## ForeSite

1. Test the saved ForeSite connection and import one well with a recent pump-fillage reading. Compare the provider's value, unit and source time with **Authoring → Live preview → Run live preview** for a saved pump-fillage threshold draft. Use the same draft in Workflow and IDE.
2. Select **Read last 7 days of production**. Compare the returned inferred total fluid rate, unit and period start/end to the ForeSite production-day records. Compare source offsets around a clock change if the test data includes one. Check a populated value and a missing value when present. The preview lists the periods the source returned in the requested window.
3. Select **Read stored surface cards**. Compare the available CURRENT and FULL cards to ForeSite's stored surface-card library for the same well. Check capture time, position and load units, point count and chart shape. Confirm an empty library yields an empty preview. Check the provider's acquisition status separately if the deployment exposes it. AutoPilot reports **Unknown** when the library supplies no verified quality code.
4. Run the saved sample cases, submit the exact draft revision, approve and publish it through the reviewer workflow, then assign the approved module version to the test well. Trigger one configured Shadow run. Compare the pinned package, input value, time, quality and decision with the expected threshold result. Verify that no field command was sent.

## CygNet

1. Run the Gateway with the licensed CygNet libraries and Windows service account configured as described in [CygNet adapter](../implementation/cygnet-adapter.md). Test the saved connection and import one facility with a recent numeric pump-fillage point.
2. Open the same threshold draft in Authoring. Select the CygNet facility and run the live preview. Compare the configured UDC, value, unit, scan timestamp and quality with CygNet Explorer. A ForeSite daily or card preview on this connection should report that the capability is unavailable.
3. Assign the same approved module version to the CygNet test facility and trigger one configured Shadow run. Verify the pinned package, source, input and decision. If a mapped point is stale, missing or nonnumeric, capture the refusal or preview failure and the matching point state in Explorer.

## Follow-up evidence

- ForeSite Well Test history needs the deployed route, paging, ordering and lift-specific status evidence. Use the [Well Test capture steps](../design/adapter-capability-workflows-and-authoring-plan.md#well-test-history-evidence).
- The Idle Time and VSD Target Fillage acceptance cases require the POC's production and cycle rollover readings, site-local or trailing windows, ordered rules and dry-run proposal trace. Production-day preview and stored-card inspection are available independently of those bindings.
- CygNet VHS history requires the [licensed historian checks](../implementation/cygnet-history-phase2.md). Record live results separately from simulator or archived evidence.
