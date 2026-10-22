"""Production VLESS/transport parser methods + production Xray/Pool generators.
Run: python tests/XhttpHeaderChecks/run.py [dotnet]
WPF/registry and unrelated link importers use HysteriaXrayChecks fixtures.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
base = root / 'tests/HysteriaXrayChecks'
source = (root / 'IRSpeedyVPN/Services/V2Ray/ShareHandler.cs').read_text(encoding='utf-8-sig')
start = source.index('        private static VmessItem ResolveStdVLESS(')
end = source.index('\n        #endregion', start)
# Copy the real methods verbatim. Query decoding and parser logic are not mocked.
parser = '''using System;using System.Linq;using System.Collections.Generic;using System.Collections.Specialized;
using System.Web;using v2rayN;using v2rayN.Mode;
class ParsedTransport {
 public static VmessItem Parse(string link) { return ResolveStdVLESS(link.Replace("%3A", ":")); }
''' + source[start:end] + '\n}\n'
fixture = (base / 'Fixtures.cs').read_text()
fixture = fixture.replace('public string address, allowInsecure', 'public List<string> alpn;\n        public string packetEncoding, remarks;\n        public string address, allowInsecure')
fixture = fixture.replace('public List<string> GetAlpn() { return null; }', 'public List<string> GetAlpn() { return alpn; }')
fixture = fixture.replace('TcpHeaderHttp =', 'GrpcgunMode = "gun", TcpHeaderHttp =')
fixture = fixture.replace('public static bool ToBool(string value)', '''public static string UrlDecode(string value) { return System.Web.HttpUtility.UrlDecode(value); }
        public static bool IsNullOrEmpty(string value) { return string.IsNullOrEmpty(value); }
        public static bool ToBool(string value)''')
with tempfile.TemporaryDirectory(prefix='xhttp-header-checks-') as temp:
    path = Path(temp)
    (path / 'ParsedTransport.cs').write_text(parser)
    (path / 'Fixtures.cs').write_text(fixture)
    (path / 'Program.cs').write_text((Path(__file__).parent / 'Program.cs').read_text())
    project = ET.parse(base / 'HysteriaXrayChecks.csproj').getroot()
    for entry in project.iter('Compile'):
        entry.set('Include', str((base / entry.get('Include')).resolve()))
    (path / 'Checks.csproj').write_text(ET.tostring(project, encoding='unicode'))
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet', 'run', '--project', str(path), '-v:q'], check=True)
