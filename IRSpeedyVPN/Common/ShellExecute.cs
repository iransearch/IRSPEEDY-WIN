using System;
using System.Diagnostics;
using System.IO;

namespace IRSpeedyVPN.Common
{
    internal class ShellExecute
    {
        // Default behavior (can still be overridden by debug.txt)
        public static bool HideWindow = true;

        private static string BaseDir =>
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static bool DebugEnabled =>
            File.Exists(Path.Combine(BaseDir, "debug.txt"));

        private static bool NoCloseEnabled =>
            File.Exists(Path.Combine(BaseDir, "noclose.txt"));

        // Final decision: if debug.txt exists -> never hide
        private static bool ShouldHideWindow => HideWindow && !DebugEnabled;

        // If noclose.txt exists and we're NOT hiding -> keep cmd open
        private static bool ShouldKeepCmdOpen => NoCloseEnabled && !ShouldHideWindow;

        private static ProcessStartInfo BuildStartInfo(
            string file,
            string args,
            bool redirectStdIn,
            bool redirectStdOut,
            bool redirectStdErr,
            string workingDirectory = null)
        {
            var workDir = string.IsNullOrWhiteSpace(workingDirectory) ? BaseDir : workingDirectory;
            // If we want "no close", we must run through cmd.exe /k
            if (ShouldKeepCmdOpen)
            {
                // Quote executable path, keep args as-is
                // cmd.exe /k "<file>" <args>
                var cmdArgs = $"/k \"\"{file}\" {args}\"";

                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = false,
                    CreateNoWindow = ShouldHideWindow,
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = workDir, // current program path
                    RedirectStandardInput = redirectStdIn,
                    RedirectStandardOutput = redirectStdOut,
                    RedirectStandardError = redirectStdErr
                };
            }

            // Normal direct execution
            return new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = ShouldHideWindow,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = workDir, // current program path
                RedirectStandardInput = redirectStdIn,
                RedirectStandardOutput = redirectStdOut,
                RedirectStandardError = redirectStdErr
            };
        }

        public static string ShellexecAndReturnStringOutput(string file, string args)
        {
            // NOTE: noclose.txt + cmd /k would never exit, so this method MUST run normally.
            // We'll still respect debug.txt for visibility.
            try
            {
                using (var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = file,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,//ShouldHideWindow,
                        WorkingDirectory = BaseDir,
                        WindowStyle = ProcessWindowStyle.Normal
                    }
                })
                {
                    process.Start();
                    string ret = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return ret;
                }
            }
            catch
            {
                throw;
            }
        }

        public static Process ShellexecAndReturnProcessRedirectOutput(string file, string args, bool start = true)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = BuildStartInfo(
                        file, args,
                        redirectStdIn: true,
                        redirectStdOut: true,
                        redirectStdErr: true)
                };

                if (start) process.Start();
                return process;
            }
            catch
            {
                throw;
            }
        }

        public static Process ShellexecAndReturnProcessRedirectInput(string file, string args, bool start = true)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = BuildStartInfo(
                        file, args,
                        redirectStdIn: true,
                        redirectStdOut: false,
                        redirectStdErr: false)
                };

                if (start) process.Start();
                return process;
            }
            catch
            {
                throw;
            }
        }

        public static Process ShellexecAndReturnProcess(string file, string args)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = BuildStartInfo(
                        file, args,
                        redirectStdIn: false,
                        redirectStdOut: false,
                        redirectStdErr: false)
                };

                process.Start();
                return process;
            }
            catch
            {
                throw;
            }
        }

        public static Process ShellexecAndReturnProcess(string file, string args, string workingDirectory)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = BuildStartInfo(
                        file, args,
                        redirectStdIn: false,
                        redirectStdOut: false,
                        redirectStdErr: false,
                        workingDirectory: workingDirectory)
                };

                process.Start();
                return process;
            }
            catch
            {
                throw;
            }
        }

        public static void KillProccess(string name)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try { process.Kill(); }
                catch { /* ignore */ }
            }
        }

        public static void KillProcessTree(Process process)
        {
            if (process == null)
                return;
            try
            {
                if (process.HasExited)
                    return;
            }
            catch
            {
                return;
            }

            // taskkill /T walks the child-process tree, which Process.Kill does not.
            try
            {
                using (var taskkill = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/PID " + process.Id + " /T /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }))
                {
                    taskkill?.WaitForExit(5000);
                }
                return;
            }
            catch
            {
            }

            try { process.Kill(); }
            catch { /* ignore */ }
        }
    }
}
