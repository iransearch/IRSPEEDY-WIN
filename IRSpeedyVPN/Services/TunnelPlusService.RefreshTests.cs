using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace IRSpeedyVPN.Services
{
    partial class TunnelPlusService
    {
        private sealed class RefreshTestServer : IServer
        {
            public int ID => 0;
            public string Country { get; set; } = "";
            public string Protocol { get; set; }
            public List<Url> urls { get; set; }
        }
        internal static void TestRefreshBatch(Url[] urls, GlobalInfo info, CancellationToken cancellation,
            Action<Url, long> progress, Action<Url, long> completed)
        {
            cancellation.ThrowIfCancellationRequested();
            // Only this bounded batch's copied configs are used for chain/SNI
            // lookup. The runner has no UI connection callbacks or country pool.
            var runner = new TunnelPlusService(new RefreshTestServer { urls = urls.ToList() }, info) { Name = "server-refresh" };
            runner.UrlTestFull(urls, false, null, () => cancellation.IsCancellationRequested, cancellation,
                memberProgress: progress, memberCompleted: completed, preserveOrder: true);
        }
        internal void ResetListTestResult()
        {
            urlTestSpeed = -1; lastUrlTest = default(DateTime); selectedUrl = null;
        }
        internal void UpdateListTestResult()
        {
            var best = server.urls.Where(u => u != null && u.latency > 0).OrderBy(u => u.latency).FirstOrDefault();
            urlTestSpeed = best?.latency ?? -1; selectedUrl = best?.url; lastUrlTest = DateTime.Now;
        }
    }
}
