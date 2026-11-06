using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using v2rayN.Mode;

namespace IRSpeedyVPN.Services.SingBox
{
    internal static class WireGuardEndpoint
    {
        internal static bool IsLink(string link)
        {
            link = link?.TrimStart();
            return link != null && (link.StartsWith("wg://", StringComparison.OrdinalIgnoreCase)
                || link.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase));
        }

        internal static VmessItem ParseUri(string link)
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Port == 0)
                throw new FormatException("invalid-wireguard-uri");

            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in uri.Query.TrimStart('?').Replace("&amp;", "&").Split('&'))
            {
                int separator = part.IndexOf('=');
                if (separator > 0)
                {
                    // URI query values are not form data. A literal '+' is part of a Base64 key.
                    var key = Uri.UnescapeDataString(part.Substring(0, separator));
                    if (query.ContainsKey(key)) throw new FormatException("duplicate-wireguard-parameter");
                    query.Add(key, Uri.UnescapeDataString(part.Substring(separator + 1)));
                }
            }

            string Value(string name) => query.TryGetValue(name, out var value) ? value : null;
            var localAddress = Value("local_address") ?? Value("address") ?? Value("ip") ?? Value("lp");
            var item = new VmessItem
            {
                configType = EConfigType.WireGaurd,
                address = uri.IdnHost.Trim('[', ']'),
                port = uri.Port < 0 ? 51820 : uri.Port,
                remarks = uri.GetComponents(UriComponents.Fragment, UriFormat.Unescaped),
                privateKey = Value("private_key") ?? Value("privatekey") ?? Value("priv"),
                publicKey = Value("public_key") ?? Value("publickey") ?? Value("peer_public_key") ?? Value("pub"),
                preSharedKey = Value("pre_shared_key") ?? Value("preshared_key") ?? Value("presharedkey") ?? Value("psk"),
                mtu = Value("mtu"),
                ipList = localAddress?.Split(',', '-'),
                wireGuardAllowedIps = Value("allowed_ips")?.Split(',', '-'),
                wireGuardKeepalive = Value("persistent_keepalive_interval") ?? Value("persistent_keepalive") ?? Value("keepalive")
            };

            var amnezia = new Dictionary<string, object>();
            foreach (var name in new[] { "jc", "jmin", "jmax", "s1", "s2", "s3", "s4" })
                if (Value(name) is string number) amnezia[name] = Number(number, int.MaxValue);
            foreach (var name in new[] { "h1", "h2", "h3", "h4" })
            {
                if (!(Value(name) is string value)) continue;
                Range(value, uint.MaxValue);
                amnezia[name] = value;
            }
            foreach (var name in new[] { "i1", "i2", "i3", "i4", "i5" })
            {
                if (!(Value(name) is string value)) continue;
                // Core forwards these values to WireGuard's line-oriented configuration.
                if (value.Any(char.IsControl)) throw new FormatException("invalid-wireguard-signature");
                amnezia[name] = value;
            }
            foreach (var name in new[] { "content_padding_addition", "rekey_after_time", "rekey_timeout",
                "reject_after_time", "keepalive_timeout", "max_handshake_attempts" })
                if (Value(name) is string range) amnezia[name] = Range(range, uint.MaxValue);
            foreach (var name in new[] { "random_trailers", "disable_cookies" })
                if (Value(name) is string flag) amnezia[name] = Flag(flag);
            if (Value("header_protection_key") is string keyValue)
                amnezia["header_protection_key"] = Key(keyValue);
            if (amnezia.Count > 0 || (Value("enable_amnezia") is string enabled && Flag(enabled)))
                item.amneziaWireGuard = amnezia;

            return item;
        }

        internal static Dictionary<string, object> Build(VmessItem item, string tag)
        {
            if (Uri.CheckHostName(item.address) == UriHostNameType.Unknown || item.port < 1 || item.port > 65535)
                throw new FormatException("invalid-wireguard-peer");

            var peer = new Dictionary<string, object>
            {
                ["address"] = item.address,
                ["port"] = item.port,
                ["public_key"] = Key(item.publicKey),
                ["allowed_ips"] = Prefixes(item.wireGuardAllowedIps ?? new[] { "0.0.0.0/0", "::/0" })
            };
            if (!string.IsNullOrEmpty(item.preSharedKey))
                peer["pre_shared_key"] = Key(item.preSharedKey);
            if (!string.IsNullOrEmpty(item.wireGuardKeepalive))
                peer["persistent_keepalive_interval"] = Range(item.wireGuardKeepalive, ushort.MaxValue);

            var endpoint = new Dictionary<string, object>
            {
                ["type"] = "wireguard",
                ["tag"] = tag,
                ["address"] = Prefixes(item.ipList),
                ["private_key"] = Key(item.privateKey),
                ["peers"] = new[] { peer }
            };
            if (!string.IsNullOrEmpty(item.mtu))
                endpoint["mtu"] = Number(item.mtu, ushort.MaxValue);
            if (item.amneziaWireGuard != null)
                endpoint["amnezia_wg"] = item.amneziaWireGuard;
            return endpoint;
        }

        private static string Key(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new FormatException("invalid-wireguard-key");
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 32 || Convert.ToBase64String(bytes) != value)
                throw new FormatException("invalid-wireguard-key");
            return value;
        }

        private static string[] Prefixes(string[] values)
        {
            if (values == null || values.Length == 0) throw new FormatException("missing-wireguard-prefix");
            return values.Select(value =>
            {
                var parts = value.Trim().Split('/');
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address))
                    throw new FormatException("invalid-wireguard-prefix");
                var length = Number(parts[1], address.AddressFamily == AddressFamily.InterNetwork ? 32u : 128u);
                return address + "/" + length.ToString(CultureInfo.InvariantCulture);
            }).ToArray();
        }

        private static uint Number(string value, uint max)
        {
            if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number > max)
                throw new FormatException("invalid-wireguard-number");
            return number;
        }

        private static object Range(string value, uint max)
        {
            var parts = value.Split('-');
            if (parts.Length == 1) return Number(value, max);
            if (parts.Length != 2 || Number(parts[0], max) > Number(parts[1], max))
                throw new FormatException("invalid-wireguard-range");
            return value;
        }

        private static bool Flag(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "on":
                case "yes":
                    return true;
                case "false":
                case "0":
                case "off":
                case "no":
                    return false;
                default:
                    throw new FormatException("invalid-wireguard-flag");
            }
        }
    }
}
