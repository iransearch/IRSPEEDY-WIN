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
        CheckCrashRelayLoginFields();
        CoreRpcChecks.Run().GetAwaiter().GetResult();
#if CORE_RPC_OPERATION_CHECKS
        OperationChecks.Run();
#endif
        Console.WriteLine("PASS: 13 existing reporting checks plus " + CoreRpcChecks.Checks + " Core RPC reporting checks. Sentry was never initialized and no events were sent.");
    }

    private static void CheckCrashRelayLoginFields()
    {
        const string attempt = "api=1;dns=no-answer;route=system;phase=pre-connect-timeout;curl=28;http=none;send=not-sent";
        var inner = new Exception("SENSITIVE user and https://private.invalid");
        inner.Data["login.attempt3"] = attempt;
        ErrorReporting.Annotate(inner, "login.flow", "session-refresh", "login.api_attempts", "4",
            "login.transport_attempts", "4", "login.elapsed_ms", "32000", "login.last_dns", "no-answer",
            "login.attempt1", attempt, "login.attempt2", attempt + ";url=https://private.invalid",
            "login.attempt9", attempt, "api.request_id", "internal-request-id");
        var outer = new Exception("wrapper", inner);
        var details = ErrorReporting.CrashRelayDetails(outer);
        Require(details["login.flow"] == "session-refresh" && details["login.api_attempts"] == "4"
            && details["login.transport_attempts"] == "4" && details["login.elapsed_ms"] == "32000"
            && details["login.last_dns"] == "no-answer" && details["login.attempt1"] == attempt,
            "Safe login evidence is missing through the wrapper");
        Require(!details.ContainsKey("login.attempt2") && !details.ContainsKey("login.attempt3")
            && !details.ContainsKey("login.attempt9") && !details.ContainsKey("api.request_id")
            && details.Values.All(value => !value.Contains("SENSITIVE") && !value.Contains("private.invalid")),
            "CrashRelay exported an invalid attempt, raw exception, or internal request ID");
        Require(ErrorReporting.CrashRelayDetails(new Exception()).Keys.All(key => !key.StartsWith("login.")),
            "An unrelated error inherited login evidence");
        Require(!ErrorReporting.PrepareException(outer, false).Tags.ContainsKey("login.attempt1"),
            "CrashRelay login details expanded Sentry reporting");
        Console.WriteLine("PASS CrashRelay exports only bounded, allowlisted login evidence from the current exception");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

namespace IRSpeedyVPN.Common
{
    internal static class LogHelper {
        internal static readonly System.Collections.Generic.List<SentryEvent> Captured=new System.Collections.Generic.List<SentryEvent>();
        internal static void WriteLog(string message) { }
        internal static void WriteLog(Exception exception) {Captured.Add(ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(exception,false)));}
    }
    internal static class ConnectionDiagnostics
    {
        internal static string Caller()=>"fixture";
        internal static void RequestSnapshot(){}
        internal static string SafeCoreStartText(string value)=>"[redacted]";
        internal static string Fingerprint(string value) => new string('a',16);
        internal static string SelectedMode => "Proxy";
        internal static string ObservedMode => "not-connected";
        internal static long ObservedModeAgeMs => 0;
        internal static long EventSequence => 0;
        internal static bool MonitorStarted => false;
    }
}
