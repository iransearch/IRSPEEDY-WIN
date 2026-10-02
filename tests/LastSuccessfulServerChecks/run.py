"""Exercise persisted successful-row tracking and the production picker ordering."""
from pathlib import Path
import subprocess,sys,tempfile
root=Path(__file__).resolve().parents[2]
source=(root/'IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml.cs').read_text()
a=source.index('        public void MarkSuccessfulConnection(')
b=source.index('        private bool PrepareCountryPool(',a)
methods=source[a:b]
harness=r'''
using System;using System.Linq;using System.Collections.Generic;
using IRSpeedyVPN.Common;using IRSpeedyVPN.Interfaces;
namespace IRSpeedyVPN.Interfaces {
 public interface IVPNService { int ID {get;} object SelectedServerUrl {get;} }
 public interface ISmartFastConnection { bool IsSmartFast {get;} }
}
namespace IRSpeedyVPN.Resource {
 internal static class RegHelper {
  internal static string Value=""; internal static bool FailWrite;
  internal static string GetSettingValue(string key)=>Value;
  internal static void SetSettingValue(string key,string value){if(FailWrite)throw new Exception("fixture write failure");Value=value;}
 }
}
namespace IRSpeedyVPN.Common {internal static class LogHelper {internal static void WriteLog(Exception ex){}}}
class Service:IVPNService,ISmartFastConnection {
 public int ID {get;set;} public bool IsSmartFast {get;set;} public object SelectedServerUrl {get;set;}=new object();
}
class OtherService:Service {}
class GroupItem {
 public IVPNService Service;public bool IsLastSuccessfulConnection;public long SortKey;public string CountryName;
}
class Scroll {public int Calls;public void ScrollToTop(){Calls++;}}
class Picker {
 internal List<GroupItem> _allGroups=new List<GroupItem>();internal List<GroupItem> Visible;
 internal string Filter="";internal IVPNService Selected;internal Scroll ServerScroller=new Scroll();
 void ApplyFilter(){Visible=_allGroups.Where(g=>g.CountryName.Contains(Filter)).ToList();}
 internal void Reload(){var key=LastSuccessfulServer.Read();foreach(var g in _allGroups)g.IsLastSuccessfulConnection=LastSuccessfulServer.Key(g.Service)==key;ResortGroups();ApplyFilter();}
 internal void Reorder(){ResortGroups();}
'''
checks=r'''
}
class Program {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static Picker NewPicker(){return new Picker{_allGroups=new List<GroupItem>{
  new GroupItem{Service=new Service{ID=1},CountryName="Germany 1",SortKey=20},
  new GroupItem{Service=new Service{ID=2},CountryName="Germany 2",SortKey=80},
  new GroupItem{Service=new Service{ID=3},CountryName="Netherlands",SortKey=40}}};}
 static void Main(){
  var p=NewPicker();p.Reload();var chosen=p._allGroups.Single(g=>g.Service.ID==2).Service;
  p.MarkSuccessfulConnection(chosen);
  Check(p._allGroups[0].Service.ID==2&&p._allGroups.Count(g=>g.IsLastSuccessfulConnection)==1,"successful numbered row is pinned with exactly one marker");
  Check(p.ServerScroller.Calls==1,"successful connection restores the top of the country list");
  p._allGroups.Single(g=>g.Service.ID==1).SortKey=1;p.Reorder();
  Check(p._allGroups[0].Service.ID==2&&p._allGroups[1].Service.ID==1,"latency updates retain the pinned row and sort the remaining rows");
  p.Selected=p._allGroups.Single(g=>g.Service.ID==3).Service;p.Reorder();
  Check(p._allGroups[0].Service.ID==2,"selection and refresh alone never transfer the marker");
  var saved=IRSpeedyVPN.Resource.RegHelper.Value;
  p.MarkSuccessfulConnection(new Service{ID=1,IsSmartFast=true,SelectedServerUrl=null});
  Check(IRSpeedyVPN.Resource.RegHelper.Value==saved&&p._allGroups[0].Service.ID==2,"global Smart success retains the last country row");
  p.MarkSuccessfulConnection(new Service{ID=3,IsSmartFast=true});
  Check(p._allGroups[0].Service.ID==3&&p._allGroups.Count(g=>g.IsLastSuccessfulConnection)==1,"country-scoped Smart pool success transfers the marker");
  var reloaded=NewPicker();reloaded.Reload();
  Check(reloaded._allGroups[0].Service.ID==3&&reloaded._allGroups[0].IsLastSuccessfulConnection,"stored identity survives service-object replacement and a fresh picker");
  reloaded.Filter="Germany";reloaded.Selected=chosen;reloaded.Reorder();reloaded.Reload();
  Check(reloaded.Visible.All(g=>g.CountryName.Contains("Germany"))&&ReferenceEquals(reloaded.Selected,chosen),"pinning preserves search filtering and current selection");
  Check(LastSuccessfulServer.Key(new OtherService{ID=3})!=LastSuccessfulServer.Key(new Service{ID=3}),"same numeric ID in another service type does not inherit the star");
  var missing=NewPicker();missing._allGroups.RemoveAll(g=>g.Service.ID==3);missing.Reload();
  Check(!missing._allGroups.Any(g=>g.IsLastSuccessfulConnection)&&missing._allGroups[0].Service.ID==1,"removed server creates no phantom starred row");
  IRSpeedyVPN.Resource.RegHelper.FailWrite=true;
  p.MarkSuccessfulConnection(new Service{ID=1});
  Check(p._allGroups[0].Service.ID==1,"settings-write failure does not interrupt successful connection presentation");
 }
}
'''
with tempfile.TemporaryDirectory(prefix='last-successful-server-') as directory:
 p=Path(directory);(p/'Program.cs').write_text(harness+methods+checks)
 (p/'LastSuccessfulServer.cs').write_text((root/'IRSpeedyVPN/Common/LastSuccessfulServer.cs').read_text())
 (p/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
 subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet','run','--project',directory,'-v:q'],check=True)
