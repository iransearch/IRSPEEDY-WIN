// Existing application/service boundaries for cross-platform C# API checking.
// UI classes, their event handlers, the planner and animations are production code.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
namespace IRSpeedyVPN.Events { }
namespace IRSpeedyVPN.Models.Services { }
namespace IRSpeedyVPN.Interfaces {
 public interface IHasTitle { string Title {get;} }
 public interface IVPNService {
  int ID {get;} byte Order {get;} string Name {get;set;} string Country {get;} string CountryCode {get;}
  byte CountryIndex {get;set;} string[] Protocols {get;} string SelectedProtocol {get;}
  bool IsUrlTestSupported {get;} long UrlTestSpeed {get;} Url SelectedServerUrl {get;set;}
  List<Url> GetServerUrls(); long UrlTest();
 }
 public interface ISmartFastConnection { bool IsSmartFast {get;} void SetSmartFastUrls(string[] urls); }
}
namespace IRSpeedyVPN.Models { internal class GlobalInfo {public string Username;public IVPNService CurrentService;} }
namespace IRSpeedyVPN.Models.NewService {
 public class Url {public string url;public object extra_field_2;public int status;public long latency,LastSuccessfulLatency;public DateTime latencychkTime,LastSuccessfulCheckTime;}
 internal class ServerEx { public int ID,status;public string Country;public List<Url> urls; }
 internal class UserAccount {public IRSpeedyVPN.Residential.ResidentialConfig residential_vpn;public object GroupID,group_id,pid;public string GroupName,group_name;}
 internal class AccountInfoEx {public IRSpeedyVPN.Residential.ResidentialConfig residential_vpn;public UserAccount UserAccount;}
}
namespace IRSpeedyVPN.Common {
 static class LogHelper { public static void WriteExLog(string s){}public static void WriteLog(Exception e){} }
 static class LastSuccessfulServer {
  internal static string Key(IVPNService s)=>s?.ID.ToString()??"";internal static string Read()=>"";
  internal static bool TryRecord(IVPNService s,out string key){key=Key(s);return true;}
 }
 class ThemeManager {internal static ThemeManager Instance=new ThemeManager();internal Brush GetLiveBrush(string key)=>Brushes.Green;}
 static class TestExtensions {public static IEnumerable<T> Randomize<T>(this IEnumerable<T> s)=>s;public static IEnumerable<Url> OrderByHysteriaFirst(this IEnumerable<Url> s)=>s;}
 static class UrlTestCoordinator {internal static bool AbortRequested;internal static void CancelAll(){}internal static void BeginBatch(){} }
}
namespace IRSpeedyVPN.Resource { static class RegHelper {internal static string GetSettingValue(string key)=>"";internal static void SetSettingValue(string key,string value){} } }
namespace IRSpeedyVPN.Services {
 class TunnelPlusService:IVPNService,ISmartFastConnection {
  readonly ServerEx server;public TunnelPlusService(ServerEx s,GlobalInfo g){server=s;}
  public static string selectedChain;public string Name{get;set;}public int ID=>server.ID;public byte Order=>0;public string Country=>server.Country;public string CountryCode=>server.Country;
  public byte CountryIndex{get;set;}public string[] Protocols=>new string[0];public string SelectedProtocol=>null;public bool IsUrlTestSupported=>true;public long UrlTestSpeed=>0;
  public Url SelectedServerUrl{get;set;}public List<Url> GetServerUrls()=>server.urls;public long UrlTest()=>0;public bool IsSmartFast=>true;public void SetSmartFastUrls(string[] urls){}
  public void UrlTestFull(object a,bool b,Action<long> progress,Func<bool> cancel,CancellationToken token,bool prioritizeFailed,Action<Url,long> memberProgress){}
  public void ResetListTestResult(){}public void UpdateListTestResult(){}
  public static void TestRefreshBatch(Url[] urls,GlobalInfo g,CancellationToken c,Action<Url,long> progress,Action<Url,long> complete){}
 }
 class ServiceFactory {public List<IVPNService> Services=new List<IVPNService>();}
 class ServerCheckCache {
  internal ServerCheckCache(string a,string b){}internal IVPNService NextService;internal bool ShowInitialProgress,PrioritizeFailedServers;
  internal void Bind(IVPNService[] s){}internal void RestartForLogin(){}internal void StopRound(){}internal void RestartFromFirstCountry(){}internal void PersistQueue(){}
  internal void Restore(IVPNService s){}internal void Record(IVPNService s,DateTime t){}internal void CompleteService(IVPNService s){}internal void RecordConnectionResults(IVPNService s){}
  internal void RecordRefreshMember(IVPNService s,Url u,long l,DateTime t){}internal bool ClearForManualRefresh(IVPNService[] s)=>true;internal void CompleteRefreshRow(IVPNService s){}internal void FinishManualRefresh(IVPNService[] s){}
 }
 class ServerRefreshPlan {
  internal ServerRefreshPlan(IVPNService[] s){}internal int Total;internal bool HasPending;
  internal class Owner {internal IVPNService Service;internal Url Url;}
  internal class Candidate {internal Url Probe;internal Owner[] Owners;internal bool Completed;internal long BestSuccessfulLatency;}
  internal Candidate[] TakeBatch()=>new Candidate[0];internal IVPNService[] Complete(Candidate c)=>new IVPNService[0];
 }
}
namespace IRSpeedyVPN.Windows {class SpeedyShieldSetting:Window{} }
namespace IRSpeedyVPN {
 static class AppServices {internal static IRSpeedyVPN.Services.ServiceFactory ServiceFactory=new IRSpeedyVPN.Services.ServiceFactory();internal static GlobalInfo GlobalInfo=new GlobalInfo();}
 class MainWindow:Window {
  internal void LogoutFromSettings(){}internal void SetHeaderIcons(object owner,List<HeaderIconRegistration> icons){}
  internal void ClearHeaderIcons(object owner){}internal void OpenSettings(IVPNService service){}internal void ShowHintPopup(string text){}
 }
}
namespace IRSpeedyVPN.UserControls {
 public partial class UCServerList {
  readonly TextBox txtSearch=new TextBox();readonly Button btnConnect=new Button();readonly RadioButton vpnTab=new RadioButton(),residentialTab=new RadioButton();
  readonly IRSpeedyVPN.Components.ServerListControl.ServerCountryPicker vpnCountryPicker=new IRSpeedyVPN.Components.ServerListControl.ServerCountryPicker(),residentialCountryPicker=new IRSpeedyVPN.Components.ServerListControl.ServerCountryPicker();
  readonly IRSpeedyVPN.Residential.ResidentialTrafficBar trafficBar=new IRSpeedyVPN.Residential.ResidentialTrafficBar();
  void InitializeComponent(){}
 }
}
namespace IRSpeedyVPN.Components.ServerListControl {
 public partial class ServerCountryPicker {
  readonly ItemsControl icCountries=new ItemsControl();readonly ScrollViewer ServerScroller=new ScrollViewer();readonly ServerListCometOverlay CometOverlay=new ServerListCometOverlay();
  readonly Grid SmartCardHost=new Grid(),smartCheck=new Grid();readonly TextBlock EmptyResults=new TextBlock(),MoreLocations=new TextBlock();
  void InitializeComponent(){}
 }
}

