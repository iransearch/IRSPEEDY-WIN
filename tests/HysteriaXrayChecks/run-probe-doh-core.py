"""Validate exported app profiles and real composite DoH against Throne-G.
Usage: GO=/path/to/go python run-probe-doh-core.py /path/to/Throne-G /path/to/probe.json
"""
from pathlib import Path
import os, subprocess, sys
core, fixture = (Path(arg).resolve() for arg in sys.argv[1:])
server = core / "core/server"
assert (server / "internal/probedns/transport.go").exists(), "Build/test the matching Core with https-race support"
subprocess.run([os.environ.get("GO", "go"), "test", "-race", "./internal/probedns", "./internal/boxmain"],
               cwd=server, env={**os.environ, "IRSPEEDY_PROBE_DNS_FIXTURE": str(fixture)}, check=True)
