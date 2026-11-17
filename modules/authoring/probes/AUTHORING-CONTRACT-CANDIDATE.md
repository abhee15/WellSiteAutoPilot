# Authoring contract candidate: shared document, authoritative evaluation

Status: design/probe decision, not a production API or supported file format.
Related plan: #320. Evidence: #321 and CONFIDENCE-REVIEW.md.

## Responsibility split

The web workspace and console are two clients of an authoring document. Neither
client defines different calculation semantics. The selected, versioned execution
profile owns validation and evaluation. Browser arithmetic is illustrative only
until it uses that authority or verified cross-runtime conformance.

An authoring document, a deployment binding, a module manifest, a built package
and a test report are separate artifacts. A document is not executable permission.
The current CLI does not yet consume this candidate document.

## Candidate document shape

| Part | Required meaning |
| --- | --- |
| Identity | Document schema version, project ID and immutable revision/content digest |
| Inputs | Stable IDs, semantic quantities, asset roles, schema IDs, units and freshness/quality requirements |
| Parameters | Stable IDs, schemas, defaults and explicit literal representation |
| Steps | Stable step/port IDs, operation/profile version, typed dependencies and supported configuration |
| Code-backed steps | Exact module/package identity and hash, declared input/output ports; retain original source/package as an opaque artifact |
| Conditions | Explicit comparison and selected outcomes, not inferred from diagram position |
| Dependencies | Named data/calculation roles and required capabilities; multiple connections/providers permitted |
| Outputs | Typed engineering results and proposed actions, separately identified |

Display names can change without changing dependency IDs. Edges reference explicit
ports, never array position or matching display names. Reject missing/duplicate
IDs, dangling edges, incompatible types/units and cycles in the first bounded
acyclic profile. Iteration within an SDK algorithm remains possible inside an
opaque code step; this does not imply unrestricted cyclic workflow support.
Collections and nested records retain their reviewed schemas. Do not flatten
curves to scalars to fit an editor. The scalar editing probe is not the complexity
limit: collection/custom operations require explicit representations and probes.

Code-backed steps preserve arbitrary supported algorithms without claiming that
their internals can be reconstructed visually. Unsupported imports stay intact
and visibly opaque; never silently rewrite or drop them.

## Numeric decision and executable evidence

Use the supported runtime as calculation authority. For the initial reviewed
built-in scalar operations, probe an explicitly versioned checked-decimal profile.
Store decimal literals as canonical decimal text in the AUTHORING document and
parse using invariant culture at that profile's boundary. This avoids rounding
through browser Number during document editing/export. Do not change existing
V2 value schemas or convert every runtime observation into a string.

The .NET composition probe demonstrates decimal literal transport, 0.3 / 0.1 = 3,
rejection of division by zero and checked overflow. Binary-double preview of the
same division differs. This is evidence against independent JS implementations,
not proof that decimal suits every engineering algorithm. Code-backed algorithms
retain their declared runtime/numeric semantics, including floating point where
appropriate; cross-profile conversions must be explicit and validated.

Still to probe: decimal rounding/scale, negative zero and formatting, boundary
ranges, unit conversion policy, and literal-text import in both actual clients.
No automatic conversion from the earlier probe-js-finite-double document is
approved. The initial browser probe remains a reference demonstration only.

## Binding and evaluation boundary

Keep deployment-specific connection IDs/revisions and explicit logical-asset
mapping in a separately authorized binding snapshot. No credentials or admin
settings belong in the authoring document. A matching external ID across sources
does not establish asset identity. Bind and authorize every dependency separately.

A governed coordinator must acquire data and invoke calculation capabilities;
modules do not receive credentials. Current production support does not provide
this general intermediate-call contract. Specify bounded model-artifact identity,
size/hash, input/output schemas, timeout, cancellation, side effects and ambiguous
completion before implementing that boundary. The synthetic coordinator proves
only its own dispatch preconditions, not protected acquisition or WAMI support.

## Evidence and build states

Static validation, optional test execution and verified package state are
independent. Building a valid document does not imply that it was tested or
approved. Test reports bind document/package identity, binding snapshot, fixture
or observation provenance and evaluation profile to outputs, selected branches
and explicit instrumentation. Missing evidence is not a valid zero.

An edit invalidates affected evidence and its build association. Importing a
report does not confer fresh authority or resurrect approval. Download and
catalog submission must use the same immutable verified artifact, with fresh
caller authority and separate existing approval. Those endpoints remain unproven.

## Next contract probe

Use the same versioned decimal-literal document through an actual console client
and browser import/export, then one authoritative .NET evaluator. Compare typed
outputs and rejection codes for nondefault and decimal-sensitive inputs. Include
an opaque structured-card step without pretending to infer its internal graph.
Only after that contract review should feature editor/compiler work begin.
Restricted hosting and actual-authority preview/delivery remain independent gates.

## Candidate conformance evidence

The decimal_conformance.cjs probe now transports one candidate decimal document
through a minimal real-browser text editor and download, a console harness and
the same .NET evaluator, then renders returned decimal text in the browser. All
13 local checks passed, including a nondefault 28-digit literal, exact ratio and
difference, complete document preservation and matching rejection codes.
Division by zero, overflow, numeric JSON instead of decimal text, unit mismatch
and dependency cycles are rejected. The browser never evaluates the decimal
algorithm itself. Evidence: evidence/decimal-conformance-linux.json.

This is a two-operation disposable evaluator and minimal browser harness, not
integration with the existing product CLI/React workspace or a general compiler.
CI runs the console/.NET subset on Windows and Linux; full browser evidence is
local. Restricted hosting, actual-authority preview/delivery, unit conversion,
rounding/scale policy and opaque SDK-step handoff remain independent gates.
