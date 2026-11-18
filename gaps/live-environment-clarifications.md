# Clarifications needed from the live environments (ForeSite and CygNet)

Purpose: this branch (`gaps/module-authoring`) is the bridge between the environment that has live ForeSite and CygNet and the
GitHub/CI environment, which can reach neither. It is never merged into `develop`. Claude adds questions here that only the live
environment can answer; reviewers answer in place and push back; Claude reads the answers and builds from them.

How to answer: edit the **Answer** line under a question (or add evidence below it). Redact customer names and credentials.
Where a real payload helps, paste a trimmed, redacted sample under the question or into `gaps/samples/`. Samples become simulator
fixtures, so keep the field names and shapes exactly as the provider returns them.

Status per item: Open / Answered / Not applicable. Priority: P1 blocks the three reference modules, P2 blocks correctness or
testing, P3 is for later.

Context: the reference modules are Idle Time (RPOC), Target Fillage (VSD) and Rod Buckling (Lufkin). Authoring is now
provider-first: a module is written for one primary provider (ForeSite or CygNet), fixed at creation. CygNet serves current values
only for now. WAMI is a later source role whose model file comes from the ForeSite well.

---

## F. ForeSite

**F1. What exactly is "yesterday"?** (Open, P1)
- Today a daily read returns the latest production day that has finished, from `Surveillance/Well/DailyAverage`, using the provider's
  own start/end for that day (never a clock cutoff computed by us).
- Question: for Idle Time, is "yesterday" the provider's last finished production day, the calendar day in the site's time zone, or a
  fixed contract hour? Does it differ per well or per customer? If a well has no record for that day, should the module fail, skip
  or use the day before?
- Why: decides the daily read rule, the freshness window and the failure behaviour.
- Answer:

**F2. Which daily fields do the three modules need, and are they populated on real wells?** (Open, P1)
- The `Units` map on a real well lists dozens of quantities; only some have values in a given week (see finding A1).
- Question: the exact daily field names each module needs (for example idle/run time, fluid and phase rates), their units, and
  whether they are blank for some wells or device types. Is a zero distinguishable from "not reported"?
- Answer:

**F3. Setpoints: which ForeSite element holds each setpoint, and is reading it safe?** (Open, P1)
- We know `Surveillance/Well/AllSavedSetPointParams` exists and returns saved parameter sets per scan option.
- Question: for Target Fillage (VSD) and the other modules, the exact setpoint element name/id, its path in the response, its unit,
  and whether it is the value in force or only the saved one. Can a GET ever trigger a scan, a write or a command? Is it identical
  for every well, or does it depend on controller type?
- Why: setpoint inputs are not readable by the Server at all yet; the runtime read has to be designed from the real shape.
- Answer:

**F4. Real response shapes for fixtures.** (Open, P2)
- Please provide trimmed, redacted real responses for: `Surveillance/Well/DailyAverage` (one well, 3 days, including a day with
  nulls), `Surveillance/Well/AllSavedSetPointParams`, `Surveillance/well/{id}` (current), `WellTests/GetLatestWellTestData`
  (including the empty-body case), and `Dynacards/Dynacard/LatestWithUnits/Lib/{id}/{type}`.
- Why: the simulator's ForeSite stand-in is built from recorded shapes; real shapes find bugs a guess cannot.
- Answer / location of samples:

**F5. Quality on daily and current values.** (Open, P2)
- Daily readings are labelled quality "Uncertain" because the daily record carries no quality. Is there any quality or validity
  flag we are missing (for example a "day complete" or "estimated" marker)? What do current-value readings do when the controller is
  offline: stale value with an old time, null, or an error?
- Answer:

**F6. Model file for WAMI (Rod Buckling on Lufkin).** (Open, P2; needed before PR 6)
- Question: where does the ForeSite well expose the model file (endpoint, format, size, how it is versioned)? How are asset roles
  and sensitivities carried inside it? Does the file always come with the well, or only for some device types?
- Also: should WAMI be reached through ForeSite (ForeSite calls WAMI) or directly through the WAMI adapter, for these modules?
- Answer:

**F7. Surface cards for Rod Buckling.** (Open, P2)
- Which card (current, full, a pump-off card) does Rod Buckling need, how many, and with which units? Does it need the card's
  capture time as well as the points?
- Answer:

**F8. Time zones and production-day boundary.** (Open, P2)
- Is a well's production-day start (for example 06:00) a per-well or per-customer setting, and where can we read it? The recorded
  fixtures show both UTC-offset and Z forms in the same response, including DST weeks.
