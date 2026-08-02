using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.WebServices;
using SimpleInjector;
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace IRSpeedyVPN
{
    static class Program
    {
        private static Mutex mutex;
        internal static Container container;

        private const string MutexName = "{DE02AF2D-7EF7-4604-926A-E0B9023BE634}";
        private const string ShowPipeName = "IRSpeedyVPN_ShowWindow";

        [STAThread]
        static void Main()
        {
            bool createdNew;
            mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                SignalFirstInstance();
                return;
            }

            try
            {
                var app = new App();
                app.InitializeComponent();

                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"/IRSpeedyVPN;component/Themes/DefaultThemeColor.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"/IRSpeedyVPN;component/Themes/DefaultTheme.xaml", UriKind.Relative) });

                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Themes/Theme.Button.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Themes/Theme.TextBox.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Themes/Theme.TextBoxButton.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Themes/Theme.ComboBox.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Themes/Theme.TextBlock.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Themes/Theme.LabelButton.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Themes/Theme.Progressbar.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Components/ToggleSwitch/Themes/Generic.xaml", UriKind.Relative) });

                //app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(@"Themes/ToggleSwitchStyles.xaml", UriKind.Relative) });

                container = Bootstrap();

                RunApplication(app, container);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex, true);
            }
        }

        private static void StartShowPipeServer()
        {
            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(
                            ShowPipeName,
                            PipeDirection.In,
                            1))
                        {
                            server.WaitForConnection();

                            server.ReadByte();

                            var dispatcher = Application.Current?.Dispatcher;

                            if (dispatcher != null && !dispatcher.HasShutdownStarted)
                            {
                                dispatcher.BeginInvoke((Action)(() =>
                                {
                                    var window = Application.Current?.MainWindow;

                                    if (window != null)
                                    {
                                        window.Visibility = Visibility.Visible;
                                        window.Show();
                                        window.WindowState = WindowState.Normal;
                                        window.Activate();
                                        window.Topmost = true;
                                        window.Topmost = false;
                                        window.Focus();
                                    }
                                }));
                            }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLog(ex, false);
                        Thread.Sleep(1000);
                    }
                }
            }, TaskCreationOptions.LongRunning);
        }

        private static void SignalFirstInstance()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(
                        ".",
                        ShowPipeName,
                        PipeDirection.Out))
                    {
                        client.Connect(1000);

                        client.WriteByte(1);
                        client.Flush();

                        return;
                    }
                }
                catch
                {
                    Thread.Sleep(300);
                }
            }
        }

        private static Container Bootstrap()
        {
            var container = new Container();

            container.Options.PropertySelectionBehavior =
                new ImportPropertySelectionBehavior();

            container.Register<UserControls.UCLogin, UserControls.UCLogin>(Lifestyle.Singleton);
            container.Register<UserControls.UCLoading, UserControls.UCLoading>(Lifestyle.Singleton);
            container.Register<UserControls.UCUserInfo, UserControls.UCUserInfo>(Lifestyle.Singleton);
            container.Register<UserControls.UCServerList, UserControls.UCServerList>(Lifestyle.Singleton);
            container.Register<UserControls.UCUpdate, UserControls.UCUpdate>(Lifestyle.Singleton);
            container.Register<UserControls.UCChangePassword, UserControls.UCChangePassword>(Lifestyle.Singleton);

            container.Register<NewServiceController, NewServiceController>(Lifestyle.Singleton);
            container.Register<IProxifier, Proxifier>(Lifestyle.Singleton);
            container.Register<ServiceFactory, ServiceFactory>(Lifestyle.Singleton);
            container.Register<PersianIsoNames, PersianIsoNames>(Lifestyle.Singleton);
            container.Register<GlobalInfo, GlobalInfo>(Lifestyle.Singleton);
            container.Register<ResourceManager, ResourceManager>(Lifestyle.Singleton);
            container.Register<JsonConverter, JsonConverter>(Lifestyle.Singleton);

            container.Register<MainWindow>(Lifestyle.Singleton);

            container.Verify();

            return container;
        }

        private static void RunApplication(App app, Container container)
        {
            try
            {
                var mainWindow = container.GetInstance<MainWindow>();

                // Start pipe server AFTER MainWindow exists
                StartShowPipeServer();

                app.Run(mainWindow);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex, true);
            }
        }
    }
}