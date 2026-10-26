using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace IRSpeedyVPN.WebServices
{
    // Login-only classic DNS. Connected UDP validates the peer;
    // transaction, echoed question, owner chain and public RDATA are checked.
    internal static class LoginPlainDns
    {
        internal static readonly string[] Addresses = { "8.8.8.8", "77.88.8.8", "1.1.1.1", "94.140.14.14" };
        private static readonly string[] Names = { "google", "yandex", "cloudflare", "adguard" };
        internal static string[] Resolve(string host, int timeoutMs, CancellationToken cancellation, Action<string> log)
        {
            string name = new System.Globalization.IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant();
            var endpoints = Addresses.SelectMany(ip => new[] { ip + "/1", ip + "/28" }).ToArray();
            var names = Names.SelectMany(n => new[] { n + "-A", n + "-AAAA" }).ToArray();
            try { log?.Invoke("[LoginRecovery] host=" + name + " stage=resolver-mode mode=plain-dns"); } catch { }
            return LoginDnsRace.Resolve(name, timeoutMs, cancellation, (endpoint, ms, token) =>
            {
                var parts = endpoint.Split('/');
                return Query(name, new IPEndPoint(IPAddress.Parse(parts[0]), 53), int.Parse(parts[1]), ms, token);
            }, log, endpoints, names);
        }
        internal static byte[] BuildQuery(string host, int type, ushort id)
        {
            var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (string label in host.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63 || label.Any(c => c > 127)) throw new FormatException("Invalid DNS label.");
                bytes.Add((byte)label.Length); bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
            }
            bytes.Add(0); bytes.Add((byte)(type >> 8)); bytes.Add((byte)type); bytes.Add(0); bytes.Add(1);
            if (bytes.Count > 271) throw new FormatException("Invalid DNS name length.");
            return bytes.ToArray();
        }
        internal static string[] Query(string host, IPEndPoint server, int type, int timeoutMs, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            ushort id = BitConverter.ToUInt16(Guid.NewGuid().ToByteArray(), 0);
            byte[] query = BuildQuery(host, type, id);
            using (var socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            using (token.Register(() => { try { socket.Close(); } catch { } }))
            {
                token.ThrowIfCancellationRequested(); socket.Connect(server);
                socket.SendTimeout = socket.ReceiveTimeout = Math.Max(1, timeoutMs);
                socket.Send(query);
                int left = timeoutMs - (int)clock.ElapsedMilliseconds; if (left <= 0) throw new TimeoutException();
                socket.ReceiveTimeout = left; var buffer = new byte[4096]; int count = socket.Receive(buffer);
                var data = buffer.Take(count).ToArray();
                ValidateQuestion(data, host, type, id);
                if ((data[2] & 2) == 0) return Parse(data, host, type, id);
            }
            // A truncated UDP answer requires TCP; the original lookup clock remains.
            int remaining = timeoutMs - (int)clock.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException();
            using (var socket = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            using (token.Register(() => { try { socket.Close(); } catch { } }))
            {
                token.ThrowIfCancellationRequested();
                var connect = socket.BeginConnect(server, null, null);
                using (connect.AsyncWaitHandle)
                    if (!connect.AsyncWaitHandle.WaitOne(remaining)) throw new TimeoutException();
                socket.EndConnect(connect);
                var frame = new byte[query.Length + 2]; frame[0] = (byte)(query.Length >> 8); frame[1] = (byte)query.Length;
                Buffer.BlockCopy(query, 0, frame, 2, query.Length);
                Transfer(socket, frame, true, clock, timeoutMs, token);
                var length = new byte[2]; Transfer(socket, length, false, clock, timeoutMs, token);
                int size = U16(length, 0); if (size < 12) throw new FormatException();
                var data = new byte[size]; Transfer(socket, data, false, clock, timeoutMs, token);
                return Parse(data, host, type, id);
            }
        }
        private static void Transfer(Socket socket, byte[] bytes, bool send, Stopwatch clock, int budget, CancellationToken token)
        {
            for (int offset = 0; offset < bytes.Length;)
            {
                token.ThrowIfCancellationRequested(); int left = budget - (int)clock.ElapsedMilliseconds;
                if (left <= 0) throw new TimeoutException();
                socket.SendTimeout = socket.ReceiveTimeout = left;
                int count = send ? socket.Send(bytes, offset, bytes.Length - offset, SocketFlags.None)
                    : socket.Receive(bytes, offset, bytes.Length - offset, SocketFlags.None);
                if (count <= 0) throw new FormatException("Incomplete DNS frame."); offset += count;
            }
        }
        private static int U16(byte[] data, int offset)
        { if (offset < 0 || offset + 2 > data.Length) throw new FormatException(); return (data[offset] << 8) | data[offset + 1]; }
        private static string Name(byte[] data, ref int offset)
        {
            var labels = new List<string>(); int cursor = offset, end = -1, steps = 0, length = 0;
            while (true)
            {
                if (++steps > 128 || cursor >= data.Length) throw new FormatException();
                int size = data[cursor++];
                if (size == 0) { offset = end < 0 ? cursor : end; return string.Join(".", labels).ToLowerInvariant(); }
                if ((size & 0xc0) == 0xc0)
                {
                    if (cursor >= data.Length) throw new FormatException();
                    if (end < 0) end = cursor + 1;
                    cursor = ((size & 63) << 8) | data[cursor]; continue;
                }
                if (size > 63 || cursor + size > data.Length || (length += size + 1) > 254) throw new FormatException();
                for (int i = 0; i < size; i++) if (data[cursor + i] > 127 || data[cursor + i] == 0) throw new FormatException();
                labels.Add(System.Text.Encoding.ASCII.GetString(data, cursor, size)); cursor += size;
            }
        }
        private static int ValidateQuestion(byte[] data, string host, int type, ushort id)
        {
            if (data.Length < 12 || U16(data, 0) != id || (data[2] & 0xf8) != 0x80
                || (data[3] & 15) != 0 || U16(data, 4) != 1) throw new FormatException("Invalid DNS reply.");
            int pos = 12; if (Name(data, ref pos) != host.TrimEnd('.').ToLowerInvariant()
                || U16(data, pos) != type || U16(data, pos + 2) != 1) throw new FormatException("DNS question mismatch.");
            return pos + 4;
        }
        internal static string[] Parse(byte[] data, string host, int type, ushort id)
        {
            int pos = ValidateQuestion(data, host, type, id);
            if ((data[2] & 2) != 0) throw new FormatException("Truncated DNS reply.");
            int count = U16(data, 6); if (count > 512) throw new FormatException();
            var aliases = new List<Tuple<string, string>>(); var records = new List<Tuple<string, string>>();
            for (int i = 0; i < count; i++)
            {
                string owner = Name(data, ref pos); int rrType = U16(data, pos), cls = U16(data, pos + 2), size = U16(data, pos + 8); pos += 10;
                if (pos + size > data.Length) throw new FormatException();
                if (cls == 1 && rrType == 5)
                { int p = pos; string alias = Name(data, ref p); if (p != pos + size) throw new FormatException(); aliases.Add(Tuple.Create(owner, alias)); }
                if (cls == 1 && rrType == type && (type == 1 && size == 4 || type == 28 && size == 16))
                { var address = new IPAddress(data.Skip(pos).Take(size).ToArray()).ToString(); if (LoginDnsCache.IsPublicAddress(address)) records.Add(Tuple.Create(owner, address)); }
                pos += size;
            }
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { host.TrimEnd('.') };
            for (int pass = 0; pass < aliases.Count; pass++) foreach (var alias in aliases) if (allowed.Contains(alias.Item1)) allowed.Add(alias.Item2);
            return records.Where(r => allowed.Contains(r.Item1)).Select(r => r.Item2).Distinct().Take(64).ToArray();
        }
    }

}
