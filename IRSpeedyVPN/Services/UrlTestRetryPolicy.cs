using System;
using System.Collections.Generic;
using System.Linq;
using IRSpeedyVPN.Services.Libcore;

namespace IRSpeedyVPN.Services
{
    internal static class UrlTestRetryPolicy
    {
        internal const string RetryUrl = "http://connectivitycheck.gstatic.com/generate_204";

        internal static bool IsSuccess(URLTestResp result)
        {
            return result != null && result.LatencyMs > 0 && string.IsNullOrEmpty(result.Error);
        }

        internal static bool SameEndpoint(string first, string second)
        {
            Uri a, b;
            return Uri.TryCreate(first?.Trim(), UriKind.Absolute, out a)
                && Uri.TryCreate(second?.Trim(), UriKind.Absolute, out b)
                && a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port
                && a.UserInfo == b.UserInfo && a.PathAndQuery == b.PathAndQuery;
        }

        // Both passes complete before the caller publishes latency or reorders countries.
        // Only the slow/failed subset is probed a second time; config and routing stay identical.
        internal static TestResp Run(TestReq primary, Func<TestReq, TestResp> send,
            Func<bool> cancelled, Action<string> log)
        {
            Action checkCancellation = () =>
            {
                if (cancelled()) throw new OperationCanceledException();
            };
            checkCancellation();
            var tags = primary.OutboundTags.Distinct(StringComparer.Ordinal).ToList();
            var best = tags.ToDictionary(tag => tag, tag => new URLTestResp
            {
                OutboundTag = tag, LatencyMs = -1, Error = "no-successful-result"
            }, StringComparer.Ordinal);
            log("[UrlTest] stage=primary-start candidates=" + tags.Count);
            try
            {
                Merge(best, send(primary), new HashSet<string>(tags, StringComparer.Ordinal));
            }
            catch (TimeoutException)
            {
                log("[UrlTest] stage=primary-failed reason=timeout");
            }
            checkCancellation();
            var retryTags = tags.Where(tag => !IsSuccess(best[tag]) || best[tag].LatencyMs > 500).ToList();
            if (retryTags.Count > 0 && !SameEndpoint(primary.Url, RetryUrl))
            {
                var retry = new TestReq
                {
                    Config = primary.Config, XrayConfig = primary.XrayConfig,
                    NeedXray = primary.NeedXray, UseDefaultOutbound = primary.UseDefaultOutbound,
                    TestCurrent = primary.TestCurrent, MaxConcurrency = primary.MaxConcurrency,
                    TestTimeoutMs = primary.TestTimeoutMs, Url = RetryUrl, OutboundTags = retryTags
                };
                checkCancellation();
                log("[UrlTest] stage=retry-start candidates=" + retryTags.Count);
                try
                {
                    Merge(best, send(retry), new HashSet<string>(retryTags, StringComparer.Ordinal));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A failed second request must never erase a successful first result.
                    // Avoid raw RPC text, which may include configuration secrets.
                    log("[UrlTest] stage=retry-failed exception=" + ex.GetType().Name);
                }
                checkCancellation();
            }
            var final = new TestResp { Results = tags.Select(tag => best[tag]).ToList() };
            log("[UrlTest] stage=final candidates=" + tags.Count
                + " successful=" + final.Results.Count(IsSuccess));
            return final;
        }

        private static void Merge(Dictionary<string, URLTestResp> best, TestResp response, HashSet<string> allowed)
        {
            if (response?.Results == null) return;
            foreach (var result in response.Results)
            {
                if (!IsSuccess(result) || result.OutboundTag == null || !allowed.Contains(result.OutboundTag)) continue;
                var previous = best[result.OutboundTag];
                if (!IsSuccess(previous) || result.LatencyMs < previous.LatencyMs)
                    best[result.OutboundTag] = new URLTestResp
                    {
                        OutboundTag = result.OutboundTag, LatencyMs = result.LatencyMs
                    };
            }
        }
    }
}
