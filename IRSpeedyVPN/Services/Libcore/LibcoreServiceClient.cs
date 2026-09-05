using System;
using System.IO;

namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class LibcoreServiceClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly int _timeoutMs;
        private readonly int _callDeadlineMs;

        public LibcoreServiceClient(string host, int port, int timeoutMs = 2000, int callDeadlineMs = 0)
        {
            _host = host;
            _port = port;
            _timeoutMs = timeoutMs;
            _callDeadlineMs = callDeadlineMs;
        }

        public ErrorResp Start(LoadConfigReq req)
        {
            return Call("LibcoreService.Start", LibcoreProto.EncodeLoadConfigReq(req), LibcoreProto.DecodeErrorResp);
        }

        public ErrorResp Stop()
        {
            return Call("LibcoreService.Stop", LibcoreProto.EncodeEmptyReq(), LibcoreProto.DecodeErrorResp);
        }

        public ErrorResp CheckConfig(LoadConfigReq req)
        {
            return Call("LibcoreService.CheckConfig", LibcoreProto.EncodeLoadConfigReq(req), LibcoreProto.DecodeErrorResp);
        }

        public TestResp Test(TestReq req)
        {
            return Call("LibcoreService.Test", LibcoreProto.EncodeTestReq(req), LibcoreProto.DecodeTestResp);
        }

        public EmptyResp StopTest()
        {
            return Call("LibcoreService.StopTest", LibcoreProto.EncodeEmptyReq(), data => new EmptyResp());
        }

        public QueryURLTestResponse QueryURLTest()
        {
            return Call("LibcoreService.QueryURLTest", LibcoreProto.EncodeEmptyReq(), LibcoreProto.DecodeQueryURLTestResponse);
        }

        public IsPrivilegedResponse IsPrivileged()
        {
            return Call("LibcoreService.IsPrivileged", LibcoreProto.EncodeEmptyReq(), DecodeIsPrivilegedResponse);
        }

        private TResp Call<TResp>(string method, byte[] reqBody, Func<byte[], TResp> decoder)
        {
            using (var client = ProtorpcClient.Connect(_host, _port, _timeoutMs))
            {
                return client.Call(method, reqBody, decoder, _callDeadlineMs);
            }
        }

        private static IsPrivilegedResponse DecodeIsPrivilegedResponse(byte[] data)
        {
            var response = new IsPrivilegedResponse();
            if (data == null || data.Length == 0)
                return response;

            var pos = 0;
            while (pos < data.Length)
            {
                var key = ReadVarint(data, ref pos);
                var field = (int)(key >> 3);
                var wireType = (int)(key & 0x7);
                if (field == 1 && wireType == 0)
                {
                    response.HasPrivilege = ReadVarint(data, ref pos) != 0;
                    continue;
                }
                SkipField(data, ref pos, wireType);
            }
            return response;
        }

        private static ulong ReadVarint(byte[] data, ref int pos)
        {
            ulong value = 0;
            var shift = 0;
            for (var i = 0; i < 10; i++)
            {
                if (pos >= data.Length)
                    throw new EndOfStreamException();
                var b = data[pos++];
                value |= (ulong)(b & 0x7f) << shift;
                if (b < 0x80)
                    return value;
                shift += 7;
            }
            throw new InvalidDataException("Invalid protobuf varint.");
        }

        private static void SkipField(byte[] data, ref int pos, int wireType)
        {
            switch (wireType)
            {
                case 0:
                    ReadVarint(data, ref pos);
                    return;
                case 1:
                    pos += 8;
                    break;
                case 2:
                    pos += checked((int)ReadVarint(data, ref pos));
                    break;
                case 5:
                    pos += 4;
                    break;
                default:
                    throw new InvalidDataException("Unsupported protobuf wire type: " + wireType);
            }

            if (pos < 0 || pos > data.Length)
                throw new EndOfStreamException();
        }
    }
}
