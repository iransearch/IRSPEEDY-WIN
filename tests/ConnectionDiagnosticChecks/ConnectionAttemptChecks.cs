using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using IRSpeedyVPN.Common;

internal static class ConnectionAttemptChecks
{
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }

    private static Exception NestedFailure()
    {
        try { throw new SocketException(10054); }
        catch (Exception socket)
        {
            return new InvalidOperationException("password=PRIVATE_VALUE https://private.example/ 203.0.113.9",
                new WebException("username=PRIVATE_USER", socket, WebExceptionStatus.SendFailure, null));
        }
    }

    internal static void Run()
    {
        var records = new ConcurrentQueue<string>();
        LogHelper.Sink = records.Enqueue;
        const string identity = "service=11111111111111111111111111111111 connection=22222222222222222222222222222222";
        var first = new ConnectionAttemptDiagnostic(11, "ui-request", () => identity);
        var second = new ConnectionAttemptDiagnostic(12, "ui-request", () => identity);
        first.Step("prepare-service"); second.Step("validate-runtime");
        var failure = NestedFailure();
        first.Failure(failure);
        second.Step("connect-dispatched", "dispatched");
        ConnectionDiagnostics.Flush();
        string output = string.Join("\n", records.ToArray());
        Check(output.Contains("requestVersion=11 origin=ui-request phase=prepare-service")
            && output.Contains("requestVersion=12 origin=ui-request phase=validate-runtime")
            && output.Contains("phase=connect-dispatched") && output.Contains(identity),
            "connection phases retain separate request versions and existing service identities");
        Check(output.Contains("exceptionIndex=0 parentIndex=-1 exception=System.InvalidOperationException")
            && output.Contains("exceptionIndex=1 parentIndex=0 exception=System.Net.WebException")
            && output.Contains("exceptionIndex=2 parentIndex=1 exception=System.Net.Sockets.SocketException")
            && output.Contains("webStatus=SendFailure") && output.Contains("nativeError=")
            && output.Contains("hresult=0x") && output.Contains("ConnectionAttemptChecks.NestedFailure"),
            "connection failures retain nested exception types, HRESULT, native/socket status and method frames");
        Check(!output.Contains("PRIVATE") && !output.Contains("private.example") && !output.Contains("203.0.113.9")
            && output.Contains("messageId="), "connection failure logs contain fingerprints without raw credentials or endpoints");
        Check(ErrorReporting.TestCaptures == 0, "already-reported callers do not add duplicate Sentry captures");
        first.Failure(failure, capture: true);
        Check(ErrorReporting.TestCaptures == 1, "previously swallowed startup failures reach the existing sanitized Sentry path once");

        new ConnectionAttemptDiagnostic(11, "ui-banner").Step("show-message", "displayed", "password=PRIVATE_VALUE");
        ConnectionDiagnostics.Flush();
        Check(records.Any(line => line.Contains("origin=ui-banner") && line.Contains("caller=")
            && line.Contains("ConnectionAttemptChecks.Run") && !line.Contains("PRIVATE_VALUE")),
            "shown generic errors retain caller method names without source paths or message data");

        records = new ConcurrentQueue<string>(); LogHelper.Sink = records.Enqueue;
        first.Failure(new AggregateException(Enumerable.Range(0, 30).Select(i =>
            new Win32Exception(5, "token=PRIVATE_VALUE C:\\Private\\file.json"))));
        ConnectionDiagnostics.Flush();
        Check(records.Count(line => line.Contains("stage=connect-attempt-exception")) == 8
            && !records.Any(line => line.Contains("PRIVATE_VALUE") || line.Contains("Private")),
            "aggregate failure details are bounded to eight safe exception records");

        records = new ConcurrentQueue<string>(); LogHelper.Sink = records.Enqueue;
        new ConnectionAttemptDiagnostic(13, "PRIVATE_SOURCE", () => "service=PRIVATE_VALUE")
            .Step("PRIVATE_PHASE", "PRIVATE_OUTCOME", "password=PRIVATE_VALUE");
        ConnectionDiagnostics.Flush(); output = string.Join("\n", records);
        Check(output.Contains("origin=other phase=other") && output.Contains("outcome=other")
            && output.Contains("service=other connection=none") && !output.Contains("PRIVATE"),
            "connection log metadata uses explicit aliases and validated identities");
        ErrorReporting.TestThrow = true;
        LogHelper.Sink = line => { throw new InvalidOperationException("failing local sink"); };
        first.Step("show-status"); first.Failure(failure, capture: true); ConnectionDiagnostics.Flush();
        ErrorReporting.TestThrow = false;
        LogHelper.Sink = null;
        Check(true, "failed local and Sentry sinks cannot change connection exception handling");
    }
}
