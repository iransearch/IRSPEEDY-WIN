using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IRSpeedyVPN.Services.Libcore;

namespace IRSpeedyVPN.Services
{
    // Latency 0 means no measurement, -1 a measured failure.
    internal readonly struct DisplayProbeResult
    {
        internal static DisplayProbeResult Deferred => default(DisplayProbeResult);
        internal static DisplayProbeResult Failed => new DisplayProbeResult(-1, 1);
        internal readonly long Latency;
        internal readonly int FailureCount, SampleCount;
        internal bool Success => Latency > 0;
        internal bool Measured => Latency != 0;

        internal DisplayProbeResult(long latency, int failureCount = 0, int sampleCount = 1)
        {
            Latency = latency;
            FailureCount = failureCount;
            SampleCount = sampleCount;
        }

        internal int CompareTo(DisplayProbeResult other)
        {
            int compare = Latency.CompareTo(other.Latency);
            if (compare != 0) return compare;
            compare = FailureCount.CompareTo(other.FailureCount);
            return compare != 0 ? compare : other.SampleCount.CompareTo(SampleCount);
        }
    }

    internal static class UrlTestRetryPolicy
    {
        internal const string RetryUrl = "http://connectivitycheck.gstatic.com/generate_204";

        internal const int DisplayBudgetMs = 8000;
        internal static string DisplayTarget(string target)
            => string.IsNullOrWhiteSpace(target) ? RetryUrl : target.Trim();

        // SplitHTTP is the older spelling of the XHTTP transport. Keep the
        // API link and runtime conversion unchanged; normalize only the probe copy.
        internal static string NormalizeDisplayTransport(string link)
        {
            int query = link.IndexOf('?');
            int fragment = link.IndexOf('#');
            if (query < 0 || (fragment >= 0 && fragment < query)) return link;
            int end = fragment >= 0 ? fragment : link.Length;
            var fields = link.Substring(query + 1, end - query - 1).Split('&');
            bool changed = false;
            for (int i = 0; i < fields.Length; i++)
            {
                int equals = fields[i].IndexOf('=');
                if (equals < 0 || !string.Equals(Uri.UnescapeDataString(fields[i].Substring(0, equals)), "type", StringComparison.OrdinalIgnoreCase))
                    continue;
                string transport = Uri.UnescapeDataString(fields[i].Substring(equals + 1));
                if (string.Equals(transport, "splithttp", StringComparison.OrdinalIgnoreCase)
                    || (transport != "xhttp" && string.Equals(transport, "xhttp", StringComparison.OrdinalIgnoreCase)))
                {
                    fields[i] = "type=xhttp";
                    changed = true;
                }
            }
            return changed ? link.Substring(0, query + 1) + string.Join("&", fields) + link.Substring(end) : link;
        }

