using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using IRSpeedyVPN.WebServices;

namespace IRSpeedyVPN.Common
{
    internal static class ServiceHelper
    {
        public static bool CheckAvailabilty(string username, string password)
        {
            byte retCount = 2;
            bool ret = false;
            do
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://www.google.com");


                //request.Proxy = new WebProxy("nl1.irsv.me:7080");
                request.Proxy = new WebProxy("update7.isdm.ir:443");
                request.Proxy.Credentials = new NetworkCredential(username, password);
                // Get the response.

                try
                {
                    using (WebResponse response = request.GetResponse())
                    {
                        /// Console.WriteLine(((HttpWebResponse)response).StatusDescription);
                        using (Stream dataStream = response.GetResponseStream())
                        {
                            // Open the stream using a StreamReader for easy access.
                            StreamReader reader = new StreamReader(dataStream);
                            // Read the content.
                            string responseFromServer = reader.ReadToEnd();
                            // Display the content.
                            //   Console.WriteLine(responseFromServer);
                        }

                    }
                    ret = true;
                }
                catch
                {
                    retCount--;
                }
            }
            while (retCount > 0 && !ret);
            return ret;

        }
        internal static long ProxyUrlTest(string url, int port)
        {
            byte retCount = 2;
            long delay = -1;
            var testurl = "http://google.com";
            // ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
            do
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(testurl);
                testurl = url;
                request.Proxy = new WebProxy($"127.0.0.1:{port}");
                // Get the response.
                request.Timeout = retCount == 1 ? 6000 : 3000;
                Stopwatch sw = new Stopwatch();
                try
                {
                    
                    sw.Start();
                    using (WebResponse response = request.GetResponse())
                    {
                        /// Console.WriteLine(((HttpWebResponse)response).StatusDescription);
                        using (Stream dataStream = response.GetResponseStream())
                        {
                            // Open the stream using a StreamReader for easy access.
                            StreamReader reader = new StreamReader(dataStream);
                            // Read the content.
                            string responseFromServer = reader.ReadToEnd();
                            // Display the content.
                            //   Console.WriteLine(responseFromServer);
                        }

                    }
                    delay = sw.ElapsedMilliseconds;
                }
                catch (WebException e)
                {
                    if (e.Status == WebExceptionStatus.ProtocolError && ((HttpWebResponse)(e.Response)).StatusCode==HttpStatusCode.NotFound)
                        delay = sw.ElapsedMilliseconds;
                    else retCount--;
                }
                catch
                {

                    retCount--;
                }
            }
            while (retCount > 0 && delay < 0);
            return delay;
        }
        internal static long UnsafeUrlTest(string url)
        {
            byte retCount = 2;
            long delay = -1;
            // ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
            ServicePointManager.ServerCertificateValidationCallback +=
    (sender, cert, chain, sslPolicyErrors) => true;
            do
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = retCount == 1 ? 3000 : 5000;

                try
                {
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    using (WebResponse response = request.GetResponse())
                    {
                        /// Console.WriteLine(((HttpWebResponse)response).StatusDescription);
                        using (Stream dataStream = response.GetResponseStream())
                        {
                            // Open the stream using a StreamReader for easy access.
                            StreamReader reader = new StreamReader(dataStream);
                            // Read the content.
                            string responseFromServer = reader.ReadToEnd();
                            // Display the content.
                            //   Console.WriteLine(responseFromServer);
                        }

                    }
                    delay = sw.ElapsedMilliseconds;
                }
                catch
                {
                    retCount--;
                }
            }
            while (retCount > 0 && delay < 0);
            return delay;
        }

        /// <summary>
        /// Tests the active connection's HTTP/mixed listener. A null port is for
        /// system-tunnel services, which have no local HTTP proxy.
        /// </summary>
        internal static long ConnectionUrlTest(string url, int timeoutMs, int? httpPort)
        {
            if (httpPort.HasValue && (httpPort.Value <= 0 || httpPort.Value > 65535))
                throw new ArgumentOutOfRangeException(nameof(httpPort));

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    // Some destinations reject HEAD. Only wait for GET headers; do
                    // not download the page body just to measure reachability.
                    request.Method = "GET";
                    request.UserAgent = "Mozilla/5.0";
                    request.Proxy = httpPort.HasValue
                        ? new WebProxy("http://127.0.0.1:" + httpPort.Value)
                        : null;
                    request.Timeout = timeoutMs;
                    request.ReadWriteTimeout = timeoutMs;
                    request.AllowAutoRedirect = true;
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        LogHelper.WriteExLog("[ConnectionTest] host=" + new Uri(url).Host
                            + " route=" + (httpPort.HasValue ? "http-proxy:" + httpPort.Value : "system-tunnel")
                            + " status=" + (int)response.StatusCode
                            + " elapsedMs=" + stopwatch.ElapsedMilliseconds);
                        return stopwatch.ElapsedMilliseconds;
                    }
                }
                catch (WebException ex)
                {
                    using (var response = ex.Response as HttpWebResponse)
                    {
                        LogHelper.WriteExLog("[ConnectionTest] host=" + new Uri(url).Host
                            + " route=" + (httpPort.HasValue ? "http-proxy:" + httpPort.Value : "system-tunnel")
                            + " attempt=" + attempt + " error=" + ex.Status
                            + " httpStatus=" + (response == null ? "" : ((int)response.StatusCode).ToString())
                            + " elapsedMs=" + stopwatch.ElapsedMilliseconds);
                        // An HTTP rejection is not a transient connection failure.
                        if (response != null)
                            return -1;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteExLog("[ConnectionTest] host=" + new Uri(url).Host
                        + " error=" + ex.GetType().FullName);
                    return -1;
                }
            }
            return -1;
        }

        internal static long UrlTest(string url, int timeoutMs)
        {
            byte retCount = 2;
            long delay = -1;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
            do
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "HEAD";
                    request.Proxy = new WebProxy("127.0.0.1:1080");
                    request.Timeout = timeoutMs;
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    using (WebResponse response = request.GetResponse())
                    {
                    }
                    sw.Stop();
                    delay = sw.ElapsedMilliseconds;
                }
                catch
                {
                    retCount--;
                }
            }
            while (retCount > 0 && delay < 0);
            return delay;
        }
    }
}
