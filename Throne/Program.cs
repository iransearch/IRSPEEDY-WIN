using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Throne
{
    internal class Program
    {
        private static Process _coreProcess;
        private static NamedPipeServerStream _corePipe; // pipe A — Core connects here
        private static NamedPipeServerStream _relayPipe; // pipe B — ThroneControl connects here
        private static volatile bool _running = true;
        private static StreamWriter _logWriter;
        private static readonly object _logLock = new object();

        private const int CoreConnectTimeoutMs = 15000;
        private const int RelayConnectTimeoutMs = 15000;
        private const int MaxRelayFramePayloadBytes = 16 * 1024 * 1024;

        private const int ExitUsage = 1;
        private const int ExitCoreMissing = 10;
        private const int ExitCoreLaunchFailed = 11;
        private const int ExitCoreConnectTimeout = 12;
        private const int ExitRelayConnectTimeout = 13;
        private const int ExitUnexpectedStartupFailure = 20;

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: Throne <ThroneCore.exe> [relayPipeName]");
                return ExitUsage;
            }

            var coreExePath = args[0];
            var relayPipeName = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
                ? args[1]
                : "Throne_relay";

            var logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "throne_" + Process.GetCurrentProcess().Id + ".log");

            try
            {
                _logWriter = new StreamWriter(logPath, false) { AutoFlush = true };
            }
            catch
            {
                // Logging is best-effort; never block startup on it.
            }

            Log("Throne starting. core={0} relayPipe={1} log={2}", coreExePath, relayPipeName, logPath);
            Log("Environment. os64={0} process64={1} clr={2} baseDir={3}",
                Environment.Is64BitOperatingSystem,
                Environment.Is64BitProcess,
                Environment.Version,
                AppDomain.CurrentDomain.BaseDirectory);

            try
            {
                return Run(coreExePath, relayPipeName);
            }
            catch (Exception ex)
            {
                var startupError = BuildUnexpectedStartupError(ex, coreExePath);
                Console.Error.WriteLine(startupError);
                LogException("Unexpected Throne startup failure", ex);

                // Keep Throne alive long enough for IRSpeedyVPN to connect and receive
                // the real startup error over the existing relay protocol instead of only
                // seeing a generic CLR exit code such as 0xE0434352.
                return ServeStartupError(relayPipeName, startupError, ExitUnexpectedStartupFailure);
            }
            finally
            {
                Cleanup();
            }
        }

        private static int Run(string coreExePath, string relayPipeName)
        {
            string fullCorePath;
            try
            {
                fullCorePath = Path.GetFullPath(coreExePath);
            }
            catch (Exception ex)
            {
                var message = "Invalid Core executable path: " + coreExePath
                    + ". " + ex.GetType().Name + ": " + ex.Message;
                LogException("Invalid Core path", ex);
                return ServeStartupError(relayPipeName, message, ExitCoreMissing);
            }

            if (!File.Exists(fullCorePath))
            {
                var message = "Core executable was not found: " + fullCorePath;
                Console.Error.WriteLine(message);
                Log(message);
                return ServeStartupError(relayPipeName, message, ExitCoreMissing);
            }

            var coreDir = Path.GetDirectoryName(fullCorePath);
            try
            {
                var info = new FileInfo(fullCorePath);
                Log("Core file. path={0} size={1} bytes dir={2}", fullCorePath, info.Length, coreDir);
            }
            catch (Exception ex)
            {
                LogException("Could not inspect Core file", ex);
            }

            // ————— step 1: internal pipe for Core —————
            var corePipeName = "Throne_int_" + Guid.NewGuid().ToString("N");
            var corePipePath = @"\\.\pipe\" + corePipeName;

            _corePipe = new NamedPipeServerStream(corePipeName, PipeDirection.InOut,
                maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            Log("Internal Core pipe ready: {0}", corePipePath);

            // ————— step 2: launch Core —————
            Console.Error.WriteLine("Launching Core...");
            Log("Launching Core...");

            var psi = new ProcessStartInfo(fullCorePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(coreDir)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : coreDir,
            };
            psi.EnvironmentVariables["THRONE_CORE_SOCKET"] = corePipePath;
            psi.EnvironmentVariables["THRONE_CORE_DEBUG"] = "0";

            _coreProcess = new Process { StartInfo = psi };
            _coreProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    Console.WriteLine("[core] {0}", e.Data);
                    Log("[core] {0}", e.Data);
                }
            };
            _coreProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    Console.Error.WriteLine("[core-err] {0}", e.Data);
                    Log("[core-err] {0}", e.Data);
                }
            };

            try
            {
                if (!_coreProcess.Start())
                {
                    var message = "Core process could not be started: Process.Start returned false. path="
                        + fullCorePath;
                    Log(message);
                    return ServeStartupError(relayPipeName, message, ExitCoreLaunchFailed);
                }

                Log("Core process started. pid={0}", _coreProcess.Id);
                _coreProcess.BeginOutputReadLine();
                _coreProcess.BeginErrorReadLine();
            }
            catch (Win32Exception ex)
            {
                var message = "Core process failed to launch. path=" + fullCorePath
                    + ", workingDirectory=" + psi.WorkingDirectory
                    + ", win32=" + ex.NativeErrorCode
                    + ", hresult=0x" + ex.HResult.ToString("X8")
                    + ", message=" + ex.Message;
                Console.Error.WriteLine(message);
                LogException(message, ex);
                return ServeStartupError(relayPipeName, message, ExitCoreLaunchFailed);
            }
            catch (Exception ex)
            {
                var message = "Core process failed to launch. path=" + fullCorePath
                    + ", workingDirectory=" + psi.WorkingDirectory
                    + ", exception=" + ex.GetType().FullName
                    + ", hresult=0x" + ex.HResult.ToString("X8")
                    + ", message=" + ex.Message;
                Console.Error.WriteLine(message);
                LogException(message, ex);
                return ServeStartupError(relayPipeName, message, ExitCoreLaunchFailed);
            }

            Console.Error.WriteLine("Waiting for Core to connect...");
            Log("Waiting for Core to connect. timeoutMs={0}", CoreConnectTimeoutMs);
            if (!WaitForConnection(_corePipe, CoreConnectTimeoutMs))
            {
                string processState;
                try
                {
                    processState = _coreProcess.HasExited
                        ? " Core exited with code " + _coreProcess.ExitCode + "."
                        : " Core process is still running.";
                }
                catch (Exception ex)
                {
                    processState = " Core process state could not be read: " + ex.Message;
                }

                var message = "Core did not connect to the internal Throne pipe within "
                    + CoreConnectTimeoutMs + " ms." + processState
                    + " core=" + fullCorePath
                    + " pipe=" + corePipePath;
                Console.Error.WriteLine(message);
                Log(message);
                return ServeStartupError(relayPipeName, message, ExitCoreConnectTimeout);
            }

            Console.Error.WriteLine("Core connected.");
            Log("Core connected.");

            // ————— step 3: relay pipe for ThroneControl (per-instance name) —————
            _relayPipe?.Dispose();
            _relayPipe = new NamedPipeServerStream(relayPipeName, PipeDirection.InOut,
                maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            Console.Error.WriteLine("Relay pipe ready: {0}", relayPipeName);
            Log("Relay pipe ready: {0}", relayPipeName);
            Console.Error.WriteLine("Waiting for ThroneControl to connect...");
            if (!WaitForConnection(_relayPipe, RelayConnectTimeoutMs))
            {
                Log("ThroneControl did not connect to the relay pipe within {0} ms.", RelayConnectTimeoutMs);
                Console.Error.WriteLine("ThroneControl did not connect in time.");
                return ExitRelayConnectTimeout;
            }

            Console.Error.WriteLine("ThroneControl connected, starting relay.");
            Log("ThroneControl connected, starting relay.");

            // ————— step 4: relay loop —————
            var relayThread = new Thread(RelayLoop) { IsBackground = true };
            relayThread.Start();

            Console.Error.WriteLine("Core is running. Press Ctrl+C to shutdown.");
            var waitEvent = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                waitEvent.Set();
            };
            waitEvent.Wait();

            _running = false;
            return 0;
        }

        /// <summary>
        /// Relays raw frames between _relayPipe (ThroneControl side) and _corePipe (Core side).
        /// Reads one complete request frame from relay, writes to core pipe,
        /// reads one complete response frame from core pipe, writes to relay.
        /// </summary>
        private static void RelayLoop()
        {
            while (_running)
            {
                try
                {
                    // — read complete request frame from ThroneControl —
                    // Header: [4B reqId LE][2B methodLen LE]
                    var reqHeader = new byte[6];
                    if (ReadExact(_relayPipe, reqHeader, 0, 6) != 6) break;
                    var methodLen = reqHeader[4] | (reqHeader[5] << 8);

                    var method = new byte[methodLen];
                    if (methodLen > 0 && ReadExact(_relayPipe, method, 0, methodLen) != methodLen) break;

                    // Payload length: [4B payloadLen LE]
                    var payloadLenBuf = new byte[4];
                    if (ReadExact(_relayPipe, payloadLenBuf, 0, 4) != 4) break;
                    var payloadLen = payloadLenBuf[0] | (payloadLenBuf[1] << 8)
                        | (payloadLenBuf[2] << 16) | (payloadLenBuf[3] << 24);

                    if (payloadLen < 0 || payloadLen > MaxRelayFramePayloadBytes)
                    {
                        Log("Invalid request payload length: {0}", payloadLen);
                        break;
                    }

                    var payload = new byte[payloadLen];
                    if (payloadLen > 0 && ReadExact(_relayPipe, payload, 0, payloadLen) != payloadLen) break;

                    // Reconstruct full request frame
                    var requestFrame = new byte[6 + methodLen + 4 + payloadLen];
                    Buffer.BlockCopy(reqHeader, 0, requestFrame, 0, 6);
                    Buffer.BlockCopy(method, 0, requestFrame, 6, methodLen);
                    Buffer.BlockCopy(payloadLenBuf, 0, requestFrame, 6 + methodLen, 4);
                    Buffer.BlockCopy(payload, 0, requestFrame, 6 + methodLen + 4, payloadLen);

                    // — write to Core —
                    _corePipe.Write(requestFrame, 0, requestFrame.Length);
                    _corePipe.Flush();

                    // — read complete response frame from Core —
                    // Response: [4B reqId LE][1B status][4B dataLen LE][data]
                    var respHeader = new byte[9];
                    if (ReadExact(_corePipe, respHeader, 0, 9) != 9) break;

                    var dataLen = respHeader[5] | (respHeader[6] << 8)
                        | (respHeader[7] << 16) | (respHeader[8] << 24);

                    if (dataLen < 0 || dataLen > MaxRelayFramePayloadBytes)
                    {
                        Log("Invalid response payload length: {0}", dataLen);
                        break;
                    }

                    var data = new byte[dataLen];
                    if (dataLen > 0 && ReadExact(_corePipe, data, 0, dataLen) != dataLen) break;

                    // Reconstruct full response frame
                    var responseFrame = new byte[9 + dataLen];
                    Buffer.BlockCopy(respHeader, 0, responseFrame, 0, 9);
                    Buffer.BlockCopy(data, 0, responseFrame, 9, dataLen);

                    // — write back to ThroneControl —
                    _relayPipe.Write(responseFrame, 0, responseFrame.Length);
                    _relayPipe.Flush();
                }
                catch (Exception ex)
                {
                    LogException("Relay loop stopped", ex);
                    break;
                }
            }

            _running = false;
        }

        /// <summary>
        /// When Core cannot start, expose the failure through the same relay protocol.
        /// IRSpeedyVPN will connect normally and its first RPC will receive status=1 with
        /// the detailed startup message. This prevents the UI from only seeing an opaque
        /// Throne process exit code.
        /// </summary>
        private static int ServeStartupError(string relayPipeName, string message, int exitCode)
        {
            try
            {
                try { _relayPipe?.Dispose(); } catch { }
                _relayPipe = new NamedPipeServerStream(relayPipeName, PipeDirection.InOut,
                    maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                Log("Startup error relay ready. pipe={0} exitCode={1} error={2}",
                    relayPipeName, exitCode, message);

                if (!WaitForConnection(_relayPipe, RelayConnectTimeoutMs))
                {
                    Log("No ThroneControl connection arrived for startup error relay within {0} ms.",
                        RelayConnectTimeoutMs);
                    return exitCode;
                }

                var reqHeader = new byte[6];
                if (ReadExact(_relayPipe, reqHeader, 0, reqHeader.Length) != reqHeader.Length)
                {
                    Log("Startup error relay client disconnected before request header.");
                    return exitCode;
                }

                var methodLen = reqHeader[4] | (reqHeader[5] << 8);
                var methodBytes = new byte[methodLen];
                if (methodLen > 0 && ReadExact(_relayPipe, methodBytes, 0, methodLen) != methodLen)
                {
                    Log("Startup error relay client disconnected while reading method.");
                    return exitCode;
                }

                var payloadLenBuf = new byte[4];
                if (ReadExact(_relayPipe, payloadLenBuf, 0, 4) != 4)
                {
                    Log("Startup error relay client disconnected before payload length.");
                    return exitCode;
                }

                var payloadLen = payloadLenBuf[0] | (payloadLenBuf[1] << 8)
                    | (payloadLenBuf[2] << 16) | (payloadLenBuf[3] << 24);
                if (payloadLen < 0 || payloadLen > MaxRelayFramePayloadBytes)
                {
                    Log("Startup error relay received invalid payload length: {0}", payloadLen);
                    return exitCode;
                }

                if (payloadLen > 0)
                {
                    var discard = new byte[payloadLen];
                    if (ReadExact(_relayPipe, discard, 0, payloadLen) != payloadLen)
                    {
                        Log("Startup error relay client disconnected while reading payload.");
                        return exitCode;
                    }
                }

                var method = methodBytes.Length > 0 ? Encoding.UTF8.GetString(methodBytes) : string.Empty;
                var errorData = Encoding.UTF8.GetBytes(message ?? "Core startup failed.");
                var responseHeader = new byte[9];
                responseHeader[0] = reqHeader[0];
                responseHeader[1] = reqHeader[1];
                responseHeader[2] = reqHeader[2];
                responseHeader[3] = reqHeader[3];
                responseHeader[4] = 1; // non-zero RPC status = error
                responseHeader[5] = (byte)errorData.Length;
                responseHeader[6] = (byte)(errorData.Length >> 8);
                responseHeader[7] = (byte)(errorData.Length >> 16);
                responseHeader[8] = (byte)(errorData.Length >> 24);

                _relayPipe.Write(responseHeader, 0, responseHeader.Length);
                if (errorData.Length > 0)
                    _relayPipe.Write(errorData, 0, errorData.Length);
                _relayPipe.Flush();

                Log("Startup error delivered to ThroneControl. method={0} bytes={1}",
                    method, errorData.Length);

                // Give the client a brief chance to finish reading before the process exits.
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                LogException("Failed to deliver startup error through relay", ex);
            }

            return exitCode;
        }

        private static bool WaitForConnection(NamedPipeServerStream pipe, int timeoutMs)
        {
            try
            {
                return pipe.WaitForConnectionAsync().Wait(timeoutMs);
            }
            catch (Exception ex)
            {
                LogException("WaitForConnection failed", ex);
                return false;
            }
        }

        private static string BuildUnexpectedStartupError(Exception ex, string coreExePath)
        {
            return "Unexpected Throne startup failure. core=" + coreExePath
                + ", exception=" + ex.GetType().FullName
                + ", hresult=0x" + ex.HResult.ToString("X8")
                + ", message=" + ex.Message;
        }

        private static void Log(string format, params object[] args)
        {
            try
            {
                lock (_logLock)
                {
                    _logWriter?.WriteLine("{0:yyyy-MM-dd HH:mm:ss.fff} {1}",
                        DateTime.Now,
                        string.Format(format, args));
                }
            }
            catch
            {
            }
        }

        private static void LogException(string context, Exception ex)
        {
            if (ex == null)
            {
                Log("{0}", context);
                return;
            }

            Log("{0}: type={1} hresult=0x{2:X8} message={3}",
                context,
                ex.GetType().FullName,
                ex.HResult,
                ex.Message);
            Log("{0}", ex.StackTrace ?? string.Empty);

            if (ex.InnerException != null)
            {
                Log("Inner exception: type={0} hresult=0x{1:X8} message={2}",
                    ex.InnerException.GetType().FullName,
                    ex.InnerException.HResult,
                    ex.InnerException.Message);
            }
        }

        private static int ReadExact(PipeStream pipe, byte[] buf, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                var n = pipe.Read(buf, offset + total, count - total);
                if (n == 0) return total;
                total += n;
            }
            return total;
        }

        private static void Cleanup()
        {
            try
            {
                if (_coreProcess != null)
                {
                    try
                    {
                        if (!_coreProcess.HasExited)
                        {
                            Console.Error.WriteLine("Terminating Core...");
                            Log("Terminating Core. pid={0}", _coreProcess.Id);
                            _coreProcess.Kill();
                            _coreProcess.WaitForExit(5000);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogException("Core cleanup failed", ex);
                    }
                    finally
                    {
                        try { _coreProcess.Dispose(); } catch { }
                    }
                }
            }
            finally
            {
                try { _corePipe?.Dispose(); } catch { }
                try { _relayPipe?.Dispose(); } catch { }
                lock (_logLock)
                {
                    try { _logWriter?.Dispose(); } catch { }
                    _logWriter = null;
                }
            }
        }
    }
}
