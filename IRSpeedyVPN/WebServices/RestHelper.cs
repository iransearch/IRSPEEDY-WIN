using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;

namespace IRSpeedyVPN.WebServices
{
    internal sealed class RestHelper
    {
        private readonly string _baseAddress;
        private readonly CurlHelper _curl;

        internal RestHelper(string baseAddress, string curlExePath = null)
        {
            if (string.IsNullOrWhiteSpace(baseAddress))
                throw new ArgumentNullException(nameof(baseAddress));

            _baseAddress = baseAddress;
            _curl = new CurlHelper(curlExePath);
        }

        internal string BaseAddress => _baseAddress;

        internal BaseHttpResponse<T> SendRequest<T>(
            string relPath,
            object data,
            IEncryptor encryptor,
            IDecryptor decryptor,
            string token = null)
        {
            var serializer = new JavaScriptSerializer();
            serializer.RegisterConverters(new JavaScriptConverter[]
            {
                Program.container.GetInstance<JsonConverter>()
            });

            var url = new Uri(new Uri(_baseAddress), relPath ?? string.Empty).ToString();
            var headers = BuildHeaders(token);
            var method = data == null ? "GET" : "POST";
            string body = null;

            if (data != null)
            {
                string mediaType;
                body = BuildBody(serializer, data, out mediaType);
                headers.AppendLine("Content-Type: " + mediaType);

                if (encryptor != null && body != null)
                    body = Convert.ToBase64String(encryptor.Encrypt(body.ToUTF8Bytes()));
            }

            // curl is kept intentionally for Windows 7 compatibility. The complete
            // curl configuration, including the request body, is sent over STDIN by
            // CurlHelper and is therefore not exposed in the process command line.
            var curlResponse = _curl.Send(
                url,
                method,
                headers.ToString(),
                body,
                null,
                30);

            var rawResponse = curlResponse.Body ?? string.Empty;
            var responseText = DecryptResponse(rawResponse, decryptor);

            T responseData = default(T);
            try
            {
                responseData = serializer.Deserialize<T>(responseText);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex);
            }

            return new BaseHttpResponse<T>
            {
                StatusCode = (HttpStatusCode)curlResponse.HttpCode,
                Response = rawResponse,
                ResponseData = responseData
            };
        }

        private static StringBuilder BuildHeaders(string token)
        {
            var headers = new StringBuilder();
            headers.AppendLine("User-Agent: Mozilla/5.0 (Windows NT 6.1; Win64; x64) IRSPEEDY/1.0");
            headers.AppendLine("Accept: application/json, text/plain, */*");
            headers.AppendLine("Accept-Language: en-US,en;q=0.5");
            headers.AppendLine("X-Requested-By: GMv0U73qLXINeEmEe0fP8oaSalxfuQVE");

            if (!string.IsNullOrWhiteSpace(token))
                headers.AppendLine("Authorization: Bearer " + token);

            return headers;
        }

        private static string BuildBody(
            JavaScriptSerializer serializer,
            object data,
            out string mediaType)
        {
            if (data is string text)
            {
                mediaType = "application/x-www-form-urlencoded";
                return text;
            }

            if (data is NameValueCollection values)
            {
                mediaType = "application/x-www-form-urlencoded";
                var builder = new StringBuilder();
                foreach (string key in values.AllKeys)
                {
                    if (builder.Length > 0)
                        builder.Append('&');
                    builder.Append(HttpUtility.UrlEncode(key ?? string.Empty));
                    builder.Append('=');
                    builder.Append(HttpUtility.UrlEncode(values[key] ?? string.Empty));
                }
                return builder.ToString();
            }

            mediaType = "application/json";
            return serializer.Serialize(data);
        }

        private static string DecryptResponse(string rawResponse, IDecryptor decryptor)
        {
            if (decryptor == null)
                return rawResponse;

            try
            {
                return decryptor.Decrypt(
                        Convert.FromBase64String((rawResponse ?? string.Empty).Trim()))
                    .ToUTF8String();
            }
            catch
            {
                return decryptor.Decrypt(
                        (rawResponse ?? string.Empty).ToUTF8Bytes())
                    .ToUTF8String();
            }
        }
    }
}
