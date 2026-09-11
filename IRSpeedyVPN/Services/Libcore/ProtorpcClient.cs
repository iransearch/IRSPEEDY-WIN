using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class ProtorpcClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private long _nextId;

        private ProtorpcClient(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public static ProtorpcClient Connect(string host, int port, int timeoutMs)
        {
            var client = new TcpClient();
            try
            {
                var ar = client.BeginConnect(host, port, null, null);
                using (var connected = ar.AsyncWaitHandle)
                {
                    if (!connected.WaitOne(timeoutMs))
                        throw new TimeoutException($"Timeout connecting to {host}:{port}.");
                    client.EndConnect(ar);
                }
                return new ProtorpcClient(client);
            }
            catch
            {
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

            var headerBytes = LibcoreProto.EncodeRequestHeader(
                (ulong)Interlocked.Increment(ref _nextId),
                method,
                (uint)(requestBody?.Length ?? 0));

            WriteFrame(headerBytes);
            WriteFrame(requestBody ?? Array.Empty<byte>());

            var responseHeaderBytes = ReadFrame(0);
            var responseHeader = LibcoreProto.DecodeResponseHeader(responseHeaderBytes);
            if (!string.IsNullOrEmpty(responseHeader.Error))
            {
                throw new InvalidOperationException(responseHeader.Error);
            }

            var responseBody = ReadFrame(0);
            if (responseHeader.RawResponseLen != (uint)responseBody.Length)
            {
                throw new InvalidOperationException("protorpc: unexpected response length.");
            }
            return decode != null ? decode(responseBody) : default;
        }

        // Queries are optional UI updates: close their dedicated connection at the
        // deadline, even if a core accepts it but never sends a complete response.
        public TResp CallWithDeadline<TResp>(string method, byte[] requestBody,
            Func<byte[], TResp> decode, int timeoutMs)
        {
            using (var deadline = new Timer(_ => Dispose(), null, timeoutMs, Timeout.Infinite))
                return Call(method, requestBody, decode);
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
}
