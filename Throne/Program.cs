using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Throne
{
    internal class Program
    {
        private static Process _coreProcess;
        private static NamedPipeServerStream _corePipe; // pipe A — Core connects here
        private static NamedPipeServerStream _relayPipe; // pipe B — ThroneControl connects here
        private static volatile bool _running = true;

        private const string RelayPipeName = @"\\.\pipe\Throne_relay";

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: Throne <ThroneCore.exe>");
                return 1;
            }

            var coreExePath = args[0];

            // ————— step 1: internal pipe for Core —————
            var corePipeName = "Throne_int_" + Guid.NewGuid().ToString("N");
            var corePipePath = @"\\.\pipe\" + corePipeName;

            _corePipe = new NamedPipeServerStream(corePipeName, PipeDirection.InOut,
                maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            // ————— step 2: launch Core —————
            Console.Error.WriteLine("Launching Core...");
            var psi = new ProcessStartInfo(coreExePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.EnvironmentVariables["THRONE_CORE_SOCKET"] = corePipePath;
            psi.EnvironmentVariables["THRONE_CORE_DEBUG"] = "0";

            _coreProcess = new Process { StartInfo = psi };
            _coreProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) Console.WriteLine("[core] {0}", e.Data);
            };
            _coreProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) Console.Error.WriteLine("[core-1] {0}", e.Data);
            };
            _coreProcess.Start();
            _coreProcess.BeginOutputReadLine();
            _coreProcess.BeginErrorReadLine();

            Console.Error.WriteLine("Waiting for Core to connect...");
            _corePipe.WaitForConnection();
            Console.Error.WriteLine("Core connected.");

            // ————— step 3: relay pipe for ThroneControl (fixed name) —————
            _relayPipe?.Dispose();
            _relayPipe = new NamedPipeServerStream("Throne_relay", PipeDirection.InOut,
                maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            Console.Error.WriteLine("Relay pipe ready: {0}", RelayPipeName);
            Console.Error.WriteLine("Waiting for ThroneControl to connect...");
            _relayPipe.WaitForConnection();
            Console.Error.WriteLine("ThroneControl connected, starting relay.");

            // ————— step 4: relay loop —————
            var relayThread = new Thread(RelayLoop) { IsBackground = true };
            relayThread.Start();

            // Wait for Ctrl+C
            Console.Error.WriteLine("Core is running. Press Ctrl+C to shutdown.");
            var waitEvent = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                waitEvent.Set();
            };
            waitEvent.Wait();

            _running = false;
            Cleanup();
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
                catch
                {
                    break;
                }
            }

            _running = false;
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
            if (_coreProcess != null && !_coreProcess.HasExited)
            {
                Console.Error.WriteLine("Terminating Core...");
                _coreProcess.Kill();
                _coreProcess.WaitForExit(5000);
            }
            _coreProcess?.Dispose();
            _corePipe?.Dispose();
            _relayPipe?.Dispose();
        }
    }
}
