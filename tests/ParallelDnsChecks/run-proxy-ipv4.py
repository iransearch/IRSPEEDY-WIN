"""Exercise the real Core via an additional test overlay; leave its sources unchanged."""
from pathlib import Path
import json
import os
import subprocess
import sys
import tempfile

core, fixtures = (Path(arg).resolve() for arg in sys.argv[1:])
server = core / "core/server"
source = Path(__file__).with_name("proxy_ipv4_native_test.go").resolve()
assert (server / "internal/boxbox/proxy_dns_test.go").is_file(), "Core's existing real proxy test harness is required"
assert (fixtures / "tun-proxy-behavior.json").is_file(), "Generate current Windows ParallelDnsChecks fixtures first"
with tempfile.TemporaryDirectory(prefix="irspeedy-proxy-ipv4-") as directory:
    overlay = Path(directory) / "overlay.json"
    overlay.write_text(json.dumps({"Replace": {
        str(server / "internal/boxbox/windows_proxy_ipv4_test.go"): str(source)
    }}))
    subprocess.run([os.environ.get("GO", "go"), "test", "-race", "-overlay", str(overlay),
                    "./internal/boxbox", "-run", "^TestWindowsProxyIPv4Policy$", "-v", "-count=1"],
                   cwd=server, env={**os.environ, "IRSPEEDY_DNS_FIXTURES": str(fixtures)}, check=True)
