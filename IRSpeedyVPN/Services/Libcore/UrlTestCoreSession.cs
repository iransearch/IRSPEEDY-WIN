using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using IRSpeedyVPN.Common;

namespace IRSpeedyVPN.Services.Libcore
{
    // Owned by one scheduler slot and reused across countries/rounds. The normal
    // connection core, its fixed port and its global RPC lock are never touched.
    internal sealed class UrlTestCoreSession : IDisposable
    {
        internal readonly object SyncRoot = new object();
        private readonly object lifecycle = new object();
        private readonly string executable;
        private readonly int port;
        private Process process;
        private bool disposed;

        internal UrlTestCoreSession(string executable)
        {
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                throw new FileNotFoundException("URL test core executable is unavailable.");
            this.executable = executable;
            port = FreePortManager.Dequeue();
        }

        internal TestResp Test(TestReq request, Action<TestResp> report, Func<bool> cancelled)
        {
            lock (SyncRoot)
            {
                if (cancelled()) throw new OperationCanceledException();
                EnsureStarted(cancelled);
                var running = process;
                // Killing only this owned process also unblocks an RPC waiting on
                // an unresponsive core when the user switches lists or connects.
                using (var cancellation = new Timer(_ =>
                {
                    if (cancelled()) StopProcess(running);
                }, null, 0, 100))
                {
                    try
                    {
                        var client = new LibcoreServiceClient("127.0.0.1", port, 1000, 10000);
                        var result = client.TestWithProgress(request, report, cancelled,
                            message => LogHelper.WriteExLog(message));
                        if (cancelled()) throw new OperationCanceledException();
                        return result;
                    }
                    catch
                    {
                        StopProcess(running);
                        if (cancelled()) throw new OperationCanceledException();
                        throw;
                    }
                }
            }
        }

        private void EnsureStarted(Func<bool> cancelled)
        {
            lock (lifecycle)
            {
                if (disposed) throw new ObjectDisposedException(nameof(UrlTestCoreSession));
                if (process != null && !process.HasExited) return;
                process?.Dispose();
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = "-port " + port,
                        WorkingDirectory = Path.GetDirectoryName(executable),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                // Drain output; never log raw configurations, URLs or credentials.
                process.OutputDataReceived += (sender, args) => { };
                process.ErrorDataReceived += (sender, args) => { };
                try
                {
                    if (!process.Start()) throw new InvalidOperationException("URL test core did not start.");
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                catch
                {
                    StopProcess(process);
                    throw;
                }
            }
            var startup = Stopwatch.StartNew();
            while (startup.ElapsedMilliseconds < 8000)
            {
                if (cancelled())
                {
                    StopProcess(process);
                    throw new OperationCanceledException();
                }
                if (process == null || process.HasExited)
                    throw new InvalidOperationException("URL test core exited during startup.");
                if (ProtorpcClient.CanConnect("127.0.0.1", port, 100))
                {
                    LogHelper.WriteExLog("[UrlTest] stage=worker-core-ready port=" + port);
                    return;
                }
                Thread.Sleep(50);
            }
            StopProcess(process);
            throw new TimeoutException("URL test core startup timed out.");
        }

        private void StopProcess(Process expected)
        {
            lock (lifecycle)
            {
                if (expected == null || !ReferenceEquals(process, expected)) return;
                try
                {
                    if (!expected.HasExited) expected.Kill();
                    if (!expected.WaitForExit(1000)) return;
                }
                catch (InvalidOperationException) { }
                catch { return; }
                expected.Dispose();
                process = null;
            }
        }

        public void Dispose()
        {
            lock (SyncRoot)
            {
                if (disposed) return;
                disposed = true;
                StopProcess(process);
                // Do not recycle a port if an owned process failed to terminate.
                if (process == null) FreePortManager.Enqueue(port);
            }
        }
    }
}
