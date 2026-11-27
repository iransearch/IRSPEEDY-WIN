using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using IRSpeedyVPN.WebServices;
using IRSpeedyVPN.Models.NewService;

class ApiProgram
{
    static void Main()
    {
        var contexts = new List<LoginNetworkContext>();
        var controller = new NewServiceController();
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotAcceptable })
        {
            int calls = 0;
            RestHelper.Handler = (host, payload, context) =>
            {
                calls++; contexts.Add(context);
                if (context.Endpoint == null) throw new Exception("endpoint deadline not started before serialization");
                return new BaseHttpResponse<DefaultEncryptedResponse<AccountInfoEx>> { StatusCode = code, ResponseData = null };
            };
            var response = controller.Login2("credential-must-not-be-logged", "password-must-not-be-logged");
            if (response.StatusCode != code || calls != 1) throw new Exception("empty HTTP rejection replayed");
            if (RestHelper.LastTimeoutSeconds != 20 || contexts[contexts.Count - 1].Endpoint.RemainingMs < 19000
                || contexts[contexts.Count - 1].RemainingMs < 44000)
                throw new Exception("production login endpoint/flow budgets are not 20/45 seconds");
            Console.WriteLine("PASS complete production Login2 keeps empty-body " + (int)code + " final");
        }
        Console.WriteLine("PASS production Login2 supplies a 20-second endpoint inside a 45-second flow");
        if (ReferenceEquals(contexts[0], contexts[1]) || !contexts[0].BeginRecovery("same.test") || !contexts[1].BeginRecovery("same.test"))
            throw new Exception("Login2 shared recovery state");
        Console.WriteLine("PASS complete production Login2 allocates independent per-login state");
        foreach (bool matchingId in new[] { false, true })
        {
            int calls = 0;
            RestHelper.Handler = (host, payload, context) =>
            {
                calls++;
                string id = (string)payload.GetType().GetProperty("id").GetValue(payload, null);
                return new BaseHttpResponse<DefaultEncryptedResponse<AccountInfoEx>> {
                    StatusCode = HttpStatusCode.OK,
                    ResponseData = new DefaultEncryptedResponse<AccountInfoEx> {
                        data = matchingId ? id : "previous-request", message = "api message" }
                };
            };
            var response = controller.Login2("test", "test");
            if (response.VerifiedServerList != matchingId || calls != 1)
                throw new Exception("server-list request-ID verification flag or POST count changed");
            Console.WriteLine("PASS production Login2 marks only matching encrypted request IDs as verified; no POST replay");
        }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); int calls = 0;
            RestHelper.Handler = (host, payload, context) => { calls++; throw new Exception("canceled login reached transport"); };
            try { controller.Login2("test", "test", canceled.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { }
            if (calls != 0) throw new Exception("canceled login reached transport");
            Console.WriteLine("PASS complete production Login2 cancellation stops before transport/DoH");
        }
        CheckTransportDiagnostics();
    }

    private static void CheckTransportDiagnostics()
    {
        var transport = new TimeoutTransport();
        var resolver = new LoginDnsCache((_, __, ___) => Array.Empty<string>());
        var recovery = new LoginRecovery(transport, resolver, null, LoginDnsOrder.ResolverFirst);
        RestHelper.Handler = (url, payload, context) =>
            recovery.Send(url, "POST", "", "fixture-secret", 8000, context);
        try
        {
            new NewServiceController().Login2("fixture-user", "fixture-password", refreshServerList: true);
            throw new Exception("timeout unexpectedly returned a response");
        }
        catch (LoginTransportException ex)
        {
            if (ex.MayHaveBeenSent || transport.Calls != 4
                || !Equals(ex.Data["login.flow"], "session-refresh")
                || !Equals(ex.Data["login.api_attempts"], "4")
                || !Equals(ex.Data["login.transport_attempts"], "4"))
                throw new Exception("refresh diagnostics or safe failover count is wrong");
            for (int i = 1; i <= 4; i++)
                if (!Equals(ex.Data["login.attempt" + i], "api=" + i
                    + ";dns=no-answer;route=system;phase=pre-connect-timeout;curl=28;http=none;send=not-sent"))
                    throw new Exception("refresh attempt outcome missing");
        }

        transport.Calls = 0;
        transport.CompleteTrailer = false;
        try
        {
            new NewServiceController().Login2("fixture-user", "fixture-password");
            throw new Exception("ambiguous timeout unexpectedly returned a response");
        }
        catch (LoginTransportException ex)
        {
            if (!ex.MayHaveBeenSent || transport.Calls != 1 || !Equals(ex.Data["login.flow"], "login")
                || !Equals(ex.Data["login.attempt1"],
                    "api=1;dns=no-answer;route=system;phase=transport-timeout;curl=28;http=none;send=unknown"))
                throw new Exception("ambiguous timeout evidence or replay policy changed");
        }
        Console.WriteLine("PASS production login diagnostics distinguish four pre-send refresh failures from a replay-blocked timeout");
    }

    private sealed class TimeoutTransport : ILoginTransport
    {
        internal int Calls;
        internal bool CompleteTrailer = true;

        public LoginCurlResult Curl(string url, string method, string headers, string body, string resolve,
            int timeoutMs, CancellationToken cancellation)
        {
            Calls++;
            return new LoginCurlResult
            {
                Body = "", ExitCode = 28, MetricsKnown = true, CompleteTimeoutTrailerWithEmptyPeer = CompleteTrailer,
                FirstByteSeconds = 3, TotalSeconds = 3, DownloadedBytes = 0, HttpVersion = "0"
            };
        }

        public LoginCurlResult Managed(string url, string method, string headers, string body,
            int timeoutMs, CancellationToken cancellation)
            => throw new Exception("unexpected managed transport");
    }
}
