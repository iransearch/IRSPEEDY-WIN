using System;
using System.Linq;
using System.Net;
using IRSpeedyVPN.Common;
using Sentry;

internal static class Program
{
    private static void Main()
    {
        // Never initialize the SDK or send a test event to production.
        var first = new WebException("password=SECRET https://private.invalid/?token=SECRET", WebExceptionStatus.Timeout);
        first.Data["token"] = "SECRET";
        ErrorReporting.Annotate(first, "api.request_id", "request-one", "http.stage", "get-request-stream",
            "api.attempt1", "api1:error=Timeout:ms=3000", "http.budget_ms", "3000",
            "unknown.field", "SECRET", "curl.reason", "https://private.invalid/?token=SECRET");
        var wrapped = new TimeoutException("SECRET", first);
        var clean = ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(wrapped, false));
        Require(clean.Tags["web.status"] == "Timeout", "WebException.Status missing through wrapper");
        Require(clean.Tags["http.stage"] == "get-request-stream", "Transport stage lost by before-send");
        Require(clean.Tags["api.request_id"] == "request-one", "Request ID lost");
        Require(clean.Tags["api.attempt1"] == "api1:error=Timeout:ms=3000", "Attempt history lost");
        Require(!clean.Tags.ContainsKey("unknown.field") && !clean.Tags.ContainsKey("curl.reason"), "Unsafe metadata exported");
        Require(clean.SentryExceptions.All(e => !e.Value.Contains("SECRET")), "Raw exception message exported");
        Require(clean.SentryExceptions.Any(e => e.Value.Contains("WebException.Status=Timeout")), "Safe status omitted from exception");
        var second = ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(new Exception("SECRET"), true));
        Require(!second.Tags.ContainsKey("api.request_id") && !second.Tags.ContainsKey("api.attempt1"), "Concurrent requests can inherit stale metadata");
        Require(second.Level == SentryLevel.Fatal, "Fatal severity lost");
        Require(clean.Tags["installation.id"] == second.Tags["installation.id"], "Identity unstable within process");
        Require(clean.Tags["build.mvid"].Length == 32, "Build identity missing");
        Require(ErrorReporting.EndpointAlias("https://api1.greadia.app/private?token=SECRET") == "api1", "API alias failed");
        Require(ErrorReporting.EndpointAlias("https://private.invalid") == "other", "Unknown host exported");
        Console.WriteLine("PASS: 13 reporting checks. No network requests were sent.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

namespace IRSpeedyVPN.Common
{
    internal static class LogHelper { internal static void WriteLog(string message) { } }
    internal static class ConnectionDiagnostics
    {
        internal static string SelectedMode => "Proxy";
        internal static string ObservedMode => "not-connected";
        internal static long ObservedModeAgeMs => 0;
        internal static long EventSequence => 0;
        internal static bool MonitorStarted => false;
    }
}
