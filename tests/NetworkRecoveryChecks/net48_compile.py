"""Compile production recovery integration/UI against real net48/WPF references.

Usage: python net48_compile.py /path/to/dotnet /path/to/net48/reference/directory
Application fields/window construction are doubles; networking and WPF APIs are real.
"""
from pathlib import Path
import subprocess, sys, tempfile

root = Path(__file__).resolve().parents[2]
sdk = Path(sys.argv[1]).parent
refs = Path(sys.argv[2])
compiler = next((sdk / 'sdk').glob('*/Roslyn/bincore/csc.dll'))
sources = ['Services/NetworkRecoverySession.cs', 'Services/TunnelPlusService.NetworkRecovery.cs',
           'Services/MainPoolRecoveryProof.cs', 'Common/ConnectionTlsTest.cs', 'UserControls/UCUserInfo.NetworkRecovery.cs']
sources += ['Services/Libcore/' + name + '.cs' for name in ['ProtorpcClient', 'LibcoreServiceClient', 'LibcoreProto', 'LibcoreTraffic', 'LibcoreAutoSelectors', 'LibcoreMessages']]
main = (root / 'IRSpeedyVPN/MainWindow.xaml.cs').read_text()
handler = main[main.index('        private void InternetConnectivityMonitor_AvailabilityChanged('):main.index('        private void MainWindow_Closed(')]
stubs = '''using System;using System.Threading;using System.Windows;using System.Windows.Controls;using IRSpeedyVPN.Services;
namespace IRSpeedyVPN.Common { static class LogHelper { internal static void WriteExLog(string s){} } }
namespace IRSpeedyVPN.Services {
 partial class TunnelPlusService {
  bool userCancelRequested,IsConnected;long connectionGeneration;int lastListenPort;IRSpeedyVPN.Info gInfo;object autoSelectorPlan;const int CorePort=19810;
  void Diagnostic(string s,string f){}void RequestConnectionState(string cause){}
 }
 class InternetAvailabilityChangedEventArgs:EventArgs {internal bool IsOnline;}
 static class InternetConnectivityMonitor {internal static bool? LastKnownOnline=>null;}
}
namespace IRSpeedyVPN {
 class Info { internal object CurrentService; }
 static class AppServices { internal static Info GlobalInfo; }
 class NetworkStatusToastWindow:Window {internal NetworkStatusToastWindow(Window owner){}internal void CloseAnimated(){} }
 partial class MainWindow:Window {
  NetworkStatusToastWindow networkStatusToast;bool ExitCleanupStarted;
''' + handler + '''
 }
}
namespace IRSpeedyVPN.UserControls {
 public partial class UCUserInfo:UserControl {
  IRSpeedyVPN.Info globalInfo;CancellationTokenSource connectionTestRequest;
  TextBlock NetworkRecoveryStatus;UIElement ConnectedCheckBadge;
 }
}
'''
with tempfile.TemporaryDirectory(prefix='network-recovery-net48-') as directory:
    path = Path(directory)
    (path / 'Stubs.cs').write_text(stubs)
    selected = ['mscorlib', 'System', 'System.Core', 'System.Net.Http', 'WindowsBase',
                'PresentationCore', 'PresentationFramework', 'System.Xaml']
    args = [str(sdk / 'dotnet'), str(compiler), '-nologo', '-target:library', '-langversion:7.3',
            '-nostdlib+', '-nowarn:0649,0169', '-out:' + str(path / 'Checks.dll')]
    args += ['-r:' + str(refs / (name + '.dll')) for name in selected]
    args += [str(root / 'IRSpeedyVPN' / name) for name in sources] + [str(path / 'Stubs.cs')]
    subprocess.run(args, check=True)
print('PASS production recovery state, service/TLS integration, notification handler and WPF UI compile against net48')
