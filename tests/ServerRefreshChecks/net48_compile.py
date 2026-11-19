"""Compile production refresh controller, picker and header against net48/WPF.

Application dependencies and Core transport are doubles. This does not build
Windows XAML or exercise a real VPN.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parents[2]
source = (root / 'IRSpeedyVPN/UserControls/UCServerList.xaml.cs').read_text()
region = source.split('        #region Background URL tests', 1)[1].split('        #endregion', 1)[0]
header = source[source.index('        private void UpdateHeaderIcons()'):source.index('        private void ClearHeaderIcons()')]
main = (root / 'IRSpeedyVPN/MainWindow.xaml.cs').read_text()
render = main[main.index('        private void RenderHeaderIcons('):main.index('        void ShowNotifiy(')]
linked = [
 'Services/ServerCheckCache.cs', 'Services/ServerRefreshPlan.cs', 'Services/ServerProbeScheduler.cs', 'Services/UrlTestCoordinator.cs',
 'Services/TunnelPlusService.RefreshTests.cs', 'Services/UrlTestRetryPolicy.cs',
 'Services/Libcore/LibcoreMessages.cs', 'Interfaces/IServer.cs', 'Models/NewService/Url.cs',
 'UserControls/UCServerList.ServerResultsRefresh.cs',
 'Models/HeaderIconRegistration.cs', 'Common/HeaderRefreshMotion.cs',
 'Components/ServerListControl/ServerCountryPicker.xaml.cs',
 'Components/ServerListControl/ServerCountryPicker.Transitions.cs',
 'Components/ServerListControl/ServerCountryPicker.Motion.cs',
 'Components/ServerListControl/ServerCountryPicker.ProbeShine.cs',
 'Components/ServerListControl/ServerCountryPicker.StarGlow.cs',
 'Components/ServerListControl/ServerListViewModel.cs',
 'Components/ServerListControl/ServerListRanking.cs',
 'Components/ServerListControl/ServerCometTimeline.cs',
 'Components/ServerListControl/ServerListCometOverlay.cs',
 'Components/ServerListControl/ServerListSweepAnimator.cs',
]
stubs = r'''using System;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Diagnostics;
using System.Windows;using System.Windows.Controls;using System.Windows.Media;
using IRSpeedyVPN.Common;using IRSpeedyVPN.Interfaces;using IRSpeedyVPN.Models;using IRSpeedyVPN.Models.NewService;using IRSpeedyVPN.Services;
using IRSpeedyVPN.Services.Libcore;
namespace IRSpeedyVPN.Models { public enum VPNType { NORMAL,CHAIN } internal class GlobalInfo { public object CurrentService; } }
namespace IRSpeedyVPN.Interfaces {
 public interface IVPNService { int ID{get;}string Name{get;}string CountryCode{get;}string Country{get;}bool IsUrlTestSupported{get;}Url SelectedServerUrl{get;set;}List<Url> GetServerUrls();void UrlTest(); }
 interface ISmartFastConnection { void SetSmartFastUrls(string[] urls); }
}
namespace IRSpeedyVPN.Common {
 static class LogHelper { public static void WriteLog(Exception e){}public static void WriteExLog(string s){} }
 static class CoreDiagnosticMetadata {internal static int ListenerPid(int port)=>throw new NotSupportedException();}
 static class FreePortManager {internal static int Dequeue()=>throw new NotSupportedException();internal static void Enqueue(int port){} }
 class ThemeManager { internal static ThemeManager Instance=new ThemeManager();internal Brush GetLiveBrush(string s)=>Brushes.Gray; }
 static class UrlOrder { internal static IEnumerable<Url> OrderByHysteriaFirst(this IEnumerable<Url> u)=>u; }
 static class LastSuccessfulServer { internal static string Read()=>null;internal static string Key(IVPNService s)=>null;internal static bool TryRecord(IVPNService s,out string key){key=null;return true;} }
}
namespace IRSpeedyVPN.Services {
 partial class TunnelPlusService : IVPNService {
  IServer server;GlobalInfo gInfo;long urlTestSpeed;DateTime lastUrlTest;string selectedUrl;
  public TunnelPlusService(IServer server,GlobalInfo info){this.server=server;gInfo=info;}
  public int ID=>server.ID;public string Name{get;set;}public string CountryCode=>server.Country;public string Country=>server.Country;
  public bool IsUrlTestSupported=>true;public Url SelectedServerUrl{get;set;}
  public List<Url> GetServerUrls()=>server.urls;public void UrlTest(){}
  internal static string selectedChain;
  static readonly object ownedExitGate=new object();
  static readonly HashSet<Process> ownedExitProcesses=new HashSet<Process>();
  static bool applicationExiting;
  class SniConfig {}
  class SniRuntime {internal string ListenHost,ConfigPath;internal int ListenPort;internal Process Process;}
  string ResolveCorePath()=>null;
  string GetSniLink(string link)=>null;
  string GetChainLink(string link)=>null;
  string GetDefaultChainLink()=>null;
  string[] BuildChainLinks(string link,string[] defaults)=>defaults;
  SniConfig ParseSniLink(string link)=>throw new NotSupportedException();
  SniRuntime StartSniRuntime(SniConfig config,bool persistent,Func<int> remaining,CancellationToken token)=>throw new NotSupportedException();
  void ReleaseSniListenPort(int port){}
  bool TryKillProcess(Process process)=>throw new NotSupportedException();
 }
 class CiscoService:TunnelPlusService {
  internal CiscoService(IServer server,GlobalInfo info):base(server,info){}
  internal static ServerProbeMeasurement TestDisplayServer(Url url,CancellationToken token)=>throw new NotSupportedException();
 }
}
namespace IRSpeedyVPN.Services.SingBox {
 static class ConfigGenerator {
  internal class EndpointOverride {internal string Server;internal int ServerPort;}
  internal static string GetUrlTestConfig(Dictionary<string,string[]> links,int port,out Dictionary<string,string> tags,
   Dictionary<string,EndpointOverride> endpoints,Dictionary<string,Tuple<int,string,string>> socks)=>throw new NotSupportedException();
 }
}
namespace IRSpeedyVPN.Services.Xray {
 static class ConfigGenerator {
  internal const string UrlTestOutboundDnsStrategy="";
  internal class XraySocksInfo {internal string Link,Tag,User,Pass;internal int Port;}
  internal static bool LinkNeedsXrayForUrlTest(string link)=>throw new NotSupportedException();
  internal static string UrlTestRefusalReason(string link)=>throw new NotSupportedException();
  internal static string GetUrlTestXrayConfig(List<XraySocksInfo> socks,bool useCoreDns)=>throw new NotSupportedException();
 }
}
namespace IRSpeedyVPN.Services.Libcore {
 sealed class LibcoreServiceClient {
  internal LibcoreServiceClient(string host,int port){}
  internal TestResp TestWithDeadline(TestReq request,int timeout,CancellationToken token)=>throw new NotSupportedException();
 }
}
namespace IRSpeedyVPN.Residential {
 class ResidentialService:TunnelPlusService {
  internal ResidentialService(IServer server,GlobalInfo info):base(server,info){}
  internal string City,Isp;
 }
 static class ResidentialPresentation {
  internal static string ConnectionUri(string link)=>link;
  internal static long DisplayPing(long latency)=>latency;
  internal static string DisplayIsp(string isp)=>isp;
  internal static string LocationSeparator(string city,string isp)=>"";
  internal static string LocationLine(string city,string isp)=>"";
 }
 class ResidentialCatalog {
  internal Task<bool> RefreshAsync(bool force,CancellationToken cancellation)=>Task.FromResult(false);
 }
}
'''
stubs += r'''namespace IRSpeedyVPN.Components.ServerListControl {
 public partial class ServerCountryPicker {
  Grid SmartCardHost=new Grid(),EmptyResults=new Grid(),smartCheck=new Grid();
  ServerListCometOverlay CometOverlay=new ServerListCometOverlay();
  TextBlock MoreLocations=new TextBlock();
  ScrollViewer ServerScroller=new ScrollViewer();ItemsControl icCountries=new ItemsControl();void InitializeComponent(){}
 }
}
namespace IRSpeedyVPN {
 public class MainWindow : Window {
  StackPanel panelHeaderPrimaryIcons=new StackPanel(),panelHeaderSecondaryIcons=new StackPanel();
  public void LogoutFromSettings(){}public void SetHeaderIcons(object owner,IEnumerable<HeaderIconRegistration> icons){}
''' + render + '\n} }\n'
stubs += r'''namespace IRSpeedyVPN.UserControls {
 public partial class UCServerList : UserControl {
  IVPNService[] _currentServices;GlobalInfo globalInfo;CancellationTokenSource _urlTestCts;
  Components.ServerListControl.ServerCountryPicker countryPicker=new Components.ServerListControl.ServerCountryPicker();
  bool _isUrlTestSupported;IVPNService selectedService;
  bool isResidentialTab;IRSpeedyVPN.Residential.ResidentialCatalog residential;
  internal Func<Func<bool>,CancellationToken,Task<bool>> RefreshServerListRequest {get;set;}
  void TrySelectResidentialFastest(){}void UpdateResidentialVisuals(){}
  MainWindow GetMainWindow()=>null;IVPNService GetFallbackService()=>null;void RemoveBaseService(){}void OpenServiceSettings(){}
''' + region + header + '\n} }\n'
with tempfile.TemporaryDirectory(prefix='server-refresh-net48-') as directory:
    path = Path(directory)
    (path / 'Stubs.cs').write_text(stubs)
    items = '\n'.join('<Compile Include="' + escape(str(root / 'IRSpeedyVPN' / item)) + '" />' for item in linked)
    refs = '\n'.join('<Reference Include="' + name + '"><HintPath>$(NuGetPackageRoot)microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8/' + name + '.dll</HintPath></Reference>' for name in ['WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml'])
    (path / 'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><LangVersion>7.3</LangVersion><NuGetAudit>false</NuGetAudit><NoWarn>CS0649;CS0169;CS0414</NoWarn></PropertyGroup><ItemGroup>' + items + refs + '<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" /><PackageReference Include="Newtonsoft.Json" Version="12.0.2" /></ItemGroup></Project>')
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet', 'build', str(path / 'Checks.csproj'), '-v:q'], check=True)
print('PASS production refresh controller, batch runner, picker, callbacks and header compile against net48/WPF APIs')
