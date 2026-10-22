using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using IRSpeedyVPN.Services.Xray;
using Newtonsoft.Json.Linq;
using v2rayN.Handler;

class Program
{
    static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }

    static void RoundTrip(string label, JObject extra, string queryHeaders = null, JObject expectedExtra = null)
    {
        var original = extra == null ? null : extra.ToString(Newtonsoft.Json.Formatting.None);
        expectedExtra = expectedExtra ?? extra;
        var link = "vless://00000000-0000-0000-0000-000000000001@192.0.2.1:443"
            + "?encryption=none&security=tls&sni=tls.example&fp=chrome&alpn=h2%2Chttp%2F1.1"
            + "&allowInsecure=0&type=xhttp&host=cdn.example&path=%2F&mode=auto"
            + (extra == null ? "" : "&extra=" + HttpUtility.UrlEncode(original))
            + (queryHeaders == null ? "" : "&headers=" + HttpUtility.UrlEncode(queryHeaders)) + "#fixture";
        var node = ParsedTransport.Parse(link);
        if (string.IsNullOrEmpty(queryHeaders))
            Check(node.transportExtra == original, label + ": parsed extra matches exactly after one URL decode");
        else
            Check(node.transportExtra != null && JToken.DeepEquals(expectedExtra, JObject.Parse(node.transportExtra)), label + ": query header pairs imported without a second decode");
        Check(node.requestHost == "cdn.example" && node.path == "/" && node.headerType == "auto"
            && node.alpn.SequenceEqual(new[] { "h2", "http/1.1" }), label + ": ordinary XHTTP fields unchanged");
        ShareHandler.Nodes["fixture"] = node;
        var single = JObject.Parse(ConfigGenerator.GetConfig("fixture", 19001));
        var singleExtra = single["outbounds"][0]["streamSettings"]["xhttpSettings"]["extra"];
        Check(!singleExtra.SelectTokens("$..headers").OfType<JObject>().SelectMany(o => o.Properties())
            .Where(p => p.Value.Type == JTokenType.String).Any(p => p.Value.ToString().IndexOfAny(new[] { '\r', '\n' }) >= 0),
            label + ": production formatter inserts no CR/LF into HTTP header values");
        Check(JToken.DeepEquals(expectedExtra, singleExtra), label + ": single-server JSON preserves header names/values and extra options");
        var probe = JObject.Parse(ConfigGenerator.GetUrlTestXrayConfig(new List<ConfigGenerator.XraySocksInfo> {
            new ConfigGenerator.XraySocksInfo { Link = "fixture", Tag = "probe", Port = 19002, User = "u", Pass = "p" }
        }));
        Check(JToken.DeepEquals(expectedExtra, probe["outbounds"][0]["streamSettings"]["xhttpSettings"]["extra"]),
            label + ": URL-test JSON preserves headers");
        var isolatedProbe = JObject.Parse(ConfigGenerator.GetUrlTestXrayConfig(new List<ConfigGenerator.XraySocksInfo> {
            new ConfigGenerator.XraySocksInfo { Link = "fixture", Tag = "probe", Port = 19002, User = "u", Pass = "p" }
        }, useCoreDns: true));
        Check(JToken.DeepEquals(expectedExtra, isolatedProbe["outbounds"][0]["streamSettings"]["xhttpSettings"]["extra"]),
            label + ": production probe DNS adaptation preserves headers");
        int port = 19100;
        using (var plan = AutoSelectorPoolPlan.Create(new[] { "fixture" }, new string[0], false, () => port++, p => { }))
        {
            Check(plan != null && plan.MainMembers == 1, label + ": one-member Pool accepts the link");
            var pool = JObject.Parse(plan.XrayConfig);
            var member = ((JArray)pool["outbounds"]).OfType<JObject>().Single(o => (string)o["tag"] == "smart-proxy-0");
            Check(JToken.DeepEquals(expectedExtra, member["streamSettings"]["xhttpSettings"]["extra"]),
                label + ": final SOCKS-bridge Pool member preserves exact headers");
        }
    }

    static void Main()
    {
        // Same nested header shape and Safari value as the report. Credentials
        // and destinations are synthetic; the checks never open a connection.
        RoundTrip("reported nested header", new JObject {
            ["headers"] = new JObject {
                ["headers"] = "User-Agent=\"Mozilla/5.0+(Macintosh;+Intel+Mac+OS+X+10_15_7)+AppleWebKit/605.1.15+(KHTML,+like+Gecko)+Version/17.0+Safari/605.1.15\""
            },
            ["mode"] = "auto", ["xPaddingBytes"] = "100-1000", ["xPaddingObfsMode"] = true
        });
        RoundTrip("standard header with reserved values", new JObject {
            ["headers"] = new JObject {
                ["User-Agent"] = "Example/1.0 C++ + %25 %2F %22 & = \"quoted\" \\ فارسی",
                ["X-Literal"] = "%7Bstill-encoded%7D", ["X-Space"] = "a b+c"
            },
            ["xPaddingBytes"] = "100-1000", ["xPaddingObfsMode"] = true,
            ["custom"] = new JObject { ["token"] = "literal+%2B%26" }
        });
        RoundTrip("ordinary extra without headers", new JObject { ["xPaddingBytes"] = "100-1000", ["xPaddingObfsMode"] = true });
        RoundTrip("quoted JSON punctuation in headers", new JObject {
            ["headers"] = new JObject {
                ["User-Agent"] = "Example/1.0 \"quoted,comma\" {literal} [text] \\\"quoted\\\"",
                ["X-Unmatched-Punctuation"] = "} ] { ["
            },
            ["longLiteral"] = long.MaxValue
        });
        RoundTrip("download connection header", new JObject {
            ["downloadSettings"] = new JObject {
                ["address"] = "download.example", ["port"] = 443, ["network"] = "xhttp",
                ["xhttpSettings"] = new JObject { ["extra"] = new JObject {
                    ["headers"] = new JObject { ["User-Agent"] = "Download+%2F%25" }
                }}
            }
        });
        var queryValue = "Example/1.0 C++ + %25 %2F & = \"quoted\" فارسی";
        RoundTrip("standalone query headers", null, "User-Agent|" + queryValue + "|X-Empty|", new JObject {
            ["headers"] = new JObject { ["User-Agent"] = queryValue, ["X-Empty"] = "" }
        });
        RoundTrip("query headers override extra headers", new JObject {
            ["headers"] = new JObject { ["Old-Header"] = "old" },
            ["xPaddingBytes"] = "100-1000", ["custom"] = new JObject { ["literal"] = "keep+%2F" }
        }, "User-Agent|query+%2F", new JObject {
            ["headers"] = new JObject { ["User-Agent"] = "query+%2F" },
            ["xPaddingBytes"] = "100-1000", ["custom"] = new JObject { ["literal"] = "keep+%2F" }
        });
        RoundTrip("empty query headers retain extra", new JObject {
            ["headers"] = new JObject { ["User-Agent"] = "keep" }
        }, "");
        const string plainLink = "vless://00000000-0000-0000-0000-000000000001@192.0.2.1:443?type=xhttp&path=%2F&mode=auto";
        bool refused = false;
        try { ParsedTransport.Parse(plainLink + "&headers=User-Agent%7Cvalue%7Cmissing-value"); }
        catch (FormatException) { refused = true; }
        Check(refused, "incomplete query header pair rejected rather than silently dropped");
        var plain = ParsedTransport.Parse(plainLink);
        Check(plain.transportExtra == null && plain.path == "/" && plain.headerType == "auto", "XHTTP without extra remains unchanged");
        var literals = new JObject {
            ["dateLiteral"] = "2026-10-22T18:00:00Z", ["longLiteral"] = long.MaxValue,
            ["decimalLiteral"] = 123.5, ["boolLiteral"] = true,
            ["strings"] = new JArray("quoted\"comma,", "} ] { [", "trailing\\", "line\r\nline", "فارسی")
        };
        using (var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(v2rayN.Utils.ToJson(literals))))
        {
            reader.DateParseHandling = Newtonsoft.Json.DateParseHandling.None;
            Check(JToken.DeepEquals(literals, JToken.ReadFrom(reader)), "production formatter preserves string/numeric/boolean literals and existing escaped controls");
        }
    }
}
