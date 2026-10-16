"""Exercise the exported probe DNS profile with the pinned real DoH transport.

Usage: python run-probe-doh-core.py /path/to/sing-box /absolute/path/probe.json
Set GO to select the Go executable. No public DNS server is contacted.
"""
from pathlib import Path
import os
import shutil
import subprocess
import sys

core, fixture = (Path(arg).resolve() for arg in sys.argv[1:])
assert subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=core, text=True).strip() == "f154bec036c97fc998a7714bea9977bdc343780a"
target = core / "dns/transport/irspeedy_probe_doh_test.go"
assert not target.exists(), "Refusing to overwrite an existing test"
try:
    shutil.copyfile(Path(__file__).with_name("probe_doh_core_test.go"), target)
    subprocess.run([os.environ.get("GO", "go"), "test", "./dns/transport", "-run", "TestIRSpeedyProbeDoH", "-race", "-count=1", "-v"],
                   cwd=core, env={**os.environ, "IRSPEEDY_PROBE_DNS_FIXTURE": str(fixture)}, check=True)
finally:
    target.unlink(missing_ok=True)
