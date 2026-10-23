"""Run VPN DNS tests in the updated Throne-G checkout.

Generate fixtures with IRSPEEDY_DNS_FIXTURES=/absolute/path dotnet run --project
tests/ParallelDnsChecks, then pass the Throne-G root and fixture directory.
The tests are maintained in Throne-G; the upstream rule-order race is no longer
used by the Windows live profile.
"""
from pathlib import Path
import os
import subprocess
import sys

core, fixtures = (Path(arg).resolve() for arg in sys.argv[1:])
server = core / "core/server"
assert (server / "internal/vpndns/transport.go").is_file(), "Updated Throne-G with vpn-dns-race is required"
assert all((fixtures / (name + ".json")).is_file()
           for name in ("proxy", "tun", "split-proxy", "split-tun", "scoped", "proxy-shield-ai", "proxy-behavior")), "Generate Windows fixtures first"
subprocess.run([os.environ.get("GO", "go"), "test", "-race", "./internal/dnsengine", "./internal/vpndns", "./internal/boxmain",
                "./internal/boxbox", "./internal/netdiag", "-count=1"], cwd=server,
               env={**os.environ, "IRSPEEDY_DNS_FIXTURES": str(fixtures)}, check=True)
