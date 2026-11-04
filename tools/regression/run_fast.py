#!/usr/bin/env python3
"""Run only verified offline regression harnesses, with explicit FAIL/SKIP reporting.

No network services, user accounts, Windows services or VPN connections are required.
Live Windows/Core integration tests are deliberately not part of this runner.
"""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "tests" / "RegressionGuard"

SUITES = [
    ("login-dns", "LoginRecoveryChecks", ["python", "tests/LoginRecoveryChecks/run.py", "{dotnet}", "{csc}", "{refs}"]),
    ("connection", "ConnectionTestChecks", ["python", "tests/ConnectionTestChecks/run.py", "{dotnet}"]),
    ("core-startup", "AiStartupChecks", ["python", "tests/AiStartupChecks/run.py", "{dotnet}"]),
    ("server-selection", "LastSuccessfulServerChecks", ["python", "tests/LastSuccessfulServerChecks/run.py", "{dotnet}"]),
    ("server-transitions", "ServerListTransitionChecks", ["python", "tests/ServerListTransitionChecks/run.py", "{dotnet}", "{csc}", "{refs}"]),
    ("country-cache", "ResidentialChecks", ["python", "tests/ResidentialChecks/run-cache.py", "{dotnet}", "{csc}", "{refs}"]),
    ("device-session-ui", "DeviceLimitChecks", ["python", "tests/DeviceLimitChecks/run.py", "{dotnet}"]),
]

DEFERRED = {
    "windows-wpf": "Not run: requires real Windows WPF/BAML/STA build and visual checks",
    "packaged-core": "Not run: requires packaged/native Core and live tunnel smoke tests",
    "live-login": "Not run: requires controlled API/auth endpoint; no real credentials in CI",
    "tls-system": "Not run: requires controlled Windows system proxy / trust context",
}

def locate_net8():
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("dotnet CLI not found; suite must fail, not silently skip")
    output = subprocess.check_output([dotnet, "--list-sdks"], text=True)
    matches = []
    for line in output.splitlines():
        m = re.match(r"^(8\.[^\s]+)\s+\[([^\]]+)\]", line)
        if m:
            matches.append((m.group(1), Path(m.group(2))))
    if not matches:
        raise RuntimeError("Required .NET SDK 8 not installed")
    version, sdkparent = matches[-1]
    csc = sdkparent / version / "Roslyn" / "bincore" / "csc.dll"
    packs = sdkparent.parent / "packs" / "Microsoft.NETCore.App.Ref"
    refs = sorted(packs.glob("8.*/ref/net8.0")) if packs.exists() else []
    if not csc.is_file() or not refs:
        raise RuntimeError("Required .NET 8 compiler or reference assemblies missing")
    return {"dotnet":dotnet, "csc":str(csc), "refs":str(refs[-1])}

def run_one(name, folder, template, sdk, timeout):
    cmd = [sys.executable if arg == "python" else arg.format(**sdk) for arg in template]
    started = time.monotonic()
    try:
        # Do not emit tokens or secrets from an external service: test harness is offline.
        proc = subprocess.run(cmd, cwd=ROOT, timeout=timeout, check=False)
        return {"id":name,"suite":folder,"status":"passed" if proc.returncode == 0 else "failed",
                "exit_code":proc.returncode,"duration_seconds":round(time.monotonic()-started,2)}
    except subprocess.TimeoutExpired:
        return {"id":name,"suite":folder,"status":"failed","exit_code":None,
                "error":"test-timeout","duration_seconds":round(time.monotonic()-started,2)}
    except OSError as exc:
        return {"id":name,"suite":folder,"status":"failed","exit_code":None,
                "error":type(exc).__name__,"duration_seconds":round(time.monotonic()-started,2)}

def build_report(results, started, error=None):
    return {"schema":"irspeedy-regression-v1",
            "generated_at":dt.datetime.now(dt.timezone.utc).isoformat(),
            "branch":os.environ.get("GITHUB_BASE_REF") or os.environ.get("GITHUB_REF_NAME", "local"),
            "source_sha":os.environ.get("GITHUB_SHA"),
            "execution_environment":"ubuntu-24.04 / .NET 8 (offline fixtures)",
            "results":results,"deferred":[{"id":k,"status":"not-run","reason":v} for k,v in DEFERRED.items()],
            "elapsed_seconds":round(time.monotonic()-started,2),
            "overall":"failed" if error or any(x["status"]!="passed" for x in results) or len(results)!=len(SUITES) else "passed",
            "error":error}

def summary(report):
    lines=["## IRSPEEDY regression guard (phase 1)","",
           f"**Result:** {report['overall'].upper()} — offline, Linux-only checks; not a Windows/Core release gate.","",
           "| Suite | Result | Duration (s) |","| --- | --- | ---: |"]
    lines.extend(f"| {r['suite']} | {r['status']} | {r['duration_seconds']} |" for r in report["results"])
    lines += ["","### Explicitly NOT RUN",""]
    lines.extend(f"- **{d['id']}**: {d['reason']}" for d in report["deferred"])
    if report["error"]:
        lines += ["",f"Runner error: {report['error']}"]
    return "\n".join(lines)+"\n"

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report",default="regression-phase1-report.json")
    parser.add_argument("--summary")
    parser.add_argument("--timeout",type=int,default=180)
    args=parser.parse_args()
    started=time.monotonic()
    results=[]
    error=None
    try:
        sdk=locate_net8()
        for name,folder,cmd in SUITES:
            print(f"\n=== Running real offline suite: {folder} ===",flush=True)
            result=run_one(name,folder,cmd,sdk,args.timeout)
            results.append(result)
            print(f"=== {folder}: {result['status']} ({result['duration_seconds']}s) ===",flush=True)
            # Fail closed: keep subsequent suites for diagnostic coverage, but never claim a pass.
    except (RuntimeError,subprocess.CalledProcessError) as exc:
        error=str(exc)
    report=build_report(results,started,error)
    out=Path(args.report)
    out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    msg=summary(report)
    print(msg)
    if args.summary:
        with open(args.summary,"a",encoding="utf-8") as f:
            f.write(msg)
    return 0 if report["overall"]=="passed" else 1

if __name__=="__main__":
    raise SystemExit(main())
