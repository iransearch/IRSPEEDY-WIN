"""Run the production country cache checks with .NET 8 and existing inert service boundaries.

Usage: run-cache.py DOTNET CSC NET8_REFS. No WPF desktop, network or credentials.
"""
from pathlib import Path
import json
import shutil
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
base = root / 'IRSpeedyVPN'
dotnet, csc, refs = sys.argv[1:]
sources = [base / name for name in [
    'Residential/ResidentialModels.cs', 'Residential/ResidentialSubscription.cs',
    'Residential/ResidentialService.cs', 'Services/ServerCheckCache.cs',
    'Common/Json/JsonConvertibleAttribute.cs', 'Common/Json/JsonPropertyAttribute.cs']]
sources += [Path(__file__).with_name(name) for name in ['CompileBoundary.cs', 'CacheChecks.cs']]
newtonsoft = Path(csc).resolve().parents[2] / 'Newtonsoft.Json.dll'
with tempfile.TemporaryDirectory(prefix='country-cache-checks-') as directory:
    output = Path(directory)
    entry = output / 'Entry.cs'
    entry.write_text('internal static class Entry { static void Main() { CacheChecks.Run(); } }')
    subprocess.run([dotnet, csc, '-nologo', '-langversion:7.3', '-nowarn:0649,0067',
        '-target:exe', '-out:' + str(output / 'checks.dll')]
        + ['-r:' + str(p) for p in Path(refs).glob('*.dll')]
        + ['-r:' + str(newtonsoft)] + [str(p) for p in sources + [entry]], check=True)
    shutil.copy2(newtonsoft, output / 'Newtonsoft.Json.dll')
    (output / 'checks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
    subprocess.run([dotnet, str(output / 'checks.dll')], check=True)