- Answer:

**F9. Trend history for pressures and amps.** (Open, P3)
- Finding A1: `CygNetTrends` is documented for any mapped quantity but the adapter registers only pump fillage and SPM. Which
  other quantities do you want as ForeSite history, and are they all available through that endpoint?
- Answer:

**F10. What does quality "Unknown" mean on history samples and stored cards?** (Open, P1)
- Findings P27 and P44: stored surface cards and trend samples report quality Unknown, and the runtime rejects them
  (EXECUTION_INPUT_QUALITY_REJECTED) even when a module allows uncertain quality. Does ForeSite's Unknown mean "not assessed" (fine
  to use) or "do not trust"? Is there another field that says whether a card or sample is valid? Should a module be able to accept
  Unknown explicitly?
- Answer:

**F11. How does the live system tell the lift type of a well?** (Open, P2)
- Findings P34 and P38: rod-lift quantities and cards are offered for ESP wells where they are meaningless. Which ForeSite field
  or call gives the lift type or device type reliably (for example RRL vs ESP vs gas lift), and is it always populated?
- Answer:

**F12. Which quantities exist for "current" and for "history" per lift type?** (Open, P2)
- Findings P31, P32 and P40: the same physical quantity (pump fillage) appears under different names and sources. For each lift
  type used by the three modules, please list the current, history and daily name of each quantity the modules need, so authoring
  can show one list per asset instead of one per provider operation.
- Answer:

## C. CygNet

**C1. Point (UDC) and unit for each quantity the modules read.** (Open, P1)
- The adapter maps a logical quantity (PumpFillage, CasingPressure, TubingPressure, MotorAmps, RodLoad) to a UDC and unit through
  built-in defaults, overridable per connection. Please confirm the real UDC and unit used in the live domain for every quantity
  the three modules need, and list any quantity a module needs that is not in that set.
- Answer:

**C2. Facility ids and the link to ForeSite wells.** (Open, P1)
- Asset external ids are `SITE.SERVICE::FACILITYID`. Does a CygNet facility correspond one-to-one to a ForeSite well for the
  wells these modules run on? If a module is written for ForeSite but the asset is imported through CygNet (or the reverse), how
  does the live environment tell they are the same physical well?
- Why: decides whether any cross-provider asset identity is needed (relevant to the WAMI source role).
- Answer:

**C3. Point status flags and quality.** (Open, P2)
- We map "no flag" to Good, "Unreliable" to Bad and any other flag to Uncertain. Which flags actually occur on these points in the
  live domain (for example manual, stale, comm-fail, out-of-scan), and should any of the others count as Bad?
- Answer:

**C4. Value timestamps and freshness.** (Open, P2)
- The point value carries its own timestamp. How old is "too old" for each quantity on the live system, and how often do points
  update? Used to set sensible default maximum ages.
- Answer:

**C5. Setpoints in CygNet.** (Open, P2)
- Are the setpoints for the three modules also CygNet points? If so give the UDCs and whether they are readable by the same
  account. If they exist only in ForeSite, say so; it decides whether a CygNet-primary module can have setpoint inputs.
- Answer:

**C6. History on the real client.** (Open, P3)
- Does the live CygNet client support time-range history, and is it allowed for the Gateway's service account? Today authoring
  treats CygNet as current values only.
- Answer:

**C7. Real samples for the simulator.** (Open, P2)
- Trimmed, redacted point records for 3 quantities, including one with status flags set and one with no record, as the CygNet
  client returns them (value, timestamp, status flags, units).
- Answer / location of samples:

## X. Both providers / process

**X1. Order of reference modules.** (Open, P2)
- Which of Idle Time, Target Fillage and Rod Buckling should be the first to run end to end? The first one drives which read type
  is built first (daily, setpoint or model file).
- Answer:

**X2. The module logic itself.** (Open, P1)
- Threshold drafts only support one or two current inputs and a single comparison. The three modules need real calculation logic.
  Please share each module's source (or pseudocode): calculations, outputs, parameters (defaults and limits), which input comes from
  which ForeSite/CygNet element, and the definition of "yesterday" (see F1).
- Answer:

**X3. How should the live environment verify simulator fidelity?** (Open, P3)
- Once fixtures exist, can someone run the same read plan against the live system and the simulator and compare the results so we
  know where they differ? Suggested: a small script on the live machine that prints the normalised readings for 2 wells.
- Answer:
