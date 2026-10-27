"""Contract examples and cross-language CLI parity; no ForeSite server needed."""
from __future__ import annotations

import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "python"))
from run_offline import evaluate
from well_input_check import execute, SUCCESS  # noqa: E402

GOOD = json.loads((ROOT / "fixtures/python-good.json").read_text())
MANIFEST = json.loads((ROOT / "python/manifest.json").read_text())


def cases():
    yield "good", copy.deepcopy(GOOD), SUCCESS
    changes = [
        ("missing-input", "inputs", [], "INPUT_BINDING_INVALID"),
        ("missing-asset", "assets", [], "ASSET_BINDING_INVALID"),
        ("invalid-parameters", "parametersJson", "[]", "CONTEXT_INVALID"),
        ("wrong-module", "moduleIdentity", {"moduleId": "other", "version": "1.0.0"}, "MODULE_IDENTITY_MISMATCH"),
    ]
    for name, key, value, code in changes:
        envelope = copy.deepcopy(GOOD)
        envelope["context"][key] = value
        yield name, envelope, code
    input_changes = [
        ("wrong-unit", "unit", "fraction", "EXECUTION_INPUT_QUANTITY_MISMATCH"),
        ("wrong-quantity", "quantity", "Load", "EXECUTION_INPUT_QUANTITY_MISMATCH"),
        ("bad-quality", "quality", "Bad", "EXECUTION_INPUT_QUALITY_REJECTED"),
        ("uncertain-quality", "quality", "Uncertain", "EXECUTION_INPUT_QUALITY_REJECTED"),
        ("stale", "timestampUtc", "2026-10-22T11:58:59Z", "EXECUTION_INPUT_STALE"),
        ("age-boundary", "timestampUtc", "2026-10-22T11:59:00Z", SUCCESS),
        ("future", "timestampUtc", "2026-10-22T12:00:31Z", "EXECUTION_INPUT_FUTURE"),
        ("future-boundary", "timestampUtc", "2026-10-22T12:00:30Z", SUCCESS),
        ("lower-boundary", "value", 0, SUCCESS),
        ("upper-boundary", "value", 100, SUCCESS),
        ("negative", "value", -0.1, "INPUT_VALUE_INVALID"),
        ("above-limit", "value", 100.1, "INPUT_VALUE_INVALID"),
        ("unknown-source", "source", "", "INPUT_SOURCE_MISSING"),
        ("wrong-well", "assetId", "33333333-3333-4333-8333-333333333333", "INPUT_BINDING_INVALID"),
        ("wrong-requirement", "requirementId", "other", "INPUT_BINDING_INVALID"),
    ]
    for name, key, value, code in input_changes:
        envelope = copy.deepcopy(GOOD)
        envelope["context"]["inputs"][0][key] = value
        yield name, envelope, code


