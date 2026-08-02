using System;

namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class LibcoreServiceClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly int _timeoutMs;

        public LibcoreServiceClient(string host, int port, int timeoutMs = 2000)
        {
            _host = host;
            _port = port;
            _timeoutMs = timeoutMs;
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
            return Call("CheckConfig", LibcoreProto.EncodeLoadConfigReq(req), LibcoreProto.DecodeErrorResp);
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

        private TResp Call<TResp>(string method, byte[] reqBody, Func<byte[], TResp> decoder)
        {
            using (var client = ProtorpcClient.Connect(_host, _port, _timeoutMs))
            {
                return client.Call(method, reqBody, decoder);
            }
        }
    }
}
