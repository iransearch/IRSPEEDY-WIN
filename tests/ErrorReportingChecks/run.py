"""Offline Sentry 6.11.0 checks, real loopback RPC failures and net48 compile.
Usage: run.py DOTNET CSC NET8_REFS NET48_REFS SENTRY_NET462_DLL
Sentry is never initialized. No telemetry can be submitted by this runner.
"""
from pathlib import Path
import json,shutil,subprocess,sys,tempfile
root=Path(__file__).resolve().parents[2];base=root/'IRSpeedyVPN'
dotnet,csc,refs8,refs48,sentry=sys.argv[1:]
libcore=[base/'Services/Libcore'/f'{name}.cs' for name in ['ProtorpcClient','LibcoreServiceClient','LibcoreProto','LibcoreTraffic','LibcoreAutoSelectors','LibcoreMessages']]
with tempfile.TemporaryDirectory(prefix='sentry-core-checks-') as tmp:
 p=Path(tmp)
 source=(base/'Services/TunnelPlusService.cs').read_text();assert 'const int CoreStartDeadlineMs = 30000;' in source and 'const int CoreConnectRetryDelayMs = 200;' in source;a=source.index('        private T ExecuteCoreCall<T>(');b=source.index('        private bool TryKillProcess(',a)
 diag=(base/'Services/TunnelPlusService.Diagnostics.cs').read_text();c=diag.index('        private void AnnotateCoreStartup(');d=diag.index('        internal string DiagnosticIdentity',c)
 ensure=source.index('            var startupClock = Stopwatch.StartNew();'); stop=source.index('        private string[] GetShieldFiles()',ensure)
 tail='private bool StartupHarness(string configData,out string error,bool needXray=true,string xrayConfig="XRAY SECRET",string xrayOutboundDnsStrategy=""){error=null;'+source[ensure:stop]
 sa=source.index('        private bool SafeStopCore(');sb=source.index('        private T ExecuteCoreCall<T>(',sa)
 safe=source[sa:sb]
 (p/'ProductionMethods.cs').write_text('using System;using System.IO;using System.Diagnostics;using System.Threading;using IRSpeedyVPN.Common;using IRSpeedyVPN.Services.Libcore;internal sealed partial class RpcOperationProbe{'+source[a:b]+diag[c:d]+safe+tail+'}')
 sources=[base/'Common/ErrorReporting.cs',base/'Common/CoreRuntimeIdentity.cs',base/'Common/NetworkFailureDiagnostic.cs',Path(__file__).with_name('Program.cs'),Path(__file__).with_name('CoreRpcChecks.cs'),Path(__file__).with_name('OperationChecks.cs'),p/'ProductionMethods.cs']+libcore
 for refs,name in [(refs48,'Checks.dll'),(refs8,'Checks8.dll')]:
  args=[dotnet,csc,'-nologo','-langversion:7.3','-nowarn:0649,0169','-define:CORE_RPC_OPERATION_CHECKS','-target:exe','-out:'+str(p/name)]
  args+=['-r:'+str(f) for f in Path(refs).glob('*.dll') if f.name not in ['System.EnterpriseServices.Wrapper.dll','System.EnterpriseServices.Thunk.dll']]+['-r:'+sentry]+[str(f) for f in sources]
  subprocess.run(args,check=True)
 shutil.copy2(sentry,p/'Sentry.dll')
 (p/'Checks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 subprocess.run([dotnet,str(p/'Checks.dll')],check=True,timeout=30)
print('PASS production RPC tracing, Sentry filtering and retry/startup annotators compile against actual .NET Framework 4.8 and Sentry 6.11.0 references')