class AuthoringTests(unittest.TestCase):
    def test_python_contract_cases(self):
        for name, envelope, expected in cases():
            with self.subTest(name=name):
                original = copy.deepcopy(envelope)
                result = evaluate(envelope["context"], MANIFEST)
                self.assertEqual(expected, result["outcomeCode"])
                self.assertEqual(None if expected == SUCCESS else expected, result["failureCode"])
                self.assertEqual(original, envelope, "Calculation mutated its context")
                self.assertEqual([], result["controlIntents"])
                self.assertEqual([], result["recommendations"])
                self.assertEqual({}, result["stateUpdates"])
                self.assertEqual(1 if expected == SUCCESS else 0, len(result["engineeringResults"]))

    def run_cli(self, runtime, path, manifest=None):
        if runtime == "python":
            command = [sys.executable, str(ROOT / "python/run_offline.py"), str(path)]
        else:
            command = ["dotnet", str(ROOT / "dotnet/OfflineRunner/bin/Release/net10.0/OfflineRunner.dll"), str(path)]
        if manifest is not None:
            command.append(str(manifest))
        return subprocess.run(command, capture_output=True, text=True, timeout=20, check=False)

    def runtimes(self):
        return ("python", "dotnet") if os.environ.get("WSA_TEST_DOTNET") == "1" else ("python",)

    def test_cli_outcomes_and_provenance(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.json"
            for name, envelope, expected in cases():
                results = []
                for runtime in self.runtimes():
                    with self.subTest(name=name, runtime=runtime):
                        fixture = copy.deepcopy(envelope)
                        if name != "wrong-module":
                            fixture["context"]["moduleIdentity"]["moduleId"] = "weatherford.foresite-well-input-check." + runtime
                        path.write_text(json.dumps(fixture), encoding="utf-8")
                        process = self.run_cli(runtime, path)
                        self.assertEqual(0 if expected == SUCCESS else 2, process.returncode, process.stderr)
                        report = json.loads(process.stdout)
                        self.assertEqual(fixture["context"], report["context"])
                        self.assertEqual("OfflineShadow", report["mode"])
                        self.assertEqual(expected, report["result"]["outcomeCode"])
                        self.assertEqual(None if expected == SUCCESS else expected, report["result"]["failureCode"])
                        results.append(report["result"])
                if len(results) == 2:
                    self.assertEqual(*results)

    def test_malformed_fixtures_fail_closed(self):
        variants = []
        fixture = copy.deepcopy(GOOD)
        fixture["contractVersion"] = 2
        variants.append(json.dumps(fixture))
        for key in ("value", "timestampUtc", "quality", "source"):
            fixture = copy.deepcopy(GOOD)
            del fixture["context"]["inputs"][0][key]
            variants.append(json.dumps(fixture))
        for value in (True, None, "72.5", float("nan"), float("inf")):
            fixture = copy.deepcopy(GOOD)
            fixture["context"]["inputs"][0]["value"] = value
            variants.append(json.dumps(fixture))
        variants.extend(["{", "null", "{}", "[]", "x" * 262145])
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "invalid.json"
            for index, text in enumerate(variants):
                for runtime in self.runtimes():
                    with self.subTest(index=index, runtime=runtime):
                        # Identity is runtime-specific, including negative fixtures.
                        path.write_text(text.replace(".python", "." + runtime), encoding="utf-8")
                        process = self.run_cli(runtime, path)
                        self.assertEqual(2, process.returncode, process.stderr)
                        self.assertNotIn("Traceback", process.stderr)

    @unittest.skipUnless(os.environ.get("WSA_TEST_DOTNET") == "1", "Set WSA_TEST_DOTNET=1 after building .NET runner")
    def test_dotnet_parity_is_enabled(self):
        self.assertTrue((ROOT / "dotnet/OfflineRunner/bin/Release/net10.0/OfflineRunner.dll").is_file())


    def test_reviewed_policy_changes_without_changing_algorithm(self):
        # Both hosts accept 90-second evidence under a reviewed 120-second policy
        # and reject it under the original 60-second manifest.
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.json"
            manifest_path = Path(directory) / "manifest.json"
            for runtime in self.runtimes():
                for maximum_age, quality, allow_uncertain, expected in [
                    (120, "Good", False, SUCCESS),
                    (60, "Good", False, "EXECUTION_INPUT_STALE"),
                    (120, "Uncertain", True, SUCCESS),
                    (120, "Uncertain", False, "EXECUTION_INPUT_QUALITY_REJECTED"),
                    (120, "Unknown", True, "EXECUTION_INPUT_QUALITY_REJECTED"),
                ]:
                    with self.subTest(runtime=runtime, maximum_age=maximum_age, quality=quality, allow_uncertain=allow_uncertain):
                        fixture = copy.deepcopy(GOOD)
                        fixture["context"]["moduleIdentity"]["moduleId"] = "weatherford.foresite-well-input-check." + runtime
                        fixture["context"]["inputs"][0].update(timestampUtc="2026-10-22T11:58:30Z", quality=quality)
                        manifest = json.loads((ROOT / runtime / "manifest.json").read_text())
                        manifest["dataRequirements"][0].update(maximumAgeSeconds=maximum_age, allowUncertainQuality=allow_uncertain)
                        path.write_text(json.dumps(fixture), encoding="utf-8")
                        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                        process = self.run_cli(runtime, path, manifest_path)
                        self.assertEqual(0 if expected == SUCCESS else 2, process.returncode, process.stderr)
                        self.assertEqual(expected, json.loads(process.stdout)["result"]["outcomeCode"])

    def test_algorithm_does_not_override_reviewed_age_or_quality(self):
        fixture = copy.deepcopy(GOOD)
        fixture["context"]["inputs"][0].update(timestampUtc="2026-10-22T11:58:30Z", quality="Uncertain")
        self.assertEqual(SUCCESS, execute(fixture["context"])["outcomeCode"])

    def test_manifest_agreement(self):
        for runtime in ("python", "dotnet"):
            manifest = json.loads((ROOT / runtime / "manifest.json").read_text())
            fixture = json.loads((ROOT / "fixtures" / f"{runtime}-good.json").read_text())
            self.assertEqual(manifest["moduleId"], fixture["context"]["moduleIdentity"]["moduleId"])
            self.assertEqual(manifest["version"], fixture["context"]["moduleIdentity"]["version"])
            self.assertEqual("foresite", manifest["requiredAdapterKey"])
            self.assertEqual([], manifest["commandRequirements"])
            self.assertEqual("1.1.0", manifest["minimumSdkVersion"])
            self.assertEqual(60, manifest["dataRequirements"][0]["maximumAgeSeconds"])


if __name__ == "__main__":
    unittest.main()
