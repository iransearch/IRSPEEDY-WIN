using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace IRSpeedyVPN.Services.Libcore
{
    internal sealed class CoreTrafficConnection
    {
        public string Id = "";
        public string Process = "";
        public string ProcessPath = "";
        public string Outbound = "", Network = "", DiagnosticTarget = "other";
        internal int LoopbackSourcePort;
        internal bool IsRecoveryTarget;
        public readonly List<string> Chain = new List<string>();
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
        // Retain usage and routing tags. Classify the app's fixed IP providers,
        // Only a loopback source port and the fixed recovery-target classification
        // are retained for matching the app's live TLS socket. No source addresses
        // or browsing destinations/domains are retained.
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
                else if (wire == WireLengthDelimited && (field == 5 || field == 6 || field == 7 || field == 9 || field == 12 || field == 14))
                {
                    string value = reader.ReadString();
                    if (field == 5) row.Outbound = value;
                    else if (field == 6) row.Network = value;
                    else if (field == 12) { if (row.Chain.Count < 16) row.Chain.Add(value); }
                    else if (field == 14) row.LoopbackSourcePort = LoopbackPort(value);
                    else
                    {
                        string target = DiagnosticTarget(value);
                        if (target != "other") row.DiagnosticTarget = target;
                        if (value == "www.microsoft.com" || value == "www.microsoft.com:443"
                            || value == "www.cloudflare.com" || value == "www.cloudflare.com:443")
                            row.IsRecoveryTarget = true;
                    }
                }
                else reader.SkipField(wire);
            }
            return row;
        }

        private static int LoopbackPort(string source)
        {
            int colon = source.LastIndexOf(':');
            IPAddress address;
            int port;
            return colon > 0 && IPAddress.TryParse(source.Substring(0, colon).Trim('[', ']'), out address)
                && (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).Equals(IPAddress.Loopback)
                && int.TryParse(source.Substring(colon + 1), out port)
                && port > 0 && port <= 65535 ? port : 0;
        }

        internal static string DiagnosticTarget(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 256) return "other";
            // ConnectionMetaData.dest is host:port; domain is just the hostname.
            string host = value;
            int colon = host.LastIndexOf(':');
            if (colon >= 0) host = host.Substring(0, colon);
            host = host.TrimEnd('.').ToLowerInvariant();
            switch (host)
            {
                case "api.ipify.org": return "public-ip-ipify";
                case "checkip.amazonaws.com": return "public-ip-amazon";
                case "icanhazip.com": return "public-ip-icanhazip";
                default: return "other";
            }
        }
    }
}
