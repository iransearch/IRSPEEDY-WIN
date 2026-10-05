"""Compile production diagnostics/RPC code against real .NET Framework 4.8 APIs.

The fixture supplies application fields only. This is not a full Windows build
or a VPN runtime test.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

directory = Path(__file__).resolve().parent
project = ET.parse(directory / "ConnectionDiagnosticChecks.csproj")
sources = [(directory / node.attrib["Include"]).resolve()
           for node in project.findall(".//Compile")]
sources.append(directory / "Fixtures.cs")
with tempfile.TemporaryDirectory(prefix="connection-state-net48-") as name:
    path = Path(name)
    items = "\n".join('<Compile Include="' + escape(str(source)) + '" />' for source in sources)
    (path / "Checks.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net48</TargetFramework><LangVersion>7.3</LangVersion>
<NoWarn>CS0649;CS0169;CS0168</NoWarn></PropertyGroup><ItemGroup>''' + items + '''
<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" />
</ItemGroup></Project>''')
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else "dotnet", "build",
                    str(path / "Checks.csproj"), "-v:q", "-p:NuGetAudit=false"], check=True)
print("PASS production core-state diagnostics and RPC client compile against net48 APIs")
