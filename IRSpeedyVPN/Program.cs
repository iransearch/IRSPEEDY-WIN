using IRSpeedyVPN.Common;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace IRSpeedyVPN
{
    static class Program
    {
        private static Mutex mutex;

        private const string MutexName = "{DE02AF2D-7EF7-4604-926A-E0B9023BE634}";
        private const string ShowPipeName = "IRSpeedyVPN_ShowWindow";

        [STAThread]
        static void Main()
        {
            RegisterEmbeddedAssemblyResolver();

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

                // App.xaml already loads the base/theme dictionaries. Keep only the two
                // dictionaries that are not declared there; loading every theme twice was
                // unnecessary work on the critical startup path.
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(@"Themes/Theme.Progressbar.xaml", UriKind.Relative)
                });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(@"Components/ToggleSwitch/Themes/Generic.xaml", UriKind.Relative)
                });

                // Construct only dependencies required for the first visible Login frame.
                AppServices.InitializeFast();

                // Runtime validation, filesystem cleanup and SysThumbPrint/WMI now run in
                // parallel with window construction instead of blocking first paint.
                AppServices.BeginDeferredInitialize();

                RunApplication(app);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex, true);
            }
        }

        private static void RegisterEmbeddedAssemblyResolver()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var assemblyName = new AssemblyName(args.Name).Name;

                if (assemblyName != "Transitionals")
                    return null;

                Assembly executing = Assembly.GetExecutingAssembly();
                string resourceName = executing.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Transitionals.dll", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                    return null;

                using (Stream stream = executing.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);
                    return Assembly.Load(data);
                }
            };
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

        private static void RunApplication(App app)
        {
            try
            {
                var mainWindow = new MainWindow();

                // Suppress the old Activated-time heavy path and put the Login control in
                // place before the first Show. Deferred work resumes after first render.
                mainWindow.PrepareFastStartup();

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
