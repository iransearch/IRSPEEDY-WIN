"""Production VLESS/transport parser methods + production Xray/Pool generators.
Run: python tests/XhttpHeaderChecks/run.py [dotnet] [--generators]
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
# Exercise the real final formatter too. The old fixture's direct Newtonsoft
# serialization skipped a production mutation of quoted header values.
utils = (root / 'IRSpeedyVPN/Services/V2Ray/Utils.cs').read_text(encoding='utf-8-sig')
formatter_start = utils.index('        static string FormatJson(')
formatter_end = utils.index('        /// <summary>', formatter_start)
fixture = 'using System.IO;using System.Linq;\n' + fixture
serialization_stub = 'return JsonConvert.SerializeObject(value, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });'
if fixture.count(serialization_stub) != 1:
    raise RuntimeError('JSON fixture changed: production FormatJson must remain in the serialization path')
fixture = fixture.replace(serialization_stub,
    'return FormatJson(JsonConvert.SerializeObject(value, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));')
fixture = fixture.replace('public static bool ToBool(string value)', utils[formatter_start:formatter_end] + '\n        public static bool ToBool(string value)')
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
    if '--generators' in sys.argv:
        for program in base.glob('*.cs'):
            if program.name != 'Fixtures.cs':
                (path / program.name).write_text(program.read_text())
    else:
        (path / 'Program.cs').write_text((Path(__file__).parent / 'Program.cs').read_text())
    project = ET.parse(base / 'HysteriaXrayChecks.csproj').getroot()
    for entry in project.iter('Compile'):
        entry.set('Include', str((base / entry.get('Include')).resolve()))
    (path / 'Checks.csproj').write_text(ET.tostring(project, encoding='unicode'))
    sdk = next((arg for arg in sys.argv[1:] if not arg.startswith('--')), 'dotnet')
    subprocess.run([sdk, 'run', '--project', str(path), '-v:q'], check=True)
