"""Exercise the standalone checker and the production build target with test archives.

Run with Python 3 and the .NET 8 SDK:
python tests/CoreIdentityChecks/archive_check.py
"""
from hashlib import sha256
from pathlib import Path
import subprocess
import tempfile
import zipfile
from xml.sax.saxutils import quoteattr

ROOT = Path(__file__).resolve().parents[2]
CORE_PATH = "V-Guard/SGuard64.exe"
POOL = b"auto-selector-round-robin"
DNS = b"vpn-dns-race"


def msbuild(project, *args):
    return subprocess.run(["dotnet", "msbuild", str(project), "-nologo", *args],
                          text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)


def check(result, expected_exit, *snippets):
    assert (result.returncode == 0) == expected_exit, result.stdout
    for snippet in snippets:
        assert snippet in result.stdout, result.stdout


with tempfile.TemporaryDirectory(prefix="core-archive-check-") as tmp:
    app = Path(tmp) / "Build With Spaces"
    (app / "Resources").mkdir(parents=True)
    (app / "Resource").mkdir()
    (app / "obj").mkdir()
    project = app / "Fixture.proj"
    project.write_text(
        '<Project><PropertyGroup><IntermediateOutputPath>obj/</IntermediateOutputPath></PropertyGroup>'
        f'<Import Project={quoteattr(str(ROOT / "Directory.Build.targets"))} />'
        '<Target Name="CoreCompile" /></Project>', encoding="utf-8")
    archive_path = app / "Resources/Files.zip"

    check(msbuild(project, "-t:CoreCompile"), True)
    print("PASS offline project without an embedded archive keeps its existing behavior")
    (app / "Resource/ResourceManager.cs").touch()
    check(msbuild(project, "-t:CoreCompile"), False, "Embedded runtime archive was not found")
    print("PASS application project without an embedded archive keeps its existing missing-file error")

    # Span a scan buffer with the first marker. Hash both markers and the tail.
    valid = b"\xff" * (65536 - 10) + POOL + b"\x00" + DNS + b"\xfe"
    cases = [
        ("both markers", [(CORE_PATH, valid)], True,
         ("SHA256=" + sha256(valid).hexdigest(), "auto-selector-round-robin=True", "vpn-dns-race=True")),
        ("missing DNS", [(CORE_PATH, POOL)], False,
         ("SHA256=" + sha256(POOL).hexdigest(), "missing required feature vpn-dns-race", "5c47531b", "c0fa2071")),
        ("missing Pool", [(CORE_PATH, DNS)], False,
         ("SHA256=" + sha256(DNS).hexdigest(), "missing required feature auto-selector-round-robin", "5c47531b", "c0fa2071")),
        ("missing exe", [("not-a-core/SGuard64.exe", valid)], False,
         ("missing a V-Guard/SGuard Core executable", "Core SHA256 unavailable")),
        ("two entries for one exe", [(CORE_PATH, valid), (r"v-guard\.\sguard64.exe", valid)], False,
         ("multiple Core entries", sha256(valid).hexdigest())),
        ("distinct OS/architecture slots", [(CORE_PATH, valid), ("V-Guard/SGuard732.exe", valid)], True,
         ("SGuard64.exe SHA256=", "SGuard732.exe SHA256=")),
        ("incompatible secondary slot", [(CORE_PATH, valid), ("V-Guard/SGuard32.exe", DNS)], False,
         ("SGuard32.exe SHA256=" + sha256(DNS).hexdigest(), "missing required feature auto-selector-round-robin"))
    ]
    for label, content, expected_exit, snippets in cases:
        with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, payload in content:
                archive.writestr(name, payload)
        check(msbuild(project, "-t:CoreCompile"), expected_exit, *snippets)
        if expected_exit:
            archive_digest = sha256(archive_path.read_bytes()).hexdigest()
            generated = (app / "obj/FilesZipIntegrity.Fixture.g.cs").read_text(encoding="utf-8-sig")
            assert archive_digest.lower() in generated.lower(), generated
        print("PASS build gate:", label)

    check(msbuild(ROOT / "tools/Check-CoreArchive.proj", "-p:ArchivePath=" + str(archive_path)), False,
          "SGuard32.exe", "missing required feature auto-selector-round-robin")
    print("PASS standalone checker reports the same incompatible Core")
