# Module authoring: one source model

Status: proposal. Addresses live observations P30-P34, P36-P38, P40, P41, P44 and P45 and the open provider questions F10-F12.

## Problem

An author picks a "Quantity source" by ForeSite operation name (Surveillance, Daily averages, Well trend, Well test), then a quantity,
then a different button per source (Load history, Read last 7 days, Read stored surface cards). Consequences seen live:

- The author must know which provider operation carries a quantity; the same physical quantity (pump fillage) is offered under
  some operations and not others (P40) and the labels name the provider and "well" (P32, P33).
- Cards sit outside the model (P34); applicability ignores lift type (P38); values are listed but never shown (P36); preview
  and compatibility results disagree about the same input (P37, P44).

## Model

A module input is described by what the author needs, not by how the provider serves it:

| Author concept | Meaning | Today's backing |
| --- | --- | --- |
| Current value | the latest reading of a quantity | ForeSite surveillance; CygNet current value |
| History | readings of a quantity over a window | ForeSite well trend |
| Daily value | one value per production day, including "yesterday" | ForeSite daily averages |
| Latest test | fields of the most recent test | ForeSite well test |
| Cards | stored surface cards, latest or over a window, optionally by type | ForeSite dynacards (rod lift only) |
| Setpoint | a value held by the controller | ForeSite saved set-point parameters (read design pending F3) |

Each row is a **source role** with a fixed set of options (window, card type, day offset). The provider adapter declares which
roles it can serve, for which asset kinds and lift types, with which quantities and units. The authoring screen never shows an
operation name.

## Rules

1. **One catalogue per asset and role.** The quantity list is always fetched for the selected asset and role, sorted the same way
   (names alphabetical, no special first entry), each entry carrying its unit and, when the provider offers it, the latest value
   with time and quality (P36).
2. **Applicability comes from the adapter, not the UI.** An asset that cannot serve a role (cards on an ESP well) either hides it or
   shows it disabled with the reason (P38). Lift type comes from the provider (F11).
3. **One verdict per input.** The compatibility check and the preview use the same evaluation (age, quality, availability), and
   the preview shows what the contract would conclude, not only what the provider returned (P37, P44).
4. **Asset-neutral wording.** Labels say Current values, History, Daily values, Latest test, Cards; provider and operation names stay
   in the adapter and in diagnostics (P33).
5. **One read action per selection.** Choosing role and quantity offers a single "Read values" action whose result states the
   quantity, unit, time range and quality; cards render as a plot with axes and age (P41, P45).
6. **Contract unchanged.** A declared input is still `{id, quantity, requiredCapability, ...}`; roles map onto the existing
   capabilities (`engineering.current`, `engineering.history`, daily, structured). This is a presentation and catalogue change plus
   the adapter's applicability metadata, so existing drafts keep working.

## Delivery order

1. Adapter applicability metadata (role, asset kind, lift type) and a single discovery endpoint returning the catalogue with latest
   values; keeps today's endpoints until the web moves over.
2. Web: replace the operation select and per-source buttons with role, quantity and one Read action; asset-neutral strings.
3. Cards as a role (latest/by type/window) once F7 is answered; setpoints once F3 is answered.
4. Remove the superseded endpoints and strings.

## Needs from the live environment

F10 (what quality "Unknown" means), F11 (lift type), F12 (current and history quantities per lift type); see
`gaps/live-environment-clarifications.md` on `gaps/module-authoring`.
