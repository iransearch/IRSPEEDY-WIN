"""Compile the production feature and UI handler against real net48/WPF APIs.

This is a compatibility check, not a WPF runtime or complete application build.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parents[2]
linked = [
    'Common/VpnTimeZoneSession.cs', 'Common/VpnTimeZone.cs',
    'Common/TimeZoneLookup.cs', 'Common/PublicIpLookup.cs', 'Common/TimeZoneJournal.cs',
    'Common/WindowsTimeZoneSystem.cs', 'Services/SingBox/VpnTimeZoneRouting.cs',
    'UserControls/UCUserInfo.TimeZone.cs', 'UserControls/UCUserInfo.PublicIp.cs',
]
stubs = '''
using System;
using System.Windows;
using System.Windows.Controls;
namespace IRSpeedyVPN.Common {
 static class LogHelper { internal static void WriteLog(Exception e){} }
 static class ConnectionDiagnostics { internal static void Write(string stage,string fields){} internal static string Fingerprint(string value)=>value; }
}
namespace IRSpeedyVPN.Interfaces { interface IVPNService { int? HttpPort { get; } } }
namespace IRSpeedyVPN.Services {
 class Service : Interfaces.IVPNService { public int? HttpPort { get; set; } }
 class TunnelPlusService : Service { public bool IsTunnelConnected; internal string DiagnosticIdentity=>""; }
}
namespace IRSpeedyVPN {
 class MainWindow { public void ShowHintPopup(string text, UIElement anchor){} }
}
namespace IRSpeedyVPN.UserControls {
 class Info { public Interfaces.IVPNService CurrentService; public DateTime ConnectionTime; }
 public partial class UCUserInfo : UserControl {
  private Info globalInfo;
  private Button btnTimeZone;
  private TextBlock txtReceivedIp;
  private System.Windows.Shapes.Path TimeZoneClockIcon;
  private MainWindow GetMainWindow()=>null;
  private static string PersianDigits(string text)=>text;
 }
}
'''
with tempfile.TemporaryDirectory(prefix='time-zone-net48-') as directory:
    path = Path(directory)
    (path / 'Stubs.cs').write_text(stubs)
    items = '\n'.join(
        '<Compile Include="' + escape(str(root / 'IRSpeedyVPN' / source)) + '" />'
        for source in linked)
    refs = '\n'.join(
        '<Reference Include="' + name + '"><HintPath>$(NuGetPackageRoot)'
        'microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8/'
        + name + '.dll</HintPath></Reference>'
        for name in ['WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml', 'System.Net.Http'])
    (path / 'Checks.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net48</TargetFramework><LangVersion>7.3</LangVersion>
<NoWarn>CS0649</NoWarn></PropertyGroup><ItemGroup>''' + items + refs + '''
<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" />
<PackageReference Include="Newtonsoft.Json" Version="12.0.2" />
</ItemGroup></Project>''')
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else 'dotnet', 'build',
                    str(path / 'Checks.csproj'), '-v:q'], check=True)
print('PASS production time-zone/public-IP features and UI handlers compile against net48/WPF APIs')
