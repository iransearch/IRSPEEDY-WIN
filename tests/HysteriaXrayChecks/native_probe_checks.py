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
methods='\n'.join(method(n) for n in ['GetConfigEx','GenerateConfig','ApplyChainOutbound','Filloutbound','GetRejectedProbeLinks',
    'GetUrlTestConfig','ApplyEndpointOverride','CreateChainOutbound','ValidateProbeOutbound','FillOutboundForItem','CreateXraySocksOutbound','SerializeConfig'])
fixtures=(root/'tests/HysteriaXrayChecks/Fixtures.cs').read_text()
fixtures=fixtures.replace('Hysteria2, WireGaurd','Hysteria2, WireGaurd, Shadowsocksr, SSH, Tuic')
fixtures=fixtures.replace('public int port, alterId;','public int port, alterId, portEnd; public string packetEncoding, protocol, protocol_param, username, congestionControl, udpRelayMode;')
fixtures=fixtures.replace('public const string RealitySecurity', 'public static readonly List<string> vmessSecuritys=new List<string>{"auto"};\n public static readonly List<string> ssSecuritysInSagerNet='+re.search(r'new List<string> \{[^\n]+', (root/'IRSpeedyVPN/Services/V2Ray/Global.cs').read_text().split('ssSecuritysInSagerNet = ')[1]).group(0)+'\n public const string DefaultSecurity="auto",vmessProtocolLite="vmess",ssProtocolLite="shadowsocks",ssrProtocolLite="shadowsocksr",socksProtocolLite="socks",vlessProtocolLite="vless",trojanProtocolLite="trojan",wireguardProtocolLite="wireguard",sshProtocolLite="ssh";\n public const string RealitySecurity')
fixtures=fixtures.replace('public static string ToJson(object value)', 'public static bool IsNullOrEmpty(string v)=>string.IsNullOrEmpty(v); public static T FromJson<T>(string value)=>JsonConvert.DeserializeObject<T>(value); public static string ToJson(object value)')
program='''using System.Web.Script.Serialization;using System;using System.Linq;using System.Collections.Generic;using System.IO;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using IRSpeedyVPN.Services.SingBox;using v2rayN;using v2rayN.Mode;using v2rayN.Handler;
namespace System.Web.Script.Serialization {class JavaScriptSerializer {public T Deserialize<T>(string value)=>JsonConvert.DeserializeObject<T>(value);}}
namespace IRSpeedyVPN.Services.SingBox { public class ConfigGenerator {
public class EndpointOverride {public string Server;public int ServerPort;}
static void boundStreamSettings(VmessItem node,string direction,Outbound o){if(node.network=="throw")throw new InvalidOperationException("stream fixture");}
static void FillDns(SingBoxConfig c,VmessItem n,bool vpn,string[] excluded){}
static void FillInbound(SingBoxConfig c,VmessItem n,int port,bool vpn,bool share){}
static void FillRoute(SingBoxConfig c,VmessItem n,bool vpn,string[] files,bool chain,string[] excluded,bool game){}
static void FillLog(SingBoxConfig c,string name){}
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
var wgKey=Convert.ToBase64String(Enumerable.Repeat((byte)251,32).ToArray());
var wg="wg://wg.example:11912?private_key="+wgKey+"&public_key="+Uri.EscapeDataString(wgKey)
    +"&local_address=10.0.0.2/32-fd00::2/128&mtu=1407&enable_amnezia=true&jc=6&s4=13&h4=42"
    +"&i1=%3Cr%20164%3E&header_protection_key="+wgKey+"&content_padding_addition=21-34"
    +"&random_trailers=true&disable_cookies=true&persistent_keepalive_interval=25";
nodes[wg]=WireGuardEndpoint.ParseUri(wg);
nodes["bad-wg"]=new VmessItem{configType=EConfigType.WireGaurd,address="wg.example",port=11912,privateKey="invalid"};
var wgJson=ConfigGenerator.GetUrlTestConfig(new Dictionary<string,string[]>{{wg,new[]{"valid"}},{"bad-wg",null},{"valid",null}},1234,out tags,
    new Dictionary<string,ConfigGenerator.EndpointOverride>{{wg,new ConfigGenerator.EndpointOverride{Server="192.0.2.10",ServerPort=51820}}},rejected:rejected);
var endpoint=JObject.Parse(wgJson)["endpoints"].Single();
var peer=endpoint["peers"].Single();
var awg=endpoint["amnezia_wg"];
Check((string)endpoint["type"]=="wireguard"&&(string)endpoint["private_key"]==wgKey
    &&(string)peer["public_key"]==wgKey&&(string)awg["header_protection_key"]==wgKey
    &&(string)peer["address"]=="192.0.2.10"&&(int)peer["port"]==51820
    &&(int)endpoint["mtu"]==1407&&endpoint["address"].Count()==2&&(int)peer["persistent_keepalive_interval"]==25
    &&(int)awg["jc"]==6&&(int)awg["s4"]==13&&(string)awg["h4"]=="42"
    &&(string)awg["i1"]=="<r 164>"&&(string)awg["content_padding_addition"]=="21-34"
    &&(bool)awg["random_trailers"]&&(bool)awg["disable_cookies"]&&(string)endpoint["detour"]=="chain-0-1",
    "Throne AWG 3.1 profile is a native URL test endpoint with intact keys, options and chain");
Check(tags.Count==2&&tags["proxy"]==wg&&rejected.ContainsKey("bad-wg")
    &&ConfigGenerator.GetRejectedProbeLinks(wgJson,"create service: initialize endpoint[0]: invalid key",tags).SequenceEqual(new[]{wg}),
    "bad AWG inputs and native endpoint errors affect only the corresponding probe");
var direct=JObject.Parse(ConfigGenerator.GetConfigEx(wg,1234,false,false,"",false,false,null,null,null,false));
Check((string)direct["endpoints"].Single()["tag"]=="proxy"
    &&(string)direct["endpoints"][0]["peers"][0]["address"]=="wg.example"
    &&(int)direct["endpoints"][0]["peers"][0]["port"]==11912
    &&(string)direct["route"]["final"]=="proxy"&&!direct["outbounds"].Any(o=>(string)o["tag"]=="proxy"),
    "manual AWG connection targets the native peer and keeps proxy routing");
bool refused=false;
try{ConfigGenerator.GetConfigEx("wg://invalid",1234,false,false,"",false,false,null,null,null,false);}
catch(FormatException){refused=true;}
Check(refused,"a malformed manual WireGuard link cannot fall back to a local tunnel");
refused=false;
try{WireGuardEndpoint.ParseUri(wg.Replace("%3Cr%20164%3E","%3Cr%20164%3E%0Aallowed_ip%3D192.0.2.0/24"));}
catch(FormatException){refused=true;}
Check(refused,"WireGuard signature values cannot add a Core configuration line");
var legacy=WireGuardEndpoint.ParseUri("wg://legacy.example:51820?priv="+wgKey+"&pub="+wgKey+"&lp=10.0.0.2/32");
Check(!WireGuardEndpoint.Build(legacy,"proxy").ContainsKey("amnezia_wg"),"legacy WireGuard stays compatible");
}}
'''
# All simple model files; policy/routing helpers have platform dependencies.
exclude={'ConfigGenerator.cs','ParallelDnsRouting.cs','TunBrowserCompatibility.cs','VpnTimeZoneRouting.cs','WireGuardUnformatted.cs'}
with tempfile.TemporaryDirectory(prefix='native-probe-') as directory:
 p=Path(directory);(p/'Program.cs').write_text(program);(p/'Fixtures.cs').write_text(fixtures)
 includes=''.join('<Compile Include="'+str(f)+'" />' for f in (root/'IRSpeedyVPN/Services/SingBox').glob('*.cs') if f.name not in exclude)
 (p/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup><ItemGroup>'+includes+'<PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',str(p/'Checks.csproj')],check=True)
