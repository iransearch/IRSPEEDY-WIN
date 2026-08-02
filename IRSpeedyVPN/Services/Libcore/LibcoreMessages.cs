using System.Collections.Generic;

namespace IRSpeedyVPN.Services.Libcore
{
    internal class RequestHeader
    {
        public ulong Id { get; set; }

        public string Method { get; set; }

        public uint RawRequestLen { get; set; }
    }

    internal class ResponseHeader
    {
        public ulong Id { get; set; }

        public string Error { get; set; }

        public uint RawResponseLen { get; set; }
    }

    internal class EmptyReq { }

    internal class EmptyResp { }

    internal class ErrorResp
    {
        public string Error { get; set; }
    }

    internal class LoadConfigReq
    {
        public string CoreConfig { get; set; }

        public bool DisableStats { get; set; }

        public bool NeedExtraProcess { get; set; }

        public string ExtraProcessPath { get; set; }

        public string ExtraProcessArgs { get; set; }

        public string ExtraProcessConf { get; set; }

        public string ExtraProcessConfDir { get; set; }

        public bool ExtraNoOut { get; set; }

        public bool NeedXray { get; set; }

        public string XrayConfig { get; set; }
    }

    internal class URLTestResp
    {
        public string OutboundTag { get; set; }

        public int LatencyMs { get; set; }

        public string Error { get; set; }
        public override string ToString()
        {
            return string.Format($"Tag: {OutboundTag},  LatencyMs: {LatencyMs}, Err: {Error}");
        }
    }

    internal class TestReq
    {
        public string Config { get; set; }

        public List<string> OutboundTags { get; set; } = new List<string>();

        public bool UseDefaultOutbound { get; set; }

        public string Url { get; set; }

        public bool TestCurrent { get; set; }

        public int MaxConcurrency { get; set; }

        public int TestTimeoutMs { get; set; }

        public bool NeedXray { get; set; }

        public string XrayConfig { get; set; }
    }

    internal class TestResp
    {
        public List<URLTestResp> Results { get; set; } = new List<URLTestResp>();
    }

    internal class QueryURLTestResponse
    {
        public List<URLTestResp> Results { get; set; } = new List<URLTestResp>();
    }
}
