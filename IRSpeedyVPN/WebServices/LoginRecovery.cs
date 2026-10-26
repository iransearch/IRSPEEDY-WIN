using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace IRSpeedyVPN.WebServices
{
    // One instance per Login2 call. DNS answers may be shared; replay eligibility
    // and cancellation never are. Both endpoint attempts share this deadline.
    internal sealed class LoginNetworkContext
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly int budgetMs;
        private readonly HashSet<string> recovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly CancellationToken Cancellation;
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal LoginDeadline Endpoint { get; private set; }
        internal LoginNetworkContext(CancellationToken cancellation, int budgetMs = 30000)
        { Cancellation = cancellation; this.budgetMs = budgetMs; }
        internal int RemainingMs => (int)Math.Max(0, budgetMs - clock.ElapsedMilliseconds);
        internal void Check()
        {
            Cancellation.ThrowIfCancellationRequested();
            if (RemainingMs <= 0) throw new TimeoutException("Login network deadline expired.");
        }
        internal bool BeginRecovery(string host)
        { Check(); lock (recovered) return recovered.Add(host); }
        internal bool RecoveryAvailable(string host)
        { lock (recovered) return !recovered.Contains(host); }
        internal void BeginEndpoint(int timeoutMs)
        { Check(); Endpoint = new LoginDeadline(this, timeoutMs); }
    }

    internal sealed class LoginDeadline
    {
        internal const int RecoveryPostReserveMs = 3000;
        private readonly LoginNetworkContext context;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly int budgetMs;
        internal LoginDeadline(LoginNetworkContext context, int budgetMs)
        { this.context = context; this.budgetMs = budgetMs; }
        internal int RemainingMs => Math.Min(context.RemainingMs, (int)Math.Max(0, budgetMs - clock.ElapsedMilliseconds));
        internal void Check()
        { context.Check(); if (RemainingMs <= 0) throw new TimeoutException("Login endpoint deadline expired."); }
        internal int LookupBudgetMs => Math.Min(LoginDohProviders.LookupBudgetMs, RemainingMs - RecoveryPostReserveMs);
    }

    internal sealed class LoginCurlResult
    {
        internal int ExitCode, HttpCode;
        internal string Body, RemoteIp;
        internal bool Unavailable, MetricsKnown;
        internal double ConnectSeconds, TlsSeconds, PretransferSeconds, UploadedBytes;
        internal int Redirects;
        // These fields come from -w even on nonzero exit. Unknown/partial metrics
        // are never evidence that it is safe to repeat a credential POST.
        internal bool MayHaveBeenSent => !MetricsKnown || UploadedBytes > 0 || HttpCode > 0
            || Redirects != 0 || TlsSeconds > 0 || PretransferSeconds > 0;
        // The all-zero TLS proof is specific to HTTPS. Retain the previous
        // conservative exit-code guard when a caller supplies a plain HTTP URL.
        internal bool MayHaveBeenSentFor(bool https) => MayHaveBeenSent || (!https
            && (ExitCode == 52 || ExitCode == 55 || ExitCode == 56 || (ExitCode == 28 && ConnectSeconds > 0)));
        internal bool CanRecover => !Unavailable && !MayHaveBeenSent &&
            (ExitCode == 6 || ExitCode == 7 || ExitCode == 35 || ExitCode == 60
                || (ExitCode == 28 && ConnectSeconds == 0));
        // This only authorizes resolving. A retry additionally requires a public
        // address different from every known failed peer below.
        internal bool CanResolveAlternatePeer => !Unavailable && !MayHaveBeenSent
            && ExitCode == 28 && ConnectSeconds > 0 && RemoteIp != null && IPAddress.TryParse(RemoteIp, out _);
        internal string Summary => "exit=" + ExitCode + " http=" + HttpCode
            + " metrics=" + MetricsKnown + " connect=" + ConnectSeconds.ToString("R", CultureInfo.InvariantCulture)
            + " tls=" + TlsSeconds.ToString("R", CultureInfo.InvariantCulture)
            + " upload=" + UploadedBytes.ToString("R", CultureInfo.InvariantCulture)
            + " pretransfer=" + PretransferSeconds.ToString("R", CultureInfo.InvariantCulture)
            + " redirects=" + Redirects + " unavailable=" + Unavailable
            + " mayHaveBeenSent=" + MayHaveBeenSent + " canRecover=" + CanRecover
            + " canResolveAlternatePeer=" + CanResolveAlternatePeer
            + " remoteIpKnown=" + (RemoteIp != null);
    }

    internal sealed class LoginTransportException : Exception
    {
        internal readonly bool MayHaveBeenSent;
        internal LoginTransportException(string message, bool mayHaveBeenSent, Exception inner = null)
            : base(message, inner) { MayHaveBeenSent = mayHaveBeenSent; }
    }

    // A forcibly stopped curl has no trustworthy trailer. Preserve TimeoutException
    // semantics for callers while preventing the controller from replaying its POST.
    internal sealed class LoginTransportTimeoutException : TimeoutException
    {
        internal LoginTransportTimeoutException() : base("Login curl deadline expired; request stage is unknown.") { }
        internal LoginTransportTimeoutException(string initial, Exception inner)
            : base("Initial: " + initial + "; recovery: login curl deadline expired; request stage is unknown.", inner) { }
    }

    internal sealed class LoginAddresses
    {
        internal readonly string[] Values;
        internal readonly bool FromCache;
        internal LoginAddresses(string[] values, bool fromCache) { Values = values; FromCache = fromCache; }
    }

    internal interface ILoginTransport
    {
        LoginCurlResult Curl(string url, string method, string headers, string body,
            string resolve, int timeoutMs, CancellationToken cancellation);
        LoginCurlResult Managed(string url, string method, string headers, string body,
            int timeoutMs, CancellationToken cancellation);
    }
    internal interface ILoginResolver
    {
        LoginAddresses Resolve(string host, int timeoutMs, CancellationToken cancellation, bool refresh);
        void Invalidate(string host, LoginAddresses answer);
    }

    internal sealed class LoginRecovery
    {
        // Background account refresh and a foreground login may overlap. Only
        // credential attempts take this gate; DNS GETs can race independently.
        private static readonly SemaphoreSlim CredentialGate = new SemaphoreSlim(1, 1);
        private readonly ILoginTransport transport;
        private readonly ILoginResolver resolver;
        private readonly Action<string> log;
        internal LoginRecovery(ILoginTransport transport, ILoginResolver resolver, Action<string> log)
        { this.transport = transport; this.resolver = resolver; this.log = log; }
        private void Log(LoginNetworkContext context, string host, string detail)
        {
            try { log?.Invoke("[LoginRecovery] login=" + context.Id + " host=" + host + " " + detail); }
            catch { }
        }
        internal LoginCurlResult Send(string url, string method, string headers, string body,
            int timeoutMs, LoginNetworkContext context)
        {
            var target = new Uri(url);
            var deadline = context.Endpoint ?? new LoginDeadline(context, timeoutMs);
            deadline.Check();
            var progress = new LoginConnectProgress();
            using (var prefetch = new LoginPrefetch(target.Scheme == Uri.UriSchemeHttps
                && !IPAddress.TryParse(target.Host, out _) && transport is ILoginConnectTransport
                ? resolver as ILoginPrefetchResolver : null, target.Host, deadline, context, progress))
                return SendCore(url, method, headers, body, context, target, deadline, progress, prefetch);
        }
        private LoginCurlResult SendCore(string url, string method, string headers, string body,
            LoginNetworkContext context, Uri target, LoginDeadline deadline, LoginConnectProgress progress, LoginPrefetch prefetch)
        {
            LoginCurlResult first;
            try
            {
                var observed = transport as ILoginConnectTransport;
                first = CredentialAttempt(deadline, context, remaining => observed == null
                    ? transport.Curl(url, method, headers, body, null, remaining, context.Cancellation)
                    : observed.CurlObserved(url, method, headers, body, remaining, context.Cancellation, progress));
            }
            finally { progress.Complete(); }
            Log(context, target.Host, "stage=system " + first.Summary);
            if (first.Unavailable)
            {
                // The only .NET fallback: no curl process could start, so no POST
                // was sent. Never use system DNS again after a curl network failure.
                deadline.Check();
                Log(context, target.Host, "stage=managed reason=curl-unavailable");
                try
                {
                    var managed = CredentialAttempt(deadline, context, remaining =>
                        transport.Managed(url, method, headers, body, remaining, context.Cancellation));
                    Log(context, target.Host, "stage=managed-result http=" + managed.HttpCode);
                    return managed;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    bool mayHaveBeenSent = target.Scheme != Uri.UriSchemeHttps || (ex is LoginTransportException managedFailure
                        ? managedFailure.MayHaveBeenSent
                        : HttpFallback.ManagedMayHaveBeenSent(ex, true));
                    Log(context, target.Host, "stage=failed reason=managed type=" + ex.GetType().Name
                        + " mayHaveBeenSent=" + mayHaveBeenSent);
                    throw new LoginTransportException("Initial: curl-unavailable; managed login failed (" + ex.GetType().Name + ").", mayHaveBeenSent);
                }
            }
            if (first.HttpCode > 0)
            {
                Log(context, target.Host, "stage=http-return reason=http-response doh=False nonzeroExit="
                    + (first.ExitCode != 0) + " " + first.Summary);
                return first;
            }
            Log(context, target.Host, "stage=recovery-decision https=" + (target.Scheme == Uri.UriSchemeHttps)
                + " literalHost=" + IPAddress.TryParse(target.Host, out _)
                + " recoveryAvailable=" + context.RecoveryAvailable(target.Host)
                + " remainingMs=" + deadline.RemainingMs + " lookupBudgetMs=" + deadline.LookupBudgetMs
                + " " + first.Summary);
            if ((!first.CanRecover && !first.CanResolveAlternatePeer)
                || target.Scheme != Uri.UriSchemeHttps || IPAddress.TryParse(target.Host, out _))
            {
                Log(context, target.Host, "stage=skip reason=unsafe-or-ineligible " + first.Summary);
                throw Failure(first, null, target.Scheme == Uri.UriSchemeHttps);
            }
            deadline.Check();
            if (!context.BeginRecovery(target.Host))
            {
                Log(context, target.Host, "stage=skip reason=recovery-already-used");
                throw Failure(first, null);
            }
            Log(context, target.Host, "stage=doh reason=pre-send-failure " + first.Summary);

            LoginCurlResult second = null;
            LoginAddresses answer = null;
            try
            {
                deadline.Check();
                // Leave time for the real request. No lookup gets a new deadline.
                int lookupMs = deadline.LookupBudgetMs;
                if (lookupMs <= 0) throw Failure(first, null);
                bool wasPrefetched;
                answer = prefetch.Take(out wasPrefetched);
                Log(context, target.Host, "stage=lookup-start prefetched=" + wasPrefetched + " budgetMs=" + lookupMs);
                if (!wasPrefetched) answer = resolver.Resolve(target.Host, lookupMs, context.Cancellation, false);
                var failedPeers = new HashSet<string>(StringComparer.Ordinal);
                if (first.RemoteIp != null) failedPeers.Add(first.RemoteIp);
                // One cached set and at most one fresh set, never recursive. Keep
                // the original answer for reference-safe cache invalidation.
                for (int pass = 0; pass < 2; pass++)
                {
                    deadline.Check();
                    Log(context, target.Host, "stage=lookup-result pass=" + pass
                        + " addressCount=" + (answer == null ? 0 : answer.Values.Length)
                        + " cached=" + (answer != null && answer.FromCache));
                    if (answer == null || answer.Values.Length == 0) throw Failure(first, second);
                    var addresses = answer.Values.Where(ip => !failedPeers.Contains(ip)).ToArray();
                    if (addresses.Length != 0)
                    {
                        if (wasPrefetched) prefetch.Accept(answer);
                        second = Retry(target, url, method, headers, body, addresses, deadline, context);
                        Log(context, target.Host, "stage=" + (pass == 0 ? "doh-result" : "refresh-result")
                            + " cached=" + answer.FromCache + " " + second.Summary);
                        if (second.HttpCode > 0) return second;
                        resolver.Invalidate(target.Host, answer);
                        if (!second.CanRecover && !second.CanResolveAlternatePeer) throw Failure(first, second);
                        if (second.RemoteIp != null) failedPeers.Add(second.RemoteIp);
                    }
                    else Log(context, target.Host, "stage=skip reason=same-failed-peer cached=" + answer.FromCache);
                    if (!answer.FromCache || pass != 0) break;
                    resolver.Invalidate(target.Host, answer);
                    lookupMs = deadline.LookupBudgetMs;
                    if (lookupMs <= 0) break;
                    Log(context, target.Host, "stage=cache-refresh reason=pre-send-failure");
                    answer = resolver.Resolve(target.Host, lookupMs, context.Cancellation, true);
                    wasPrefetched = false;
                }
                throw Failure(first, second);
            }
            catch (OperationCanceledException) { throw; }
            catch (LoginTransportTimeoutException ex)
            {
                Log(context, target.Host, "stage=failed reason=retry-process-timeout first=" + first.Summary);
                throw new LoginTransportTimeoutException(first.Summary, ex);
            }
            catch (LoginTransportException ex)
            {
                Log(context, target.Host, "stage=failed firstExit=" + first.ExitCode
                    + " retryExit=" + (second == null ? "none" : second.ExitCode.ToString())
                    + " mayHaveBeenSent=" + ex.MayHaveBeenSent);
                throw;
            }
            catch (Exception ex)
            {
                Log(context, target.Host, "stage=failed type=" + ex.GetType().Name + " firstExit=" + first.ExitCode);
                // Only sanitized fixed metadata is retained, never curl stderr,
                // credential bodies, headers, or arbitrary resolver error text.
                throw new LoginTransportException("Initial: " + first.Summary + "; recovery: "
                    + (second == null ? ex.GetType().Name : second.Summary + "; " + ex.GetType().Name),
                    second != null && second.MayHaveBeenSent);
            }
        }
        private LoginCurlResult Retry(Uri target, string url, string method, string headers, string body,
            string[] addresses, LoginDeadline deadline, LoginNetworkContext context)
        {
            deadline.Check();
            return CredentialAttempt(deadline, context, remaining => transport.Curl(url, method, headers, body,
                target.Host + ":" + target.Port + ":" + string.Join(",", addresses),
                remaining, context.Cancellation));
        }
        private static LoginCurlResult CredentialAttempt(LoginDeadline deadline, LoginNetworkContext context,
            Func<int, LoginCurlResult> attempt)
        {
            deadline.Check();
            if (!CredentialGate.Wait(deadline.RemainingMs, context.Cancellation))
                throw new TimeoutException("Login attempt queue deadline expired before sending.");
            try { deadline.Check(); CurlHelper.EnsureCredentialCurlStopped(); return attempt(deadline.RemainingMs); }
            finally { CredentialGate.Release(); }
        }
        private static LoginTransportException Failure(LoginCurlResult first, LoginCurlResult second, bool https = true)
            => new LoginTransportException("Initial: " + first.Summary + "; recovery: "
                + (second == null ? "not-performed-or-no-answer" : second.Summary),
                second == null ? first.MayHaveBeenSentFor(https) : second.MayHaveBeenSentFor(https));
    }

    // DNS only: a completion-order race under one capped lookup clock. Every
    // provider is canceled and joined before returning, including on cancellation.
    internal static class LoginDohProviders
    {
        internal const int LookupBudgetMs = 5000;
        internal static readonly string[] Providers =
        {
            "https://8.8.8.8/resolve?type=A&name=",
            "https://1.1.1.1/dns-query?type=A&name="
        };
        internal static string[] Resolve(string host, int timeoutMs, CancellationToken cancellation,
            Func<string, int, CancellationToken, string[]> query, Action<string> log)
        {
            int budget = Math.Min(LookupBudgetMs, timeoutMs);
            var clock = Stopwatch.StartNew();
            cancellation.ThrowIfCancellationRequested();
            if (budget <= 100) return new string[0];
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var replies = new BlockingCollection<ProviderReply>())
            {
                stop.CancelAfter(budget);
                for (int index = 0; index < Providers.Length; index++)
                    Write(log, host, index, "started", 0);
                var workers = Providers.Select((provider, index) => Task.Factory.StartNew(() =>
                {
                    var reply = new ProviderReply { Index = index, Outcome = "no-answer", Values = new string[0] };
                    try
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        int remaining = budget - (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
                        if (remaining > 100)
                            reply.Values = (query(provider + Uri.EscapeDataString(host), remaining, stop.Token) ?? new string[0])
                                .Where(LoginDohCache.IsPublicIPv4).Distinct().ToArray();
                    }
                    catch (OperationCanceledException) { reply.Outcome = "canceled"; }
                    catch (Exception ex) { reply.Outcome = ex is TimeoutException ? "timeout" : "failed"; }
                    if (reply.Values.Length != 0) reply.Outcome = "valid-answer";
                    reply.ElapsedMs = clock.ElapsedMilliseconds;
                    replies.Add(reply);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                string[] result = new string[0];
                try
                {
                    for (int received = 0; received < workers.Length; received++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int remaining = budget - (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
                        ProviderReply reply;
                        if (remaining <= 0 || !replies.TryTake(out reply, remaining, cancellation)) break;
                        if (reply.Values.Length != 0 && reply.ElapsedMs < budget)
                        {
                            Write(log, host, reply.Index, "winner", reply.ElapsedMs);
                            result = reply.Values; break;
                        }
                        Write(log, host, reply.Index, reply.Outcome, reply.ElapsedMs);
                    }
                }
                finally
                {
                    stop.Cancel(); Task.WaitAll(workers);
                    ProviderReply pending;
                    while (replies.TryTake(out pending))
                        Write(log, host, pending.Index, "joined-" + pending.Outcome, pending.ElapsedMs);
                }
                cancellation.ThrowIfCancellationRequested();
                return result;
            }
        }
        private sealed class ProviderReply
        { internal int Index; internal string Outcome; internal string[] Values; internal long ElapsedMs; }
        private static void Write(Action<string> log, string host, int provider, string outcome, long elapsedMs)
        {
            try { log?.Invoke("[LoginRecovery] host=" + host + " stage=provider provider="
                + (provider == 0 ? "google" : "cloudflare") + " outcome=" + outcome + " elapsedMs=" + elapsedMs); }
            catch { }
        }
    }

    // Cache only public IPv4 answer sets, never login retry state. Reference
    // comparison prevents an old failing caller from evicting a newer answer.
    internal sealed class LoginDohCache : ILoginPrefetchResolver
    {
        private sealed class Entry { internal string[] Values; internal DateTime Expires; }
        private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new object();
        private readonly Func<string, int, CancellationToken, string[]> fetch;
        internal LoginDohCache(Func<string, int, CancellationToken, string[]> fetch) { this.fetch = fetch; }
        public LoginAddresses Resolve(string host, int timeoutMs, CancellationToken cancellation, bool refresh)
            => ResolveCore(host, timeoutMs, cancellation, refresh, true);
        public LoginAddresses Prefetch(string host, int timeoutMs, CancellationToken cancellation)
            => ResolveCore(host, timeoutMs, cancellation, false, false);
        private LoginAddresses ResolveCore(string host, int timeoutMs, CancellationToken cancellation, bool refresh, bool commit)
        {
            cancellation.ThrowIfCancellationRequested();
            if (timeoutMs <= 0) return null;
            lock (gate)
            {
                Entry entry;
                if (!refresh && cache.TryGetValue(host, out entry) && entry.Expires > DateTime.UtcNow)
                    return new LoginAddresses(entry.Values, true);
            }
            var values = (fetch(host, Math.Min(LoginDohProviders.LookupBudgetMs, timeoutMs), cancellation) ?? new string[0])
                .Where(IsPublicIPv4).Select(value => IPAddress.Parse(value).ToString()).Distinct().ToArray();
            cancellation.ThrowIfCancellationRequested();
            if (values.Length == 0) return null;
            var answer = new LoginAddresses(values, false);
            if (commit) Accept(host, answer);
            return answer;
        }
        public void Accept(string host, LoginAddresses answer)
        { if (!answer.FromCache) lock (gate) cache[host] = new Entry { Values = answer.Values, Expires = DateTime.UtcNow.AddMinutes(5) }; }
        public void Invalidate(string host, LoginAddresses answer)
        {
            lock (gate)
            {
                Entry entry;
                if (cache.TryGetValue(host, out entry) && ReferenceEquals(entry.Values, answer.Values)) cache.Remove(host);
            }
        }
        internal static bool IsPublicIPv4(string value)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(value, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
                && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                && !(b[0] == 198 && (b[1] == 18 || b[1] == 19));
        }
    }
}
