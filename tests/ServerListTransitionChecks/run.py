"""Production slot planner/timeline; WPF value types are inert Linux doubles."""
from pathlib import Path
import subprocess,sys,tempfile
root=Path(__file__).resolve().parents[2]
fixture=r'''
using System;
using System.Collections.Generic;
using System.Linq;
using IRSpeedyVPN.Components.ServerListControl;
using IRSpeedyVPN.Interfaces;
namespace System.Windows.Input {
 public class Cursor {} public static class Cursors { public static Cursor Hand=new Cursor(),Arrow=new Cursor(); }
}
namespace System.Windows.Media { public class ImageSource {} public class Brush {} }
namespace IRSpeedyVPN.Interfaces { public interface IVPNService { int ID {get;} } }
namespace IRSpeedyVPN.Common {
 static class LastSuccessfulServer { internal static string Key(IVPNService s)=>s==null?"":s.GetType().FullName+":"+s.ID; }
}
namespace IRSpeedyVPN.Components.ServerListControl {
 class GroupItem {
  public IVPNService Service; public string CountryName,Initials,SignalText,SignalToolTip="";
  public System.Windows.Media.ImageSource FlagSource;
  public System.Windows.Media.Brush SignalBrush;
  public bool IsSelectable=true,IsChecking,IsSelectedCountry,IsLastSuccessfulConnection;
  public long SortKey;
 }
}
class Service:IVPNService { public int ID {get;set;} }
class Program {
 static GroupItem G(int id,bool selected=false)=>new GroupItem{Service=new Service{ID=id},CountryName="کشور "+id,Initials="ک",SignalText=id+" ms",SortKey=id,IsSelectedCountry=selected};
 static ServerItem S(GroupItem g)=>new ServerItem(g);
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Unique(ServerListViewModel vm){
  var owners=new Dictionary<string,int>();
  for(int i=0;i<vm.Slots.Count;i++)for(int p=0;p<5;p++){
   var item=vm.Slots[i].GetPart((ServerRowPart)p);if(item==null)continue;
   if(owners.TryGetValue(item.Identity,out var owner)&&owner!=i)throw new Exception("duplicate identity in different displayed slots");owners[item.Identity]=i;
  }
 }
 static int Drain(ServerListViewModel vm){
  int n=0;ServerSortPass pass;
  while((pass=vm.TryBeginPass())!=null){if(n++>vm.Servers.Count*4+20)throw new Exception("queue failed to converge");for(int i=0;i<5;i++){vm.Reveal(pass,(ServerRowPart)i);Unique(vm);}vm.Finish(pass);Unique(vm);}
  if(!vm.Slots.Select(s=>s.DisplayedServer?.Identity).SequenceEqual(vm.Servers.Select(s=>s.Identity)))throw new Exception("lost/stale identity after drain");return n;
 }
 static void Main(){
  var a=G(1,true);var b=G(2);var c=G(3);var vm=new ServerListViewModel();int work=0;vm.WorkAvailable+=()=>work++;
  vm.Publish(new[]{S(a),S(b),S(c)},true);var stable=vm.Slots.ToArray();
  Check(!vm.IsTransitioning,"initial load has no light");var before=vm.Slots.Select(s=>s.DisplayedServer.Identity).ToArray();vm.CompleteImmediately();work=0;
  a.IsChecking=b.IsChecking=true;a.SignalText=b.SignalText="—";vm.Publish(new[]{S(c),S(b),S(a)},true,false);
  Check(work==0&&!vm.IsTransitioning&&vm.Slots.Select(s=>s.DisplayedServer.Identity).SequenceEqual(before),"check again/reset preserves visible order with no light");
  vm.Publish(new[]{S(c),S(b),S(a)},false,false);
  Check(vm.Slots.Select(s=>s.DisplayedServer.Identity).SequenceEqual(before),"reduced-motion reset also preserves visible order");
  Check(vm.Slots[0].DisplayedServer.IsChecking&&vm.Slots[0].SignalServer.SignalText=="—","pending indicators/data update normally");
  b.IsChecking=false;b.SignalText="80 ms";vm.Publish(new[]{S(b),S(a),S(c)},true);var pass=vm.TryBeginPass();var frozen=pass.Target;
  Check(pass.Index==0&&pass.Target.Service.ID==2&&a.IsChecking,"one country completion sorts immediately while another is checking");
  Check(vm.Servers[0].Service.ID==2&&vm.Slots[0].NameServer.Service.ID==1,"sorted truth is immediate but displayed country remains old until reveal");
  Check(vm.TryBeginPass()==null,"only one pass can run");vm.Reveal(pass,ServerRowPart.Signal);Unique(vm);
  Check(vm.Slots[1].IsVacant&&vm.Slots[0].NameServer.Service.ID==1&&vm.Slots[0].SignalServer.Service.ID==2,"first destination group conceals its source without prematurely changing the name");
  vm.Publish(new[]{S(c),S(b),S(a)},true,false);Check(vm.Slots[1].IsVacant,"progress cannot restore a stale identity in a vacated source");
  for(int i=0;i<200;i++){b.SignalText=i+" ms";vm.Publish(new[]{S(c),S(b),S(a)},true);}
  Check(ReferenceEquals(pass.Target,frozen)&&vm.Slots[0].SignalServer.SignalText=="80 ms","200 completions coalesce while active target stays frozen");vm.Finish(pass);Unique(vm);
  var next=vm.TryBeginPass();Check(next.Index==0&&next.Target.Service.ID==3,"next boundary replans first current mismatch against newest order");vm.Finish(next);Unique(vm);Drain(vm);
  Check(vm.Slots[1].SignalServer.SignalText=="199 ms"&&vm.Slots[2].SelectionServer.IsSelectedCountry&&a.IsSelectedCountry,"latest ping and selection follow stable identity");
  Check(vm.Slots.SequenceEqual(stable),"visual slots are never reordered/replaced");
  vm.Publish(vm.Servers.ToArray(),true);Check(vm.TryBeginPass()==null,"unchanged positions consume no pass time");
  a.SignalText="—";a.IsSelectable=false;vm.Publish(new[]{S(c),S(b),S(a)},true,false);
  Check(!vm.IsTransitioning&&vm.Slots[2].SignalServer.SignalText=="—"&&!vm.Slots[2].IsSelectable,"ping-only/failed data updates without any reveal pass");
  var cycle=new ServerListViewModel();var all=new[]{S(G(1)),S(G(2)),S(G(3))};cycle.Publish(all,true);cycle.Publish(new[]{all[1],all[2],all[0]},true);
  int n=Drain(cycle);Check(n==3&&n*ServerCometTiming.SORT_PASS_MS==2400,"three changed positions require three sequential 800ms passes");
  cycle.Publish(all,true);var old=cycle.TryBeginPass();cycle.Reveal(old,ServerRowPart.Signal);cycle.CompleteImmediately();var baseline=cycle.Slots.Select(s=>s.DisplayedServer.Identity).ToArray();
  cycle.Publish(all.Reverse(),true,false);cycle.Reveal(old,ServerRowPart.Name);cycle.Finish(old);
  Check(!cycle.IsTransitioning&&cycle.Slots.Select(s=>s.DisplayedServer.Identity).SequenceEqual(baseline),"new cycle cancels partial presentation and stale callbacks without light");Unique(cycle);
  cycle.Publish(new[]{all[2],S(G(4))},true);Drain(cycle);Check(cycle.Slots.Count==2&&cycle.Slots[1].DisplayedServer.Service.ID==4,"added/removed countries reconcile without duplicates or loss");
  cycle.Publish(new ServerItem[0],true);Check(cycle.Slots.Count==0&&!cycle.IsTransitioning,"empty source cancels immediately");
  cycle.Publish(new[]{S(G(9))},true);Drain(cycle);cycle.Publish(new[]{S(G(10))},false);Check(!cycle.IsTransitioning&&cycle.Slots[0].DisplayedServer.Service.ID==10,"single row and reduced-motion fallback apply latest state");
  var random=new Random(17);var churn=new ServerListViewModel();var many=Enumerable.Range(1,37).Select(id=>S(G(id,id==17))).ToArray();churn.Publish(many,true);
  for(int i=0;i<150;i++){churn.Publish(many.OrderBy(x=>random.Next()).ToArray(),true);var active=churn.TryBeginPass();if(active!=null){churn.Reveal(active,ServerRowPart.Signal);Unique(churn);for(int j=0;j<3;j++)churn.Publish(many.OrderBy(x=>random.Next()).ToArray(),true);churn.Finish(active);Unique(churn);}}
  Drain(churn);Check(churn.Slots.Select(s=>s.DisplayedServer.Service.ID).OrderBy(x=>x).SequenceEqual(Enumerable.Range(1,37)),"rapid replanning preserves every country with no duplicate visible owner");
  var large=new ServerListViewModel();var lots=Enumerable.Range(1,4096).Select(id=>S(G(id))).ToArray();large.Publish(lots,true);large.Publish(lots.Reverse(),true);int instant=0;ServerSortPass offscreen;while((offscreen=large.TryBeginPass())!=null){large.Finish(offscreen);instant++;}
  Check(instant==4096&&!large.IsTransitioning,"4096 unrealized passes drain iteratively without recursion");
  Check(ServerCometTiming.SORT_PASS_MS==800&&ServerCometTiming.FLASH_MS==400&&ServerCometTiming.BAND_WIDTH_DIP==170&&ServerCometTiming.SKEW_DEG==-18,"exact named timing and geometry constants");
  Check(Math.Abs(ServerCometTiming.Ease(.5)-.5)<1e-10&&ServerCometTiming.Ease(-1)==0&&ServerCometTiming.Ease(2)==1,"clamped accelerate-decelerate cosine easing");
  var timeline=new ServerCometTimeline(360,new[]{350d,160d,300d,40d,200d});var reveals=new List<int>();timeline.Advance(0,(i,t)=>reveals.Add(i));timeline.Advance(800,(i,t)=>reveals.Add(i));timeline.Advance(1200,(i,t)=>reveals.Add(i));
  Check(reveals.SequenceEqual(new[]{3,1,4,2,0}),"dropped frames reveal groups once in measured physical order, including RTL coordinates");
  Check(Math.Abs(ServerCometTiming.BandLeft(400,360)-(.5*(360+.85*170)-.85*170))<1e-8,"exact band/leading-edge coordinate formula");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='server-comet-') as directory:
    path=Path(directory)
    (path/'Program.cs').write_text(fixture)
    for name in ['ServerListViewModel.cs','ServerCometTimeline.cs']:
        (path/name).write_text((root/'IRSpeedyVPN/Components/ServerListControl'/name).read_text())
    (path/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
    subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',directory,'-v:q'],check=True)
