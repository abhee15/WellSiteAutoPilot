"""Run the trusted starter locally; this is not a package loader or a sandbox."""
from __future__ import annotations

import json
from pathlib import Path
import sys

from well_input_check import SUCCESS, execute


def run(path: Path) -> tuple[dict, int]:
    if path.stat().st_size > 262144:
        raise ValueError("Fixture exceeds 256 KiB")
    envelope = json.loads(path.read_text(encoding="utf-8"))
    if type(envelope.get("contractVersion")) is not int or envelope["contractVersion"] != 1:
        raise ValueError("Only contractVersion 1 is supported")
    context = envelope["context"]
    result = execute(context)
    return {"contractVersion": 1, "mode": "OfflineShadow", "context": context, "result": result}, (
        0 if result["outcomeCode"] == SUCCESS else 2)


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: python run_offline.py <fixture.json>", file=sys.stderr)
        return 2
    try:
        report, code = run(Path(sys.argv[1]))
        print(json.dumps(report, indent=2, allow_nan=False))
        return code
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        print(f"Invalid offline fixture: {type(error).__name__}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
