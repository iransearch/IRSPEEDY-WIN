using System;
using System.Linq;
using System.Net;
using System.Net.Security;
using IRSpeedyVPN.WebServices;

class Program
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    static void Main()
    {
        string username = "user+&=سلام";
        var form = PasswordChangeClient.BuildForm(username, "old&+=secret", "00123")
            .Split('&').Select(x => x.Split(new[] { '=' }, 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
        Check(form.Count == 6 && !form.ContainsKey("wkey"), "exact form fields without key");
        Check(form["Action"] == "ChangePassword" && form["email"] == "info@irspeedy.us", "action and fixed email");
        Check(form["username"] == username && form["newuser"] == username, "preserve username");
        Check(form["password"] == "old&+=secret" && form["newpass"] == "00123", "encoding and leading zeroes");
        var request = PasswordChangeClient.CreateRequest();
        Check(request.RequestUri.Scheme == "https" && request.RequestUri.Query == "", "HTTPS with no query credentials");
        Check(request.Method == "POST" && request.ContentType == "application/x-www-form-urlencoded", "form POST");
        Check(request.Headers["X-IRSPEEDY-Client"] == "windows-v1" && !request.AllowAutoRedirect, "client header and no replay redirect");
        Check(!request.ServerCertificateValidationCallback(null, null, null, SslPolicyErrors.RemoteCertificateChainErrors), "reject bad TLS certificate");
        request.Abort(); // No network calls in these tests.
        var accepted = PasswordChangeClient.Parse(HttpStatusCode.OK, "{\"st\":true,\"code\":0,\"msg\":\"queued\"}", "old", "00123");
        Check(accepted.ResponseData.IsSuccess && accepted.Response == null, "queue acceptance without raw body retention");
        foreach (string body in new[] { "{}", "<html>error</html>", "{\"st\":\"true\",\"code\":0}", "{\"st\":true,\"code\":32}", "{\"st\":true}" })
            Check(!PasswordChangeClient.Parse(HttpStatusCode.OK, body, "old", "00123").ResponseData.IsSuccess, "fail closed on malformed response");
        foreach (int code in new[] {32, 34})
        {
            var denied = PasswordChangeClient.Parse(HttpStatusCode.Forbidden, "{\"st\":false,\"code\":" + code + ",\"msg\":\"denied\"}", "old", "00123");
            Check(!denied.ResponseData.IsSuccess && denied.ResponseData.Code == code && denied.ResponseData.ErrorMessage == "denied", "403 error parsing");
        }
        Check(!PasswordChangeClient.Parse(HttpStatusCode.Forbidden, "{\"st\":true,\"code\":0}", "old", "00123").ResponseData.IsSuccess, "HTTP status is authoritative");
        var conflict = PasswordChangeClient.Parse(HttpStatusCode.Conflict, "{\"st\":false,\"code\":36,\"msg\":\"ambiguous service\"}", "old", "00123");
        Check(!conflict.ResponseData.IsSuccess && conflict.ResponseData.Code == 36 && conflict.ResponseData.ErrorMessage == "ambiguous service", "409 preserves server explanation");
        var missingConflictMessage = PasswordChangeClient.Parse(HttpStatusCode.Conflict, "{\"st\":false,\"code\":36}", "old", "00123");
        Check(!missingConflictMessage.ResponseData.IsSuccess && missingConflictMessage.ResponseData.ErrorMessage.Contains("هیچ سرویسی تغییر نکرد"), "409 fallback explains no mutation");
        Check(!PasswordChangeClient.Parse(HttpStatusCode.Conflict, "{\"st\":true,\"code\":0}", "old", "00123").ResponseData.IsSuccess, "409 cannot initiate relogin even with success body");
        var echoed = PasswordChangeClient.Parse(HttpStatusCode.BadRequest, "{\"st\":false,\"code\":32,\"msg\":\"old&pass old%26pass 00123\"}", "old&pass", "00123");
        Check(!echoed.ResponseData.ErrorMessage.Contains("old") && !echoed.ResponseData.ErrorMessage.Contains("00123"), "redact echoed credentials");
        Console.WriteLine("PASS: form contract, fixed email, leading zeroes, TLS/redirect policy, queued response, 403/409/invalid responses and secret redaction; no live requests.");
    }
}
