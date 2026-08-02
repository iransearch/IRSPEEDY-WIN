using IRSpeedyVPN.Common;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Models.Services;
using IRSpeedyVPN.Security;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using v2rayN;

namespace IRSpeedyVPN.WebServices
{
    public class NewServiceController
    {
        private const string ChangePasswordKey = "5LCzP4gMSpZ5nMMmuCXnkWJwwGWgEWcJ";

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
                new RestHelper("https://api3.greadia.ir/"),
                new RestHelper("https://api1.greadia.app/"),
                new RestHelper("https://api2.greadia.app/")
                
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

            return ExecuteWithFailover(svc => svc.SendRequest<ChangePasswordResult>(url, null, null, null));
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
            return ExecuteWithFailover(service =>
            {
                string guid = Guid.NewGuid().ToString();

                var res = service.SendRequest<DefaultEncryptedResponse<AccountInfoEx>>(
                    "api/server/list/all",
                    BuildAuthPayload(userName, password, deviceName, deviceToken, guid),
                    null, null);

                // Keep your original validation:
                if (IsValidEncryptedServerListResponse(res, guid))
                    return res;

                // If server returned a meaningful message, return it (do not retry)
                if (res?.ResponseData?.message != null)
                    return res;

                // Otherwise treat as retryable
                return null;
            });
        }

        internal BaseHttpResponse<DefaultPlainResponse<AccountInfoEx>> RemoveToken(
            string userName,
            string password,
            string deviceName,
            string deviceToken)
        {
            return ExecuteWithFailover(service =>
            {
                string guid = Guid.NewGuid().ToString();

                return service.SendRequest<DefaultPlainResponse<AccountInfoEx>>(
                    "api/remove/token",
                    BuildAuthPayload(userName, password, deviceName, deviceToken, guid),
                    null, null);
            });
        }

        internal BaseHttpResponse<object> CheckToken(string deviceToken)
        {
            return ExecuteWithFailover(service =>
            {
                string url = $"api/check/token?device_token={Utils.UrlEncode(deviceToken)}";
                return service.SendRequest<object>(url, null, null, null, null);
            });
        }

        internal BaseHttpResponse<DefaultPlainResponse<SettingInfo>> GetSettings()
        {
            return ExecuteWithFailover(service =>
            {
                string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                string url = $"api/version?type=windows&version_number={Utils.UrlEncode(version)}";
                return service.SendRequest<DefaultPlainResponse<SettingInfo>>(url, null, null, null, null);
            });
        }

        // ==========================================================
        // Failover core (null or 500 -> try next endpoint)
        // ==========================================================

        private BaseHttpResponse<TResponse> ExecuteWithFailover<TResponse>(
            Func<RestHelper, BaseHttpResponse<TResponse>> call)
        {
            int startIndex = _index;
            int attempts = 0;
            Exception lastException = null;

            while (attempts < _services.Count)
            {
                var svc = _services[_index];

                try
                {
                    var result = call(svc);

                    if (!IsRetriableFailure(result))
                    {
                        _lastGoodBaseUrl = GetBaseUrl(svc);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                _index = (_index + 1) % _services.Count;
                attempts++;

                if (_index == startIndex)
                    break;
            }

            throw lastException ?? new Exception("All service endpoints failed.");
        }

        private static bool IsRetriableFailure<TResponse>(BaseHttpResponse<TResponse> result)
        {
            if (result == null) return true;
            return result.StatusCode == HttpStatusCode.InternalServerError;
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
                device_token = deviceToken
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
            // Common patterns are BaseUrl / BaseAddress / Url / Host, etc.
            // Replace with the real property name if available.
            var prop = helper.GetType().GetProperty("BaseUrl") ?? helper.GetType().GetProperty("BaseAddress");
            return prop?.GetValue(helper)?.ToString() ?? helper.ToString();
        }
    }
}
