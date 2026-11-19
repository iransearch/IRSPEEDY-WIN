using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using v2rayN.Mode;

namespace IRSpeedyVPN.Services.SingBox
{
    internal static class WireGuardEndpoint
    {
        private const string ProfilePrefix = "vpn://";
        private const int MaxEncodedProfileLength = 262144;
        private static readonly string[] IntegerOptions = { "jc", "jmin", "jmax", "s1", "s2", "s3", "s4" };
        private static readonly string[] HeaderOptions = { "h1", "h2", "h3", "h4" };
        private static readonly string[] SignatureOptions = { "i1", "i2", "i3", "i4", "i5" };
        private static readonly string[] RangeOptions = { "content_padding_addition", "rekey_after_time",
            "rekey_timeout", "reject_after_time", "keepalive_timeout", "max_handshake_attempts" };
        private static readonly string[] FlagOptions = { "random_trailers", "disable_cookies" };
        private static readonly Dictionary<string, string> InterfaceFields =
            new[] { "private_key", "local_address", "mtu", "header_protection_key" }
                .Concat(IntegerOptions).Concat(HeaderOptions).Concat(SignatureOptions).Concat(RangeOptions).Concat(FlagOptions)
                .ToDictionary(name => name == "local_address" ? "address" : name.Replace("_", ""),
                    StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> PeerFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["publickey"] = "public_key",
            ["presharedkey"] = "pre_shared_key",
            ["allowedips"] = "allowed_ips",
            ["persistentkeepalive"] = "persistent_keepalive_interval",
            ["endpoint"] = "endpoint"
        };

        // Framework exception messages can contain input. Only our fixed parser codes may enter logs.
        internal static string DiagnosticReason(Exception error)
        {
            switch (error.Message)
            {
                case "duplicate-wireguard-parameter":
                case "invalid-wireguard-flag":
                case "invalid-wireguard-key":
                case "invalid-wireguard-mtu":
                case "invalid-wireguard-number":
                case "invalid-wireguard-peer":
                case "invalid-wireguard-prefix":
                case "invalid-wireguard-profile-encoding":
                case "invalid-wireguard-profile-endpoint":
                case "invalid-wireguard-profile-field":
                case "invalid-wireguard-profile-section":
                case "invalid-wireguard-profile-size":
                case "invalid-wireguard-profile-text":
                case "invalid-wireguard-profile-value":
                case "invalid-wireguard-range":
                case "invalid-wireguard-signature":
                case "invalid-wireguard-uri":
                case "missing-wireguard-prefix":
                    return error.Message;
                default:
                    return error.GetType().Name;
            }
        }

        internal static bool IsLink(string link)
        {
            link = link?.TrimStart();
            return link != null && (link.StartsWith("wg://", StringComparison.OrdinalIgnoreCase)
                || link.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase)
                || link.StartsWith(ProfilePrefix, StringComparison.OrdinalIgnoreCase));
        }

        internal static VmessItem ParseUri(string link)
        {
            link = link?.TrimStart();
            if (link?.StartsWith(ProfilePrefix, StringComparison.OrdinalIgnoreCase) == true)
                return ParseProfile(link);

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

            return CreateItem(uri, query, uri.GetComponents(UriComponents.Fragment, UriFormat.Unescaped));
        }

