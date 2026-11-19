using IRSpeedyVPN.Common;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Residential;
using IRSpeedyVPN.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN
{
    public partial class MainWindow
    {
        // Catalog state belongs to the dispatcher and one account generation.
        private static readonly int[] subscriptionRetrySeconds = { 5, 15, 30, 60, 60, 60 };
        private CancellationTokenSource countryDownloads;
        private long countrySession;
        private readonly Dictionary<string, string[]> countryNodes = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> countryFetchedAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private void RetireCountryCatalog(long endedGeneration)
        {
            void Retire()
            {
                if (countrySession >= endedGeneration) return;
                countryDownloads?.Cancel();
                countryDownloads = null;
                countryNodes.Clear();
                countryFetchedAt.Clear();
            }
            if (Dispatcher.CheckAccess()) Retire();
            else Dispatcher.BeginInvoke((Action)Retire);
        }

        private void ApplyCountryCatalog(List<Group> groups, long generation)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke((Action)(() => ApplyCountryCatalog(groups, generation)));
                return;
            }
            if (!accountSessionMaintenance.IsCurrent(generation) || ExitCleanupStarted) return;
            var rows = groups.SelectMany(g => g.servers)
                .SelectMany(row => row.urls.Where(u => u?.status == 1)
                    .Select(u => new { source = CountrySubscription.Source(u), row.ID, marked = CountrySubscription.Marked(row, u) }))
                .Where(entry => entry.source != null).ToArray();
            var sources = rows.ToLookup(entry => entry.source, entry => entry.ID, StringComparer.Ordinal);
            var marked = new HashSet<string>(rows.Where(entry => entry.marked).Select(entry => entry.source), StringComparer.Ordinal);
            var obsolete = countryNodes.Keys.Where(key => countrySession != generation || !sources.Contains(key)
                || DateTime.UtcNow - countryFetchedAt[key] >= TimeSpan.FromDays(1)).ToArray();
            foreach (string key in obsolete) { countryNodes.Remove(key); countryFetchedAt.Remove(key); }
            serviceFactory.RenewServiceList(CountrySubscription.Expand(groups, countryNodes));

            countryDownloads?.Cancel();
            countryDownloads = null;
            countrySession = generation;
            if (sources.Count == 0) return;
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(loginApplicationExit.Token);
            countryDownloads = lifetime;
            _ = WatchCountryCatalogAsync(groups, sources, marked, generation, lifetime);
        }

        private async Task WatchCountryCatalogAsync(List<Group> groups, ILookup<string, int> rows,
            HashSet<string> marked, long generation, CancellationTokenSource lifetime)
        {
            var token = lifetime.Token;
            using (var slots = new SemaphoreSlim(3))
            {
                async Task Watch(string source)
                {
                    int failures = 0;
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        string[] links = null;
                        string outcome = "loaded";
                        bool nonSubscription = false;
                        await slots.WaitAsync(token);
                        try
                        {
                            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                                links = await Task.Run(async () =>
                                {
                                    var payload = await ResidentialViewModel.DownloadCountryAsync(source, deadline.Token).ConfigureAwait(false);
                                    return CountrySubscription.Parse(payload.Body);
                                }, deadline.Token);
                            }
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            outcome = ex.GetType().Name;
                            nonSubscription = ex is FormatException || ex is System.Text.DecoderFallbackException;
                            if (ex is WebException web && web.Response is HttpWebResponse response)
                            {
                                outcome = "http-" + (int)response.StatusCode;
                                nonSubscription = (int)response.StatusCode >= 400 && (int)response.StatusCode < 500
                                    && response.StatusCode != HttpStatusCode.RequestTimeout && (int)response.StatusCode != 429;
                                response.Dispose();
                            }
                        }
                        finally { slots.Release(); }
                        // Continuations run on the dispatcher. A stale payload must never replace
                        // a newer country list, including within the same account generation.
                        if (token.IsCancellationRequested || !ReferenceEquals(lifetime, countryDownloads)
                            || !accountSessionMaintenance.IsCurrent(generation) || ExitCleanupStarted) return;
                        // Empty/HTML responses at an unmarked legacy gateway carry no
                        // evidence of subscription ownership. Do not poll that gateway.
                        if (!marked.Contains(source) && !countryNodes.ContainsKey(source)
                            && (nonSubscription || links?.Length == 0))
                        {
                            foreach (int row in rows[source].Distinct())
                                ConnectionDiagnostics.Write("country-subscription", "row=" + row + " result=" + outcome + " classified=false");
                            return;
                        }
                        bool changed = false;
                        if (links != null)
                        {
                            changed = !countryNodes.TryGetValue(source, out var old) || !old.SequenceEqual(links);
                            countryNodes[source] = links;
                            countryFetchedAt[source] = DateTime.UtcNow;
                            failures = 0;
                        }
                        else if (countryFetchedAt.TryGetValue(source, out var at) && DateTime.UtcNow - at >= TimeSpan.FromDays(1))
                        {
                            countryFetchedAt.Remove(source);
                            changed = countryNodes.Remove(source);
                        }
                        foreach (int row in rows[source].Distinct())
                            ConnectionDiagnostics.Write("country-subscription", "row=" + row + " result=" + outcome
                                + " nodes=" + (links?.Length ?? 0) + " wireguardLinks=" + (links?.Count(Services.SingBox.WireGuardEndpoint.IsLink) ?? 0));
                        if (changed)
                        {
                            serviceFactory.RenewServiceList(CountrySubscription.Expand(groups, countryNodes));
                            uCServerList.RefreshCountrySubscriptionRows();
                        }
                        int delay = links == null && failures < subscriptionRetrySeconds.Length ? subscriptionRetrySeconds[failures++] : 300;
                        await Task.Delay(TimeSpan.FromSeconds(delay), token);
                    }
                }
                foreach (var group in rows)
                    foreach (int row in group.Distinct())
                        ConnectionDiagnostics.Write("country-subscription", "row=" + row + " result=scheduled");
                try { await Task.WhenAll(rows.Select(group => Watch(group.Key))); }
                catch (Exception ex) when (token.IsCancellationRequested || ex is OperationCanceledException) { }
                catch (Exception ex) { ConnectionDiagnostics.Write("country-subscription", "result=" + ex.GetType().Name); }
                finally
                {
                    if (ReferenceEquals(lifetime, countryDownloads)) countryDownloads = null;
                    lifetime.Dispose();
                }
            }
        }
    }
}
