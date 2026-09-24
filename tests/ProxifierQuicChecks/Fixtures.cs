using System;
using System.Diagnostics;
using System.IO;

namespace IRSpeedyVPN.Models
{
    internal class GlobalInfo { public string TempPath { get; set; } }
}
namespace IRSpeedyVPN.Resource { internal class Unused { } }
namespace IRSpeedyVPN
{
    internal static class AppServices
    {
        public static Models.GlobalInfo GlobalInfo = new Models.GlobalInfo();
    }
}
namespace IRSpeedyVPN.Common
{
    internal static class LogHelper { public static void WriteLog(string message) { } }
    internal static class ShellExecute
    {
        public static bool FailLaunch;
        public static Process LastProcess;
        public static Process ShellexecAndReturnProcess(string file, string args)
        {
            if (FailLaunch) throw new IOException("Simulated launch failure");
            return LastProcess = Process.Start(new ProcessStartInfo("/bin/sleep", "30") { UseShellExecute = false });
        }
        public static void KillProccess(string name) { }
    }
}
