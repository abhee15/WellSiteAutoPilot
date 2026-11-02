#!/usr/bin/env python3
"""Small, dependency-free checks of WellSite AutoPilot's architectural boundaries."""
from __future__ import annotations

import argparse
import os
import re
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import check_api_authorization

GENERATED_PREFIX = "src/web/src/api/generated/"
GENERATED_ALLOWED = {"src/web/src/api/generated/README.md"}

# These projects define contracts and business semantics, not runtime internals.
FOUNDATION = {
    "src/server/WellSiteAutoPilot.Domain",
    "src/server/WellSiteAutoPilot.Api.Contracts",
    "src/modules/WellSiteAutoPilot.ModuleSdk",
    "src/integration/WellSiteAutoPilot.Integration.Contracts",
    "src/messaging/WellSiteAutoPilot.Messaging.Contracts",
    "src/platform/WellSiteAutoPilot.Failures",
}
APPLICATION = "src/server/WellSiteAutoPilot.Application"
WORKER = "src/workers/WellSiteAutoPilot.Worker.DotNet"
IMPLEMENTATION = (
    "src/persistence/",
    "src/server/WellSiteAutoPilot.Infrastructure/",
    "src/server/WellSiteAutoPilot.Server/",
    "src/integration/WellSiteAutoPilot.IntegrationGateway/",
    "src/workers/",
    "src/simulators/",
)
WORKER_FORBIDDEN = IMPLEMENTATION[:4]


def references_from(project_file: Path) -> list[str]:
    xml = ET.parse(project_file).getroot()
    return [
        node.attrib["Include"]
        for node in xml.iter()
        if node.tag.rsplit("}", 1)[-1] == "ProjectReference"
        and "Include" in node.attrib
    ]


def validate_references(root: Path) -> list[str]:
    errors: list[str] = []
    root = root.resolve()
    for project in sorted((root / "src").rglob("*.csproj")):
        source = project.parent.relative_to(root).as_posix()
        for reference in references_from(project):
            destination = (project.parent / reference.replace("\\", "/")).resolve()
            try:
                relative = destination.relative_to(root).as_posix()
            except ValueError:
                errors.append(f"{project.relative_to(root)}: ProjectReference escapes repository: {reference}")
                continue
            if not destination.is_file():
                errors.append(f"{project.relative_to(root)}: missing ProjectReference: {reference}")
                continue
            if source in FOUNDATION and relative.startswith(IMPLEMENTATION):
                errors.append(f"{project.relative_to(root)}: foundation depends on implementation {relative}")
            elif source == APPLICATION and relative.startswith(IMPLEMENTATION):
                errors.append(f"{project.relative_to(root)}: application depends on implementation {relative}")
            elif source == WORKER and relative.startswith(WORKER_FORBIDDEN):
                errors.append(f"{project.relative_to(root)}: worker bypasses integration boundary {relative}")
    return errors


# Dispatch and every next-run display must agree about a paused or re-timed logic, so occurrences are only ever
# computed from EffectiveSchedule's result, inside the scheduling folder. Anything else reads a revision's own schedule.
SCHEDULING_FOLDER = "src/server/WellSiteAutoPilot.Application/Scheduling/"
OCCURRENCE_CALLS = ("ScheduleOccurrences.NextAfter(", "ScheduleOccurrences.LatestDue(")


def validate_schedule_resolution(root: Path) -> list[str]:
    errors: list[str] = []
    root = root.resolve()
    for source in sorted((root / "src").rglob("*.cs")):
        relative = source.relative_to(root).as_posix()
        if relative.startswith(SCHEDULING_FOLDER):
            continue
        text = source.read_text(encoding="utf-8-sig")
        for call in OCCURRENCE_CALLS:
            if call in text:
                errors.append(
                    f"{relative}: {call.rstrip('(')} outside Application/Scheduling; "
                    "use EffectiveSchedule so a paused or re-timed logic is honoured")
    return errors


# A command reaches equipment only through the control gate, which claims the action, re-reads the kill switch and sends
# once. A second caller of a command sink would bypass all of that.
CONTROL_GATE = "src/server/WellSiteAutoPilot.Application/Control/AutonomousControlService.cs"


def validate_command_dispatch(root: Path) -> list[str]:
    errors: list[str] = []
    root = root.resolve()
    for source in sorted((root / "src").rglob("*.cs")):
        relative = source.relative_to(root).as_posix()
        if relative == CONTROL_GATE:
            continue
        text = source.read_text(encoding="utf-8-sig")
        if "IControlCommandSink" in text and ".DispatchAsync(" in text:
            errors.append(
                f"{relative}: calls a control command sink outside the control gate; "
                "commands must go through AutonomousControlService so they are claimed, checked and sent once")
    return errors


