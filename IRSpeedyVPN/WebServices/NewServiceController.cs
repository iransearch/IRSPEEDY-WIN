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

        private readonly List<RestHelper> _services;
        private readonly object _failoverLock = new object();
        private static string _lastGoodBaseUrl;
        private int _index;

        private readonly RestHelper _geoIpService;
        private readonly TripleDesHelper _tdes;

        public NewServiceController()
        {
            _services = new List<RestHelper>
            {
                new RestHelper("https://api3.greadia.ir/"),
                new RestHelper("https://api1.greadia.app/"),
                new RestHelper("https://api2.greadia.app/")
            };

            if (!string.IsNullOrWhiteSpace(_lastGoodBaseUrl))
            {
                var idx = _services.FindIndex(s => string.Equals(
                    s.BaseAddress,
                    _lastGoodBaseUrl,
                    StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    _index = idx;
            }

            _geoIpService = new RestHelper("http://ip-api.com/json/");
            _tdes = new TripleDesHelper("J816KWWeG4Q69p72U6ueXu3W".ToAsciiBytes());
        }

        internal BaseHttpResponse<DefaultEncryptedResponse<AccountInfoEx>> Login2(
            string username,
            string password)
        {
            return getServerList(
                username,
                password,
                SysThumbPrint.GetComputerName(),
                SysThumbPrint.ValueString());
        }

        internal BaseHttpResponse<ChangePasswordResult> ChangePassword(
            string username,
            string oldPassword,
            string newPassword)
        {
            var url =
                $"change_password.php?username={Utils.UrlEncode(username)}" +
                $"&old_password={Utils.UrlEncode(oldPassword)}" +
                $"&new_password={Utils.UrlEncode(newPassword)}" +
                $"&key={ChangePasswordKey}";

            return ExecuteWithFailover(svc =>
                svc.SendRequest<ChangePasswordResult>(url, null, null, null));
        }

        internal BaseHttpResponse<GeoIp> GetIpInfo()
        {
            return _geoIpService.SendRequest<GeoIp>("", null, null, null);
        }

        internal bool CheckUserPermission(string username, string password)
        {
            return true;
        }

        internal BaseHttpResponse<DefaultEncryptedResponse<AccountInfoEx>> getServerList(
            string userName,
            string password,
            string deviceName,
            string deviceToken)
        {
            return ExecuteWithFailover(service =>
            {
                var guid = Guid.NewGuid().ToString();
                var res = service.SendRequest<DefaultEncryptedResponse<AccountInfoEx>>(
                    "api/server/list/all",
                    BuildAuthPayload(userName, password, deviceName, deviceToken, guid),
                    null,
                    null);

                if (IsValidEncryptedServerListResponse(res, guid))
                    return res;
                if (res?.ResponseData?.message != null)
                    return res;
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
                var guid = Guid.NewGuid().ToString();
                return service.SendRequest<DefaultPlainResponse<AccountInfoEx>>(
                    "api/remove/token",
                    BuildAuthPayload(userName, password, deviceName, deviceToken, guid),
                    null,
                    null);
            });
        }

        internal BaseHttpResponse<object> CheckToken(string deviceToken)
        {
            return ExecuteWithFailover(service =>
            {
                var url = $"api/check/token?device_token={Utils.UrlEncode(deviceToken)}";
                return service.SendRequest<object>(url, null, null, null, null);
            });
        }

        internal BaseHttpResponse<DefaultPlainResponse<SettingInfo>> GetSettings()
        {
            return ExecuteWithFailover(service =>
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                var url = $"api/version?type=windows&version_number={Utils.UrlEncode(version)}";
                return service.SendRequest<DefaultPlainResponse<SettingInfo>>(url, null, null, null, null);
            });
        }

        private BaseHttpResponse<TResponse> ExecuteWithFailover<TResponse>(
            Func<RestHelper, BaseHttpResponse<TResponse>> call)
        {
            if (call == null)
                throw new ArgumentNullException(nameof(call));

            lock (_failoverLock)
            {
                var startIndex = _index;
                var attempts = 0;
                Exception lastException = null;

                while (attempts < _services.Count)
                {
                    var service = _services[_index];
                    try
                    {
                        var result = call(service);
                        if (!IsRetriableFailure(result))
                        {
                            _lastGoodBaseUrl = service.BaseAddress;
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
        }

        private static bool IsRetriableFailure<TResponse>(BaseHttpResponse<TResponse> result)
        {
            if (result == null)
                return true;

            return (int)result.StatusCode == 0
                || result.StatusCode == HttpStatusCode.RequestTimeout
                || result.StatusCode == HttpStatusCode.InternalServerError
                || result.StatusCode == HttpStatusCode.BadGateway
                || result.StatusCode == HttpStatusCode.ServiceUnavailable
                || result.StatusCode == HttpStatusCode.GatewayTimeout;
        }

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
    }
}
