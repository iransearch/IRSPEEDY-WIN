using System;
using System.Net;
using System.Text;
using System.Threading;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models.NewService;

namespace IRSpeedyVPN.Interfaces
{
    interface IEncryptor { byte[] Encrypt(byte[] value); }
    interface IDecryptor { byte[] Decrypt(byte[] value); }
}
namespace IRSpeedyVPN.Common.Json { class NamespaceMarker { } }
namespace IRSpeedyVPN.Common
{
    static class FixtureExtensions
    {
        internal static byte[] ToAsciiBytes(this string value) => Encoding.ASCII.GetBytes(value);
        internal static byte[] ToUTF8Bytes(this string value) => Encoding.UTF8.GetBytes(value);
        internal static string ToUTF8String(this byte[] value) => Encoding.UTF8.GetString(value);
    }
    static class SysThumbPrint
    { internal static string GetComputerName() => "test-device"; internal static string ValueString() => "test-token"; }
}
namespace IRSpeedyVPN.Models { class GeoIp { } }
namespace IRSpeedyVPN.Models.Services { class NamespaceMarker { } }
namespace IRSpeedyVPN.Models.NewService
{
    class AccountInfoEx { public string id; }
    class SettingInfo { }
}
namespace IRSpeedyVPN.Security { class TripleDesHelper { internal TripleDesHelper(byte[] value) { } } }
namespace v2rayN { static class Utils { internal static string UrlEncode(string value) => Uri.EscapeDataString(value); } }
namespace IRSpeedyVPN.WebServices
{
    class NewServiceDecryptor
    {
        internal T DecryptFromBase64<T>(string value) => default(T);
        internal string DecryptFromBase64String(string value) => "";
    }
    class ChangePasswordResult { }
    static class PasswordChangeClient
    {
        internal static BaseHttpResponse<ChangePasswordResult> Send(string user, string oldPassword, string newPassword)
            => throw new NotImplementedException();
    }
#if API_FLOW_TEST
    // Transport boundary only; the complete production controller, context,
    // result types and authorization-status validation are compiled unchanged.
    sealed class RestHelper
    {
        internal static Func<string, object, LoginNetworkContext, object> Handler;
        internal static int? LastTimeoutSeconds;
        internal readonly string BaseAddress;
        internal RestHelper(string address) { BaseAddress = address; }
        internal BaseHttpResponse<T> SendRequest<T>(string path, object data, IEncryptor encryptor, IDecryptor decryptor,
            string token = null, int? timeoutSeconds = null, bool skipDoh = false, LoginNetworkContext loginContext = null)
        {
            LastTimeoutSeconds = timeoutSeconds;
            return (BaseHttpResponse<T>)Handler(BaseAddress, data, loginContext);
        }
    }
#endif
}
