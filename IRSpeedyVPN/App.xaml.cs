
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;
using v2rayN;
using IRSpeedyVPN.Resource;

namespace IRSpeedyVPN
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            // Normal Exit already awaited bounded cleanup. Do not repeat blocking
            // teardown here after the dispatcher is starting to shut down.
            if (!IRSpeedyVPN.MainWindow.ExitCleanupStarted)
            {
                Services.TunnelPlusService.BeginApplicationExit();
                Services.Hotspot.DirectHotspot.StopPollingForExit();
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { AppServices.Proxifier?.Detach(); } catch { }
                    try { Services.Hotspot.DirectHotspot.Controller.Stop(); } catch { }
                });
                Services.TunnelPlusService.StopOwnedProcessesForExit();
            }
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {

            base.OnStartup(e);
          
        }
    }
}

