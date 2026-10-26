using System;
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
        internal sealed class Runtime { internal string TempPath => "/nonexistent-test-runtime"; }
        internal static Runtime ResourceManager => new Runtime();
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
        public T Deserialize<T>(string value)
        {
            using (var document = System.Text.Json.JsonDocument.Parse(value))
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
        // Linux executes the installed curl. PE/runtime selection remains Windows
        // specific; process deadlines, cancellation, args and parsing are production.
        internal static LoginCurlResult ExecuteLocal(string args, int timeoutMs, CancellationToken cancellation)
        {
            bool started;
            var result = ExecuteCurl("/usr/bin/curl", args, timeoutMs, out started, cancellation);
            return ParseLoginResult(result.Output, result.ExitCode);
        }
    }
}
