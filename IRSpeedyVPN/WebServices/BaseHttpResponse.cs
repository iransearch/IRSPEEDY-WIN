using System.Net;

namespace IRSpeedyVPN.WebServices
{
    internal class BaseHttpResponse<T>
    {
        public T ResponseData { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public string Response { get; set; }
        // Set only by the authenticated server-list controller after request-ID validation.
        internal bool VerifiedServerList { get; set; }
    }
}

