"""Disposable trusted-template probe; does not establish untrusted-build isolation."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET
import zipfile


REPO = Path(__file__).resolve().parents[3]
KIT = REPO / "modules/authoring"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    report = {
        "probe": "trusted-dotnet-starter-build-package",
        "checks": [],
        "notEstablished": [
            "Windows restricted-account/process/network/resource isolation",
            "Offline restore on a clean customer installation",
            "Authenticated web download or catalog submission",
            "Live ForeSite acquisition or provider writes",
            "Execution of newly built binaries or reviewed entry-point identity",
        ],
    }

    def run(label, command, expected=0):
        if command[:2] in (["dotnet", "build"], ["dotnet", "publish"]):
            command = [*command, "--disable-build-servers", "-m:1"]
        started = time.monotonic()
        result = subprocess.run(command, cwd=REPO, capture_output=True,
                                text=True, timeout=180, check=False)
        ok = result.returncode == expected if expected == 0 else result.returncode != 0
        report["checks"].append({"name": label, "passed": ok,
                                 "exitCode": result.returncode,
                                 "seconds": round(time.monotonic() - started, 2)})
        if not ok:
            raise RuntimeError(label + " failed:\n" + (result.stdout + result.stderr)[-6000:])
        return result

    artifacts = KIT / "artifacts"
    artifacts.mkdir(exist_ok=True)
    try:
        report["dotnetSdk"] = run("SDK available", ["dotnet", "--version"]).stdout.strip()
        report["baseline"] = run("Repository baseline", ["git", "rev-parse", "HEAD"]).stdout.strip()
        run("Build existing CLI", ["dotnet", "build", "tools/WellSiteAutoPilot.Cli",
                                   "--configuration", "Release", "--nologo"])
        cli = REPO / "tools/WellSiteAutoPilot.Cli/bin/Release/net10.0/WellSiteAutoPilot.Cli.dll"
        with tempfile.TemporaryDirectory(prefix="authoring-probe-", dir=artifacts) as directory:
            stage = Path(directory)
            project = stage / "WellInputCheck"
            shutil.copytree(KIT / "dotnet/WellInputCheck", project,
                            ignore=shutil.ignore_patterns("bin", "obj"))
            csproj = project / "WellInputCheck.csproj"
            tree = ET.parse(csproj)
            tree.find(".//ProjectReference").set("Include", str(
                REPO / "src/modules/WellSiteAutoPilot.ModuleSdk/WellSiteAutoPilot.ModuleSdk.csproj"))
            tree.write(csproj, encoding="unicode")
            payload = stage / "payload"
            run("Compile copied trusted starter", ["dotnet", "publish", str(csproj),
                "--configuration", "Release", "--output", str(payload), "--nologo"])
            assert (payload / "WellInputCheck.dll").is_file(), "Missing compiled module"
            manifest = KIT / "dotnet/manifest.json"
            metadata = json.loads(manifest.read_text(encoding="utf-8"))
            call = ["dotnet", str(cli), "module"]
            run("Validate manifest", call + ["validate", str(manifest)])
            first, second = stage / "first.wsamodule", stage / "second.wsamodule"
            for label, target in [("Package compiled payload", first), ("Repeat identical package", second)]:
                run(label, call + ["pack", str(manifest), str(payload), str(target)])
            assert first.read_bytes() == second.read_bytes(), "Packing is not deterministic"
            with zipfile.ZipFile(first) as archive:
                assert json.loads(archive.read("manifest.json")) == metadata
                expected = {"manifest.json"}
                for file in payload.rglob("*"):
                    if file.is_file():
                        name = "payload/" + file.relative_to(payload).as_posix()
                        expected.add(name)
                        assert archive.read(name) == file.read_bytes(), name
                assert set(archive.namelist()) == expected
            digest = hashlib.sha256(first.read_bytes()).hexdigest()
            report["artifact"] = {"sha256": digest, "bytes": first.stat().st_size,
                                  "files": sorted(expected), "moduleId": metadata["moduleId"],
                                  "version": metadata["version"]}
            run("Verify compiled archive", call + ["verify", str(first)])
            preflight = [str(first), metadata["moduleId"], metadata["version"], digest, "DotNet", "1.2.0"]
            run("Preflight current host contract", call + ["preflight", *preflight])
            mismatch = preflight.copy()
            mismatch[3] = "0" * 64
            rejected = run("Reject mismatched artifact hash", call + ["preflight", *mismatch], expected=2)
            assert "MODULE_PACKAGE_HASH_MISMATCH" in rejected.stderr
            run("Rebuild with warmed restore cache", ["dotnet", "publish", str(csproj),
                "--configuration", "Release", "--output", str(payload), "--no-restore", "--nologo"])
            with (project / "EntryPoint.cs").open("a", encoding="utf-8") as file:
                file.write("\n#error AUTHORING_PROBE_EXPECTED_COMPILER_FAILURE\n")
            failed = run("Surface deliberate compiler failure", ["dotnet", "publish", str(csproj),
                "--configuration", "Release", "--output", str(stage / "failed-payload"),
                "--no-restore", "--nologo"], expected=1)
            assert "AUTHORING_PROBE_EXPECTED_COMPILER_FAILURE" in failed.stdout + failed.stderr
            report["checks"].append({"name": "Archive byte round trip and deterministic packing", "passed": True})
        report["temporaryWorkspaceCleaned"] = not stage.exists()
        report["status"] = "passed"
    except Exception as error:
        report["status"] = "failed"
        report["error"] = str(error)
        raise
    finally:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"status": report["status"], "report": str(args.report)}))


if __name__ == "__main__":
    main()
