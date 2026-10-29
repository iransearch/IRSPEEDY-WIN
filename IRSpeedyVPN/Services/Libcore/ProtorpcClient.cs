using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class ProtorpcClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private long _nextId;
        private int _deadlineMs;
        private CoreRpcTrace _trace;

        private ProtorpcClient(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public static ProtorpcClient Connect(string host, int port, int timeoutMs)
        {
            return Connect(host, port, timeoutMs, CancellationToken.None);
        }

        public static ProtorpcClient Connect(string host, int port, int timeoutMs,
            CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var client = new TcpClient();
            var trace = new CoreRpcTrace("other", timeoutMs); trace.Stage = "connect";
            try
            {
                var ar = client.BeginConnect(host, port, null, null);
                using (var connected = ar.AsyncWaitHandle)
                {
                    var result = cancellation.CanBeCanceled
                        ? WaitHandle.WaitAny(new[] { connected, cancellation.WaitHandle }, timeoutMs)
                        : (connected.WaitOne(timeoutMs) ? 0 : WaitHandle.WaitTimeout);
                    if (result == 1) throw new OperationCanceledException(cancellation);
                    if (result == WaitHandle.WaitTimeout)
                        throw new TimeoutException($"Timeout connecting to {host}:{port}.");
                    client.EndConnect(ar);
                }
                return new ProtorpcClient(client);
            }
            catch (Exception ex)
            {
                trace.Attach(ex, ex is OperationCanceledException ? "canceled" : ex is TimeoutException ? "connect-timeout" : "transport");
                client.Close();
                throw;
            }
        }

        public static bool CanConnect(string host, int port, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                    {
                        return false;
                    }
                    client.EndConnect(ar);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public TResp Call<TResp>(string method, byte[] requestBody, Func<byte[], TResp> decode)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new ArgumentException("RPC method is required.", nameof(method));
            }

            var trace = _trace = new CoreRpcTrace(CoreRpcTrace.Alias(method), _deadlineMs);
            try
            {
                var headerBytes = LibcoreProto.EncodeRequestHeader(
                    (ulong)Interlocked.Increment(ref _nextId), method, (uint)(requestBody?.Length ?? 0));
                trace.Stage = "write-header"; WriteFrame(headerBytes);
                trace.Stage = "write-body"; WriteFrame(requestBody ?? Array.Empty<byte>());
                trace.Stage = "read-header";
                var responseHeaderBytes = ReadFrame(0);
                var responseHeader = LibcoreProto.DecodeResponseHeader(responseHeaderBytes);
                if (!string.IsNullOrEmpty(responseHeader.Error))
                {
                    trace.Stage = "server-error";
                    throw new InvalidOperationException(responseHeader.Error);
                }
                trace.Stage = "read-body";
                var responseBody = ReadFrame(0);
                if (responseHeader.RawResponseLen != (uint)responseBody.Length)
                    throw new InvalidOperationException("protorpc: unexpected response length.");
                trace.Stage = "decode";
                var response = decode != null ? decode(responseBody) : default;
                trace.Stage = "complete";
                return response;
            }
            catch (Exception ex) { trace.Attach(ex, "transport"); throw; }
        }

        // Queries are optional UI updates: close their dedicated connection at the
        // deadline, even if a core accepts it but never sends a complete response.
        public TResp CallWithDeadline<TResp>(string method, byte[] requestBody,
            Func<byte[], TResp> decode, int timeoutMs)
        {
            _deadlineMs = timeoutMs;
            int outcome = 0;
            using (var deadline = new Timer(_ =>
            {
                if (Interlocked.CompareExchange(ref outcome, 3, 0) == 0) Dispose();
            }, null, timeoutMs, Timeout.Infinite))
            {
                try
                {
                    var response = Call(method, requestBody, decode);
                    Interlocked.CompareExchange(ref outcome, 1, 0);
                    return response; // Preserve the legacy overload's response semantics.
                }
                catch (Exception ex)
                {
                    var winner = Interlocked.CompareExchange(ref outcome, 1, 0);
                    _trace?.Attach(ex, winner == 3 ? "deadline" : "transport"); throw;
                }
            }
        }

        // The Start RPC has its own connection. Closing it releases a pending read
        // immediately when the user cancels, without interrupting any other RPC.
        public TResp CallWithDeadline<TResp>(string method, byte[] requestBody,
            Func<byte[], TResp> decode, int timeoutMs, CancellationToken cancellation, bool reportTimeout = false)
        {
            cancellation.ThrowIfCancellationRequested();
            _deadlineMs = timeoutMs;
            // Only the winner may close the socket. A timer queued after a successful
            // response must not turn that response into a deadline failure.
            int outcome = 0; // pending=0, completed=1, canceled=2, timed out=3
            using (var registration = cancellation.Register(() =>
            {
                if (Interlocked.CompareExchange(ref outcome, 2, 0) == 0) Dispose();
            }))
            using (var deadline = new Timer(_ =>
            {
                if (Interlocked.CompareExchange(ref outcome, 3, 0) == 0) Dispose();
            }, null, timeoutMs, Timeout.Infinite))
            {
                try
                {
                    var response = Call(method, requestBody, decode);
                    var winner = Interlocked.CompareExchange(ref outcome, 1, 0);
                    if (winner == 2)
                    {
                        var canceled = new OperationCanceledException(cancellation);
                        _trace?.Attach(canceled, "canceled"); throw canceled;
                    }
                    if (winner == 3)
                    {
                        Exception failure = reportTimeout ? (Exception)new TimeoutException("Core Start RPC deadline exceeded.")
                            : new IOException("Core RPC deadline exceeded.");
                        _trace?.Attach(failure, "deadline"); throw failure;
                    }
                    return response;
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException
                    || ex is SocketException)
                {
                    var winner = Interlocked.CompareExchange(ref outcome, 1, 0);
                    if (winner == 2)
                    {
                        var canceled = new OperationCanceledException(cancellation);
                        _trace?.Attach(canceled, "canceled"); throw canceled;
                    }
                    if (winner == 3 && reportTimeout)
                    {
                        var failure = new TimeoutException("Core Start RPC deadline exceeded.", ex);
                        _trace?.Attach(failure, "deadline"); throw failure;
                    }
                    _trace?.Attach(ex, winner == 3 ? "deadline" : "transport");
                    throw;
                }
            }
        }

        private void WriteFrame(byte[] data)
        {
            var size = data?.Length ?? 0;
            WriteUVarint((ulong)size);
            if (size > 0)
            {
                _stream.Write(data, 0, size);
            }
        }

        private byte[] ReadFrame(int maxSize)
        {
            var size = ReadUVarint();
            if (maxSize > 0 && size > (ulong)maxSize)
            {
                throw new InvalidOperationException($"protorpc: frame size {size} exceeds {maxSize}.");
            }
            if (size == 0)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[size];
            ReadExactly(buffer, 0, (int)size);
            return buffer;
        }

        private void ReadExactly(byte[] buffer, int offset, int count)
        {
            var read = 0;
            while (read < count)
            {
                var n = _stream.Read(buffer, offset + read, count - read);
                if (n <= 0)
                {
                    throw new IOException("Unexpected end of stream.");
                }
                read += n;
            }
        }

        private void WriteUVarint(ulong value)
        {
            while (value >= 0x80)
            {
                _stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            _stream.WriteByte((byte)value);
        }

        private ulong ReadUVarint()
        {
            ulong x = 0;
            int s = 0;
            for (var i = 0; ; i++)
            {
                int b = _stream.ReadByte();
                if (b < 0)
                {
                    throw new IOException("Unexpected end of stream.");
                }
                if ((b & 0x80) == 0)
                {
                    if (i > 9 || (i == 9 && b > 1))
                    {
                        throw new InvalidOperationException("protorpc: varint overflows a 64-bit integer.");
                    }
                    return x | ((ulong)b << s);
                }
                x |= ((ulong)(b & 0x7f)) << s;
                s += 7;
            }
        }

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
        }
    }
    // Only this private-key snapshot can supply low-level RPC metadata. No raw
    // request/response, method argument, endpoint or exception message is stored.
    internal sealed class CoreRpcTrace
    {
        private static readonly object Key = new object();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string rpc;
        private readonly int budget;
        internal string Stage = "connect";
        internal CoreRpcTrace(string rpc, int budget) { this.rpc = rpc; this.budget = budget; }
        internal void Attach(Exception exception, string reason)
        {
            try
            {
                exception.Data[Key] = new Dictionary<string, string>
                {
                    ["core.rpc"] = rpc, ["core.rpc_stage"] = Stage,
                    ["core.rpc_budget_ms"] = Math.Max(0, budget).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["core.rpc_elapsed_ms"] = clock.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["core.close_reason"] = reason
                };
            }
            catch { }
        }
        internal static IDictionary<string, string> Read(Exception exception)
        {
            try { return exception.Data[Key] as IDictionary<string, string>; } catch { return null; }
        }
        internal static string Alias(string method)
        {
            switch (method)
            {
                case "LibcoreService.Start": return "start";
                case "LibcoreService.Stop": return "stop";
                case "LibcoreService.Test": return "test";
                case "LibcoreService.StopTest": return "stop-test";
                case "LibcoreService.CheckConfig": return "check-config";
                case "LibcoreService.QueryConnections": return "query-connections";
                case "LibcoreService.QueryAutoSelectors": return "query-selectors";
                case "LibcoreService.QueryURLTest": return "query-url-test";
                case "LibcoreService.AutoSelectorAction": return "selector-action";
                case "LibcoreService.IsPrivileged": return "build-info";
                default: return "other";
            }
        }
    }

}
