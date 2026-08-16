using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.UserControls;
using IRSpeedyVPN.WebServices;

namespace IRSpeedyVPN
{
    /// <summary>
    /// Central, statically reachable service/control locator for the WPF app.
    /// Must be initialized once on the UI thread from Program.Main before the
    /// main window is created (the user controls call InitializeComponent).
    /// </summary>
    internal static class AppServices
    {
        public static GlobalInfo GlobalInfo { get; private set; }
        public static ResourceManager ResourceManager { get; private set; }
        public static JsonConverter JsonConverter { get; private set; }
        public static PersianIsoNames PersianIsoNames { get; private set; }
        public static NewServiceController NewServiceController { get; private set; }
        public static ServiceFactory ServiceFactory { get; private set; }
        public static Proxifier Proxifier { get; private set; }

        public static UCLogin UCLogin { get; private set; }
        public static UCServerList UCServerList { get; private set; }
        public static UCUserInfo UCUserInfo { get; private set; }
        public static UCUpdate UCUpdate { get; private set; }
        public static UCChangePassword UCChangePassword { get; private set; }

        public static bool IsInitialized { get; private set; }

        public static void Initialize()
        {
            if (IsInitialized)
                return;

            GlobalInfo = new GlobalInfo();
            ResourceManager = new ResourceManager();
            JsonConverter = new JsonConverter();
            PersianIsoNames = new PersianIsoNames();
            NewServiceController = new NewServiceController();
            ServiceFactory = new ServiceFactory();
            Proxifier = new Proxifier();

            UCLogin = new UCLogin();
            UCServerList = new UCServerList();
            UCUserInfo = new UCUserInfo();
            UCUpdate = new UCUpdate();
            UCChangePassword = new UCChangePassword();

            IsInitialized = true;
        }
    }
}
