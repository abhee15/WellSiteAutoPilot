"""Build the existing dependency-free starter with empty NuGet cache and cleared feeds."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

REPO = Path(__file__).resolve().parents[3]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    report = {'probe': 'empty-nuget-cache-existing-starter', 'status': 'failed', 'checks': [],
              'notEstablished': ['Installing the .NET SDK on a clean customer machine',
                                 'Offline CLI/Application dependencies such as NJsonSchema',
                                 'Customer package/analyzer dependencies', 'Network-denial sandbox policy']}
    try:
        with tempfile.TemporaryDirectory(prefix='authoring-empty-cache-') as directory:
            root = Path(directory)
            for name in ['global.json', 'Directory.Build.props', 'Directory.Packages.props']:
                shutil.copy2(REPO / name, root / name)
            for relative in ['src/modules/WellSiteAutoPilot.ModuleSdk', 'modules/authoring/dotnet/WellInputCheck']:
                shutil.copytree(REPO / relative, root / relative, ignore=shutil.ignore_patterns('bin', 'obj'))
            config = root / 'NuGet.Config'
            config.write_text('<configuration><packageSources><clear /></packageSources></configuration>', encoding='utf-8')
            cache = root / 'empty-packages'
            cache.mkdir()
            project = root / 'modules/authoring/dotnet/WellInputCheck'
            env = {**os.environ, 'NUGET_PACKAGES': str(cache), 'DOTNET_CLI_TELEMETRY_OPTOUT': '1'}
            for label, command in [
                ('Restore with empty cache and no package feeds', ['dotnet', 'restore', str(project), '--configfile', str(config), '--force', '--no-cache', '--disable-parallel', '-p:NuGetAudit=false']),
                ('Compile existing starter without restore', ['dotnet', 'publish', str(project), '--configuration', 'Release', '--no-restore', '--output', str(root / 'payload'), '--disable-build-servers', '-m:1'])]:
                result = subprocess.run(command, cwd=root, env=env, capture_output=True, text=True, timeout=180, check=False)
                report['checks'].append({'name': label, 'passed': result.returncode == 0})
                if result.returncode:
                    raise RuntimeError(label + '\n' + (result.stdout + result.stderr)[-4000:])
            assert (root / 'payload/WellInputCheck.dll').is_file()
            report['status'] = 'passed'
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({'status': report['status'], 'report': str(args.report)}))

if __name__ == '__main__':
    main()
