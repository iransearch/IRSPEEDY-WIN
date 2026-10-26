"""Offline production login recovery checks (Python 3, .NET 8, native curl).

python tests/LoginRecoveryChecks/run.py <dotnet> <csc.dll> <net8-reference-dir>
Optional fourth argument: .NET Framework 4.8 reference directory for compilation.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
import os

root = Path(__file__).resolve().parents[2]
here = Path(__file__).resolve().parent
source = root / 'IRSpeedyVPN/WebServices'
dotnet, compiler, refs = sys.argv[1:4]

def method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]

controller = (source/'NewServiceController.cs').read_text(encoding='utf-8-sig')
start = controller.index('        private BaseHttpResponse<TResponse> ExecuteWithFailover<TResponse>(')
end = controller.index('        // ==========================================================', controller.index('        private static string GetResponseStatus'))
get_base = method(controller, '        private static string GetBaseUrl(')
fixture = '''using System;using System.Collections.Generic;using System.Diagnostics;
using System.Net;using System.Runtime.ExceptionServices;using IRSpeedyVPN.Common;
namespace IRSpeedyVPN.WebServices {
 class RestHelper { internal string BaseAddress; }
 class BaseHttpResponse<T> { internal HttpStatusCode StatusCode; }
 class ControllerHarness {
 const int LoginRequestTimeoutSeconds=10,LoginEndpointTimeoutSeconds=8;
 readonly List<RestHelper> _services=new List<RestHelper>{new RestHelper{BaseAddress="https://first.test/"},new RestHelper{BaseAddress="https://second.test/"}};
 int _index; static string _lastGoodBaseUrl;
 internal BaseHttpResponse<object> Run(Func<RestHelper,int?,BaseHttpResponse<object>> call,LoginNetworkContext context)
 => ExecuteWithFailover("test","Login",call,30,true,context);
'''+controller[start:end]+get_base+'''} }
class ControllerChecks {
 internal static void Run() {
  int calls=0;
  var result=new IRSpeedyVPN.WebServices.ControllerHarness().Run((host,timeout)=>{
   if(++calls==1)throw new IRSpeedyVPN.WebServices.LoginTransportException("initial 7; retry 60",false);
   return new IRSpeedyVPN.WebServices.BaseHttpResponse<object>{StatusCode=HttpStatusCode.OK};
  },new IRSpeedyVPN.WebServices.LoginNetworkContext(default(System.Threading.CancellationToken)));
  if(calls!=2||result.StatusCode!=HttpStatusCode.OK)throw new Exception("safe failure did not advance endpoint");
  Console.WriteLine("PASS actual controller advances to next endpoint after a bounded pre-send recovery failure");
  foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.NotAcceptable}){
   calls=0;
   new IRSpeedyVPN.WebServices.ControllerHarness().Run((host,timeout)=>{calls++;return new IRSpeedyVPN.WebServices.BaseHttpResponse<object>{StatusCode=status};},null);
   if(calls!=1)throw new Exception("HTTP authentication rejection replayed");
  }
  calls=0;new IRSpeedyVPN.WebServices.ControllerHarness().Run((host,timeout)=>new IRSpeedyVPN.WebServices.BaseHttpResponse<object>{StatusCode=++calls==1?HttpStatusCode.InternalServerError:HttpStatusCode.OK},null);
  if(calls!=2)throw new Exception("HTTP500 policy changed");
  Console.WriteLine("PASS controller retains final 401/406 and existing HTTP500 failover policy");
  calls=0;
  try{new IRSpeedyVPN.WebServices.ControllerHarness().Run((host,timeout)=>{calls++;throw new IRSpeedyVPN.WebServices.LoginTransportException("posted",true);},null);throw new Exception("posted failure accepted");}
  catch(IRSpeedyVPN.WebServices.LoginTransportException){}
  if(calls!=1)throw new Exception("posted request replayed on another hostname");
  Console.WriteLine("PASS actual controller never replays posted or ambiguous requests on the next hostname");
 }
}
'''

files = [source/name for name in ['LoginRecovery.cs','LoginPrefetch.cs','CurlHelper.Login.cs','CurlHelper.cs','HttpFallback.cs','DohResolver.cs']]
files.append(root/'IRSpeedyVPN/Common/LogPolicy.cs')
with tempfile.TemporaryDirectory(prefix='login-recovery-') as directory:
    path = Path(directory)
    shim = path/'dns_fail.so'
    subprocess.run(['cc','-shared','-fPIC',str(here/'dns_fail.c'),'-ldl','-o',str(shim)],check=True)
    test_env = dict(os.environ, IRSPEEDY_TEST_DNS_SHIM=str(shim))
    (path/'ControllerFixture.cs').write_text(fixture)
    output = path/'checks.dll'
    command = [dotnet, compiler, '-nologo', '-langversion:7.3', '-target:exe', '-out:'+str(output)]
    command += ['-r:'+str(item) for item in Path(refs).glob('*.dll')]
    command += [str(item) for item in files]+[str(item) for item in here.glob('*.cs')]+[str(path/'ControllerFixture.cs')]
    subprocess.run(command, check=True)
    (path/'checks.runtimeconfig.json').write_text('{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}')
    subprocess.run([dotnet, str(output)], check=True, env=test_env)
    api = [source/name for name in ['NewServiceController.cs','LoginRecovery.cs','LoginPrefetch.cs','BaseHttpResponse.cs','DefaultEncryptedResponse.cs','DefaultPlainResponse.cs']]
    api += [here/'Fixtures.cs', here/'Api/Fixtures.cs', here/'Api/Program.cs']
    # Only the test bridge in Fixtures needs CurlHelper. Use the same actual
    # production transport files as the integration executable above.
    api += [source/name for name in ['CurlHelper.cs','CurlHelper.Login.cs','DohResolver.cs','HttpFallback.cs']]
    subprocess.run([dotnet, compiler, '-nologo','-langversion:7.3','-define:API_FLOW_TEST','-target:exe','-out:'+str(output)]
        + ['-r:'+str(item) for item in Path(refs).glob('*.dll')]+[str(item) for item in api], check=True)
    subprocess.run([dotnet,str(output)],check=True)
    if len(sys.argv) > 4:
        framework = Path(sys.argv[4])
        references = ['mscorlib','System','System.Core','System.Web.Extensions']
        subprocess.run([dotnet, compiler, '-nologo','-langversion:7.3','-define:NETFRAMEWORK','-target:library',
            '-out:'+str(path/'net48.dll')] + ['-r:'+str(framework/(name+'.dll')) for name in references]
            + [str(item) for item in files]+[str(here/'Fixtures.cs')], check=True)
        print('PASS production transport/recovery compile against .NET Framework 4.8')
        api_framework = [source/name for name in ['NewServiceController.cs','RestHelper.cs','BaseHttpResponse.cs','DefaultEncryptedResponse.cs','DefaultPlainResponse.cs']]
        subprocess.run([dotnet, compiler, '-nologo','-langversion:7.3','-define:NETFRAMEWORK','-target:library',
            '-out:'+str(path/'api-net48.dll')] + ['-r:'+str(framework/(name+'.dll')) for name in references]
            + [str(item) for item in files+api_framework]+[str(here/'Fixtures.cs'),str(here/'Api/Fixtures.cs')],check=True)
        print('PASS complete production controller, REST serialization and transport wiring compile against .NET 4.8')
    subprocess.run([sys.executable, str(root/'tests/PasswordReloginChecks/run.py'), dotnet, compiler, refs],check=True)
    subprocess.run([sys.executable, str(root/'tests/LoginPresentationChecks/run.py'), dotnet, compiler, refs],check=True)
    subprocess.run([sys.executable, str(here/'ui_lifecycle.py'), dotnet, compiler, refs],check=True)
