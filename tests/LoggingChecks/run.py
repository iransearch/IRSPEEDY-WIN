"""Exercise production log retention, filtering and failed storage isolation (.NET 8)."""
from pathlib import Path
import os
import subprocess
import tempfile

project = Path(__file__).with_name("LoggingChecks.csproj")
with tempfile.TemporaryDirectory(prefix="irspeedy-logging-") as directory:
    local = Path(directory) / "local-data"
    local.mkdir()
    env = dict(os.environ, XDG_DATA_HOME=str(local))
    subprocess.run(["dotnet", "run", "--project", str(project), "--", directory], env=env, check=True)
