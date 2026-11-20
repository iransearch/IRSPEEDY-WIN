using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace IRSpeedyVPN.Common
{
    // Parse only fixed categories and numeric local-bind metadata from an RPC
    // error. Never return the raw message, endpoint, config or an inbound tag.
    internal static class CoreStartFailureDiagnostic
    {
        private static readonly Regex Socket = new Regex(
            @"\b(?<op>listen|bind|dial|connect|accept|read|write)\s+(?<network>tcp[46]?|udp[46]?|ip[46]?(?::icmp)?)\b", RegexOptions.IgnoreCase);
        private static readonly Regex Endpoint = new Regex(
            @"^\s+(?:on\s+)?(?:address\s*:\s*)?(?<host>\[[0-9a-f:.%]+\]|[0-9.]+):(?<port>[0-9]{1,5})(?=[:\s,]|$)", RegexOptions.IgnoreCase);
        private static readonly Regex Inbound = new Regex(@"\binbound/(?<type>[a-z0-9_-]+)\b", RegexOptions.IgnoreCase);
        private static readonly Regex NativeCode = new Regex(
            @"\b(?:native(?:error|code)|errno|win32(?:error)?|socketerror|error\s+code)\s*[:=]\s*(?<code>[0-9]{1,7})\b", RegexOptions.IgnoreCase);
        private static readonly Regex IndexedInbound = new Regex(@"\binitialize inbound\[[0-9]+\]");
        private static readonly Regex BlockedDnsFallback = new Regex(
            @"\b(?:bind|denied|wsaeacces|wsaeaddrinuse|wsaeaddrnotavail)\b|operation not permitted|forbidden by its access permissions");
        private static readonly Regex DnsRulePath = new Regex(@"^dns\.rules\[[0-9]+\][.:]");
        private static readonly Regex DnsRaceError = new Regex(
            @"\b(?:unknown transport type: vpn-dns-race(?![\w-])|start dns/vpn-dns-race\[|vpn-dns-race (?:requires|timeouts must be positive|servers must share one vpn detour|fallback must use the same vpn detour)\b|invalid vpn-dns-race doh options\b|unsupported vpn-dns-race transport:)");

        internal static string Context(string error)
        {
            string text = error ?? string.Empty;
            if (text.Length > 8192) text = text.Substring(0, 8192);
            // A config dump is not useful error context and may contain secrets.
            int json = text.IndexOf('{');
            if (json >= 0) text = text.Substring(0, json);
            return Regex.Replace(text,
                @"(?i)\b(?:password|token|authorization|uuid|secret|user(?:name)?|id|key)[""']?\s*[:=]\s*(?:""(?:\\.|[^""])*(?:""|$)|'(?:\\.|[^'])*(?:'|$)|[^\s,;]+)", " REDACTED ");
        }

        internal static string Fields(string error)
        {
            int length = (error ?? string.Empty).Length;
            string text = StartupContext(error);
            string lower = text.ToLowerInvariant();
            var inbound = Inbound.Match(text);
            string kind = inbound.Success ? AllowedInbound(inbound.Groups["type"].Value.ToLowerInvariant()) : "unknown";
            string component = Component(lower, kind, inbound.Success);
            var socket = Socket.Match(text);
            string network = socket.Success ? socket.Groups["network"].Value.ToLowerInvariant().Replace(':', '-') : "unknown";
            string operation = socket.Success ? socket.Groups["op"].Value.ToLowerInvariant() : Operation(lower);
            // The inner syscall is more precise than an outer 'listen tcp'.
            if (Regex.IsMatch(lower, @"(?:^|:\s*)bind\s*:")) operation = "bind";
            int port = -1;
            string scope = "unknown";
            if (socket.Success && (socket.Groups["op"].Value.Equals("listen", StringComparison.OrdinalIgnoreCase)
                || socket.Groups["op"].Value.Equals("bind", StringComparison.OrdinalIgnoreCase)))
            {
                var endpoint = Endpoint.Match(text.Substring(socket.Index + socket.Length));
                IPAddress address;
                int parsed;
                if (endpoint.Success && IPAddress.TryParse(endpoint.Groups["host"].Value.Trim('[', ']'), out address)
                    && int.TryParse(endpoint.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                    && parsed <= 65535)
                {
                    port = parsed;
                    scope = IPAddress.IsLoopback(address) ? "loopback"
                        : address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ? "wildcard" : "other-local";
                }
            }
            int code = -1;
            string codeSource = "unavailable";
            var native = NativeCode.Match(text);
            if (native.Success && int.TryParse(native.Groups["code"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out code))
                codeSource = "reported";
            else if (Regex.IsMatch(lower, @"\bwsaeacces\b")) { code = 10013; codeSource = "symbol"; }
            else if (Regex.IsMatch(lower, @"\bwsaeaddrinuse\b")) { code = 10048; codeSource = "symbol"; }
            else if (Regex.IsMatch(lower, @"\bwsaeaddrnotavail\b")) { code = 10049; codeSource = "symbol"; }
            return "component=" + component + " inboundType=" + kind + " operation=" + operation
                + " network=" + network + " localPort=" + port.ToString(CultureInfo.InvariantCulture)
                + " bindScope=" + scope + " nativeError=" + code.ToString(CultureInfo.InvariantCulture)
                + " codeSource=" + codeSource + " errorChars=" + length.ToString(CultureInfo.InvariantCulture)
                + " errorTruncated=" + (length > 8192);
        }

        internal static bool IsDnsComponent(string error)
        {
            string text = StartupContext(error);
            string lower = text.ToLowerInvariant();
            var inbound = Inbound.Match(text);
            string kind = inbound.Success ? AllowedInbound(inbound.Groups["type"].Value.ToLowerInvariant()) : "unknown";
            var native = NativeCode.Match(text);
            string code = native.Success ? native.Groups["code"].Value : "";
            return code != "10013" && code != "10048" && code != "10049" && !BlockedDnsFallback.IsMatch(lower)
                && Component(lower, kind, inbound.Success) == "dns";
        }

        private static string StartupContext(string error)
        {
            // Throne-G puts the original config before the parser error. Skip
            // that object, including quoted braces, without classifying its data.
            const string prefix = "decode config at ";
            if (error == null || !error.StartsWith(prefix, StringComparison.Ordinal)) return Context(error);
            int first = prefix.Length;
            while (first < error.Length && char.IsWhiteSpace(error[first])) first++;
            if (first == error.Length || error[first] != '{') return string.Empty;
            int depth = 0;
            bool quoted = false;
            for (int i = first; i < error.Length; i++)
            {
                char c = error[i];
                if (quoted)
                {
                    if (c == '\\') i++;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    if (--depth != 0) continue;
                    int separator = i + 1;
                    while (separator < error.Length && char.IsWhiteSpace(error[separator])) separator++;
                    if (separator >= error.Length || error[separator] != ':') return string.Empty;
                    int cause = separator + 1;
                    while (cause < error.Length && char.IsWhiteSpace(error[cause])) cause++;
                    return Context(error.Substring(cause, Math.Min(error.Length - cause, 8192)));
                }
            }
            return string.Empty;
        }

        private static string AllowedInbound(string value)
        {
            switch (value)
            {
                case "tun": case "mixed": case "http": case "socks": case "direct":
                case "redirect": case "tproxy": return value;
                default: return "unknown";
            }
        }

        private static string Component(string text, string inbound, bool hasInbound)
        {
            if (text.Contains("wintun")) return "tun-driver";
            if (inbound == "tun" || text.Contains("tun interface") || text.Contains("tun adapter") || text.Contains("tun device")) return "tun";
            if (hasInbound || IndexedInbound.IsMatch(text)) return "inbound";
            if (text.Contains("network monitor") || text.Contains("interface monitor")) return "network-monitor";
            if (text.StartsWith("duplicate outbound/endpoint tag: ", StringComparison.Ordinal)) return "unknown";
            if (text.Contains("dns server") || text.Contains("dns transport") || text.Contains("dns router")
                || DnsRulePath.IsMatch(text) || DnsRaceError.IsMatch(text)) return "dns";
            if (text.Contains("xray") || text.Contains("app/proxyman") || text.Contains("proxy/socks")) return "xray";
            if (text.Contains("rule-set") || text.Contains("rule set") || text.Contains("routing")) return "routing";
            if (text.Contains("createprocess") || text.Contains("extra process")) return "extra-process";
            if (text.Contains("open file") || text.Contains("read file") || text.Contains("load file")) return "file";
            return "unknown";
        }

        private static string Operation(string text)
        {
            foreach (string value in new[] { "bind", "listen", "configure", "open", "create", "initialize", "start", "read", "write" })
                if (Regex.IsMatch(text, @"\b" + value + @"\b")) return value;
            return "unknown";
        }
    }
}
