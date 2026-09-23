using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IRSpeedyVPN.Services
{
    // The Core resolves local rule sets relative to its V-Guard working directory.
    // Omit only file-backed geo routing when the single-file runtime lacks its data.
    internal static class GeoRoutingFallback
    {
        internal sealed class Result
        {
            internal string SingBoxConfig { get; set; }
            internal string XrayConfig { get; set; }
            internal string[] MissingFiles { get; set; }
        }

        internal static Result Apply(string singBoxConfig, string xrayConfig, string coreDirectory)
        {
            if (string.IsNullOrWhiteSpace(coreDirectory))
                throw new ArgumentException("Core working directory is unavailable.", nameof(coreDirectory));

            var result = new Result { SingBoxConfig = singBoxConfig, XrayConfig = xrayConfig };
            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var singBox = JObject.Parse(singBoxConfig);
            var route = singBox["route"] as JObject;
            var definitions = route?["rule_set"] as JArray;
            var rules = route?["rules"] as JArray;
            var missingTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool missingIranFiles = false;

            if (definitions != null)
            {
                foreach (var definition in definitions.OfType<JObject>())
                {
                    var path = (string)definition["path"];
                    var tag = (string)definition["tag"];
                    if (!string.Equals((string)definition["type"], "local", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(path)
                        || !path.Replace('\\', '/').StartsWith("geo/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Only the app's local geo directory is eligible for this fallback.
                    // Invalid paths must not silently turn off an unrelated rule set.
                    var relative = path.Replace('/', Path.DirectorySeparatorChar)
                        .Replace('\\', Path.DirectorySeparatorChar);
                    var fullPath = Path.GetFullPath(Path.Combine(coreDirectory, relative));
                    var geoDirectory = Path.GetFullPath(Path.Combine(coreDirectory, "geo"))
                        + Path.DirectorySeparatorChar;
                    if (!fullPath.StartsWith(geoDirectory, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (File.Exists(fullPath)) continue;
                    missingTags.Add(tag);
                    missing.Add(Path.GetFileName(fullPath));
                    if (tag.Equals("ir_IP", StringComparison.OrdinalIgnoreCase)
                        || tag.Equals("category-ir_SITE", StringComparison.OrdinalIgnoreCase))
                        missingIranFiles = true;
                }
            }

            var xray = string.IsNullOrWhiteSpace(xrayConfig) ? null : JObject.Parse(xrayConfig);
            var xrayRules = xray?["routing"]?["rules"] as JArray;
            bool hasXrayGeoRules = xrayRules != null && xrayRules.OfType<JObject>().Any(UsesXrayGeo);
            if (hasXrayGeoRules)
            {
                foreach (var name in new[] { "geoip.dat", "geosite.dat" })
                    if (!File.Exists(Path.Combine(coreDirectory, name))) missing.Add(name);
            }

            // Both cores participate in Smart routing: one missing country dataset
            // disables the whole country bypass, not half of its IP/domain matches.
            bool skipCountry = missingIranFiles || hasXrayGeoRules &&
                (missing.Contains("geoip.dat") || missing.Contains("geosite.dat"));

            if (skipCountry && definitions != null)
            {
                foreach (var tag in new[] { "ir_IP", "category-ir_SITE" })
                    if (definitions.OfType<JObject>().Any(d => string.Equals((string)d["tag"], tag,
                        StringComparison.OrdinalIgnoreCase))) missingTags.Add(tag);
            }

            if (rules != null && missingTags.Count > 0)
            {
                foreach (var rule in rules.OfType<JObject>().ToList())
                {
                    var tags = rule["rule_set"] as JArray;
                    if (tags != null && tags.Values<string>().Any(missingTags.Contains))
                        rule.Remove();
                }
            }
            if (definitions != null && missingTags.Count > 0)
            {
                foreach (var definition in definitions.OfType<JObject>().ToList())
                    if (missingTags.Contains((string)definition["tag"] ?? "")) definition.Remove();
                if (definitions.Count == 0) route.Remove("rule_set");
                result.SingBoxConfig = singBox.ToString(Formatting.None);
            }

            if (skipCountry && xrayRules != null)
            {
                foreach (var rule in xrayRules.OfType<JObject>().Where(UsesXrayGeo).ToList())
                    rule.Remove();
                result.XrayConfig = xray.ToString(Formatting.None);
            }

            result.MissingFiles = missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            return result;
        }

        private static bool UsesXrayGeo(JObject rule)
        {
            foreach (var key in new[] { "ip", "domain" })
            {
                var values = rule[key] as JArray;
                if (values != null && values.Values<string>().Any(value => value != null &&
                    (value.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase)
                     || value.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))))
                    return true;
            }
            return false;
        }
    }
}
