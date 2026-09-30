using System;
using System.Collections.Generic;
using System.IO;

namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class CoreTrafficConnection
    {
        public string Id = "";
        public string Process = "";
        public string ProcessPath = "";
        public long Upload;
        public long Download;
    }

    internal sealed class QueryConnectionsResponse
    {
        public readonly List<CoreTrafficConnection> Active = new List<CoreTrafficConnection>();
        public readonly List<CoreTrafficConnection> Closed = new List<CoreTrafficConnection>();
    }

    internal static partial class LibcoreProto
    {
        // Throne libcore.proto: active=1, recently-closed (non-draining)=2.
        // Read only fields required for usage; never retain destinations or IPs.
        public static QueryConnectionsResponse DecodeQueryConnections(byte[] data)
        {
            var result = new QueryConnectionsResponse();
            var reader = new ProtoReader(data ?? Array.Empty<byte>());
            while (reader.TryReadField(out int field, out int wire))
            {
                if ((field == 1 || field == 2) && wire == WireLengthDelimited)
                {
                    var row = ReadTrafficConnection(reader.ReadBytes());
                    if (!string.IsNullOrEmpty(row.Id))
                        (field == 1 ? result.Active : result.Closed).Add(row);
                }
                else reader.SkipField(wire);
            }
            return result;
        }

        private static CoreTrafficConnection ReadTrafficConnection(byte[] data)
        {
            var row = new CoreTrafficConnection();
            var reader = new ProtoReader(data);
            while (reader.TryReadField(out int field, out int wire))
            {
                if (wire == WireLengthDelimited && (field == 1 || field == 10 || field == 11))
                {
                    var value = reader.ReadString();
                    if (field == 1) row.Id = value;
                    else if (field == 10) row.Process = value;
                    else row.ProcessPath = value;
                }
                else if (wire == WireVarint && (field == 3 || field == 4))
                {
                    long value = unchecked((long)reader.ReadVarint());
                    if (value < 0) throw new InvalidDataException("Negative traffic counter.");
                    if (field == 3) row.Upload = value; else row.Download = value;
                }
                else reader.SkipField(wire);
            }
            return row;
        }
    }
}
