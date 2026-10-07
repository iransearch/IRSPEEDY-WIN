"""Compile the production candidate/tag ordering against deterministic shuffle input."""
from pathlib import Path
import subprocess, sys, tempfile

root = Path(__file__).resolve().parents[2]
source = (root/'IRSpeedyVPN/Services/TunnelPlusService.cs').read_text()
start = source.index('                IEnumerable<Url> orderedUrls = prioritizeFailed')
end = source.index('                candidateOrder\n', start)
candidate_code = source[start:end]
start = source.index('OutboundTags = tagToUrl.OrderBy(') + len('OutboundTags = ')
end = source.index('.ToList(),', start) + len('.ToList()')
tag_expression = source[start:end]
assert 'bool prioritizeFailed = false' in source
assert 'MaxConcurrency = 15,' in source

prefix = r'''
using System; using System.Collections.Generic; using System.Linq;
using IRSpeedyVPN.Models.NewService;
namespace IRSpeedyVPN.Models.NewService { public enum VPNType { NORMAL,VOD,CHAIN } }
static class Shuffle {
 public static int Calls;
 public static IEnumerable<T> Randomize<T>(this IEnumerable<T> input){Calls++;return input.Reverse();}
}
class Program {
 static string[] Candidates(List<Url> sourceUrls,bool prioritizeFailed,bool preserveOrder=false) {
'''
middle = r'''
 return candidateOrder;
 }
 static List<string> Tags(string[] candidateOrder,Dictionary<string,string> tagToUrl) {
 return
'''
tests = r''';
 }
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static Url U(string name,long latency)=>new Url{url=name,latency=latency,LastSuccessfulLatency=200};
 static void Main(){
 var urls=new List<Url>{U("success-1",80),U("failed-1",-1),U("unknown",0),U("failed-2",-1),U("success-2",90)};
 var ordered=Candidates(urls,true);
 Check(ordered.SequenceEqual(new[]{"failed-1","failed-2","success-1","unknown","success-2"}),"failed members precede others and both groups retain link order");
 Check(Shuffle.Calls==0 && urls[0].url=="success-1","disconnect ordering neither shuffles nor mutates the source pool");
 Check(Candidates(urls,false).SequenceEqual(urls.AsEnumerable().Reverse().Select(u=>u.url)) && Shuffle.Calls==1,"bootstrap and other callers retain the existing shuffle path");
 Check(Candidates(urls,true).SequenceEqual(ordered) && Shuffle.Calls==1,"repeated disconnect ordering stays stable despite success history");
 var duplicates=new List<Url>{U("same",100),U("other",100),U("same",-1),U("second-failed",-1)};
 Check(Candidates(duplicates,true).SequenceEqual(new[]{"same","second-failed","other"}),"duplicate link keeps its earliest priority and is tested once");
 var mapping=new Dictionary<string,string>{{"tag-success-2","success-2"},{"tag-unknown","unknown"},{"tag-failed-2","failed-2"},{"tag-success-1","success-1"},{"tag-failed-1","failed-1"}};
 Check(Tags(ordered,mapping).SequenceEqual(new[]{"tag-failed-1","tag-failed-2","tag-success-1","tag-unknown","tag-success-2"}),"RPC tag priority survives a different dictionary enumeration order");
 mapping.Remove("tag-failed-1");
 Check(Tags(ordered,mapping).SequenceEqual(new[]{"tag-failed-2","tag-success-1","tag-unknown","tag-success-2"}),"config-rejected members do not disturb remaining priority");
 var large=Enumerable.Range(0,20).Select(i=>U("success-"+i,100)).Concat(Enumerable.Range(0,20).Select(i=>U("failed-"+i,-1))).ToList();
 Check(Candidates(large,true).Take(15).All(u=>u.StartsWith("failed-")),"failed candidates occupy the first 15-concurrency submission slots");
 Check(large.All(u=>u.latency==100 || u.latency==-1),"ordering leaves latency and historical result values unchanged");
 int shuffles=Shuffle.Calls;
 Check(Candidates(urls,false,true).SequenceEqual(urls.Select(u=>u.url))&&Shuffle.Calls==shuffles,"manual global permutation reaches Core unchanged");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='url-priority-') as d:
 p=Path(d);(p/'Program.cs').write_text(prefix+candidate_code+middle+tag_expression+tests)
 (p/'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup><ItemGroup><Compile Include="'+str(root/'IRSpeedyVPN/Models/NewService/Url.cs')+'" /><PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',str(p/'Check.csproj'),'-v:q'],check=True)

