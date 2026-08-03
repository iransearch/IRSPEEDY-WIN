using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace IRSpeedyVPN.Common
{
    internal class ShellExecute
    {
        private static readonly ConcurrentDictionary<int, Process> OwnedProcesses =
            new ConcurrentDictionary<int, Process>();

        public static bool HideWindow = true;

        private static string BaseDir =>
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static bool DebugEnabled =>
            File.Exists(Path.Combine(BaseDir, "debug.txt"));

        private static bool NoCloseEnabled =>
            File.Exists(Path.Combine(BaseDir, "noclose.txt"));

        private static bool ShouldHideWindow => HideWindow && !DebugEnabled;
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
            if (ShouldKeepCmdOpen)
            {
                var cmdArgs = $"/k \"\"{file}\" {args}\"";
                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = false,
                    CreateNoWindow = ShouldHideWindow,
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = workDir,
                    RedirectStandardInput = redirectStdIn,
                    RedirectStandardOutput = redirectStdOut,
                    RedirectStandardError = redirectStdErr
                };
            }

            return new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = ShouldHideWindow,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = workDir,
                RedirectStandardInput = redirectStdIn,
                RedirectStandardOutput = redirectStdOut,
                RedirectStandardError = redirectStdErr
            };
        }

        public static string ShellexecAndReturnStringOutput(string file, string args)
        {
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = BaseDir,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            })
            {
                process.Start();
                RegisterOwned(process);
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(60000))
                {
                    KillProcessTree(process);
                    throw new TimeoutException("External process timed out.");
                }
                UnregisterOwned(process);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? "External process failed with exit code " + process.ExitCode
                        : error.Trim());
                return output;
            }
        }

        public static Process ShellexecAndReturnProcessRedirectOutput(string file, string args, bool start = true)
        {
            var process = new Process
            {
                StartInfo = BuildStartInfo(file, args, true, true, true)
            };
            if (start)
            {
                process.Start();
                RegisterOwned(process);
            }
            return process;
        }

        public static Process ShellexecAndReturnProcessRedirectInput(string file, string args, bool start = true)
        {
            var process = new Process
            {
                StartInfo = BuildStartInfo(file, args, true, false, false)
            };
            if (start)
            {
                process.Start();
                RegisterOwned(process);
            }
            return process;
        }

        public static Process ShellexecAndReturnProcess(string file, string args)
        {
            var process = new Process
            {
                StartInfo = BuildStartInfo(file, args, false, false, false)
            };
            process.Start();
            RegisterOwned(process);
            return process;
        }

        public static Process ShellexecAndReturnProcess(string file, string args, string workingDirectory)
        {
            var process = new Process
            {
                StartInfo = BuildStartInfo(file, args, false, false, false, workingDirectory)
            };
            process.Start();
            RegisterOwned(process);
            return process;
        }

        public static void KillProcessTree(Process process)
        {
            if (process == null)
                return;

            try
            {
                UnregisterOwned(process);
                if (process.HasExited)
                    return;

                foreach (var child in GetChildProcesses(process.Id))
                    KillProcessTree(child);

                process.Kill();
                process.WaitForExit(3000);
            }
            catch
            {
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }

        public static void KillProccess(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var owned = OwnedProcesses.Values
                .Where(x => x != null)
                .Where(x =>
                {
                    try
                    {
                        return string.Equals(x.ProcessName, name, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();

            foreach (var process in owned)
                KillProcessTree(process);
        }

        private static void RegisterOwned(Process process)
        {
            if (process == null)
                return;
            try
            {
                OwnedProcesses[process.Id] = process;
                process.EnableRaisingEvents = true;
                process.Exited += OwnedProcessExited;
            }
            catch
            {
            }
        }

        private static void OwnedProcessExited(object sender, EventArgs e)
        {
            var process = sender as Process;
            UnregisterOwned(process);
        }

        private static void UnregisterOwned(Process process)
        {
            if (process == null)
                return;
            try
            {
                process.Exited -= OwnedProcessExited;
                OwnedProcesses.TryRemove(process.Id, out _);
            }
            catch
            {
            }
        }

        private static Process[] GetChildProcesses(int parentId)
        {
            var children = new System.Collections.Generic.List<Process>();
            IntPtr snapshot = IntPtr.Zero;
            try
            {
                snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
                if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                    return children.ToArray();

                var entry = new PROCESSENTRY32
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32))
                };
                if (!Process32First(snapshot, ref entry))
                    return children.ToArray();

                do
                {
                    if (entry.th32ParentProcessID != (uint)parentId)
                        continue;
                    try { children.Add(Process.GetProcessById((int)entry.th32ProcessID)); }
                    catch { }
                }
                while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                if (snapshot != IntPtr.Zero && snapshot != new IntPtr(-1))
                    CloseHandle(snapshot);
            }
            return children.ToArray();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
