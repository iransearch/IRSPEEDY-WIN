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
        internal LoginNetworkContext(CancellationToken cancellation, int budgetMs = 45000)
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
        // Optional diagnostics only: absence never changes replay eligibility.
        internal double? FirstByteSeconds, TotalSeconds, DownloadedBytes;
        internal string HttpVersion;
        internal string ResponseHeadersSummary = "headersKnown=False", BodyKind = "unknown";
        internal int Redirects;
        internal bool TransferMetricsKnown => FirstByteSeconds.HasValue && TotalSeconds.HasValue
            && DownloadedBytes.HasValue && IsKnownHttpVersion(HttpVersion);
        internal static bool IsKnownHttpVersion(string value)
            => value == "0" || value == "1" || value == "1.0" || value == "1.1" || value == "2" || value == "3";
        private static string DiagnosticNumber(double? value)
            => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "unknown";
        private string RemoteIpForLog => IPAddress.TryParse(RemoteIp, out var ip) ? ip.ToString() : "unknown";
        private string RemoteFamilyForLog => !IPAddress.TryParse(RemoteIp, out var ip) ? "unknown"
            : ip.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
        // These fields come from -w even on nonzero exit. Unknown/partial metrics
        // are never evidence that it is safe to repeat a credential POST.
        internal bool MayHaveBeenSent => !MetricsKnown || UploadedBytes > 0 || HttpCode > 0
            || Redirects != 0 || TlsSeconds > 0 || PretransferSeconds > 0;
        // Bundled Windows curl can report positive time_pretransfer on exit 6
        // without resolving/connecting. Only complete DNS-stage evidence can
        // override that conservative signal; callers apply this proof to HTTPS.
        internal bool DefinitelyNotSent => !Unavailable && MetricsKnown && ExitCode == 6
            && ConnectSeconds == 0 && TlsSeconds == 0 && UploadedBytes == 0
            && HttpCode == 0 && Redirects == 0 && RemoteIp == null;
        // The all-zero TLS proof is specific to HTTPS. Retain the previous
        // conservative exit-code guard when a caller supplies a plain HTTP URL.
        internal bool MayHaveBeenSentFor(bool https) => (MayHaveBeenSent && !(https && DefinitelyNotSent)) || (!https
            && (ExitCode == 52 || ExitCode == 55 || ExitCode == 56 || (ExitCode == 28 && ConnectSeconds > 0)));
        internal bool CanRecover => !Unavailable && (!MayHaveBeenSent || DefinitelyNotSent) &&
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
            + " mayHaveBeenSent=" + MayHaveBeenSent + " definitelyNotSent=" + DefinitelyNotSent
            + " canRecover=" + CanRecover
            + " canResolveAlternatePeer=" + CanResolveAlternatePeer
            + " remoteIpKnown=" + (RemoteIp != null)
            + " remoteIp=" + RemoteIpForLog + " remoteFamily=" + RemoteFamilyForLog
            + " transferMetrics=" + TransferMetricsKnown
            + " firstByte=" + DiagnosticNumber(FirstByteSeconds)
            + " total=" + DiagnosticNumber(TotalSeconds)
            + " download=" + DiagnosticNumber(DownloadedBytes)
            + " httpVersion=" + (IsKnownHttpVersion(HttpVersion) ? HttpVersion : "unknown")
            + " bodyChars=" + (Body == null ? "unknown" : Body.Length.ToString(CultureInfo.InvariantCulture))
            + " " + ResponseHeadersSummary + " bodyKind=" + BodyKind;
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
        private readonly bool dohFirst;
        internal LoginRecovery(ILoginTransport transport, ILoginResolver resolver, Action<string> log, bool dohFirst = false)
        { this.transport = transport; this.resolver = resolver; this.log = log; this.dohFirst = dohFirst; }
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
            if (dohFirst && target.Scheme == Uri.UriSchemeHttps && !IPAddress.TryParse(target.Host, out _))
                return SendDohFirst(url, method, headers, body, context, target, deadline);
            var progress = new LoginConnectProgress();
            using (var prefetch = new LoginPrefetch(target.Scheme == Uri.UriSchemeHttps
                && !IPAddress.TryParse(target.Host, out _) && transport is ILoginConnectTransport
                ? resolver as ILoginPrefetchResolver : null, target.Host, deadline, context, progress))
                return SendCore(url, method, headers, body, context, target, deadline, progress, prefetch);
        }

        private LoginCurlResult SendDohFirst(string url, string method, string headers, string body,
            LoginNetworkContext context, Uri target, LoginDeadline deadline)
        {
            Log(context, target.Host, "stage=dns-order mode=" + (CurlHelper.LoginPlainDnsVerification ? "plain-dns-first-verification" : "doh-first-verification") + " remainingMs=" + deadline.RemainingMs);
            LoginCurlResult last = null;
            var answer = context.BeginRecovery(target.Host)
                ? LookupDohFirst(context, target.Host, deadline, false) : null;
            var failedPeers = new HashSet<string>(StringComparer.Ordinal);
            for (int pass = 0; answer != null && pass < 2; pass++)
            {
                deadline.Check();
                var addresses = answer.Values.Where(ip => !failedPeers.Contains(ip)).ToArray();
                if (addresses.Length != 0)
                {
                    Log(context, target.Host, "stage=doh-first-attempt resolve=True cached=" + answer.FromCache
                        + " addressCount=" + addresses.Length + " pass=" + pass + " remainingMs=" + deadline.RemainingMs);
                    try { last = Retry(target, url, method, headers, body, addresses, deadline, context); }
                    catch (OperationCanceledException) { throw; }
                    catch (TimeoutException ex)
                    { throw new LoginTransportTimeoutException("DoH first: " + (last == null ? "no completed attempt" : last.Summary), ex); }
                    catch (Exception ex)
                    {
                        // A launched attempt with no result has no trustworthy trailer.
                        // Do not turn an unexpected transport error into a second POST.
                        throw new LoginTransportException("DoH first: " + (last == null ? "no completed attempt" : last.Summary)
                            + "; transport failed (" + ex.GetType().Name + ").", true);
                    }
                    Log(context, target.Host, "stage=doh-first-result resolve=True cached=" + answer.FromCache
                        + " effectiveMayHaveBeenSent=" + last.MayHaveBeenSentFor(true) + " " + last.Summary);
                    if (last.HttpCode > 0)
                    {
                        // A timed-out/partial HTTP response is still a sent POST.
                        if (last.ExitCode != 0) throw Failure(last, null);
                        return last; // Every HTTP response is final for this DNS order.
                    }
                    if (last.Unavailable) break; // No curl ran; system path may use managed fallback.
                    if (last.MayHaveBeenSentFor(true)) throw Failure(last, null);
                    resolver.Invalidate(target.Host, answer);
                    if (last.RemoteIp != null) failedPeers.Add(last.RemoteIp);
                    if (!last.CanRecover && !last.CanResolveAlternatePeer) break;
                }
                else Log(context, target.Host, "stage=doh-first-skip reason=same-failed-peer");
                // Preserve one bounded refresh for a failing cached set. No fresh
                // answer failure restarts lookup, and system DNS cannot restart DoH.
                if (!answer.FromCache || pass != 0) break;
                resolver.Invalidate(target.Host, answer);
                answer = LookupDohFirst(context, target.Host, deadline, true);
            }
            Log(context, target.Host, "stage=system-fallback reason=" + (last == null ? "no-doh-answer"
                : last.Unavailable ? "curl-unavailable" : "proven-unsent-doh-failure")
                + " remainingMs=" + deadline.RemainingMs + (last == null ? "" : " initial=" + last.Summary));
            deadline.Check();
            try
            {
                // No speculative prefetch and no second DNS recovery in this mode.
                var system = SendCore(url, method, headers, body, context, target, deadline,
                    new LoginConnectProgress(), null, allowRecovery: false);
                Log(context, target.Host, "stage=verification-result source=system resolve=False " + system.Summary);
                return system;
            }
            catch (LoginTransportException ex)
            {
                throw new LoginTransportException("DoH first: " + (last == null ? "no-answer" : last.Summary)
                    + "; system fallback: " + ex.Message, ex.MayHaveBeenSent, ex);
            }
            catch (LoginTransportTimeoutException ex)
            {
                throw new LoginTransportTimeoutException("DoH first: " + (last == null ? "no-answer" : last.Summary), ex);
            }
        }

        private LoginAddresses LookupDohFirst(LoginNetworkContext context, string host, LoginDeadline deadline, bool refresh)
        {
            deadline.Check();
            int budget = deadline.LookupBudgetMs;
            Log(context, host, "stage=doh-first-lookup refresh=" + refresh + " budgetMs=" + budget);
            if (budget <= 100) return null;
            try
            {
                var answer = resolver.Resolve(host, budget, context.Cancellation, refresh);
                deadline.Check();
                Log(context, host, "stage=doh-first-answer addressCount=" + (answer == null ? 0 : answer.Values.Length)
                    + " cached=" + (answer != null && answer.FromCache));
                return answer;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                deadline.Check();
                Log(context, host, "stage=doh-first-lookup-failed type=" + ex.GetType().Name);
                return null; // DNS GETs contain no POST; system fallback is safe.
            }
        }

        private LoginCurlResult SendCore(string url, string method, string headers, string body,
            LoginNetworkContext context, Uri target, LoginDeadline deadline, LoginConnectProgress progress, LoginPrefetch prefetch,
            bool allowRecovery = true)
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
                if (!allowRecovery && first.ExitCode != 0) throw Failure(first, null, target.Scheme == Uri.UriSchemeHttps);
                Log(context, target.Host, "stage=http-return reason=http-response doh=False nonzeroExit="
                    + (first.ExitCode != 0) + " " + first.Summary);
                return first;
            }
            if (!allowRecovery)
            {
                Log(context, target.Host, "stage=skip reason=verification-system-only " + first.Summary);
                throw Failure(first, null, target.Scheme == Uri.UriSchemeHttps);
            }
            Log(context, target.Host, "stage=recovery-decision https=" + (target.Scheme == Uri.UriSchemeHttps)
                + " effectiveMayHaveBeenSent=" + first.MayHaveBeenSentFor(target.Scheme == Uri.UriSchemeHttps)
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
                    second != null && second.MayHaveBeenSentFor(true));
            }
        }
        private LoginCurlResult Retry(Uri target, string url, string method, string headers, string body,
            string[] addresses, LoginDeadline deadline, LoginNetworkContext context)
        {
            deadline.Check();
            return CredentialAttempt(deadline, context, remaining => transport.Curl(url, method, headers, body,
                target.Host + ":" + target.Port + ":" + string.Join(",", addresses.Select(ip => ip.Contains(":") ? "[" + ip + "]" : ip)),
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
            Func<string, int, CancellationToken, string[]> query, Action<string> log,
            string[] providers = null, string[] names = null, bool plain = false)
        {
            providers = providers ?? Providers;
            int budget = Math.Min(LookupBudgetMs, timeoutMs);
            var clock = Stopwatch.StartNew();
            cancellation.ThrowIfCancellationRequested();
            if (budget <= 100) return new string[0];
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var replies = new BlockingCollection<ProviderReply>())
            {
                stop.CancelAfter(budget);
                for (int index = 0; index < providers.Length; index++)
                    Write(log, host, index, "started", 0, names);
                var workers = providers.Select((provider, index) => Task.Factory.StartNew(() =>
                {
                    var reply = new ProviderReply { Index = index, Outcome = "no-answer", Values = new string[0] };
                    try
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        int remaining = budget - (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
                        if (remaining > 100)
                            reply.Values = (query(plain ? provider : provider + Uri.EscapeDataString(host), remaining, stop.Token) ?? new string[0])
                                .Where(value => plain ? LoginDohCache.IsPublicAddress(value) : LoginDohCache.IsPublicIPv4(value)).Distinct().ToArray();
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
                            Write(log, host, reply.Index, "winner", reply.ElapsedMs, names);
                            result = reply.Values; break;
                        }
                        Write(log, host, reply.Index, reply.Outcome, reply.ElapsedMs, names);
                    }
                }
                finally
                {
                    stop.Cancel(); Task.WaitAll(workers);
                    ProviderReply pending;
                    while (replies.TryTake(out pending))
                        Write(log, host, pending.Index, "joined-" + pending.Outcome, pending.ElapsedMs, names);
                }
                cancellation.ThrowIfCancellationRequested();
                return result;
            }
        }
        private sealed class ProviderReply
        { internal int Index; internal string Outcome; internal string[] Values; internal long ElapsedMs; }
        private static void Write(Action<string> log, string host, int provider, string outcome, long elapsedMs, string[] names = null)
        {
            try { log?.Invoke("[LoginRecovery] host=" + host + " stage=provider provider="
                + (names == null ? (provider == 0 ? "google" : "cloudflare") : names[provider]) + " outcome=" + outcome + " elapsedMs=" + elapsedMs); }
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
                .Where(IsPublicAddress).Select(value => IPAddress.Parse(value).ToString()).Distinct().ToArray();
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
        internal static bool IsPublicAddress(string value)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(value, out ip)) return false;
            if (ip.AddressFamily == AddressFamily.InterNetwork) return IsPublicIPv4(value);
            if (ip.AddressFamily != AddressFamily.InterNetworkV6 || ip.ScopeId != 0) return false;
            var b = ip.GetAddressBytes();
            return (b[0] & 0xe0) == 0x20
                && !(b[0] == 0x20 && b[1] == 1 && b[2] == 0x0d && b[3] == 0xb8);
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
    // Experimental login-only classic DNS. Connected UDP validates the peer;
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
            try { log?.Invoke("[LoginRecovery] host=" + name + " stage=resolver-mode mode=plain-dns-verification"); } catch { }
            return LoginDohProviders.Resolve(name, timeoutMs, cancellation, (endpoint, ms, token) =>
            {
                var parts = endpoint.Split('/');
                return Query(name, new IPEndPoint(IPAddress.Parse(parts[0]), 53), int.Parse(parts[1]), ms, token);
            }, log, endpoints, names, true);
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
                { var address = new IPAddress(data.Skip(pos).Take(size).ToArray()).ToString(); if (LoginDohCache.IsPublicAddress(address)) records.Add(Tuple.Create(owner, address)); }
                pos += size;
            }
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { host.TrimEnd('.') };
            for (int pass = 0; pass < aliases.Count; pass++) foreach (var alias in aliases) if (allowed.Contains(alias.Item1)) allowed.Add(alias.Item2);
            return records.Where(r => allowed.Contains(r.Item1)).Select(r => r.Item2).Distinct().Take(64).ToArray();
        }
    }

}
