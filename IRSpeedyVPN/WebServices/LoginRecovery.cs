using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

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
        { Check(); return recovered.Add(host); }
        internal void BeginEndpoint(int timeoutMs)
        { Check(); Endpoint = new LoginDeadline(this, timeoutMs); }
    }

    internal sealed class LoginDeadline
    {
        private readonly LoginNetworkContext context;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly int budgetMs;
        internal LoginDeadline(LoginNetworkContext context, int budgetMs)
        { this.context = context; this.budgetMs = budgetMs; }
        internal int RemainingMs => Math.Min(context.RemainingMs, (int)Math.Max(0, budgetMs - clock.ElapsedMilliseconds));
        internal void Check()
        { context.Check(); if (RemainingMs <= 0) throw new TimeoutException("Login endpoint deadline expired."); }
    }

    internal sealed class LoginCurlResult
    {
        internal int ExitCode, HttpCode;
        internal string Body;
        internal bool Unavailable, MetricsKnown;
        internal double ConnectSeconds, TlsSeconds, PretransferSeconds, UploadedBytes;
        internal int Redirects;
        // These fields come from -w even on nonzero exit. Unknown/partial metrics
        // are never evidence that it is safe to repeat a credential POST.
        internal bool MayHaveBeenSent => !MetricsKnown || UploadedBytes > 0 || HttpCode > 0
            || Redirects != 0 || TlsSeconds > 0 || PretransferSeconds > 0
            || ExitCode == 52 || ExitCode == 55 || ExitCode == 56
            || (ExitCode == 28 && ConnectSeconds > 0);
        internal bool CanRecover => !Unavailable && !MayHaveBeenSent &&
            (ExitCode == 6 || ExitCode == 7 || ExitCode == 35 || ExitCode == 60
                || (ExitCode == 28 && ConnectSeconds == 0));
        internal string Summary => "exit=" + ExitCode + " http=" + HttpCode
            + " metrics=" + MetricsKnown + " connect=" + ConnectSeconds.ToString("R", CultureInfo.InvariantCulture)
            + " tls=" + TlsSeconds.ToString("R", CultureInfo.InvariantCulture)
            + " upload=" + UploadedBytes.ToString("R", CultureInfo.InvariantCulture);
    }

    internal sealed class LoginTransportException : Exception
    {
        internal readonly bool MayHaveBeenSent;
        internal LoginTransportException(string message, bool mayHaveBeenSent, Exception inner = null)
            : base(message, inner) { MayHaveBeenSent = mayHaveBeenSent; }
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
            var first = transport.Curl(url, method, headers, body, null,
                deadline.RemainingMs, context.Cancellation);
            Log(context, target.Host, "stage=system " + first.Summary);
            if (first.Unavailable)
            {
                // The only .NET fallback: no curl process could start, so no POST
                // was sent. Never use system DNS again after a curl network failure.
                deadline.Check();
                Log(context, target.Host, "stage=managed reason=curl-unavailable");
                try
                {
                    var managed = transport.Managed(url, method, headers, body, deadline.RemainingMs, context.Cancellation);
                    Log(context, target.Host, "stage=managed-result http=" + managed.HttpCode);
                    return managed;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log(context, target.Host, "stage=failed reason=managed type=" + ex.GetType().Name);
                    throw new LoginTransportException("Initial: curl-unavailable; managed login failed (" + ex.GetType().Name + ").", true);
                }
            }
            if (first.HttpCode > 0) return first;
            if (!first.CanRecover || target.Scheme != Uri.UriSchemeHttps || IPAddress.TryParse(target.Host, out _))
            {
                Log(context, target.Host, "stage=skip reason=unsafe-or-ineligible " + first.Summary);
                throw Failure(first, null);
            }
            deadline.Check();
            if (!context.BeginRecovery(target.Host)) throw Failure(first, null);
            Log(context, target.Host, "stage=doh reason=pre-send-failure " + first.Summary);

            LoginCurlResult second = null;
            LoginAddresses answer = null;
            try
            {
                deadline.Check();
                // Leave time for the real request. No lookup gets a new deadline.
                int lookupMs = Math.Min(2000, deadline.RemainingMs - 1000);
                if (lookupMs <= 0) throw Failure(first, null);
                answer = resolver.Resolve(target.Host, lookupMs, context.Cancellation, false);
                deadline.Check();
                if (answer == null || answer.Values.Length == 0) throw Failure(first, null);
                second = Retry(target, url, method, headers, body, answer, deadline, context);
                Log(context, target.Host, "stage=doh-result cached=" + answer.FromCache + " " + second.Summary);
                if (second.HttpCode > 0) return second;
                resolver.Invalidate(target.Host, answer);
                // A cached bad answer gets one fresh network lookup, inside this
                // same recovery and deadline. Never replay a POST that may be sent.
                if (answer.FromCache && second.CanRecover)
                {
                    deadline.Check();
                    lookupMs = Math.Min(2000, deadline.RemainingMs - 1000);
                    if (lookupMs > 0)
                    {
                        Log(context, target.Host, "stage=cache-refresh reason=pre-send-failure");
                        answer = resolver.Resolve(target.Host, lookupMs, context.Cancellation, true);
                        deadline.Check();
                        if (answer != null && answer.Values.Length > 0)
                        {
                            var cachedFailure = second;
                            second = Retry(target, url, method, headers, body, answer, deadline, context);
                            Log(context, target.Host, "stage=refresh-result previousExit=" + cachedFailure.ExitCode + " " + second.Summary);
                            if (second.HttpCode > 0) return second;
                            resolver.Invalidate(target.Host, answer);
                        }
                    }
                }
                throw Failure(first, second);
            }
            catch (OperationCanceledException) { throw; }
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
            LoginAddresses answer, LoginDeadline deadline, LoginNetworkContext context)
        {
            deadline.Check();
            return transport.Curl(url, method, headers, body,
                target.Host + ":" + target.Port + ":" + string.Join(",", answer.Values),
                deadline.RemainingMs, context.Cancellation);
        }
        private static LoginTransportException Failure(LoginCurlResult first, LoginCurlResult second)
            => new LoginTransportException("Initial: " + first.Summary + "; recovery: "
                + (second == null ? "not-performed-or-no-answer" : second.Summary),
                second == null ? first.MayHaveBeenSent : second.MayHaveBeenSent);
    }

    // Cache only public IPv4 answer sets, never login retry state. Reference
    // comparison prevents an old failing caller from evicting a newer answer.
    internal sealed class LoginDohCache : ILoginResolver
    {
        private sealed class Entry { internal string[] Values; internal DateTime Expires; }
        private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new object();
        private readonly Func<string, int, CancellationToken, string[]> fetch;
        internal LoginDohCache(Func<string, int, CancellationToken, string[]> fetch) { this.fetch = fetch; }
        public LoginAddresses Resolve(string host, int timeoutMs, CancellationToken cancellation, bool refresh)
        {
            cancellation.ThrowIfCancellationRequested();
            if (timeoutMs <= 0) return null;
            lock (gate)
            {
                Entry entry;
                if (!refresh && cache.TryGetValue(host, out entry) && entry.Expires > DateTime.UtcNow)
                    return new LoginAddresses(entry.Values, true);
            }
            var values = (fetch(host, Math.Min(2000, timeoutMs), cancellation) ?? new string[0])
                .Where(IsPublicIPv4).Select(value => IPAddress.Parse(value).ToString()).Distinct().ToArray();
            cancellation.ThrowIfCancellationRequested();
            if (values.Length == 0) return null;
            lock (gate) cache[host] = new Entry { Values = values, Expires = DateTime.UtcNow.AddMinutes(5) };
            return new LoginAddresses(values, false);
        }
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
