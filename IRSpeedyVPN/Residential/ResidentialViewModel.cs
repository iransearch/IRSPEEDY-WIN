using IRSpeedyVPN.Common;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace IRSpeedyVPN.Residential
{
    // Owned by the dispatcher. Only the bounded, credential-free GET runs off it.
    internal sealed class ResidentialViewModel : INotifyPropertyChanged
    {
        private readonly GlobalInfo global;
        private readonly DispatcherTimer timer;
        private readonly Func<string, CancellationToken, Task<Payload>> fetch;
        private CancellationTokenSource download;
        private string key = "", url, membership = "";
        private long generation;
        private bool visible, connected, failed;
        private DateTime fetchedAt;
        internal IVPNService[] Services { get; private set; } = new IVPNService[0];
        internal ResidentialTraffic Traffic { get; private set; }
        internal ResidentialTraffic VisibleTraffic => failed ? null : Traffic;
        internal bool IsAllowed { get; private set; }
        internal bool CanConnect => IsAllowed && !(Traffic?.Exhausted ?? false);
        public event PropertyChangedEventHandler PropertyChanged;
        internal event Action CatalogChanged;
        internal ResidentialViewModel(GlobalInfo global, Func<string, CancellationToken, Task<Payload>> fetch = null)
        {
            this.global = global;
            this.fetch = fetch ?? DownloadAsync;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timer.Tick += (s, e) => { if (visible || connected || failed) Refresh(); };
        }
        internal void Configure(AccountInfoEx account)
        {
            var user = account?.UserAccount;
            var config = account?.residential_vpn ?? user?.residential_vpn;
            bool allowed = config?.IsUsable == true && !ResidentialPresentation.IsEco(user?.GroupID ?? user?.group_id ?? user?.pid, user?.GroupName ?? user?.group_name);
            string next = account == null ? "" : ResidentialPresentation.Digest((global.Username ?? "") + "\n" + config?.subscription_url + "\n" + allowed);
            if (key == next) return;
            generation++;
            download?.Cancel(); download = null;
            timer.Stop();
            key = next; url = allowed ? config.subscription_url : null; IsAllowed = allowed;
            Services = new IVPNService[0]; Traffic = null; membership = ""; failed = false;
            fetchedAt = default(DateTime); connected = false;
            Changed(); CatalogChanged?.Invoke();
            if (allowed) { timer.Start(); Refresh(); }
        }
        internal void SetVisible(bool value) { visible = value; if (value) Refresh(); }
        internal void SetConnected(bool value) { connected = value; if (value) Refresh(); }
        private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        internal async void Refresh()
        {
            try { await RefreshAsync(); }
            catch (Exception ex) { LogHelper.WriteExLog("[Residential] refresh-error type=" + ex.GetType().Name); }
        }
        internal async Task RefreshAsync()
        {
            if (!IsAllowed || download != null || (!failed && DateTime.UtcNow - fetchedAt < TimeSpan.FromSeconds(30))) return;
            long epoch = generation;
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            download = cancellation;
            try
            {
                var payload = await fetch(url, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (epoch != generation) return;
                var parsed = ResidentialSubscription.Parse(payload.Body);
                var catalog = parsed.Select(country =>
                {
                    var compatible = new ResidentialCountry { Code = country.Code };
                    compatible.Nodes.AddRange(country.Nodes.Where(n => ResidentialService.IsPoolCompatible(n.Link)));
                    return compatible;
                }).Where(c => c.Nodes.Count > 0).ToArray();
                int unsupported = parsed.Sum(c => c.Nodes.Count) - catalog.Sum(c => c.Nodes.Count);
                if (unsupported > 0) LogHelper.WriteExLog("[Residential] unsupported-pool-links=" + unsupported);
                string next = string.Join("\n", catalog.OrderBy(c => c.Id).SelectMany(c => c.Nodes.Select(n => n.Link)));
                bool changed = membership != next;
                if (changed)
                {
                    Services = catalog.Select(c => (IVPNService)new ResidentialService(c, global, key)).ToArray();
                    membership = next;
                }
                Traffic = ResidentialTraffic.Parse(payload.TrafficHeader);
                failed = false; fetchedAt = DateTime.UtcNow;
                Changed();
                if (changed) CatalogChanged?.Invoke();
            }
            catch (Exception ex)
            {
                if (epoch != generation) return;
                failed = true;
                // Never log the token-bearing subscription URL or subscription links.
                LogHelper.WriteExLog("[Residential] refresh-failed type=" + ex.GetType().Name);
                Changed();
            }
            finally
            {
                if (ReferenceEquals(download, cancellation)) download = null;
                cancellation.Dispose();
            }
        }
        internal sealed class Payload { internal string Body, TrafficHeader; }
        internal static async Task<Payload> DownloadAsync(string url, CancellationToken token)
        {
            for (int redirects = 0; redirects <= 3; redirects++)
            {
                token.ThrowIfCancellationRequested();
                if (!ResidentialConfig.ValidUrl(url)) throw new FormatException("Invalid subscription URL");
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET"; request.Accept = "text/plain";
                request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                request.AllowAutoRedirect = false; request.Timeout = 10000; request.ReadWriteTimeout = 10000;
                // No CookieContainer, credentials, auth headers, or certificate callback.
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400)
                    {
                        if (redirects == 3 || string.IsNullOrWhiteSpace(response.Headers["Location"])) throw new WebException("Subscription redirects");
                        url = new Uri(new Uri(url), response.Headers["Location"]).AbsoluteUri;
                        continue;
                    }
                    if (code < 200 || code >= 300 || response.ContentLength > ResidentialSubscription.MaxBytes) throw new WebException("Subscription response");
                    using (var stream = response.GetResponseStream())
                    using (var bytes = new MemoryStream())
                    {
                        var buffer = new byte[8192]; int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                        {
                            if (bytes.Length + read > ResidentialSubscription.MaxBytes) throw new FormatException("Subscription size");
                            bytes.Write(buffer, 0, read);
                        }
                        return new Payload { Body = Encoding.UTF8.GetString(bytes.ToArray()), TrafficHeader = response.Headers["subscription-userinfo"] };
                    }
                }
            }
            throw new WebException("Subscription redirects");
        }
    }
}
