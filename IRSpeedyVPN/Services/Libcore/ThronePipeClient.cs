using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class ThronePipeClient : IDisposable
    {
        private const int ReadTimeoutMs = 60000;
        private const int WriteTimeoutMs = 15000;

        private NamedPipeClientStream _pipe;
        private long _nextId;
        private readonly object _lock = new object();

        public void Connect(int timeoutMs = 5000)
        {
            lock (_lock)
            {
                if (_pipe != null && _pipe.IsConnected)
                    return;
                _pipe?.Dispose();
                _pipe = new NamedPipeClientStream(".", "Throne_relay", PipeDirection.InOut);
                _pipe.Connect(timeoutMs);
                _pipe.ReadTimeout = ReadTimeoutMs;
                _pipe.WriteTimeout = WriteTimeoutMs;
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                _pipe?.Dispose();
                _pipe = null;
            }
        }

        public bool IsConnected
        {
            get
            {
                lock (_lock)
                    return _pipe != null && _pipe.IsConnected;
            }
        }

        public ErrorResp Start(LoadConfigReq req)
        {
            return Call("Start", LibcoreProto.EncodeLoadConfigReq(req), LibcoreProto.DecodeErrorResp);
        }

        public ErrorResp Stop()
        {
            return Call("Stop", LibcoreProto.EncodeEmptyReq(), LibcoreProto.DecodeErrorResp);
        }

        public ErrorResp CheckConfig(LoadConfigReq req)
        {
            // FASTEST routes are already validated by the URL-test stage.
            // Keep the method for source compatibility, but do not issue another
            // CheckConfig RPC before Start.
            return new ErrorResp();
        }

        public TestResp Test(TestReq req)
        {
            return Call("Test", LibcoreProto.EncodeTestReq(req), LibcoreProto.DecodeTestResp);
        }

        public EmptyResp StopTest()
        {
            return Call("StopTest", LibcoreProto.EncodeEmptyReq(), data => new EmptyResp());
        }

        public QueryURLTestResponse QueryURLTest()
        {
            return Call("QueryURLTest", LibcoreProto.EncodeEmptyReq(), LibcoreProto.DecodeQueryURLTestResponse);
        }

        public TResp Call<TResp>(string method, byte[] requestBody, Func<byte[], TResp> decode)
        {
            lock (_lock)
            {
                if (_pipe == null || !_pipe.IsConnected)
                    throw new InvalidOperationException("Throne pipe is not connected.");

                try
                {
                    var reqId = (uint)Interlocked.Increment(ref _nextId);
                    var methodBytes = Encoding.UTF8.GetBytes(method ?? "");
                    var payload = requestBody ?? Array.Empty<byte>();

                    var off = 0;
                    var frame = new byte[4 + 2 + methodBytes.Length + 4 + payload.Length];

                    frame[off++] = (byte)reqId;
                    frame[off++] = (byte)(reqId >> 8);
                    frame[off++] = (byte)(reqId >> 16);
                    frame[off++] = (byte)(reqId >> 24);

                    frame[off++] = (byte)methodBytes.Length;
                    frame[off++] = (byte)(methodBytes.Length >> 8);

                    if (methodBytes.Length > 0)
                    {
                        Buffer.BlockCopy(methodBytes, 0, frame, off, methodBytes.Length);
                        off += methodBytes.Length;
                    }

                    frame[off++] = (byte)payload.Length;
                    frame[off++] = (byte)(payload.Length >> 8);
                    frame[off++] = (byte)(payload.Length >> 16);
                    frame[off++] = (byte)(payload.Length >> 24);

                    if (payload.Length > 0)
                        Buffer.BlockCopy(payload, 0, frame, off, payload.Length);

                    _pipe.Write(frame, 0, frame.Length);
                    _pipe.Flush();

                    var respHeader = new byte[9];
                    ReadExact(respHeader, 0, 9);

                    var status = respHeader[4];
                    var dataLen = respHeader[5]
                        | (respHeader[6] << 8)
                        | (respHeader[7] << 16)
                        | (respHeader[8] << 24);

                    if (status != 0)
                    {
                        var errorData = dataLen > 0 ? new byte[dataLen] : null;
                        if (dataLen > 0)
                            ReadExact(errorData, 0, dataLen);
                        throw new InvalidOperationException(
                            errorData != null
                                ? Encoding.UTF8.GetString(errorData)
                                : $"RPC error status={status}");
                    }

                    var data = new byte[dataLen];
                    if (dataLen > 0)
                        ReadExact(data, 0, dataLen);

                    return decode != null ? decode(data) : default;
                }
                catch (Exception ex) when (
                    ex is IOException
                    || ex is EndOfStreamException
                    || ex is ObjectDisposedException
                    || ex is TimeoutException)
                {
                    try
                    {
                        _pipe?.Dispose();
                        _pipe = null;
                    }
                    catch
                    {
                    }
                    throw;
                }
            }
        }

        private void ReadExact(byte[] buf, int offset, int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = _pipe.Read(buf, offset + total, count - total);
                if (read == 0)
                    throw new EndOfStreamException("Pipe closed");
                total += read;
            }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
