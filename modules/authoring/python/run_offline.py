"""Run the trusted starter locally; this is not a package loader or a sandbox."""
from __future__ import annotations

import json
from pathlib import Path
import sys

from datetime import timedelta
from well_input_check import execute, _time



def evaluate(context: dict, manifest: dict) -> dict:
    """Admit trusted offline results using the reviewed manifest, never algorithm constants."""
    result = execute(context)
    if result["failureCode"] is not None:
        return result
    try:
        if context["moduleIdentity"] != {"moduleId": manifest["moduleId"], "version": manifest["version"]}:
            raise ValueError("Manifest identity differs from the context")
        now = _time(context["evaluationTimeUtc"])
        for item in context["inputs"]:
            requirement, = [r for r in manifest["dataRequirements"] if r["id"] == item["requirementId"]]
            age = requirement["maximumAgeSeconds"]
            if age is not None and (type(age) is not int or age <= 0):
                raise ValueError("Invalid maximum age")
            if type(requirement["allowUncertainQuality"]) is not bool:
                raise ValueError("Invalid quality policy")
            code = None
            if requirement["access"] != "Current":
                code = "EXECUTION_INPUT_ACCESS_UNSUPPORTED"
            elif item["quantity"].lower() != requirement["quantity"].lower() or (
                requirement["canonicalUnit"] is not None and item["unit"].lower() != requirement["canonicalUnit"].lower()):
                code = "EXECUTION_INPUT_QUANTITY_MISMATCH"
            elif item["quality"].lower() != "good" and not (
                requirement["allowUncertainQuality"] and item["quality"].lower() == "uncertain"):
                code = "EXECUTION_INPUT_QUALITY_REJECTED"
            elif _time(item["timestampUtc"]) > now + timedelta(seconds=30):
                code = "EXECUTION_INPUT_FUTURE"
            elif age is not None and now - _time(item["timestampUtc"]) > timedelta(seconds=age):
                code = "EXECUTION_INPUT_STALE"
            if code:
                return {"outcomeCode": code, "failureCode": code, "engineeringResults": [],
                        "recommendations": [], "controlIntents": [], "stateUpdates": {}}
        return result
    except (KeyError, TypeError, ValueError, AttributeError, OverflowError):
        return {"outcomeCode": "CONTEXT_INVALID", "failureCode": "CONTEXT_INVALID", "engineeringResults": [],
                "recommendations": [], "controlIntents": [], "stateUpdates": {}}

def run(path: Path, manifest_path: Path | None = None) -> tuple[dict, int]:
    if path.stat().st_size > 262144:
        raise ValueError("Fixture exceeds 256 KiB")
    envelope = json.loads(path.read_text(encoding="utf-8"))
    if type(envelope.get("contractVersion")) is not int or envelope["contractVersion"] != 1:
        raise ValueError("Only contractVersion 1 is supported")
    context = envelope["context"]
    result = evaluate(context, json.loads((manifest_path or Path(__file__).with_name("manifest.json")).read_text(encoding="utf-8")))
    return {"contractVersion": 1, "mode": "OfflineShadow", "context": context, "result": result}, (
        0 if result["failureCode"] is None else 2)


def main() -> int:
    if len(sys.argv) not in (2, 3):
        print("Usage: python run_offline.py <fixture.json> [reviewed-manifest.json]", file=sys.stderr)
        return 2
    try:
        report, code = run(Path(sys.argv[1]), Path(sys.argv[2]) if len(sys.argv) == 3 else None)
        print(json.dumps(report, indent=2, allow_nan=False))
        return code
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        print(f"Invalid offline fixture: {type(error).__name__}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
