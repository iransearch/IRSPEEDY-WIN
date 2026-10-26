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
            Console.WriteLine("PASS complete production Login2 keeps empty-body " + (int)code + " final");
        }
        if (ReferenceEquals(contexts[0], contexts[1]) || !contexts[0].BeginRecovery("same.test") || !contexts[1].BeginRecovery("same.test"))
            throw new Exception("Login2 shared recovery state");
        Console.WriteLine("PASS complete production Login2 allocates independent per-login state");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); int calls = 0;
            RestHelper.Handler = (host, payload, context) => { calls++; throw new Exception("canceled login reached transport"); };
            try { controller.Login2("test", "test", canceled.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { }
            if (calls != 0) throw new Exception("canceled login reached transport");
            Console.WriteLine("PASS complete production Login2 cancellation stops before transport/DoH");
        }
    }
}
