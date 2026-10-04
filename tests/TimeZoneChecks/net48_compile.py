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
    'Common/TimeZoneLookup.cs', 'Common/TimeZoneJournal.cs',
    'Common/WindowsTimeZoneSystem.cs', 'Services/SingBox/VpnTimeZoneRouting.cs',
    'UserControls/UCUserInfo.TimeZone.cs',
]
stubs = '''
using System;
using System.Windows;
using System.Windows.Controls;
namespace IRSpeedyVPN.Common { static class LogHelper { internal static void WriteLog(Exception e){} } }
namespace IRSpeedyVPN.Services {
 class Service { public int? HttpPort; }
 class TunnelPlusService : Service { public bool IsTunnelConnected; }
}
namespace IRSpeedyVPN {
 class MainWindow { public void ShowHintPopup(string text, UIElement anchor){} }
}
namespace IRSpeedyVPN.UserControls {
 class Info { public Services.Service CurrentService; }
 public partial class UCUserInfo : UserControl {
  private Info globalInfo;
  private Button btnTimeZone;
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
print('PASS production time-zone feature and clock handler compile against net48/WPF APIs')
