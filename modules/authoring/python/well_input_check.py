"""ForeSite starter calculation. Provider I/O belongs to AutoPilot, not this file."""
from __future__ import annotations

from datetime import datetime, timedelta, timezone
import math
from uuid import UUID

MODULE_ID = "weatherford.foresite-well-input-check.python"
VERSION = "1.0.0"
SUCCESS = "WELL_INPUT_CHECK_COMPLETED"


def _time(value: str) -> datetime:
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.utcoffset() != timedelta(0):
        raise ValueError("UTC timestamp required")
    return result


def _guid(value: str) -> str:
    result = UUID(value)
    if result.int == 0:
        raise ValueError("Empty identity")
    return str(result)


def execute(context: dict) -> dict:
    """V1: JSON-shaped SDK context -> JSON-shaped LogicModuleExecutionResult.

    Failure codes are outcomes, never empty successful observations. The host
    also validates inputs; these checks make offline fixtures useful to authors.
    """
    def result(code: str, values: list | None = None) -> dict:
        return {"outcomeCode": code, "engineeringResults": values or [],
                "recommendations": [], "controlIntents": [], "stateUpdates": {}}

    try:
        if context["moduleIdentity"] != {"moduleId": MODULE_ID, "version": VERSION}:
            return result("MODULE_IDENTITY_MISMATCH")
        _guid(context["executionId"])
        if not isinstance(context["correlationId"], str) or not context["correlationId"].strip():
            return result("CONTEXT_INVALID")
        # SDK keeps parameter JSON as a string in both languages.
        import json
        if not isinstance(json.loads(context["parametersJson"]), dict):
            return result("CONTEXT_INVALID")
        now = _time(context["evaluationTimeUtc"])
        assets = context["assets"]
        if len(assets) != 1 or assets[0]["role"] != "well":
            return result("ASSET_BINDING_INVALID")
        asset_id = _guid(assets[0]["assetId"])
        if not isinstance(json.loads(assets[0]["parameterOverridesJson"]), dict):
            return result("CONTEXT_INVALID")
        inputs = context["inputs"]
        if len(inputs) != 1 or inputs[0]["requirementId"] != "pump-fillage":
            return result("INPUT_BINDING_INVALID")
        item = inputs[0]
        if _guid(item["assetId"]) != asset_id:
            return result("INPUT_BINDING_INVALID")
        if item["quantity"].lower() != "pumpfillage" or item["unit"].lower() != "%":
            return result("EXECUTION_INPUT_QUANTITY_MISMATCH")
        if item["quality"].lower() != "good":
            return result("EXECUTION_INPUT_QUALITY_REJECTED")
        timestamp = _time(item["timestampUtc"])
        if timestamp > now + timedelta(seconds=30):
            return result("EXECUTION_INPUT_FUTURE")
        if now - timestamp > timedelta(seconds=60):
            return result("EXECUTION_INPUT_STALE")
        value = item["value"]
        if type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 100:
            return result("INPUT_VALUE_INVALID")
        if not isinstance(item["source"], str) or not item["source"].strip():
            return result("INPUT_SOURCE_MISSING")
        return result(SUCCESS, [{"id": "observed-value", "assetId": asset_id,
                                "quantity": item["quantity"], "value": value, "unit": item["unit"]}])
    except (KeyError, TypeError, ValueError, AttributeError, OverflowError):
        return result("CONTEXT_INVALID")
