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
namespace IRSpeedyVPN.Models {
 public enum VPNType { NORMAL,CHAIN }
 internal class GlobalInfo {public string Username;public IVPNService CurrentService;public Settings settings;}
 internal class Settings {public Setting setting;}
 internal class Setting {public string url_test;}
}
namespace IRSpeedyVPN.Models.NewService {
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
}
namespace IRSpeedyVPN.Resource { static class RegHelper {internal static string GetSettingValue(string key)=>"";internal static void SetSettingValue(string key,string value){} } }
namespace IRSpeedyVPN.Services {
 class TunnelPlusService:IVPNService,ISmartFastConnection {
  readonly ServerEx server;public TunnelPlusService(ServerEx s,GlobalInfo g){server=s;}
  public static string selectedChain;public string Name{get;set;}public int ID=>server.ID;public byte Order=>0;public string Country=>server.Country;public string CountryCode=>server.Country;
  public byte CountryIndex{get;set;}public string[] Protocols=>new string[0];public string SelectedProtocol=>null;public bool IsUrlTestSupported=>true;public long UrlTestSpeed=>0;
  public Url SelectedServerUrl{get;set;}public List<Url> GetServerUrls()=>server.urls;public long UrlTest()=>0;public bool IsSmartFast=>true;public void SetSmartFastUrls(string[] urls){}
  public void UpdateListTestResult(){}
  internal static ServerProbeMeasurement TestRefreshBatch(Url url,GlobalInfo g,string testUrl,CancellationToken token)=>throw new NotSupportedException();
 }
 class CiscoService : TunnelPlusService {
  internal CiscoService(ServerEx server,GlobalInfo info):base(server,info){}
  internal static ServerProbeMeasurement TestDisplayServer(Url url,CancellationToken token)=>throw new NotSupportedException();
 }
 class ServiceFactory {public List<IVPNService> Services=new List<IVPNService>();}
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

