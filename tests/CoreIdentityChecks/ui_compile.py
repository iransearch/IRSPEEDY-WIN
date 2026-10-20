"""Compile the production error details handlers against WPF/net48 references."""
from pathlib import Path
import subprocess, sys, tempfile
source = (Path(__file__).resolve().parents[2]/'IRSpeedyVPN/MainWindow.xaml.cs').read_text(encoding='utf-8-sig')
methods = source[source.index('        private string coreFailureDetails;'):source.index('        public void ShowUserMessage(')]
with tempfile.TemporaryDirectory(prefix='core-error-ui-') as temp:
    folder = Path(temp)
    (folder/'Handlers.cs').write_text('''using System; using System.Windows;
class CoreRuntimeIdentity { internal const string ErrorPrefix="[CoreFailure]\\n"; }
class LogHelper { internal static void WriteLog(string value) {} }
class MainWindow : Window { System.Windows.Controls.TextBlock lblErrorMessage = new System.Windows.Controls.TextBlock();
''' + methods + '}')
    (folder/'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><LangVersion>7.3</LangVersion></PropertyGroup></Project>')
    subprocess.run([sys.argv[1], 'build', str(folder/'Checks.csproj')], check=True)
print('PASS production error banner and copy-details dialog compile with WPF/net48')
