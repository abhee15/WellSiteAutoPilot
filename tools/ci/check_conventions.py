#!/usr/bin/env python3
"""Small, dependency-free checks of WellSite AutoPilot's architectural boundaries."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

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
