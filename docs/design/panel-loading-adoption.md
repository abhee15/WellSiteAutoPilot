# Initial panel loading adoption

First implementation slice for #482 after the shared link review #481.

## Finding and decision

Connections put a basic loading indicator directly under PanelHeader: its leading edge was 18px left
of the heading gutter at both desktop and phone widths, with zero inline padding. The loading panel
also collapsed to about 119px. The shared PanelLoading already owns an inset, centered content region;
this was an adoption gap rather than a reason for another page-specific margin or spinner component.

Use PanelLoading for unavailable initial content in Connections, Access's user directory and each
independently loaded Status section. Connections stops showing the initial loader as soon as rows
are available, including while adapter metadata is still pending. Access's pagination progress stays
small and inline in its existing padded action row. Preserve the existing read/abort/permission models,
empty/error states and retained Status content during background refresh. No global overlay or navigation block.

The primary-page guard rejects basic block loaders at these content boundaries. Overview's contextual
attention read is deliberately excluded: it already sits in the padded attention body and does not
replace the other available content. Record/detail reads and specialized nested contexts still need
individual review; this is not a universal replacement of every LoadingIndicator call.

## Validation

Production build/typecheck, lint, 352 UI checks and 555 interaction tests passed. The local browser
review compares the exact merged base to the new source: 72 cases across Connections, Access and
Status, four widths and populated/empty/failed terminal states. It withholds mock GET responses,
measures the actual loader center and content-region bounds, checks a single status announcement per
loading region, verifies navigation remains usable and checks overflow and terminal content/error
visibility. A Status refresh is also held to verify the existing rows remain visible without a blocking
loader. All writes are rejected by the audit. JSON evidence is retained by the visual-review workflow.

The audit and existing primary-page/responsive checks run in CI. The broader #482 gutter/indentation
review remains open, including detail panes and state transitions beyond these initial content regions.
#467 continues to own the remaining form/notice foundation migration; no new parallel component kit.

The first CI run exposed a response-gating race in the audit: Status renders both initial placeholders
before both requests have necessarily reached the interceptor. Releasing only the first captured read
left the later read withheld. The harness now waits for the required endpoint paths before releasing
initial and refresh responses and logs each case; all application geometry/content assertions remain. The Status fixture deliberately starts the service
read 200ms later than the connection read to exercise the race deterministically.
