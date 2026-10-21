"""Exercise the production transition VM with a manually advanced view clock.

WPF primitives are inert doubles on Linux; rendering needs the Windows smoke test
in docs/server-list-transitions.md. No production scheduling logic is duplicated.
"""
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
fixture = r'''
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
class Clock {
 public readonly Dictionary<ServerDisplaySlot,ServerRowTransition> Active=new Dictionary<ServerDisplaySlot,ServerRowTransition>();
 public readonly List<ServerRowTransition> Started=new List<ServerRowTransition>();
 readonly HashSet<ServerDisplaySlot> Attached=new HashSet<ServerDisplaySlot>();
 public void Attach(ServerListViewModel vm) { foreach(var slot in vm.Slots) if(Attached.Add(slot))slot.TransitionRequested+=Start; }
 void Start(ServerDisplaySlot slot,ServerRowTransition t){Active[slot]=t;Started.Add(t);}
 public void FinishWave() {
  var wave=Active.ToArray();Active.Clear();foreach(var pair in wave)pair.Key.Finish(pair.Value);
 }
 public void Drain(ServerListViewModel vm){int limit=20;while(vm.IsTransitioning&&limit-->0)FinishWave();Check(!vm.IsTransitioning,"clock drains without a stale completion/deadlock");}
 public static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
}
class Program {
 static GroupItem Group(int id,bool selected=false)=>new GroupItem{Service=new Service{ID=id},CountryName="کشور "+id,Initials="ک",SignalText=id+" ms",SortKey=id,IsSelectedCountry=selected};
 static ServerItem Snap(GroupItem g)=>new ServerItem(g);
 static void Check(bool value,string name)=>Clock.Check(value,name);
 static void Main(){
  var a=Group(1,true);var b=Group(2);var c=Group(3);var vm=new ServerListViewModel();
  vm.Publish(new[]{Snap(a),Snap(b),Snap(c)},true,0);var clock=new Clock();clock.Attach(vm);
  Check(!vm.IsTransitioning&&vm.Slots.Count==3,"initial population is immediate");
  b.SortKey=0;
  vm.Publish(new[]{Snap(b),Snap(a),Snap(c)},true,0);
  Check(vm.Servers[0].Service.ID==2&&vm.Slots[0].DisplayedServer.Service.ID==1,"sorted truth changes before displayed slots");
  Check(clock.Started.Count==2&&clock.Started[0].StaggerIndex==0&&clock.Started[1].StaggerIndex==1,"only reassigned rows sweep, in top-to-bottom order");
  var old=vm.Slots[0].DisplayedServer; a.CountryName="changed live";a.SignalText="999 ms";a.IsSelectedCountry=false;
  Check(old.CountryName=="کشور 1"&&old.SignalText=="1 ms"&&old.IsSelectedCountry,"old country, ping and selection are immutable during a sweep");
  var first=clock.Active[vm.Slots[0]];vm.Slots[0].Reveal(first);
  Check(vm.Slots[0].DisplayedServer.Service.ID==2&&!vm.Slots[0].DisplayedServer.IsSelectedCountry,"logical identity swaps only on reveal");
  clock.FinishWave();
  Check(vm.Slots[1].DisplayedServer.Service.ID==1&&vm.Slots[1].DisplayedServer.IsSelectedCountry,"selection follows server identity into its new slot");
  clock.Started.Clear();vm.Publish(vm.Servers.ToArray(),true,0);
  Check(!vm.IsTransitioning&&clock.Started.Count==0,"identical publication skips animation");

  b.SignalText="—";b.IsSelectable=false;b.IsLastSuccessfulConnection=true;
  vm.Publish(new[]{Snap(b),vm.Servers[1],Snap(c)},true,0);
  Check(clock.Active.Count==1&&!clock.Active.Values.Single().ChangesIdentity,"ping/failure/star updates use a short fade without an identity swap");
  clock.Drain(vm);
  Check(vm.Slots[0].DisplayedServer.SignalText=="—"&&!vm.Slots[0].DisplayedServer.IsSelectable&&vm.Slots[0].DisplayedServer.IsLastSuccessfulConnection,"failed ping and star reach the displayed snapshot");

  b.IsLastSuccessfulConnection=false;c.SortKey=0;b.SortKey=1;a.SortKey=2;
  clock.Started.Clear();vm.Publish(new[]{Snap(c),Snap(b),Snap(a)},true,0);
  a.SortKey=0;c.SortKey=1;b.SortKey=2;
  for(int i=0;i<200;i++){b.SignalText=i+" ms";vm.Publish(new[]{Snap(b),Snap(c),Snap(a)},true,0);}
  Check(vm.Servers[2].SignalText=="199 ms"&&clock.Started.Count==3,"rapid updates update the truth immediately and do not enqueue 200 animations");
  clock.FinishWave();
  Check(clock.Started.Count==6&&clock.Active.Values.All(t=>t.To==null||t.To.SignalText!="198 ms"),"next wave uses only the latest pending snapshot");
  clock.Drain(vm);
  Check(vm.Slots[2].DisplayedServer.SignalText=="199 ms","no stale ping remains after the final wave");

  b.SortKey=0;a.SortKey=1;c.SortKey=2;
  vm.Publish(new[]{Snap(b),Snap(a),Snap(c)},true,1);
  Check(clock.Active[vm.Slots[1]].StaggerIndex==0&&clock.Active[vm.Slots[2]].StaggerIndex==1,"scrolling starts staggering from the first visible slot");
  var stale=clock.Active.ToArray();vm.Publish(new[]{Snap(c)},true,0);vm.CompleteImmediately();
  foreach(var pair in stale){pair.Key.Reveal(pair.Value);pair.Key.Finish(pair.Value);}clock.Active.Clear();
  Check(vm.Slots.Count==1&&vm.Slots[0].DisplayedServer.Service.ID==3&&!vm.IsTransitioning,"minimize/disable completes the latest data and invalidates old clocks");
  a.SortKey=0;b.SortKey=1;
  clock.Started.Clear();vm.Publish(new[]{Snap(a),Snap(b)},true,0);clock.Drain(vm);
  Check(vm.Slots.Count==2&&vm.Slots[1].DisplayedServer.Service.ID==2,"new unrealized slots populate without waiting for a missing view");
  clock.Attach(vm);vm.Publish(new[]{Snap(b)},true,0);clock.Drain(vm);
  Check(vm.Slots.Count==1&&vm.Slots[0].DisplayedServer.Service.ID==2,"removed rows finish revealing before trailing slots are trimmed");
  vm.Publish(new ServerItem[0],false,0);
  Check(vm.Slots.Count==0&&vm.Servers.Count==0,"empty list and animation-disabled path complete immediately");
  vm.Publish(new[]{Snap(a)},true,0);
  Check(vm.Slots.Count==1&&!vm.IsTransitioning,"single row without a realized view never waits for an animation");
  clock.Attach(vm);vm.Publish(new[]{Snap(c)},true,0);
  Check(vm.IsTransitioning&&clock.Active.Values.Single().ChangesIdentity,"single realized row supports a complete identity reveal");
  vm.Publish(new[]{Snap(b)},false,0);clock.FinishWave();
  Check(!vm.IsTransitioning&&vm.Slots[0].DisplayedServer.Service.ID==2,"disabling animations mid-sweep immediately commits the latest snapshot");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='server-list-transition-') as directory:
    path = Path(directory)
    (path / 'Program.cs').write_text(fixture)
    (path / 'ServerListViewModel.cs').write_text((root / 'IRSpeedyVPN/Components/ServerListControl/ServerListViewModel.cs').read_text())
    (path / 'ServerListRanking.cs').write_text((root / 'IRSpeedyVPN/Components/ServerListControl/ServerListRanking.cs').read_text())
    (path / 'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet', 'run', '--project', directory, '-v:q'], check=True)
