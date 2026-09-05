using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IRSpeedyVPN.Services.Libcore
{
    internal static class LibcoreProto
    {
        private const int WireVarint = 0;
        private const int WireLengthDelimited = 2;

        public static byte[] EncodeRequestHeader(ulong id, string method, uint rawLen)
        {
            var w = new ProtoWriter();
            w.WriteVarintField(1, id);
            w.WriteStringField(2, method ?? "");
            w.WriteVarintField(3, rawLen);
            return w.ToArray();
        }

        public static ResponseHeader DecodeResponseHeader(byte[] data)
        {
            var r = new ProtoReader(data);
            var header = new ResponseHeader();
            while (r.TryReadField(out int field, out int wireType))
            {
                switch (field)
                {
                    case 1:
                        header.Id = (ulong)r.ReadVarint();
                        break;
                    case 2:
                        header.Error = r.ReadString();
                        break;
                    case 3:
                        header.RawResponseLen = (uint)r.ReadVarint();
                        break;
                    default:
                        r.SkipField(wireType);
                        break;
                }
            }
            return header;
        }

        public static byte[] EncodeEmptyReq()
        {
            return Array.Empty<byte>();
        }

        public static byte[] EncodeLoadConfigReq(LoadConfigReq req)
        {
            var w = new ProtoWriter();
            w.WriteStringField(1, req?.CoreConfig ?? "");
            w.WriteBoolField(2, req?.DisableStats ?? false);
            w.WriteBoolField(3, req?.NeedExtraProcess ?? false);
            w.WriteStringField(4, req?.ExtraProcessPath ?? "");
            w.WriteStringField(5, req?.ExtraProcessArgs ?? "");
            w.WriteStringField(6, req?.ExtraProcessConf ?? "");
            w.WriteStringField(7, req?.ExtraProcessConfDir ?? "");
            w.WriteBoolField(8, req?.ExtraNoOut ?? false);
            w.WriteBoolField(9, req?.NeedXray ?? false);
            w.WriteStringField(10, req?.XrayConfig ?? "");
            return w.ToArray();
        }

        public static byte[] EncodeTestReq(TestReq req)
        {
            var w = new ProtoWriter();
            w.WriteStringField(1, req?.Config ?? "");
            if (req?.OutboundTags != null)
            {
                foreach (var tag in req.OutboundTags)
                {
                    w.WriteStringField(2, tag ?? "");
                }
            }
            w.WriteBoolField(3, req?.UseDefaultOutbound ?? false);
            w.WriteStringField(4, req?.Url ?? "");
            w.WriteBoolField(5, req?.TestCurrent ?? false);
            w.WriteVarintField(6, (ulong)(req?.MaxConcurrency ?? 0));
            w.WriteVarintField(7, (ulong)(req?.TestTimeoutMs ?? 0));
            w.WriteBoolField(8, req?.NeedXray ?? false);
            w.WriteStringField(9, req?.XrayConfig ?? "");
            return w.ToArray();
        }

        public static ErrorResp DecodeErrorResp(byte[] data)
        {
            var resp = new ErrorResp();
            if (data == null || data.Length == 0) return resp;
            var r = new ProtoReader(data);
            while (r.TryReadField(out int field, out int wireType))
            {
                switch (field)
                {
                    case 1:
                        resp.Error = r.ReadString();
                        break;
                    default:
                        r.SkipField(wireType);
                        break;
                }
            }
            return resp;
        }

        public static TestResp DecodeTestResp(byte[] data)
        {
            var resp = new TestResp();
            if (data == null || data.Length == 0) return resp;
            var r = new ProtoReader(data);
            while (r.TryReadField(out int field, out int wireType))
            {
                switch (field)
                {
                    case 1:
                        var msg = r.ReadBytes();
                        resp.Results.Add(DecodeUrlTestResp(msg));
                        break;
                    default:
                        r.SkipField(wireType);
                        break;
                }
            }
            return resp;
        }

        public static QueryURLTestResponse DecodeQueryURLTestResponse(byte[] data)
        {
            var resp = new QueryURLTestResponse();
            if (data == null || data.Length == 0) return resp;
            var r = new ProtoReader(data);
            while (r.TryReadField(out int field, out int wireType))
            {
                switch (field)
                {
                    case 1:
                        var msg = r.ReadBytes();
                        resp.Results.Add(DecodeUrlTestResp(msg));
                        break;
                    default:
                        r.SkipField(wireType);
                        break;
                }
            }
            return resp;
        }

        private static URLTestResp DecodeUrlTestResp(byte[] data)
        {
            var resp = new URLTestResp();
            if (data == null || data.Length == 0) return resp;
            var r = new ProtoReader(data);
            while (r.TryReadField(out int field, out int wireType))
            {
                switch (field)
                {
                    case 1:
                        resp.OutboundTag = r.ReadString();
                        break;
                    case 2:
                        resp.LatencyMs = (int)r.ReadVarint();
                        break;
                    case 3:
                        resp.Error = r.ReadString();
                        break;
                    default:
                        r.SkipField(wireType);
                        break;
                }
            }
            return resp;
        }

        internal sealed class ProtoWriter
        {
            private readonly MemoryStream _ms = new MemoryStream();

            public void WriteVarintField(int fieldNumber, ulong value)
            {
                WriteTag(fieldNumber, WireVarint);
                WriteVarint(value);
            }

            public void WriteBoolField(int fieldNumber, bool value)
            {
                WriteVarintField(fieldNumber, value ? 1UL : 0UL);
            }

            public void WriteStringField(int fieldNumber, string value)
            {
                WriteTag(fieldNumber, WireLengthDelimited);
                var bytes = Encoding.UTF8.GetBytes(value ?? "");
                WriteVarint((ulong)bytes.Length);
                _ms.Write(bytes, 0, bytes.Length);
            }

            public void WriteBytesField(int fieldNumber, byte[] data)
            {
                WriteTag(fieldNumber, WireLengthDelimited);
                var bytes = data ?? Array.Empty<byte>();
                WriteVarint((ulong)bytes.Length);
                _ms.Write(bytes, 0, bytes.Length);
            }

            private void WriteTag(int fieldNumber, int wireType)
            {
                WriteVarint((ulong)((fieldNumber << 3) | wireType));
            }

            private void WriteVarint(ulong value)
            {
                while (value >= 0x80)
                {
                    _ms.WriteByte((byte)(value | 0x80));
                    value >>= 7;
                }
                _ms.WriteByte((byte)value);
            }

            public byte[] ToArray() => _ms.ToArray();
        }

        internal sealed class ProtoReader
        {
            private readonly byte[] _data;
            private int _pos;

            public ProtoReader(byte[] data)
            {
                _data = data ?? Array.Empty<byte>();
                _pos = 0;
            }

            public bool TryReadField(out int fieldNumber, out int wireType)
            {
                fieldNumber = 0;
                wireType = 0;
                if (_pos >= _data.Length) return false;
                var key = ReadVarint();
                wireType = (int)(key & 0x7);
                fieldNumber = (int)(key >> 3);
                return true;
            }

            public ulong ReadVarint()
            {
                ulong x = 0;
                int s = 0;
                for (int i = 0; ; i++)
                {
                    if (_pos >= _data.Length) throw new EndOfStreamException();
                    byte b = _data[_pos++];
                    if (b < 0x80)
                    {
                        if (i > 9 || (i == 9 && b > 1))
                        {
                            throw new InvalidOperationException("varint overflows 64-bit integer");
                        }
                        return x | ((ulong)b << s);
                    }
                    x |= ((ulong)(b & 0x7f)) << s;
                    s += 7;
                }
            }

            public string ReadString()
            {
                var bytes = ReadBytes();
                return Encoding.UTF8.GetString(bytes);
            }

            public byte[] ReadBytes()
            {
                var len = (int)ReadVarint();
                if (len == 0) return Array.Empty<byte>();
                if (len < 0 || len > _data.Length - _pos) throw new EndOfStreamException();
                var bytes = new byte[len];
                Buffer.BlockCopy(_data, _pos, bytes, 0, len);
                _pos += len;
                return bytes;
            }

            public void SkipField(int wireType)
            {
                switch (wireType)
                {
                    case WireVarint:
                        ReadVarint();
                        return;
                    case WireLengthDelimited:
                        var len = (int)ReadVarint();
                        _pos += len;
                        if (_pos > _data.Length) throw new EndOfStreamException();
                        return;
                    case 1:
                        _pos += 8;
                        return;
                    case 5:
                        _pos += 4;
                        return;
                    default:
                        throw new InvalidOperationException($"Unsupported wire type: {wireType}");
                }
            }
        }
    }
}
