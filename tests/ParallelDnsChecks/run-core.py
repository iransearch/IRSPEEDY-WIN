"""Check generated DNS fixtures with the exact sing-box source used by Throne.

Generate fixtures with IRSPEEDY_DNS_FIXTURES=/absolute/path dotnet run --project
tests/ParallelDnsChecks, then run this script with the core checkout and fixture
directory as arguments. The temporary test file is always removed.
"""
from pathlib import Path
import os
import shutil
import subprocess
import sys

core, fixtures = (Path(arg).resolve() for arg in sys.argv[1:])
pin = "f154bec036c97fc998a7714bea9977bdc343780a"
assert subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=core, text=True).strip() == pin
target = core / "dns/irspeedy_parallel_dns_test.go"
assert not target.exists(), "Refusing to overwrite an existing test"
try:
    shutil.copyfile(Path(__file__).with_name("core_race_test.go"), target)
    subprocess.run([os.environ.get("GO", "go"), "test", "./dns", "-run", "TestIRSpeedyParallelDNS", "-count=1", "-race"],
                   cwd=core, env={**os.environ, "IRSPEEDY_DNS_FIXTURES": str(fixtures)}, check=True)
finally:
    target.unlink(missing_ok=True)
