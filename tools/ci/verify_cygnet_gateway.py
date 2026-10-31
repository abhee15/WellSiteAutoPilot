#!/usr/bin/env python3
"""Exercise CygNet internal authorization and explicit runtime absence on a portable Gateway build."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    dll = root / 'src/integration/WellSiteAutoPilot.IntegrationGateway/bin/Release/net10.0/WellSiteAutoPilot.IntegrationGateway.dll'
    with socket.socket() as listener:
        listener.bind(('127.0.0.1', 0))
        port = listener.getsockname()[1]
    keys = {name: secrets.token_hex(32) for name in ('server', 'worker')}
    env = dict(os.environ)
    env['ASPNETCORE_URLS'] = f'http://127.0.0.1:{port}'
    env['ASPNETCORE_ENVIRONMENT'] = 'Production'
    for name, key in keys.items():
        env[f'Security__Callers__{name}__Verifier'] = base64.b64encode(hashlib.sha256(key.encode()).digest()).decode()
    with tempfile.TemporaryFile() as output:
        process = subprocess.Popen([args.dotnet, str(dll)], cwd=root, env=env, stdout=output, stderr=output)
        try:
            def request(path, body=None, caller='server', expected=200):
                data = None if body is None else json.dumps(body).encode()
                headers = {'Content-Type': 'application/json'}
                if caller:
                    headers['Authorization'] = 'WSA-Service ' + caller + ':' + keys[caller]
                req = urllib.request.Request(f'http://127.0.0.1:{port}/api/internal/v1/' + path, data=data, headers=headers)
                try:
                    with urllib.request.urlopen(req, timeout=3) as response:
                        status, payload = response.status, response.read()
                except urllib.error.HTTPError as error:
                    status, payload = error.code, error.read()
                assert status == expected, f'{path}: expected {expected}, got {status}'
                return json.loads(payload) if payload else None
            deadline = time.monotonic() + 20
            while True:
                try:
                    request('adapters')
                    break
                except urllib.error.URLError:
                    if process.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError('Portable Gateway did not start') from None
                    time.sleep(0.1)
            descriptor = next(item for item in request('adapters') if item['key'] == 'cygnet')
            assert not descriptor['fields'][-1]['identifiesSource']
            settings = {'domain': '123', 'site': 'SITE', 'service': 'UIS',
                        'pointMappings': '{"AveragePumpFillage":{"udc":"RRLPFILAVG","unit":"%"}}'}
            assert not request('adapters/cygnet/validate', {'settings': settings})['problems']
            invalid = {**settings, 'pointMappings': '{"Q":{"udc":"A::B","unit":"%"}}'}
            assert request('adapters/cygnet/validate', {'settings': invalid})['problems']
            assert request('adapters/cygnet/test', {'settings': settings})['outcome'] == 'NeedsAttention'
            assert not request('adapters/cygnet/discover', {'settings': settings})['complete']
            read = {'connectionId': str(uuid.uuid4()), 'connectionRevision': 1, 'settings': settings,
                    'externalId': 'SITE.UIS::WELL1', 'quantity': 'AveragePumpFillage'}
            request('adapters/cygnet/current', read, caller=None, expected=401)
            request('adapters/cygnet/current', read, caller='worker', expected=403)
            assert request('adapters/cygnet/current', read, expected=503)['code'] == 'CYGNET_RUNTIME_UNAVAILABLE'
            assert request('adapters/cygnet/current', {**read, 'externalId': 'OTHER.UIS::WELL1'}, expected=503)['code'] == 'CYGNET_FACILITY_INVALID'
            print('CygNet Gateway HTTP checks passed: validation, runtime absence, server-only authority and source isolation.')
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)


if __name__ == '__main__':
    main()