GATEWAY_PROGRAM = "src/integration/WellSiteAutoPilot.IntegrationGateway/Program.cs"
GATEWAY_ROUTE = re.compile(r"\bv1\.Map(?:Get|Post|Put|Patch|Delete)\(")
RETRY_HANDLERS = ("AddStandardResilienceHandler", "AddResilienceHandler", "AddStandardHedgingHandler",
                  "AddPolicyHandler", "AddTransientHttpErrorPolicy", "Polly")


def validate_gateway_routes(program: str) -> list[str]:
    """Every Gateway route needs an authorization policy, and a command route must be server-only."""
    errors: list[str] = []
    starts = [match.start() for match in GATEWAY_ROUTE.finditer(program)]
    end = program.find("app.Run()")
    boundaries = starts + [end if end > (starts[-1] if starts else -1) else len(program)]
    for begin, stop in zip(starts, boundaries[1:]):
        block = program[begin:stop]
        route = re.search(r'"(/[^"]*)"', block)
        name = route.group(1) if route else block[:40].strip()
        if ".RequireAuthorization(" not in block:
            errors.append(f"Gateway route {name} has no authorization policy")
        elif "command" in name.lower() and "ServiceCaller.ServerPolicy" not in block:
            errors.append(f"Gateway command route {name} must require ServiceCaller.ServerPolicy so a worker can never reach it")
    return errors


def validate_command_client(program: str) -> list[str]:
    """A repeated send applies a command twice, so no retry or resilience handler may sit on the command client."""
    errors: list[str] = []
    anchor = "SimulatorCommandProvider.ClientName"
    start = program.find("AddHttpClient(" + anchor)
    if start < 0:
        return ["Gateway Program.cs no longer registers the simulator command client by SimulatorCommandProvider.ClientName"]
    # The registration is one statement: it ends at the first ";" line break after the lambda's closing brace.
    body_end = program.find("}", start)
    statement = program[start:program.find(";\n", body_end) + 1]
    errors += [f"the command client must not have {handler}: a retry could send a command twice"
               for handler in RETRY_HANDLERS if handler in statement]
    if "ConfigureHttpClientDefaults" in program and any(handler in program for handler in RETRY_HANDLERS):
        errors.append("the Gateway must not add retry handlers to every HTTP client: the command client must never retry")
    return errors


def validate_changed_paths(changed: list[str]) -> list[str]:
    return [
        f"generated API client edited: {path} (regenerate from OpenAPI instead)"
        for original in changed
        if (path := original.replace("\\", "/")).startswith(GENERATED_PREFIX)
        and path not in GENERATED_ALLOWED
    ]


def changed_paths(root: Path, base_sha: str) -> list[str]:
    result = subprocess.run(
        ["git", "diff", "--name-only", "--diff-filter=ACMRT", "-z", f"{base_sha}...HEAD"],
        cwd=root, check=True, capture_output=True,
    )
    return [entry.decode("utf-8") for entry in result.stdout.split(b"\0") if entry]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--base-sha", default=os.environ.get("WSA_PR_BASE_SHA", ""))
    args = parser.parse_args()
    root = args.repo.resolve()
    try:
        errors = validate_references(root)
        errors += validate_schedule_resolution(root)
        errors += validate_command_dispatch(root)
        gateway = (root / GATEWAY_PROGRAM).read_text(encoding="utf-8-sig")
        errors += validate_gateway_routes(gateway)
        errors += validate_command_client(gateway)
        # Fail the existing CI conventions job when a protected API changes its
        # reviewed route name or legacy authorization metadata. This remains a
        # static tripwire, not scoped-grant enforcement or cutover approval.
        program = root / "src/server/WellSiteAutoPilot.Server/Program.cs"
        errors += check_api_authorization.validate_routes(
            program.read_text(encoding="utf-8")
        )
        if args.base_sha:
            errors += validate_changed_paths(changed_paths(root, args.base_sha))
    except (OSError, ET.ParseError, subprocess.CalledProcessError) as error:
        print(f"ERROR: convention verification could not complete: {error}", file=sys.stderr)
        return 2
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    if errors:
        print(f"Convention verification failed: {len(errors)} violation(s).", file=sys.stderr)
        return 1
    print("Engineering conventions passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
