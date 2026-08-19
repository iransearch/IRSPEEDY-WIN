using IRSpeedyVPN.Common;
using IRSpeedyVPN.Common.Json;
using IRSpeedyVPN.Interfaces;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Resource;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.WebServices;
using System;
using System.Collections.Generic;
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
        private static readonly object embeddedAssemblyLock = new object();
        private static readonly Dictionary<string, Assembly> embeddedAssemblies =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        private const string MutexName = "{DE02AF2D-7EF7-4604-926A-E0B9023BE634}";
        private const string ShowPipeName = "IRSpeedyVPN_ShowWindow";
        private const string EmbeddedAssemblyPrefix = "EmbeddedAssemblies.";

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

                AppServices.Initialize();

                RunApplication(app);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLog(ex, true);
            }
        }

        private static void RegisterEmbeddedAssemblyResolver()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
        }

        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            string assemblyName;
            try
            {
                assemblyName = new AssemblyName(args.Name).Name;
            }
            catch
            {
                return null;
            }

            lock (embeddedAssemblyLock)
            {
                if (embeddedAssemblies.TryGetValue(assemblyName, out var loaded))
                    return loaded;

                Assembly executing = Assembly.GetExecutingAssembly();
                string expectedName = EmbeddedAssemblyPrefix + assemblyName + ".dll";
                string resourceName = executing.GetManifestResourceNames().FirstOrDefault(n =>
                    string.Equals(n, expectedName, StringComparison.OrdinalIgnoreCase) ||
                    n.EndsWith("." + expectedName, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                    return null;

                using (Stream stream = executing.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        loaded = Assembly.Load(buffer.ToArray());
                        embeddedAssemblies[assemblyName] = loaded;
                        return loaded;
                    }
                }
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

        private static void RunApplication(App app)
        {
            try
            {
                var mainWindow = new MainWindow();

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
