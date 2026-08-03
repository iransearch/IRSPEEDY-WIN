using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Common
{
    internal class ShellExecute
    {
        public static bool HideWindow = true;

        private static string BaseDir =>
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

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
            var workDir = string.IsNullOrWhiteSpace(workingDirectory)
                ? BaseDir
                : workingDirectory;

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
                    RedirectStandardError = redirectStdErr,
                    StandardInputEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
            }

            return new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = ShouldHideWindow,
                WindowStyle = ShouldHideWindow
                    ? ProcessWindowStyle.Hidden
                    : ProcessWindowStyle.Normal,
                WorkingDirectory = workDir,
                RedirectStandardInput = redirectStdIn,
                RedirectStandardOutput = redirectStdOut,
                RedirectStandardError = redirectStdErr,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        public static string ShellexecAndReturnStringOutput(string file, string args)
        {
            return ShellexecAndReturnStringOutputWithInput(file, args, null, 60000);
        }

        public static string ShellexecAndReturnStringOutputWithInput(
            string file,
            string args,
            string standardInput,
            int timeoutMs = 60000)
        {
            using (var process = new Process
            {
                StartInfo = BuildStartInfo(
                    file,
                    args,
                    redirectStdIn: true,
                    redirectStdOut: true,
                    redirectStdErr: true)
            })
            {
                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                try
                {
                    if (standardInput != null)
                    {
                        var inputBytes = Encoding.UTF8.GetBytes(standardInput);
                        process.StandardInput.BaseStream.Write(
                            inputBytes,
                            0,
                            inputBytes.Length);
                        process.StandardInput.BaseStream.Flush();
                    }
                }
                finally
                {
                    try { process.StandardInput.Close(); } catch { }
                }

                if (!process.WaitForExit(Math.Max(1000, timeoutMs)))
                {
                    KillProcessTree(process);
                    throw new TimeoutException("External process timed out.");
                }

                Task.WaitAll(
                    new Task[] { outputTask, errorTask },
                    Math.Min(5000, Math.Max(1000, timeoutMs)));

                var output = outputTask.IsCompleted
                    ? outputTask.Result
                    : string.Empty;
                var error = errorTask.IsCompleted
                    ? errorTask.Result
                    : string.Empty;

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error)
                            ? "External process failed with exit code " + process.ExitCode
                            : error.Trim());
                }

                return output;
            }
        }

        public static Process ShellexecAndReturnProcessRedirectOutput(
            string file,
            string args,
            bool start = true)
        {
            var process = new Process
            {
                StartInfo = BuildStartInfo(file, args, true, true, true)
            };
            if (start)
                process.Start();
            return process;
        }

        public static Process ShellexecAndReturnProcessRedirectInput(
            string file,
            string args,
            bool start = true)
        {
            var process = new Process
            {
                StartInfo = BuildStartInfo(file, args, true, false, false)
            };
            if (start)
                process.Start();
            return process;
        }

        public static Process ShellexecAndReturnProcess(string file, string args)
        {
            var process = new Process
            {
                StartInfo = BuildStartInfo(file, args, false, false, false)
            };
            process.Start();
            return process;
        }

        public static Process ShellexecAndReturnProcess(
            string file,
            string args,
            string workingDirectory)
        {
            var process = new Process
            {
                StartInfo = BuildStartInfo(
                    file,
                    args,
                    false,
                    false,
                    false,
                    workingDirectory)
            };
            process.Start();
            return process;
        }

        public static void KillProcessTree(Process process)
        {
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                {
                    foreach (var child in GetChildProcesses(process.Id))
                        KillProcessTree(child);

                    process.Kill();
                    process.WaitForExit(3000);
                }
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

            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    KillProcessTree(process);
                }
                catch
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        private static Process[] GetChildProcesses(int parentId)
        {
            var children = new List<Process>();
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
                    try
                    {
                        children.Add(Process.GetProcessById((int)entry.th32ProcessID));
                    }
                    catch
                    {
                    }
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
        private static extern IntPtr CreateToolhelp32Snapshot(
            uint dwFlags,
            uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Process32First(
            IntPtr hSnapshot,
            ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Process32Next(
            IntPtr hSnapshot,
            ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
