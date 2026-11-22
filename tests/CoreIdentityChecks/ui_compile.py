"""Verify the user-only banner and compile its production method for WPF/net48.

Usage: ui_compile.py --check-only
       ui_compile.py /path/to/dotnet
       ui_compile.py /path/to/dotnet /path/to/csc.dll /path/to/net48/references
"""
from pathlib import Path
import subprocess, sys, tempfile
source = (Path(__file__).resolve().parents[2]/'IRSpeedyVPN/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
root = Path(__file__).resolve().parents[2]
xaml = (root/'IRSpeedyVPN/MainWindow.xaml').read_text(encoding='utf-8-sig')
assert 'CoreFailureDetails_Click' not in source and 'coreFailureDetails' not in source
assert 'نمایش جزئیات خطا و نسخهٔ Core' not in source and 'کپی گزارش خطا' not in source
assert 'ShowMessage(CoreRuntimeIdentity.UserConnectionError(Message))' in source
assert 'ShowMessage(CoreRuntimeIdentity.ConnectionFailureMessage)' in source
assert 'MouseLeftButtonUp="CoreFailureDetails_Click"' not in xaml
import xml.etree.ElementTree as ET
label = next(e for e in ET.fromstring(xaml).iter()
             if e.attrib.get('{http://schemas.microsoft.com/winfx/2006/xaml}Name') == 'lblErrorMessage')
assert label.attrib.get('IsHitTestVisible') == 'False' and 'ToolTip' not in label.attrib
methods = source[source.index('        void ShowMessage('):source.index('        public void ShowUserMessage(')]
assert 'CoreRuntimeIdentity.ConnectionFailureMessage' in methods and 'lblErrorMessage.ToolTip = null' in methods
assert 'Message = CoreRuntimeIdentity.UserConnectionError(Message)' in methods
assert 'Message.Length < 60 || Message == CoreRuntimeIdentity.CoreCompatibilityMessage' in methods
print('PASS connection error and Core upgrade banners with no technical popup/link/tooltip')
if sys.argv[1:] == ['--check-only']:
    raise SystemExit(0)
with tempfile.TemporaryDirectory(prefix='core-error-ui-') as temp:
    folder = Path(temp)
    (folder/'Handlers.cs').write_text('''using System; using System.Threading; using System.Windows; using IRSpeedyVPN.Services;
class CoreRuntimeIdentity { internal const string ErrorPrefix="[CoreFailure]\\n";
 internal const string ConnectionFailureMessage="خطا در برقراری اتصال VPN";
 internal const string CoreCompatibilityMessage="upgrade";
 internal static string UserConnectionError(string error) => ConnectionFailureMessage; }
class LogHelper { internal static void WriteLog(string value) {} }
class ConnectionAttemptDiagnostic {
 internal ConnectionAttemptDiagnostic(long version, string owner, Func<string> identity) {}
 internal void Step(string name, string result, string message) {}
}
static class gInfo { internal static object CurrentService => null; }
namespace IRSpeedyVPN.Services {
 class TunnelPlusService { internal string DiagnosticIdentity => "fixture"; }
}
class MainWindow : Window {
 long connectionRequestVersion;
 System.Windows.Controls.TextBlock lblErrorMessage = new System.Windows.Controls.TextBlock();
''' + methods + '}')
    if len(sys.argv) == 4:
        names = ['mscorlib.dll', 'System.dll', 'System.Core.dll', 'WindowsBase.dll',
                 'PresentationCore.dll', 'PresentationFramework.dll', 'System.Xaml.dll']
        references = [Path(sys.argv[3])/name for name in names]
        assert all(p.is_file() for p in references), 'WPF/net48 references required'
        subprocess.run([sys.argv[1], sys.argv[2], '-nologo', '-nostdlib+', '-target:library', '-langversion:7.3',
                        '-out:'+str(folder/'Checks.dll'), *['-r:'+str(p) for p in references], str(folder/'Handlers.cs')], check=True)
    else:
        (folder/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup><ItemGroup><Reference Include="PresentationFramework"/><Reference Include="PresentationCore"/><Reference Include="WindowsBase"/></ItemGroup></Project>')
        subprocess.run([sys.argv[1], 'build', str(folder/'Checks.csproj')], check=True)
print('PASS production generic error banner compiles with WPF/net48')

