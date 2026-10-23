# ForeSite well input check authoring kit

This first slice supplies equivalent .NET and Python calculations, fixtures and
local test hosts. It is ready for **offline authoring**, not live ForeSite
execution or arbitrary package installation. Author in your usual editor; no
new authoring application is needed.

The sample observes one numeric pump-fillage input and returns that value. It
does not optimize the well, call ForeSite, save optimization data or send
setpoints. PumpFillage is the existing SDK sample quantity, not a verified
ForeSite API field. The fixture is synthetic and its source says so.

## Quick start (from the repository root)

Use Python 3.12 (the kit has no third-party Python dependencies):

```sh
python modules/authoring/python/run_offline.py modules/authoring/fixtures/python-good.json
python -m unittest discover -s modules/authoring/tests -p 'test_*.py' -v
```

Use the repository's .NET 10 SDK:

```sh
dotnet run --project modules/authoring/dotnet/OfflineRunner -- modules/authoring/fixtures/dotnet-good.json
```

Both commands write a JSON report to stdout and return 0 on success, 2 on an
invalid fixture or failed observation. Redirect stdout to a local
`example.report.json` to retain the result, source timestamp, quality, units,
correlation and execution identity. Diagnostics for malformed files go to stderr.
No wall clock is read: evaluation time comes from the fixture, so recorded tests
remain repeatable. Changing an old fixture's evaluation time does not make its
telemetry fresh.

The cross-runtime test job builds the .NET host and runs the same successful and
failing inputs on Linux and Windows. To run it locally after a Release build:

```powershell
dotnet build modules/authoring/dotnet/OfflineRunner --configuration Release
$env:WSA_TEST_DOTNET = '1'
python -m unittest discover -s modules/authoring/tests -p 'test_*.py' -v
```

On a POSIX shell, use `WSA_TEST_DOTNET=1 python -m unittest discover -s
modules/authoring/tests -p 'test_*.py' -v` instead of the last two lines.

## Authoring convention

Start from `python/well_input_check.py` or `dotnet/WellInputCheck/EntryPoint.cs`.
The .NET module implements the existing `ILogicModuleV1` directly. Python uses
`execute(context: dict) -> dict`; keys follow the camel-case serialization of
the same SDK records. [contract-v1.md](contract-v1.md) specifies this starter
wire profile. The runners invoke the exact example entry points; they do not
rewrite the algorithm for testing.

1. Choose a new stable module ID and version in the implementation, manifest
   and fixtures. Python and .NET variants use separate IDs to avoid colliding
   immutable packages. Change version when package bytes change.
2. Declare roles, data requirements, parameters and provider restriction in the
   manifest. Keep vendor URLs, Windows credentials and API calls out of code.
3. Implement calculation over supplied inputs and return SDK results. Any
   future writes must be returned as intents for platform policy/dispatch.
4. Add recorded inputs and expected outcomes, including failure and boundary
   cases. Do not use customer data or server addresses in committed fixtures.
5. Validate and package with the existing CLI. Offline success and a checksum
   do not grant trust, deployment compatibility or execution permission.

The examples declare `requiredAdapterKey: foresite`, a neutral `well` role and
one `pump-fillage` requirement. They do **not** claim that all ForeSite or Lufkin
SAM wells expose this measurement. Controller restrictions and live measurement
availability need assignment-time evidence; do not invent traits to satisfy them.

## Validate and package

The CLI is source-run here; an installed `wsa` executable accepts the same
arguments. The existing product validator remains authoritative:

```sh
dotnet run --project tools/WellSiteAutoPilot.Cli -- module validate modules/authoring/dotnet/manifest.json
dotnet run --project tools/WellSiteAutoPilot.Cli -- module validate modules/authoring/python/manifest.json
dotnet publish modules/authoring/dotnet/WellInputCheck --configuration Release --output modules/authoring/artifacts/dotnet-payload
dotnet run --project tools/WellSiteAutoPilot.Cli -- module pack modules/authoring/dotnet/manifest.json modules/authoring/artifacts/dotnet-payload modules/authoring/artifacts/well-input-check-dotnet.wsamodule
```

For Python, make a clean payload directory containing **only**
`well_input_check.py`, then pass that directory to `module pack` with
`python/manifest.json`. The offline host, fixtures, caches and local settings are
not deployment payload. Verify either archive with `module verify <package>`.
Use approved pinned offline dependency bundles when dependencies are introduced;
the sample requires none. No worker should install packages from the Internet
at execution time.

The .NET production resolver currently contains only the built-in Shadow sample;
Python's production worker is a scaffold. Catalog import/approval does not load
these new packages into either worker. This kit does not change those facts.
The Python `module:function` entry-point syntax is a versioned kit convention
to implement in the future host, not an assertion that the current worker loads it.

## Next live slice

See [the implementation plan](../../docs/design/foresite-authoring-kit-implementation.md).
AutoPilot must resolve the selected local Asset through its saved SourceBinding
and Connection to ForeSite's well ID. The module does not search by name or
receive credentials. A future authenticated, scope-checked input-preview API
collects inputs through the existing adapter. The offline calculation remains
unchanged when those recorded inputs are replaced by normalized live inputs.

The first live acceptance is one explicitly selected well, one verified input,
manual Shadow execution and persisted observations/results with zero provider
writes. Views help select wells; assignment freezes the reviewed set. PowerShell
is a future worker option. CygNet remains a separate workstream.
