"""Validate exported system-DNS probe profiles against the real Throne Core.
Usage: GO=/path/to/go python run-probe-dns-core.py /path/to/Throne-G /path/to/probe.json
"""
from pathlib import Path
import os, shutil, subprocess, sys
core, fixture = (Path(arg).resolve() for arg in sys.argv[1:])
server = core / "core/server"
target = server / "internal/boxmain/irspeedy_system_probe_dns_test.go"
assert not target.exists(), "Refusing to overwrite an existing test"
try:
    shutil.copyfile(Path(__file__).with_name("probe_dns_core_test.go"), target)
    subprocess.run([os.environ.get("GO", "go"), "test", "-race", "./internal/boxmain", "-run", "TestIRSpeedySystemProbeDNS", "-count=1"],
                   cwd=server, env={**os.environ, "IRSPEEDY_PROBE_DNS_FIXTURE": str(fixture)}, check=True)
finally:
    target.unlink(missing_ok=True)
