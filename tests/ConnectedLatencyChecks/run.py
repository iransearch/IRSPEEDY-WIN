from pathlib import Path
import subprocess, sys, tempfile
root=Path(__file__).resolve().parents[2]
s=(root/'IRSpeedyVPN/UserControls/UCUserInfo.xaml.cs').read_text(encoding='utf-8-sig')
method=s[s.index('        internal static long ResolveRecordedLatency'):s.index('        private void UserControl_IsVisibleChanged')]
source="""using System; using System.Linq; using System.Collections.Generic;
class Url { public long latency; }
class IVPNService { public bool IsUrlTestSupported=true; public Url SelectedServerUrl; public long UrlTestSpeed=-1; public List<Url> Urls=new List<Url>(); public List<Url> GetServerUrls()=>Urls; }
class Program {
"""+method+"""
static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
static void Main(){
 var s=new IVPNService();var marker=new Url{latency=-1};s.SelectedServerUrl=marker;s.Urls.Add(marker);s.Urls.Add(new Url{latency=140});s.Urls.Add(new Url{latency=90});
 Check(ResolveRecordedLatency(s,true)==90,"country pool retains recorded minimum after speed reset");
 Check(ResolveRecordedLatency(s,false)==-1,"fixed node does not borrow sibling latency");
 marker.latency=200;Check(ResolveRecordedLatency(s,false)==200,"fixed node uses own result");
 s.SelectedServerUrl=null;Check(ResolveRecordedLatency(s,true)==90,"global pool recorded minimum");
 s.Urls.Clear();s.UrlTestSpeed=70;Check(ResolveRecordedLatency(s,true)==70,"service result fallback");
 s.UrlTestSpeed=-1;Check(ResolveRecordedLatency(s,true)<=0,"missing result remains unknown");
 s.IsUrlTestSupported=false;Check(ResolveRecordedLatency(s,false)==0,"unsupported service stays unknown");
}}
"""
with tempfile.TemporaryDirectory() as d:
 p=Path(d);(p/'Program.cs').write_text(source)
 (p/'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
 subprocess.run([sys.argv[1],'run','--project',str(p/'Check.csproj'),'-v:q'],check=True)
