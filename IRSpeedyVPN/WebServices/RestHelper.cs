using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;

namespace IRSpeedyVPN.WebServices
{
    internal sealed class RestHelper
    {
        private static readonly HttpClient Client = CreateClient();
        private readonly string _baseAddress;

        internal RestHelper(string baseAddress, string curlExePath = null)
        {
            _baseAddress = baseAddress ?? throw new ArgumentNullException(nameof(baseAddress));
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

            var requestUri = new Uri(new Uri(_baseAddress), relPath ?? string.Empty);
            using (var request = new HttpRequestMessage(data == null ? HttpMethod.Get : HttpMethod.Post, requestUri))
            {
                request.Headers.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) IRSPEEDY/1.0");
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
                request.Headers.TryAddWithoutValidation(
                    "X-Requested-By",
                    "GMv0U73qLXINeEmEe0fP8oaSalxfuQVE");
                if (!string.IsNullOrWhiteSpace(token))
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                if (data != null)
                {
                    var body = BuildBody(serializer, data, out var mediaType);
                    if (encryptor != null && body != null)
                        body = Convert.ToBase64String(encryptor.Encrypt(body.ToUTF8Bytes()));
                    request.Content = new StringContent(body ?? string.Empty, Encoding.UTF8, mediaType);
                }

                using (var response = Client.SendAsync(request).GetAwaiter().GetResult())
                {
                    var rawResponse = response.Content == null
                        ? string.Empty
                        : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
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
                        StatusCode = response.StatusCode,
                        Response = rawResponse,
                        ResponseData = responseData
                    };
                }
            }
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
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
                return decryptor.Decrypt(Convert.FromBase64String((rawResponse ?? string.Empty).Trim()))
                    .ToUTF8String();
            }
            catch
            {
                return decryptor.Decrypt((rawResponse ?? string.Empty).ToUTF8Bytes())
                    .ToUTF8String();
            }
        }
    }
}
