using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.UserControls;
using IRSpeedyVPN.WebServices;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IRSpeedyVPN
{
    /// <summary>
    /// Simple, obfuscation-safe service locator. UI-facing dependencies are created on
    /// the startup thread, while ResourceManager (runtime validation + device fingerprint)
    /// is intentionally created in the background so the login window can paint first.
    /// </summary>
    static class AppServices
    {
        private static readonly object initLock = new object();
        private static readonly ManualResetEventSlim resourceManagerReady = new ManualResetEventSlim(false);
        private static bool fastInitialized;
        private static ResourceManager resourceManager;
        private static Exception resourceManagerError;
        private static Task deferredInitialization;

        public static GlobalInfo GlobalInfo { get; private set; }

        public static ResourceManager ResourceManager
        {
            get
            {
                ResourceManager ready;
                lock (initLock)
                {
                    ready = resourceManager;
                    if (ready != null)
                        return ready;
                }

                // Any late consumer is allowed to request the deferred initializer. The
                // wait is expected to happen only on worker threads during login/connect;
                // Program starts the task before the main window is shown.
                BeginDeferredInitialize();
                resourceManagerReady.Wait();

                lock (initLock)
                {
                    if (resourceManagerError != null)
                        throw new InvalidOperationException(
                            "IRSpeedy runtime initialization failed.", resourceManagerError);
                    return resourceManager;
                }
            }
        }

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

        /// <summary>
        /// Creates only objects needed to construct and display MainWindow/Login.
        /// ResourceManager is deliberately excluded because its constructor performs
        /// runtime-state I/O and the hardware fingerprint WMI scan.
        /// </summary>
        public static void InitializeFast()
        {
            lock (initLock)
            {
                if (fastInitialized)
                    return;

                GlobalInfo = new GlobalInfo();
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

                fastInitialized = true;
            }
        }

        /// <summary>
        /// Starts the slow startup work once. It is safe to call repeatedly.
        /// </summary>
        public static Task BeginDeferredInitialize()
        {
            InitializeFast();

            lock (initLock)
            {
                if (deferredInitialization != null)
                    return deferredInitialization;

                deferredInitialization = Task.Run(() =>
                {
                    try
                    {
                        var manager = new ResourceManager();
                        lock (initLock)
                            resourceManager = manager;
                    }
                    catch (Exception ex)
                    {
                        lock (initLock)
                            resourceManagerError = ex;
                    }
                    finally
                    {
                        resourceManagerReady.Set();
                    }
                });

                return deferredInitialization;
            }
        }

        /// <summary>
        /// Compatibility entry point for code/tests that require the old synchronous
        /// fully-initialized behavior.
        /// </summary>
        public static void Initialize()
        {
            InitializeFast();
            BeginDeferredInitialize().GetAwaiter().GetResult();

            // Surface a deferred initialization failure to synchronous callers.
            var unused = ResourceManager;
        }
    }
}
