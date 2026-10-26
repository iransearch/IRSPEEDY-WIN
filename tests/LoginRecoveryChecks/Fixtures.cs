using System;
using System.Web.Script.Serialization;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using IRSpeedyVPN.WebServices;

namespace IRSpeedyVPN.Common
{
    static class LogHelper { internal static void WriteExLog(string message) { } }
    static class ErrorReporting
    {
        internal static void Annotate(Exception ex, params string[] values) { }
        internal static string EndpointAlias(string value) => "api";
        internal static string FailureCode(Exception ex) => ex.GetType().Name;
    }
    static class ConnectionDiagnostics
    {
        internal static long EventSequence => 0;
        internal static string SelectedMode => "none";
        internal static string ObservedMode => "none";
        internal static long ObservedModeAgeMs => 0;
    }
    static class AppServices
    {
#if NETFRAMEWORK
        internal static System.Web.Script.Serialization.JavaScriptConverter JsonConverter => null;
#endif
        internal static string RuntimePath = "/nonexistent-test-runtime";
        internal static int RuntimeDelayMs;
        internal sealed class Runtime { internal string TempPath => RuntimePath; }
        internal static Runtime ResourceManager
        { get { if (RuntimeDelayMs > 0) Thread.Sleep(RuntimeDelayMs); return new Runtime(); } }
        internal static void GetRuntimeSnapshot(out bool ready, out string path, out string error)
        { ready = true; path = "/nonexistent-test-runtime"; error = null; }
    }
}

#if !NETFRAMEWORK
// .NET 8 has no System.Web.Extensions. Only the framework JSON adapter is
// replaced; production response parsing/filtering and all transport code run.
namespace System.Web.Script.Serialization
{
    sealed class JavaScriptSerializer
    {
        public int MaxJsonLength { get; set; } = 2097152;
        public int RecursionLimit { get; set; } = 100;
        public T Deserialize<T>(string value)
        {
            if (value.Length > MaxJsonLength) throw new ArgumentException("JSON exceeds fixture limit");
            using (var document = System.Text.Json.JsonDocument.Parse(value,
                new System.Text.Json.JsonDocumentOptions { MaxDepth = RecursionLimit }))
                return (T)Convert(document.RootElement);
        }
        static object Convert(System.Text.Json.JsonElement item)
        {
            switch (item.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    var result = new Dictionary<string, object>();
                    foreach (var property in item.EnumerateObject()) result[property.Name] = Convert(property.Value);
                    return result;
                case System.Text.Json.JsonValueKind.Array:
                    var list = new List<object>(); foreach (var value in item.EnumerateArray()) list.Add(Convert(value)); return list;
                case System.Text.Json.JsonValueKind.String: return item.GetString();
                case System.Text.Json.JsonValueKind.Number: return item.GetInt32();
                default: return null;
            }
        }
    }
}
#endif

namespace IRSpeedyVPN.WebServices
{
    internal sealed partial class CurlHelper
    {
        internal static string[] ParseLoginAddresses(string json)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object status, answers;
            if (root == null || !root.TryGetValue("Status", out status) || status == null || Convert.ToString(status, CultureInfo.InvariantCulture) != "0"
                || !root.TryGetValue("Answer", out answers)) return new string[0];
            var records = answers as IEnumerable;
            var values = new List<string>();
            if (records != null) foreach (var record in records)
            {
                var item = record as Dictionary<string, object>;
                object type, data;
                if (item != null && item.TryGetValue("type", out type) && Convert.ToString(type) == "1"
                    && item.TryGetValue("data", out data) && data != null && LoginDnsCache.IsPublicIPv4(data.ToString().Trim()))
                    values.Add(data.ToString().Trim());
            }
            return values.ToArray();
        }
        internal static void SetAvailabilityForTest(bool unavailable) { _curlUnavailable = unavailable; }
        internal static void RememberPendingForTest(System.Diagnostics.Process process)
        { RememberPendingStop(process); ReleaseCurlProcess(process); }
        internal static void RememberOwnedForTest(System.Diagnostics.Process process) { RememberPendingStop(process); }
        internal static void ReleaseOwnerForTest(System.Diagnostics.Process process) { ReleaseCurlProcess(process); }
        internal static int PendingCountForTest { get { lock (PendingStopGate) return PendingStops.Count; } }
        internal static bool PendingTimerForTest { get { lock (PendingStopGate) return pendingStopTimer != null; } }
        internal static Timer PendingTimerInstanceForTest { get { lock (PendingStopGate) return pendingStopTimer; } }
        internal static void ExecuteCommandForTest(string executable, string args, int timeout, CancellationToken cancellation)
        { bool started; ExecuteCurl(executable, args, timeout, out started, cancellation); }
        // Linux executes the installed curl. PE/runtime selection remains Windows
        // specific; process deadlines, cancellation, args and parsing are production.
        internal static LoginCurlResult ExecuteLocal(string args, int timeoutMs, CancellationToken cancellation,
            LoginConnectProgress progress = null, LoginOutputProgress outputProgress = null, Action<string> outputLog = null)
        {
            bool started;
            var responseHeaders = new LoginResponseDiagnostics();
            var result = ExecuteCurl("/usr/bin/curl", args, timeoutMs, out started, cancellation,
                progress == null ? (Action)null : progress.Start,
                line => { progress?.Observe(line); responseHeaders.Observe(line); }, outputProgress, outputLog);
            return ParseLoginResult(result.Output, result.ExitCode, responseHeaders);
        }
    }
}

namespace IRSpeedyVPN.WebServices
{
    // Historical DoH fixtures exercise the shared race without adding a second
    // DNS transport or a runtime experiment switch to the production login.
    internal static class FixtureDohProviders
    {
        internal const int LookupBudgetMs = LoginDnsRace.LookupBudgetMs;
        internal static string[] Resolve(string host, int timeoutMs, CancellationToken cancellation,
            Func<string, int, CancellationToken, string[]> query, Action<string> log)
            => LoginDnsRace.Resolve(host, timeoutMs, cancellation,
                (provider, ms, token) => (query(provider + Uri.EscapeDataString(host), ms, token) ?? new string[0])
                    .Where(LoginDnsCache.IsPublicIPv4).ToArray(), log,
                new[] { "https://8.8.8.8/resolve?type=A&name=", "https://1.1.1.1/dns-query?type=A&name=" },
                new[] { "google", "cloudflare" });
    }
}
