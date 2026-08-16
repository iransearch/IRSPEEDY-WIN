using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.UserControls;
using IRSpeedyVPN.WebServices;

namespace IRSpeedyVPN
{
    /// <summary>
    /// Simple, obfuscation-safe service locator. All dependencies are singletons
    /// created explicitly (no reflection / attribute-based injection), so
    /// Dotfuscator renaming cannot break resolution.
    /// </summary>
    static class AppServices
    {
        public static GlobalInfo GlobalInfo { get; private set; }
        public static ResourceManager ResourceManager { get; private set; }
        public static JsonConverter JsonConverter { get; private set; }
        public static PersianIsoNames PersianIsoNames { get; private set; }
        public static NewServiceController NewServiceController { get; private set; }
        public static IProxifier Proxifier { get; private set; }
        public static ServiceFactory ServiceFactory { get; private set; }

        public static UCLogin UCLogin { get; private set; }
        public static UCLoading UCLoading { get; private set; }
        public static UCUserInfo UCUserInfo { get; private set; }
        public static UCServerList UCServerList { get; private set; }
        public static UCUpdate UCUpdate { get; private set; }
        public static UCChangePassword UCChangePassword { get; private set; }

        public static void Initialize()
        {
            GlobalInfo = new GlobalInfo();
            ResourceManager = new ResourceManager();
            JsonConverter = new JsonConverter();
            PersianIsoNames = new PersianIsoNames();
            NewServiceController = new NewServiceController();
            Proxifier = new Proxifier();
            ServiceFactory = new ServiceFactory();

            UCLogin = new UCLogin();
            UCLoading = new UCLoading();
            UCUserInfo = new UCUserInfo();
            UCServerList = new UCServerList();
            UCUpdate = new UCUpdate();
            UCChangePassword = new UCChangePassword();
        }
    }
}
