# Responsive detail focus review

Research and probe preceded implementation. PR #315 reuses ActionButton for copy and refresh,
adds one shared Status presentation and resolves audit names inside the existing administrator
read transaction. Its seven CI checks and local frontend lint/build, 253 interaction tests and
237 logic tests passed before this follow-up.

The actual application was probed at 390px and 1440px using the design-preview fixtures.
There was no horizontal overflow or browser exception. At 390px keyboard activation of an
Activity row hides the browse pane, leaving focus on BODY; Back does the same. Desktop keeps
the originating button focused. The same split-browse/split-selected structure is used in
Runs, Assets and both Logic tabs, so fix the pattern once and use it in all four screens.

Reference: https://developer.chrome.com/docs/lighthouse/accessibility/managed-focus

## Reviewed contract

- A shared hook captures the originating control (including Create above the list) before
  activation and observes selection identity.
- When the browse pane is hidden by the responsive CSS, focus the labelled detail section.
- On closing, return to the still-visible originating control. Do not steal focus from an
  unrelated control or reset focus on data refreshes with the same selection.
- If both panes remain visible, opening details leaves keyboard focus where it is.
- The helper owns no selection, routing, authorization, loading or breakpoint constants.
- Detail sections use tabindex=-1, so programmatic focus adds no extra tab stop.

Verify with interaction tests for narrow/wide transitions, refreshes, and removed origins;
then repeat real-browser keyboard probes and inspect responsive screenshots before integration.

## Verification

The repeated real-browser probe passed at 320px, 390px and 1440px: narrow Activity
details receive focus and Back restores the exact originating row; desktop retains
focus in the browse pane. At 390px the same open/Back assertions passed for Runs,
Assets, configured Logic and the Modules tab. Screenshots were inspected; Activity
had no horizontal overflow or page exceptions. These probes use fixture data, not
Windows authentication or a live directory/provider.

Frontend lint and TypeScript/Vite build passed, as did all 258 interaction tests,
237 logic tests, architecture conventions and 15 convention guard tests.
