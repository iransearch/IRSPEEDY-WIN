using IRSpeedyVPN.Common;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Security;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using v2rayN;

namespace IRSpeedyVPN.WebServices
{
    public class NewServiceController
    {
        private const string ChangePasswordKey = "5LCzP4gMSpZ5nMMmuCXnkWJwwGWgEWcJ";

        // Server-list payload key version. The server encrypts its response with the key
        // matching this value; an old server that does not read "kv" keeps using the
        // legacy key, and the client tries both keys on decrypt, so nothing breaks.
        private const int ServerListKeyVersion = 2;
        // Network budget shared by the endpoints of each authentication flow.
        // A separate settings request may follow login when its response omits settings.
        private const int LoginRequestTimeoutSeconds = 10;
        private const int SlowNetworkLoginBudgetSeconds = 30;
        private const int LoginEndpointTimeoutSeconds = 8;
        // Secondary settings call is kept short so total login work stays fast.
        private const int GetSettingsTimeoutSeconds = 4;

        // ----------------------------
        // NEW API (failover endpoints)
        // ----------------------------
        private readonly List<RestHelper> _services;
        private static string _lastGoodBaseUrl; // store URL instead of instance ref
        private int _index;

        private readonly RestHelper _geoIpService;
        private readonly TripleDesHelper _tdes;
        //_availabilityService = new RestHelper("https://apichcek-p.isdm.ir/Service");
        public NewServiceController()
        {
            _services = new List<RestHelper>
            {
                new RestHelper("https://api1.greadia.app/"),
                new RestHelper("https://api3.greadia.ir/"),
                new RestHelper("https://api2.greadia.app/"),
                new RestHelper("https://apix.myapifast.ir/")

            };

            // Start from the last known good URL if available
            if (!string.IsNullOrWhiteSpace(_lastGoodBaseUrl))
            {
                int idx = _services.FindIndex(s => string.Equals(GetBaseUrl(s), _lastGoodBaseUrl, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _index = idx;
            }

            _geoIpService = new RestHelper("http://ip-api.com/json/");
            _tdes = new TripleDesHelper("J816KWWeG4Q69p72U6ueXu3W".ToAsciiBytes());
        }

        /// <summary>
        /// Old ServiceController.Login2 -> New service server list.
        /// </summary>
        internal BaseHttpResponse<DefaultEncryptedResponse<AccountInfoEx>> Login2(string username, string password)
            => getServerList(username, password, SysThumbPrint.GetComputerName(), SysThumbPrint.ValueString());

        internal BaseHttpResponse<ChangePasswordResult> ChangePassword(string username, string oldPassword, string newPassword)
        {
            // Now uses the same failover pool (no _mainService).
            string url =
                $"change_password.php?username={Utils.UrlEncode(username)}" +
                $"&old_password={Utils.UrlEncode(oldPassword)}" +
                $"&new_password={Utils.UrlEncode(newPassword)}" +
                $"&key={ChangePasswordKey}";

            return ExecuteWithFailover(
                Guid.NewGuid().ToString("N"),
                "ChangePassword",
                (svc, timeoutSeconds) => svc.SendRequest<ChangePasswordResult>(
                    url, null, null, null, null, timeoutSeconds, skipDoh: true),
                LoginRequestTimeoutSeconds);
        }

        internal BaseHttpResponse<GeoIp> GetIpInfo()
            => _geoIpService.SendRequest<GeoIp>("", null, null, null);

        internal bool CheckUserPermission(string username, string password)
        {
            // currently always true, keeping behavior
            return true;

            /*
            string id = Guid.NewGuid().ToString();
            PermissionUser user = new PermissionUser(
                username,
                _tdes.Encrypt(password.ToUTF8Bytes()).ToBase64String(),
                id
            );

            var availabilityService = new RestHelper("https://apichcek-p.isdm.ir/Service");
            var res = availabilityService.SendRequest<EncryptedResponse>("CheckUser", user, null, null);

            return res.StatusCode == HttpStatusCode.OK &&
                   _tdes.Decrypt(res.ResponseData.encMessage.FromBase64String()).ToUTF8String() == id;
            */
        }
        
        internal BaseHttpResponse<DefaultEncryptedResponse<AccountInfoEx>> getServerList(
            string userName,
            string password,
            string deviceName,
            string deviceToken)
        {
            string requestId = Guid.NewGuid().ToString("N");
            LogHelper.WriteExLog("[StartupAuth] stage=start flow=Login requestId=" + requestId);

            return ExecuteWithFailover(
                requestId,
                "Login",
                (service, timeoutSeconds) =>
            {
                string guid = Guid.NewGuid().ToString();

                var res = service.SendRequest<DefaultEncryptedResponse<AccountInfoEx>>(
                    "api/server/list/all",
                    BuildAuthPayload(userName, password, deviceName, deviceToken, guid),
                    null, null, null, timeoutSeconds, skipDoh: true);

                bool validResponse = IsValidEncryptedServerListResponse(res, guid);
                LogHelper.WriteExLog("[StartupAuth] stage=validate-response requestId=" + requestId
                    + " endpoint=" + GetBaseUrl(service) + " valid-guid=" + validResponse
                    + " status=" + GetResponseStatus(res)
                    + " has-message=" + (res != null && res.ResponseData != null && !string.IsNullOrEmpty(res.ResponseData.message)));

                // Keep your original validation:
                if (validResponse)
                    return res;

                // If server returned a meaningful message, return it (do not retry)
                if (res?.ResponseData?.message != null)
                    return res;

                // Otherwise treat as retryable
                return null;
            },
                SlowNetworkLoginBudgetSeconds);
        }

        internal BaseHttpResponse<DefaultPlainResponse<AccountInfoEx>> RemoveToken(
            string userName,
            string password,
            string deviceName,
            string deviceToken)
        {
            return ExecuteWithFailover(
                Guid.NewGuid().ToString("N"),
                "RemoveToken",
                (service, timeoutSeconds) =>
                {
                    string guid = Guid.NewGuid().ToString();

                    return service.SendRequest<DefaultPlainResponse<AccountInfoEx>>(
                        "api/remove/token",
                        BuildAuthPayload(userName, password, deviceName, deviceToken, guid),
                        null, null, null, timeoutSeconds, skipDoh: true);
                });
        }

        internal BaseHttpResponse<object> CheckToken(string deviceToken)
        {
            return ExecuteWithFailover(
                Guid.NewGuid().ToString("N"),
                "CheckToken",
                (service, timeoutSeconds) =>
            {
                string url = $"api/check/token?device_token={Utils.UrlEncode(deviceToken)}";
                return service.SendRequest<object>(url, null, null, null, null, timeoutSeconds, skipDoh: true);
            },
                LoginRequestTimeoutSeconds);
        }

        internal BaseHttpResponse<DefaultPlainResponse<SettingInfo>> GetSettings()
        {
            string requestId = Guid.NewGuid().ToString("N");
            LogHelper.WriteExLog("[StartupAuth] stage=start flow=GetSettings requestId=" + requestId);

            return ExecuteWithFailover(
                requestId,
                "GetSettings",
                (service, timeoutSeconds) =>
            {
                string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                string url = $"api/version?type=windows&version_number={Utils.UrlEncode(version)}";
                var result = service.SendRequest<DefaultPlainResponse<SettingInfo>>(
                    url, null, null, null, null, timeoutSeconds, skipDoh: true);
                bool hasSettings = result != null && result.ResponseData != null && result.ResponseData.data != null;
                LogHelper.WriteExLog("[StartupAuth] stage=validate-response requestId=" + requestId
                    + " flow=GetSettings endpoint=" + GetBaseUrl(service)
                    + " status=" + GetResponseStatus(result)
                    + " has-settings=" + hasSettings);
                return result;
            },
                GetSettingsTimeoutSeconds,
                false);
        }

        // ==========================================================
        // Failover core (null or 500 -> try next endpoint)
        // ==========================================================

        private BaseHttpResponse<TResponse> ExecuteWithFailover<TResponse>(
            string requestId,
            string flowName,
            Func<RestHelper, int?, BaseHttpResponse<TResponse>> call)
        {
            return ExecuteWithFailover(
                requestId,
                flowName,
                call,
                LoginRequestTimeoutSeconds,
                true);
        }

        private BaseHttpResponse<TResponse> ExecuteWithFailover<TResponse>(
            string requestId,
            string flowName,
            Func<RestHelper, int?, BaseHttpResponse<TResponse>> call,
            int timeoutSeconds,
            bool allowFailover = true)
        {
            LogHelper.WriteExLog("[StartupAuth] stage=start requestId=" + requestId + " flow=" + flowName);

            int startIndex = _index;
            int attempts = 0;
            Exception lastException = null;
            int attemptNumber = 1;
            var flowStopwatch = Stopwatch.StartNew();
            long flowBudgetMs = timeoutSeconds > 0 ? timeoutSeconds * 1000L : -1;

            while (attempts < _services.Count)
            {
                var svc = _services[_index];
                string endpoint = GetBaseUrl(svc);
                int? perAttemptTimeout = null;
                if (flowBudgetMs > 0)
                {
                    long remainingMs = flowBudgetMs - flowStopwatch.ElapsedMilliseconds;
                    // The transport accepts whole seconds. Do not round up past
                    // the remaining budget or let one endpoint consume every retry.
                    if (remainingMs < 1000)
                        throw new TimeoutException(
                            "Authentication flow timeout budget exhausted: flow=" + flowName,
                            lastException);

                    int remainingEndpoints = allowFailover ? _services.Count - attempts : 1;
                    // Login must accommodate slow DNS/TLS and leave room for the
                    // managed transport retry. Fast responses still return immediately.
                    perAttemptTimeout = flowName == "Login"
                        ? Math.Min(LoginEndpointTimeoutSeconds, (int)(remainingMs / 1000))
                        : Math.Max(1, (int)(remainingMs / 1000 / remainingEndpoints));
                }

                var attemptSw = Stopwatch.StartNew();
                LogHelper.WriteExLog("[StartupAuth] stage=endpoint-start requestId=" + requestId
                    + " flow=" + flowName
                    + " attempt=" + attemptNumber
                    + " endpoint=" + endpoint
                    + " timeoutSec=" + (perAttemptTimeout.HasValue ? perAttemptTimeout.Value.ToString() : "")
                    + " flowRemainingMs="
                    + (flowBudgetMs > 0
                        ? Math.Max(0L, flowBudgetMs - flowStopwatch.ElapsedMilliseconds).ToString()
                        : ""));

                try
                {
                    var result = call(svc, perAttemptTimeout);
                    attemptSw.Stop();

                    bool retryable = IsRetriableFailure(result);
                    LogHelper.WriteExLog("[StartupAuth] stage=endpoint-end requestId=" + requestId
                        + " flow=" + flowName
                        + " attempt=" + attemptNumber
                        + " endpoint=" + endpoint
                        + " status=" + GetResponseStatus(result)
                        + " retryable=" + retryable
                        + " elapsedMs=" + attemptSw.ElapsedMilliseconds);

                    if (!retryable)
                    {
                        _lastGoodBaseUrl = GetBaseUrl(svc);
                        LogHelper.WriteExLog("[StartupAuth] stage=flow-end requestId=" + requestId
                            + " flow=" + flowName
                            + " endpoint=" + endpoint
                            + " result=success");
                        return result;
                    }

                    if (!allowFailover)
                    {
                        LogHelper.WriteExLog("[StartupAuth] stage=flow-end requestId=" + requestId
                            + " flow=" + flowName
                            + " endpoint=" + endpoint
                            + " result=single-endpoint-failed");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    attemptSw.Stop();
                    lastException = ex;
                    LogHelper.WriteExLog("[StartupAuth] stage=endpoint-end requestId=" + requestId
                        + " flow=" + flowName
                        + " attempt=" + attemptNumber
                        + " endpoint=" + endpoint
                        + " status=exception"
                        + " elapsedMs=" + attemptSw.ElapsedMilliseconds
                        + " exception=" + ex.GetType().FullName
                        + " hresult=0x" + ex.HResult.ToString("X8"));

                    if (!allowFailover)
                        throw;
                }

                _index = (_index + 1) % _services.Count;
                attempts++;
                attemptNumber++;

                if (_index == startIndex)
                    break;
            }

            if (flowBudgetMs > 0 && flowStopwatch.ElapsedMilliseconds >= flowBudgetMs)
            {
                throw new TimeoutException(
                    "Authentication flow timeout budget exhausted: flow=" + flowName,
                    lastException);
            }

            if (lastException != null)
            {
                // Rethrow with the original stack intact. Plain "throw lastException"
                // resets it, which is why a field report of this failing only showed the
                // failover frame and not the call that actually threw.
                ExceptionDispatchInfo.Capture(lastException).Throw();
            }

            throw new Exception("All service endpoints failed.");
        }

        private static bool IsRetriableFailure<TResponse>(BaseHttpResponse<TResponse> result)
        {
            if (result == null) return true;
            return result.StatusCode == HttpStatusCode.InternalServerError;
        }

        private static string GetResponseStatus<TResponse>(BaseHttpResponse<TResponse> response)
        {
            if (response == null)
                return "null";

            return response.StatusCode.ToString();
        }

        // ==========================================================
        // Small helpers (avoid duplicate code)
        // ==========================================================

        private static object BuildAuthPayload(
            string userName,
            string password,
            string deviceName,
            string deviceToken,
            string guid)
        {
            return new
            {
                username = userName,
                pwd = password,
                id = guid,
                device_name = deviceName,
                device_token = deviceToken,
                kv = ServerListKeyVersion
            };
        }

        private static bool IsValidEncryptedServerListResponse(
            BaseHttpResponse<DefaultEncryptedResponse<AccountInfoEx>> res,
            string guid)
        {
            return res != null
                && res.StatusCode == HttpStatusCode.OK
                && !string.IsNullOrEmpty(res.ResponseData?.data)
                && guid == res.ResponseData.Decrypted?.id;
        }

        // Best-effort: RestHelper likely has a base URL field/property;
        // if not, update this to match your RestHelper implementation.
        private static string GetBaseUrl(RestHelper helper)
        {
            return helper == null ? "<null>" : helper.BaseAddress;
        }
    }
}