        private static VmessItem ParseProfile(string link)
        {
            int fragment = link.IndexOf('#');
            int payloadLength = (fragment < 0 ? link.Length : fragment) - ProfilePrefix.Length;
            if (payloadLength == 0 || payloadLength > MaxEncodedProfileLength)
                throw new FormatException("invalid-wireguard-profile-size");

            // Android shares wg-quick text as standard or URL-safe Base64, often without padding.
            var payload = Uri.UnescapeDataString(link.Substring(ProfilePrefix.Length, payloadLength))
                .Replace('-', '+').Replace('_', '/');
            if (payload.Any(char.IsWhiteSpace)) throw new FormatException("invalid-wireguard-profile-encoding");
            if (!payload.Contains("=")) payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var profile = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(payload));
            if (profile.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                throw new FormatException("invalid-wireguard-profile-text");

            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> fields = null;
            bool hasInterface = false, hasPeer = false;
            foreach (var rawLine in profile.TrimStart('\uFEFF').Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int comment = rawLine.IndexOf('#');
                var line = (comment < 0 ? rawLine : rawLine.Substring(0, comment)).Trim();
                if (line.Length == 0 || line[0] == ';') continue;
                if (line[0] == '[')
                {
                    if (!hasInterface && string.Equals(line, "[Interface]", StringComparison.OrdinalIgnoreCase))
                    {
                        hasInterface = true;
                        fields = InterfaceFields;
                    }
                    else if (!hasPeer && string.Equals(line, "[Peer]", StringComparison.OrdinalIgnoreCase))
                    {
                        hasPeer = true;
                        fields = PeerFields;
                    }
                    else throw new FormatException("invalid-wireguard-profile-section");
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0 || fields == null)
                    throw new FormatException("invalid-wireguard-profile-field");
                var name = line.Substring(0, separator).Trim().Replace("_", "");
                // Only transport fields reach Core. DNS, routing and hooks are wg-quick directives.
                if (!fields.TryGetValue(name, out var option)) continue;
                var value = line.Substring(separator + 1).Trim();
                if (value.Length == 0 || value.Any(char.IsControl) || query.ContainsKey(option))
                    throw new FormatException("invalid-wireguard-profile-value");
                query.Add(option, value);
            }

            if (!hasInterface || !hasPeer || !query.ContainsKey("private_key") || !query.ContainsKey("local_address")
                || !query.ContainsKey("public_key") || !query.TryGetValue("endpoint", out var endpoint)
                || endpoint.IndexOfAny(new[] { '/', '\\', '?', '#', '@' }) >= 0
                || !Uri.TryCreate("wg://" + endpoint, UriKind.Absolute, out var uri) || uri.Port <= 0
                || Uri.CheckHostName(uri.IdnHost.Trim('[', ']')) == UriHostNameType.Unknown)
                throw new FormatException("invalid-wireguard-profile-endpoint");
            if (!query.TryGetValue("mtu", out var mtu)) query.Add("mtu", "1420");
            else if (Number(mtu, 9000) < 576) throw new FormatException("invalid-wireguard-mtu");

            var remarks = fragment < 0 ? "" : Uri.UnescapeDataString(link.Substring(fragment + 1));
            return CreateItem(uri, query, remarks);
        }

        private static VmessItem CreateItem(Uri uri, Dictionary<string, string> query, string remarks)
        {
            string Value(string name) => query.TryGetValue(name, out var value) ? value : null;
            var localAddress = Value("local_address") ?? Value("address") ?? Value("ip") ?? Value("lp");
            var item = new VmessItem
            {
                configType = EConfigType.WireGaurd,
                address = uri.IdnHost.Trim('[', ']'),
                port = uri.Port < 0 ? 51820 : uri.Port,
                remarks = remarks,
                privateKey = Value("private_key") ?? Value("privatekey") ?? Value("priv"),
                publicKey = Value("public_key") ?? Value("publickey") ?? Value("peer_public_key") ?? Value("pub"),
                preSharedKey = Value("pre_shared_key") ?? Value("preshared_key") ?? Value("presharedkey") ?? Value("psk"),
                mtu = Value("mtu"),
                ipList = localAddress?.Split(',', '-'),
                wireGuardAllowedIps = Value("allowed_ips")?.Split(',', '-'),
                wireGuardKeepalive = Value("persistent_keepalive_interval") ?? Value("persistent_keepalive") ?? Value("keepalive")
            };

            var amnezia = new Dictionary<string, object>();
            foreach (var name in IntegerOptions)
                if (Value(name) is string number) amnezia[name] = Number(number, int.MaxValue);
            foreach (var name in HeaderOptions)
            {
                if (!(Value(name) is string value)) continue;
                Range(value, uint.MaxValue);
                amnezia[name] = value;
            }
            foreach (var name in SignatureOptions)
            {
                if (!(Value(name) is string value)) continue;
                // Core forwards these values to WireGuard's line-oriented configuration.
                if (value.Any(char.IsControl)) throw new FormatException("invalid-wireguard-signature");
                amnezia[name] = value;
            }
            foreach (var name in RangeOptions)
                if (Value(name) is string range) amnezia[name] = Range(range, uint.MaxValue);
            foreach (var name in FlagOptions)
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
