"""Exercise the shipped CLI and real starter payloads without loading module code."""
from __future__ import annotations

import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = ROOT.parents[1]
CLI = REPOSITORY / "tools/WellSiteAutoPilot.Cli/bin/Release/net10.0/WellSiteAutoPilot.Cli.dll"


@unittest.skipUnless(os.environ.get("WSA_TEST_PACKAGES") == "1", "Set WSA_TEST_PACKAGES=1 after building the CLI and publishing the .NET template")
class PackageTests(unittest.TestCase):
    def cli(self, *arguments, expected=0):
        result = subprocess.run(
            ["dotnet", str(CLI), "module", *map(str, arguments)],
            capture_output=True, text=True, timeout=30, check=False,
        )
        self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
        return result

    def test_starter_packages_round_trip_deterministically(self):
        for runtime in ("python", "dotnet"):
            with self.subTest(runtime=runtime), tempfile.TemporaryDirectory() as directory:
                staging = Path(directory)
                payload = staging / "payload"
                if runtime == "python":
                    payload.mkdir()
                    (payload / "well_input_check.py").write_bytes((ROOT / "python/well_input_check.py").read_bytes())
                else:
                    payload = ROOT / "artifacts/dotnet-payload"
                    self.assertTrue((payload / "WellInputCheck.dll").is_file(), "Publish the .NET template before testing packages")
                manifest = ROOT / runtime / "manifest.json"
                first, second = staging / "first.wsamodule", staging / "second.wsamodule"
                self.cli("validate", manifest)
                self.cli("pack", manifest, payload, first)
                self.cli("pack", manifest, payload, second)
                self.assertEqual(first.read_bytes(), second.read_bytes())
                self.cli("verify", first)
                with zipfile.ZipFile(first) as archive:
                    self.assertEqual(json.loads(manifest.read_text()), json.loads(archive.read("manifest.json")))
                    expected_files = {"manifest.json"}
                    for file in payload.rglob("*"):
                        if file.is_file():
                            name = "payload/" + file.relative_to(payload).as_posix()
                            expected_files.add(name)
                            self.assertEqual(file.read_bytes(), archive.read(name))
                    self.assertEqual(expected_files, set(archive.namelist()))

    def test_host_preflight_checks_identity_hash_runtime_and_sdk(self):
        with tempfile.TemporaryDirectory() as directory:
            staging = Path(directory)
            payload = staging / "payload"
            payload.mkdir()
            (payload / "well_input_check.py").write_bytes((ROOT / "python/well_input_check.py").read_bytes())
            manifest = json.loads((ROOT / "python/manifest.json").read_text())
            package = staging / "sample.wsamodule"
            self.cli("pack", ROOT / "python/manifest.json", payload, package)
            digest = hashlib.sha256(package.read_bytes()).hexdigest()
            expected = [manifest["moduleId"], manifest["version"], digest, "Python", "1.1.0"]
            self.cli("preflight", package, *expected)
            self.cli("preflight", package, *expected[:-1], "1.1.0+host.build")
            for index, value, code in [
                (0, "other", "MODULE_PACKAGE_IDENTITY_MISMATCH"),
                (1, "9.0.0", "MODULE_PACKAGE_IDENTITY_MISMATCH"),
                (2, "0" * 64, "MODULE_PACKAGE_HASH_MISMATCH"),
                (2, "bad", "MODULE_PACKAGE_EXPECTATION_INVALID"),
                (3, "DotNet", "MODULE_RUNTIME_INCOMPATIBLE"),
                (4, "1.0.0", "MODULE_SDK_INCOMPATIBLE"),
                (4, "1.1.0-rc.1", "MODULE_SDK_INCOMPATIBLE"),
                (4, "invalid", "MODULE_HOST_SDK_VERSION_INVALID"),
            ]:
                with self.subTest(code=code, value=value):
                    arguments = expected.copy()
                    arguments[index] = value
                    result = self.cli("preflight", package, *arguments, expected=2)
                    self.assertIn(code, result.stderr)
                    self.assertNotIn("Valid Logic Module", result.stdout)
            for runtime in ("Unknown", "0"):
                result = self.cli("preflight", package, *expected[:3], runtime, "1.1.0", expected=2)
                self.assertIn("Host runtime must be DotNet or Python", result.stderr)
            self.assertEqual({"payload", "sample.wsamodule"}, {path.name for path in staging.iterdir()}, "Preflight extracted package content")

    def test_malformed_manifests_fail_as_controlled_errors(self):
        original = json.loads((ROOT / "python/manifest.json").read_text())
        variants = [{}, None]
        for key in ("runtime", "entryPoint", "assetRequirements", "dataRequirements", "commandRequirements"):
            manifest = copy.deepcopy(original)
            del manifest[key]
            variants.append(manifest)
        for key, value in (("assetRequirements", [None]), ("dataRequirements", [None]),
                           ("commandRequirements", [None]), ("runtime", 0),
                           ("minimumSdkVersion", "1.1.0-01")):
            manifest = copy.deepcopy(original)
            manifest[key] = value
            variants.append(manifest)
        with tempfile.TemporaryDirectory() as directory:
            staging = Path(directory)
            path = staging / "manifest.json"
            package = staging / "malformed.wsamodule"
            for manifest in variants:
                with self.subTest(manifest=manifest):
                    path.write_text(json.dumps(manifest), encoding="utf-8")
                    result = self.cli("validate", path, expected=2)
                    self.assertNotIn("Unhandled exception", result.stderr)
                    with zipfile.ZipFile(package, "w") as archive:
                        archive.writestr("manifest.json", json.dumps(manifest))
                        archive.writestr("payload/code.py", "raise Exception('must never execute')")
                    result = self.cli("verify", package, expected=2)
                    self.assertNotIn("Unhandled exception", result.stderr)

    def test_schema_rules_match_catalog_for_every_command(self):
        original = json.loads((ROOT / "python/manifest.json").read_text())
        schemas = [
            ([], False),
            ({"type": "object", "$ref": "https://example.invalid/schema"}, False),
            ({"type": "object", "properties": {"target": {"type": "number", "minimum": 0}}, "additionalProperties": False}, True),
            (None, True),
        ]
        with tempfile.TemporaryDirectory() as directory:
            staging = Path(directory)
            payload = staging / "payload"
            payload.mkdir()
            (payload / "well_input_check.py").write_bytes((ROOT / "python/well_input_check.py").read_bytes())
            for schema, valid in schemas:
                with self.subTest(schema=schema):
                    manifest = copy.deepcopy(original)
                    manifest["parameterSchema"] = schema
                    manifest_path = staging / "manifest.json"
                    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                    expected = 0 if valid else 2
                    validation = self.cli("validate", manifest_path, expected=expected)
                    packed = staging / "packed.wsamodule"
                    # A rejected pack must leave an existing artifact untouched.
                    packed.write_bytes(b"previous package")
                    packing = self.cli("pack", manifest_path, payload, packed, expected=expected)
                    # Build directly to prove verify cannot rely on pack having checked it.
                    external = staging / "external.wsamodule"
                    with zipfile.ZipFile(external, "w") as archive:
                        archive.writestr("manifest.json", json.dumps(manifest))
                        archive.write(payload / "well_input_check.py", "payload/well_input_check.py")
                    verification = self.cli("verify", external, expected=expected)
                    if not valid:
                        self.assertEqual(b"previous package", packed.read_bytes())
                        self.assertFalse((staging / "packed.wsamodule.tmp").exists())
                        for result in (validation, packing, verification):
                            self.assertIn("MODULE_PARAMETER_SCHEMA_INVALID", result.stderr)
                            self.assertNotIn("Valid Logic Module", result.stdout)


if __name__ == "__main__":
    unittest.main()