        internal static bool WaitForDisplayListener(Func<int> remaining, Func<int, bool> connect,
            Action checkProcess, CancellationToken cancellation)
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                int budget = remaining();
                if (budget <= 0) return false;
                if (connect(Math.Min(100, budget))) return remaining() > 0;
                checkProcess?.Invoke();
                cancellation.WaitHandle.WaitOne(Math.Min(20, remaining()));
            }
        }

        // Display attempts take one shared stopwatch (started before preparation).
        // The original Run overloads below retain connection/health probe semantics.
        internal static DisplayProbeResult RunDisplay(string requestedTarget,
            Func<string, int, DisplayProbeResult> attempt, Func<long> elapsedMilliseconds,
            CancellationToken cancellation, Action<long> measured = null)
        {
            int Remaining() => (int)Math.Max(0, DisplayBudgetMs - elapsedMilliseconds());

            DisplayProbeResult Probe(string target, int budget)
            {
                cancellation.ThrowIfCancellationRequested();
                long start = elapsedMilliseconds();
                try
                {
                    var result = attempt(target, budget);
                    return result.Measured && elapsedMilliseconds() - start > budget ? DisplayProbeResult.Failed : result;
                }
                catch (TimeoutException) { return DisplayProbeResult.Failed; }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                { return DisplayProbeResult.Deferred; }
            }

            string primary = DisplayTarget(requestedTarget);
            bool distinct = !SameEndpoint(primary, RetryUrl);
            int available = Remaining();
            if (available == 0) return DisplayProbeResult.Failed;
            var first = Probe(primary, distinct ? Math.Min(4000, available) : available);
            cancellation.ThrowIfCancellationRequested();
            if (first.Success) measured?.Invoke(first.Latency);
            if (!distinct || !first.Measured || (first.Success && first.Latency <= 500))
                return first;
            available = Remaining();
            if (available == 0) return first;
            var fallback = Probe(RetryUrl, available);
            cancellation.ThrowIfCancellationRequested();
            if (fallback.Success && (!first.Success || fallback.Latency < first.Latency)) measured?.Invoke(fallback.Latency);
            return first.Success && (!fallback.Success || first.Latency <= fallback.Latency) ? first : fallback;
        }

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

        // Ordinary callers commit after both passes. The manual global refresh
        // can finish members that need no retry before waiting for other members.
        // Only the slow/failed subset is probed a second time; config and routing stay identical.
        internal static TestResp Run(TestReq primary, Func<TestReq, TestResp> send,
            Func<bool> cancelled, Action<string> log)
        {
            return Run(primary, (request, report) => send(request), cancelled, log, null);
        }

        internal static TestResp Run(TestReq primary, Func<TestReq, Action<TestResp>, TestResp> send,
            Func<bool> cancelled, Action<string> log, Action<long> progress,
            Action<URLTestResp> memberProgress = null, Action<URLTestResp> memberCompleted = null)
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
            long displayedBest = long.MaxValue;
            var displayedMembers = new Dictionary<string, int>(StringComparer.Ordinal);
            Action<TestResp, HashSet<string>> accept = (response, allowed) =>
            {
                checkCancellation();
                Merge(best, response, allowed);
                if (memberProgress != null) foreach (var pair in best.Where(p => IsSuccess(p.Value)))
                {
                    int previous;
                    if (!displayedMembers.TryGetValue(pair.Key, out previous) || pair.Value.LatencyMs < previous)
                    {
                        displayedMembers[pair.Key] = pair.Value.LatencyMs;
                        memberProgress?.Invoke(pair.Value);
                    }
                }
                var minimum = best.Values.Where(IsSuccess).Select(r => (long)r.LatencyMs)
                    .DefaultIfEmpty(long.MaxValue).Min();
                if (minimum < displayedBest)
                {
                    displayedBest = minimum;
                    progress?.Invoke(minimum);
                }
            };
            var primaryTags = new HashSet<string>(tags, StringComparer.Ordinal);
            log("[UrlTest] stage=primary-start candidates=" + tags.Count);
            try
            {
                accept(send(primary, partial => accept(partial, primaryTags)), primaryTags);
            }
            catch (TimeoutException)
            {
                log("[UrlTest] stage=primary-failed reason=timeout");
            }
            checkCancellation();
            var retryTags = tags.Where(tag => !IsSuccess(best[tag]) || best[tag].LatencyMs > 500).ToList();
            var completed = new HashSet<string>(StringComparer.Ordinal);
            if (memberCompleted != null)
                foreach (var tag in tags.Except(retryTags)) { checkCancellation(); memberCompleted(best[tag]); completed.Add(tag); }
            if (retryTags.Count > 0 && !SameEndpoint(primary.Url, RetryUrl))
            {
                var retry = new TestReq
                {
                    Config = primary.Config, XrayConfig = primary.XrayConfig,
                    XrayOutboundDnsStrategy = primary.XrayOutboundDnsStrategy,
                    NeedXray = primary.NeedXray, UseDefaultOutbound = primary.UseDefaultOutbound,
                    TestCurrent = primary.TestCurrent, MaxConcurrency = primary.MaxConcurrency,
                    TestTimeoutMs = primary.TestTimeoutMs, Url = RetryUrl, OutboundTags = retryTags
                };
                checkCancellation();
                log("[UrlTest] stage=retry-start candidates=" + retryTags.Count);
                var allowedRetryTags = new HashSet<string>(retryTags, StringComparer.Ordinal);
                try
                {
                    accept(send(retry, partial => accept(partial, allowedRetryTags)), allowedRetryTags);
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
            if (memberCompleted != null)
                foreach (var tag in tags.Where(tag => !completed.Contains(tag))) { checkCancellation(); memberCompleted(best[tag]); }
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
