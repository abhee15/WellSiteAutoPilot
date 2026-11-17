"""Measure ordinary-child access to synthetic resources; NEVER an isolation certificate."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import platform
import socket
import subprocess
import sys
import tempfile
from threading import Thread


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    # Entirely synthetic resources owned by this probe. No credentials or real services.
    with tempfile.TemporaryDirectory(prefix='authoring-boundary-') as directory:
        root = Path(directory)
        build = root / 'build'
        build.mkdir()
        sentinel = root / 'synthetic-operational-resource.txt'
        sentinel.write_text('SYNTHETIC-ONLY', encoding='utf-8')
        with socket.socket() as listener:
            listener.bind(('127.0.0.1', 0))
            listener.listen(1)
            listener.settimeout(8)
            accepted = []

            def serve():
                try:
                    connection, _ = listener.accept()
                    with connection:
                        accepted.append(connection.recv(64) == b'PROBE')
                except TimeoutError:
                    pass

            thread = Thread(target=serve, daemon=True)
            thread.start()
            child = subprocess.run([sys.executable, '-c',
                'import json,pathlib,socket,sys; '
                'read=pathlib.Path(sys.argv[1]).read_text()=="SYNTHETIC-ONLY"; '
                's=socket.create_connection(("127.0.0.1",int(sys.argv[2])),timeout=3); '
                's.sendall(b"PROBE"); s.close(); '
                'print(json.dumps({"readOutsideBuildDirectory":read,"connectedToLoopback":True}))',
                str(sentinel), str(listener.getsockname()[1])], cwd=build,
                capture_output=True, text=True, timeout=10, check=True)
            thread.join(timeout=9)
        measured = json.loads(child.stdout)
        assert measured['readOutsideBuildDirectory'] and measured['connectedToLoopback'] and accepted == [True]
    report = {
        'probe': 'ordinary-child-synthetic-boundary', 'platform': platform.system(),
        'status': 'measurement-completed', 'releaseGate': 'blocked', 'observed': measured,
        'conclusion': 'A temporary working directory and ordinary subprocess provide neither filesystem nor network isolation.',
        'notEstablished': ['Restricted Windows identity/ACLs', 'Operational secret denial',
                           'CPU/memory limits', 'Complete process-tree cancellation',
                           'Clean offline SDK provisioning', 'A deployable sandbox']}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'report': str(args.report), 'releaseGate': 'blocked'}))


if __name__ == '__main__':
    main()
