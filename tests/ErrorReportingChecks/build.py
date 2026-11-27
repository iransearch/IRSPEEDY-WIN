"""Offline MSBuild checks for the CrashRelay Release packaging target.

Run with the .NET 8 SDK available on PATH.
"""
from pathlib import Path
from tempfile import TemporaryDirectory
import json
import os
import shutil
import subprocess

root = Path(__file__).resolve().parents[2]
resource_name = "IRSpeedyVPN.CrashRelayDsn"
fixture_dsn = "https://example.invalid|00000000-0000-0000-0000-000000000001|" + "a" * 64

with TemporaryDirectory(prefix="CrashRelay build ") as directory:
    work = Path(directory)
    build_home = work / "build account"
    dsn_file = build_home / "IRSpeedyVPN/Build/crashrelay.dsn"
    dsn_file.parent.mkdir(parents=True)
    env = {**os.environ, "LOCALAPPDATA": str(build_home)}
    for prop in ("CrashRelayDsnFile", "UseCosturaSingleFile", "DotfuscatorExe", "DesignTimeBuild"):
        env.pop(prop, None)
    tools = work / "tools"
    tools.mkdir()
    shutil.copyfile(root / "tools/CrashRelayBuild.targets", tools / "CrashRelayBuild.targets")
    project = work / "Fixture.csproj"
    project.write_text("""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <Import Project="tools/CrashRelayBuild.targets" />
</Project>""")
    (work / "Program.cs").write_text("""
using System;
using System.IO;
using System.Reflection;
using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("IRSpeedyVPN.CrashRelayDsn");
if (resource == null) return 1;
using var reader = new StreamReader(resource);
return reader.ReadToEnd() == File.ReadAllText(Environment.GetEnvironmentVariable("EXPECTED_DSN_FILE")!) ? 0 : 2;
""")

    def verify(status, *args, code=None, project_path=project):
        result = subprocess.run(["dotnet", "msbuild", str(project_path), "-nologo", "-t:ValidateCrashRelayBuild",
                                 *args], env=env, text=True, capture_output=True, check=False)
        output = result.stdout + result.stderr
        assert (result.returncode == 0) == status, output
        if code is not None:
            assert code in output, output
        assert fixture_dsn not in output and "a" * 64 not in output, "DSN appeared in MSBuild output"

    def embedded(*args):
        output = subprocess.check_output(["dotnet", "msbuild", str(project), "-nologo", "-getItem:EmbeddedResource",
                                          *args], env=env, text=True)
        return [Path(item["FullPath"]) for item in json.loads(output)["Items"]["EmbeddedResource"]
                if item.get("LogicalName") == resource_name]

    release = "-p:Configuration=Release"
    single_file = "-p:UseCosturaSingleFile=true"
    verify(False, release, single_file, code="error IRSC001")
    verify(True, release, code="warning IRSC001")
    verify(True, "-p:Configuration=Debug", single_file)
    verify(True, release, single_file, "-p:DesignTimeBuild=true")
    packer = work / "packer.exe"
    packer.touch()
    verify(False, release, "-p:DotfuscatorExe=" + str(packer), code="error IRSC001")
    print("PASS packaging requires a DSN while ordinary and design-time builds retain their behavior")

    for invalid in ("", fixture_dsn.replace("https:", "http:"), fixture_dsn.replace(".invalid", ".invalid/path"),
                    fixture_dsn.replace("00000000", "INVALID0", 1), fixture_dsn[:-1]):
        dsn_file.write_text(invalid)
        verify(False, release, single_file, "-verbosity:diagnostic", code="error IRSC002")
    print("PASS empty and malformed DSNs stop packaging without disclosing the ingest key")

    dsn_file.write_bytes(("\ufeff" + fixture_dsn + "\r\n").encode("utf-8"))
    verify(True, release, single_file, "-verbosity:diagnostic")
    assert embedded(release, single_file) == [dsn_file]
    nuget_config = work / "NuGet.Config"
    nuget_config.write_text("<configuration><packageSources><clear /></packageSources></configuration>")
    result = subprocess.run(["dotnet", "build", str(project), "--configfile", str(nuget_config), "-nologo",
                             release, single_file, "-v:q"], env=env, text=True, capture_output=True, check=False)
    assert result.returncode == 0, result.stdout + result.stderr
    subprocess.run(["dotnet", str(work / "bin/Release/net8.0/Fixture.dll")],
                   env={**env, "EXPECTED_DSN_FILE": str(dsn_file)}, check=True)
    print("PASS a clean checkout embeds the build-account file in a compiled assembly")

    checkout_file = work / ".crashrelay-dsn"
    checkout_file.write_text(fixture_dsn)
    assert embedded() == [checkout_file]
    assert embedded("-p:CrashRelayDsnFile=" + str(dsn_file)) == [dsn_file]
    missing_override = "-p:CrashRelayDsnFile=" + str(work / "missing.dsn")
    verify(False, release, single_file, missing_override, code="error IRSC001")
    print("PASS checkout compatibility and explicit path precedence")

    real_project = root / "IRSpeedyVPN/IRSpeedyVPN.csproj"
    verify(False, release, single_file, missing_override, code="error IRSC001", project_path=real_project)
    verify(True, release, single_file, "-p:CrashRelayDsnFile=" + str(dsn_file), project_path=real_project)
    print("PASS the Windows app project imports the guard")
