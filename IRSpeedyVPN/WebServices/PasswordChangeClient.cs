using IRSpeedyVPN.Models.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Threading;

namespace IRSpeedyVPN.WebServices
{
    // Single POST: never replay a password mutation through login failover or a redirect.
    internal static class PasswordChangeClient
    {
        internal const string Endpoint = "https://shop.ir-speedy.online/app-change-password.php";
        // Compatibility field only: the endpoint ignores email and authenticates service credentials.
        internal const string CompatibilityEmail = "info@irspeedy.us";
        internal const string ClientHeader = "windows-v1";
        private const string UnknownStatus = "وضعیت ثبت درخواست مشخص نشد؛ پیش از ارسال مجدد، وضعیت سرویس را بررسی کنید.";

        internal static string BuildForm(string username, string password, string newPassword)
        {
            var fields = new[] {
                new KeyValuePair<string, string>("Action", "ChangePassword"),
                new KeyValuePair<string, string>("email", CompatibilityEmail),
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
                new KeyValuePair<string, string>("newuser", username),
                new KeyValuePair<string, string>("newpass", newPassword)
            };
            var body = new StringBuilder();
            foreach (var field in fields)
            {
                if (body.Length != 0) body.Append('&');
                body.Append(field.Key).Append('=').Append(Uri.EscapeDataString(field.Value ?? ""));
            }
            return body.ToString();
        }

        internal static HttpWebRequest CreateRequest()
        {
            var request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.Accept = "application/json";
            request.Headers["X-IRSPEEDY-Client"] = ClientHeader;
            request.AllowAutoRedirect = false;
            request.Proxy = null;
            request.Timeout = request.ReadWriteTimeout = 20000;
            // Per-request validation must not inherit the permissive callback in legacy ServiceHelper.
            request.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => errors == SslPolicyErrors.None;
            return request;
        }

        internal static BaseHttpResponse<ChangePasswordResult> Send(string username, string password, string newPassword)
        {
            try
            {
                var request = CreateRequest();
                byte[] payload = Encoding.UTF8.GetBytes(BuildForm(username, password, newPassword));
                request.ContentLength = payload.Length;
                // Bound the entire request including response reads, not just each individual operation.
                using (var deadline = new Timer(_ => { try { request.Abort(); } catch { } }, null, 20000, Timeout.Infinite))
                {
                    try
                    {
                        using (var stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
                    }
                    finally { Array.Clear(payload, 0, payload.Length); }
                    HttpWebResponse response;
                    try { response = (HttpWebResponse)request.GetResponse(); }
                    catch (WebException ex)
                    {
                        response = ex.Response as HttpWebResponse;
                        if (response == null) throw;
                    }
                    using (response)
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        var text = new StringBuilder();
                        var buffer = new char[1024];
                        int count;
                        while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (text.Length + count > 32768) throw new InvalidDataException();
                            text.Append(buffer, 0, count);
                        }
                        return Parse(response.StatusCode, text.ToString(), password, newPassword);
                    }
                }
            }
            catch
            {
                // No raw exception/body/credential is logged, reported or exposed to the caller.
                return Failure(0, UnknownStatus);
            }
        }

        internal static BaseHttpResponse<ChangePasswordResult> Parse(HttpStatusCode status, string body, string password, string newPassword)
        {
            try
            {
                JObject json;
                using (var reader = new JsonTextReader(new StringReader(body)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
                    json = JObject.Load(reader);
                bool accepted = status == HttpStatusCode.OK && json["st"]?.Type == JTokenType.Boolean
                    && (bool)json["st"] && json["code"]?.Type == JTokenType.Integer && (int)json["code"] == 0;
                string message = json["msg"]?.Type == JTokenType.String ? (string)json["msg"] : null;
                int code = json["code"]?.Type == JTokenType.Integer ? (int)json["code"] : -1;
                if (string.IsNullOrWhiteSpace(message))
                {
                    if (accepted) message = "درخواست تغییر رمز ثبت شد";
                    else if (status == HttpStatusCode.Conflict && code == 36)
                        message = "این نام کاربری و رمز به چند سرویس تعلق دارد؛ هیچ سرویسی تغییر نکرد. برای رفع تداخل با پشتیبانی تماس بگیرید.";
                    else if (status == HttpStatusCode.Forbidden && code == 32)
                        message = "نام کاربری یا رمز فعلی سرویس نامعتبر است.";
                    else if (status == HttpStatusCode.Forbidden && code == 34)
                        message = "درخواست اپ پذیرفته نشد؛ برنامه را به‌روز کنید و در صورت تکرار با پشتیبانی تماس بگیرید.";
                    else message = UnknownStatus;
                }
                foreach (string secret in new[] { password, newPassword })
                    if (!string.IsNullOrEmpty(secret))
                        message = message.Replace(secret, "[حذف شد]").Replace(Uri.EscapeDataString(secret), "[حذف شد]");
                return new BaseHttpResponse<ChangePasswordResult>
                {
                    StatusCode = status,
                    ResponseData = new ChangePasswordResult { IsSuccess = accepted, Code = code,
                        ErrorMessage = message.Length > 512 ? message.Substring(0, 512) : message }
                };
            }
            catch { return Failure(status, UnknownStatus); }
        }

        private static BaseHttpResponse<ChangePasswordResult> Failure(HttpStatusCode status, string message)
        {
            return new BaseHttpResponse<ChangePasswordResult> { StatusCode = status,
                ResponseData = new ChangePasswordResult { Code = -1, ErrorMessage = message } };
        }
    }
}
