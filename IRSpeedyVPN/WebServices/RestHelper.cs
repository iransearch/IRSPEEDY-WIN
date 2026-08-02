using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using System;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace IRSpeedyVPN.WebServices
{
    internal sealed class RestHelper
    {
        private readonly string _baseAddress;
        private readonly CurlHelper _curl;

        internal RestHelper(string baseAddress, string curlExePath = null)
        {
            _baseAddress = baseAddress;
            _curl = new CurlHelper();
            if (!string.IsNullOrWhiteSpace(curlExePath))
                _curl.CurlExePath = curlExePath;
        }

        internal BaseHttpResponse<T> SendRequest<T>(
            string relPath,
            object data,
            IEncryptor encryptor,
            IDecryptor decryptor,
            string token = null)
        {
            string url = _baseAddress + relPath;

            // Serializer
            var serializer = new JavaScriptSerializer();
            serializer.RegisterConverters(new JavaScriptConverter[]
            {
                Program.container.GetInstance<JsonConverter>()
            });

            // Headers (same idea as your original RestHelper)
            var headers = new StringBuilder();
            headers.AppendLine("User-Agent: Mozilla/5.0 (Windows NT 5.1; rv:52.0) Gecko/20100101 Firefox/52.0");
            headers.AppendLine("Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            headers.AppendLine("Accept-Language: en-US,en;q=0.5");
            headers.AppendLine("X-Requested-By: GMv0U73qLXINeEmEe0fP8oaSalxfuQVE");

            if (!string.IsNullOrEmpty(token))
                headers.AppendLine("Authorization: Bearer " + token);

            string method = "GET";
            string body = null;

            // Decide body + content type
            if (data != null)
            {
                method = "POST";

                if (data is string s)
                {
                    headers.AppendLine("Content-Type: application/x-www-form-urlencoded");
                    body = s;
                }
                else if (data is NameValueCollection nvc)
                {
                    headers.AppendLine("Content-Type: application/x-www-form-urlencoded");
                    body = nvc.ToString();
                }
                else
                {
                    headers.AppendLine("Content-Type: application/json");
                    body = serializer.Serialize(data);
                }

                // Encrypt request body (bytes -> turn into text for curl)
                if (encryptor != null && body != null)
                {
                    byte[] encBytes = encryptor.Encrypt(body.ToUTF8Bytes());

                    // IMPORTANT:
                    // curl sends text; safest is base64 transport.
                    // If your backend expects raw binary, tell me and I'll switch to --data-binary via temp file.
                    body = Convert.ToBase64String(encBytes);
                }
            }

            // Send through curl.exe
            var curlResp = _curl.Send(url, method, headers.ToString(), body);
            var rawResponse = curlResp.Body;
            var httpCodeInt = curlResp.HttpCode;

            // Decrypt response if needed
            string responseText = rawResponse;

            if (decryptor != null)
            {
                // Try base64 -> decrypt (common when we base64 encrypted body)
                try
                {
                    byte[] b64 = Convert.FromBase64String(rawResponse.Trim());
                    responseText = decryptor.Decrypt(b64).ToUTF8String();
                }
                catch
                {
                    // Fallback: treat as UTF8 bytes (matches deobfuscated behavior)
                    responseText = decryptor.Decrypt(rawResponse.ToUTF8Bytes()).ToUTF8String();
                }
            }

            // Parse to T if possible
            T responseData = default;
            try
            {
                responseData = serializer.Deserialize<T>(responseText);
            }
            catch
            {
                // keep default; caller can read Response for debugging
            }

            return new BaseHttpResponse<T>
            {
                StatusCode = (HttpStatusCode)httpCodeInt,
                Response = rawResponse,
                ResponseData = responseData
            };
        }
    }
}
