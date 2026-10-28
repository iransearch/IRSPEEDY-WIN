"""Execute production shared-Core exit routing; compile it against net48 too.

Usage: core_exit.py DOTNET CSC NET8_REFS NET48_REFS
The Linux test child processes are always killed, drained and disposed.
"""
from pathlib import Path
import json, subprocess, sys, tempfile
root=Path(__file__).resolve().parents[2]
dotnet,csc,refs8,refs48=sys.argv[1:]
source=(root/'IRSpeedyVPN/Services/TunnelPlusService.cs').read_text()
start=source.index('        private void BindReusedCoreExitSource(')
end=source.index('        private void TryReconnect()',start)
methods=source[start:end]
assert 'BindReusedCoreExitSource(port);' in source[source.index('private void EnsureCoreRunning('):start]
assert 'ownedMainCoreProcess = startedProcess;' in source
assert 'recoveryCoreProcess = startedProcess;' in source
assert 'Residential' not in methods
for contract in ['const int MaxCoreExitsInWindow = 10;', 'const int CoreExitWindowSeconds = 30;', 'const int ImmediateExitSeconds = 2;']: assert contract in source
assert 'active.HandleActiveCoreExit(process);' in methods
assert 'coreOwned = true' not in methods and '.Kill(' not in methods and '.Dispose(' not in methods
with tempfile.TemporaryDirectory(prefix='shared-core-exit-') as directory:
    tmp=Path(directory)
    (tmp/'Production.cs').write_text('using System;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using IRSpeedyVPN.Common;namespace IRSpeedyVPN.Services{partial class TunnelPlusService{'+methods+'}}')
    fixture=Path(__file__).with_name('CoreExitFixtures.cs')
    for refs,name,target in [(refs8,'Checks.dll','exe'),(refs48,'Compile.dll','library')]:
        args=[dotnet,csc,'-nologo','-langversion:7.3','-nowarn:0649,0169','-target:'+target,'-out:'+str(tmp/name)]
        args+=['-r:'+str(p) for p in Path(refs).glob('*.dll') if p.name not in ['System.EnterpriseServices.Wrapper.dll','System.EnterpriseServices.Thunk.dll']]
        subprocess.run(args+[str(tmp/'Production.cs'),str(fixture)],check=True)
    (tmp/'Checks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([dotnet,str(tmp/'Checks.dll')],check=True,timeout=35)
print('PASS shared exit routing and binding compile against real .NET Framework 4.8 references; ownership is not transferred')
