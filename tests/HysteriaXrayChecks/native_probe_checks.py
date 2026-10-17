"""Compile production native probe conversion in isolation from WPF/link parsing.
Uses real outbound models and conversion methods; stream setup can throw deliberately.
"""
from pathlib import Path
import subprocess, sys, tempfile, re
root=Path(__file__).resolve().parents[2]
source=(root/'IRSpeedyVPN/Services/SingBox/ConfigGenerator.cs').read_text(encoding='utf-8-sig')
def method(name):
    m=re.search(r'        (?:public|private) static [^\n]+\b'+name+r'\(',source)
    assert m,name
    a=source.index('{',m.start());level=1;b=a+1
    while level:
        level+=(source[b]=='{')-(source[b]=='}');b+=1
    return source[m.start():b]
methods='\n'.join(method(n) for n in ['GetRejectedProbeLinks','GetUrlTestConfig','ApplyEndpointOverride','CreateChainOutbound','ValidateProbeOutbound','FillOutboundForItem','CreateXraySocksOutbound'])
fixtures=(root/'tests/HysteriaXrayChecks/Fixtures.cs').read_text()
fixtures=fixtures.replace('VMess, VLESS, Trojan, Shadowsocks, Socks, Http, Hysteria2','VMess, VLESS, Trojan, Shadowsocks, Socks, Http, Hysteria2, Shadowsocksr, WireGaurd, SSH, Tuic')
fixtures=fixtures.replace('public int port, alterId;','public int port, alterId, portEnd; public string packetEncoding, protocol, protocol_param, privateKey, preSharedKey, username, congestionControl, udpRelayMode; public string[] ipList;')
fixtures=fixtures.replace('public const string RealitySecurity', 'public static readonly List<string> vmessSecuritys=new List<string>{"auto"};\n public static readonly List<string> ssSecuritysInSagerNet='+re.search(r'new List<string> \{[^\n]+', (root/'IRSpeedyVPN/Services/V2Ray/Global.cs').read_text().split('ssSecuritysInSagerNet = ')[1]).group(0)+'\n public const string DefaultSecurity="auto",vmessProtocolLite="vmess",ssProtocolLite="shadowsocks",ssrProtocolLite="shadowsocksr",socksProtocolLite="socks",vlessProtocolLite="vless",trojanProtocolLite="trojan",wireguardProtocolLite="wireguard",sshProtocolLite="ssh";\n public const string RealitySecurity')
fixtures=fixtures.replace('public static string ToJson(object value)', 'public static bool IsNullOrEmpty(string v)=>string.IsNullOrEmpty(v); public static T FromJson<T>(string value)=>JsonConvert.DeserializeObject<T>(value); public static string ToJson(object value)')
program='''using System.Web.Script.Serialization;using System;using System.Linq;using System.Collections.Generic;using System.IO;using Newtonsoft.Json;using v2rayN;using v2rayN.Mode;using v2rayN.Handler;
namespace System.Web.Script.Serialization {class JavaScriptSerializer {public T Deserialize<T>(string value)=>JsonConvert.DeserializeObject<T>(value);}}
namespace IRSpeedyVPN.Services.SingBox { public class ConfigGenerator {
public class EndpointOverride {public string Server;public int ServerPort;}
static void boundStreamSettings(VmessItem node,string direction,Outbound o){if(node.network=="throw")throw new InvalidOperationException("stream fixture");}
'''+methods+'''}}
class Program {
static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
static void Main(){
var nodes=ShareHandler.Nodes;
foreach(var name in new[]{"valid","bad-cipher","bad-key","bad-port","throws","chain","valid2022"})nodes[name]=new VmessItem{configType=EConfigType.Shadowsocks,address="127.0.0.1",port=1081,id="test-secret",security="aes-128-gcm"};
nodes["bad-cipher"].security="unsupported-cipher";nodes["bad-key"].security="2022-blake3-aes-128-gcm";nodes["bad-port"].port=0;nodes["throws"].network="throw";
nodes["valid2022"].security="2022-blake3-aes-128-gcm";nodes["valid2022"].id=Convert.ToBase64String(new byte[16]);
var links=new Dictionary<string,string[]>{{"bad-cipher",null},{"bad-key",null},{"bad-port",null},{"throws",null},{"missing",null},{"chain",new[]{"valid","throws"}},{"valid",null},{"valid2022",null}};
var rejected=new Dictionary<string,string>();Dictionary<string,string> tags;
var json=IRSpeedyVPN.Services.SingBox.ConfigGenerator.GetUrlTestConfig(links,1234,out tags,tagPrefix:"case-",rejected:rejected);
var cfg=JsonConvert.DeserializeObject<IRSpeedyVPN.Services.SingBox.SingBoxConfig>(json);
Check(tags.Count==2&&tags.Values.Contains("valid")&&tags.Values.Contains("valid2022"),"one malformed native member never poisons healthy members");
Check(rejected.Count==6&&!rejected.Values.Any(v=>v.Contains("secret")),"all rejected members are accounted for without secrets");
Check(cfg.outbounds.Count==3&&cfg.outbounds.All(o=>!string.IsNullOrEmpty(o.type)),"partial outbounds and chains are rolled back atomically");
Check(cfg.outbounds[1].type=="shadowsocks"&&cfg.outbounds[1].method=="aes-128-gcm"&&cfg.outbounds[1].password=="test-secret","Shadowsocks conversion works with no legacy LazyConfig initialization");
Check(cfg.dns.servers.Count==2&&cfg.dns.servers.All(d=>d.type=="local")&&cfg.dns.servers[0].domain_resolver=="dns-local","actual typed probe generator preserves the original system DNS profile");
Check(tags.Keys.Distinct().Count()==tags.Count,"rejections do not duplicate accepted tags");
Check(IRSpeedyVPN.Services.SingBox.ConfigGenerator.GetRejectedProbeLinks(json,"create service: initialize outbound[1]: invalid key",tags).SequenceEqual(new[]{"valid"}),"native constructor error prunes only its exact owner");
Check(IRSpeedyVPN.Services.SingBox.ConfigGenerator.GetRejectedProbeLinks(json,"decode config at "+json+": outbounds[2]: unknown outbound type: ",tags).SequenceEqual(new[]{"valid2022"}),"native decode index maps to the exact member");
Check(IRSpeedyVPN.Services.SingBox.ConfigGenerator.GetRejectedProbeLinks(json,"dns lookup failed: outbounds[1]: reset",tags).Length==0&&IRSpeedyVPN.Services.SingBox.ConfigGenerator.GetRejectedProbeLinks(json,"create service: initialize outbound[0]: unavailable",tags).Length==0,"network and shared direct failures never delete unrelated members");
var fixture=Environment.GetEnvironmentVariable("IRSPEEDY_NATIVE_PROBE_FIXTURE");if(fixture!=null)File.WriteAllText(fixture,json);
}}
'''
# All simple model files; policy/routing helpers have platform dependencies.
exclude={'ConfigGenerator.cs','ParallelDnsRouting.cs','TunBrowserCompatibility.cs','VpnTimeZoneRouting.cs','WireGuardUnformatted.cs'}
with tempfile.TemporaryDirectory(prefix='native-probe-') as directory:
 p=Path(directory);(p/'Program.cs').write_text(program);(p/'Fixtures.cs').write_text(fixtures)
 includes=''.join('<Compile Include="'+str(f)+'" />' for f in (root/'IRSpeedyVPN/Services/SingBox').glob('*.cs') if f.name not in exclude)
 (p/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup><ItemGroup>'+includes+'<PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',str(p/'Checks.csproj')],check=True)
