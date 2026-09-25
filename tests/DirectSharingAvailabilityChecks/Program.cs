using System;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Services.Hotspot;
using IRSpeedyVPN.Windows;

namespace System.Windows { public enum Visibility { Visible, Collapsed } }
namespace IRSpeedyVPN.Services.Hotspot
{
    internal static class HotspotProcessChannel
    { internal static bool SupportedWindows => false; internal static bool Installed => true; }
    internal sealed class HotspotView
    { internal string State = "off", Error = ""; }
}
namespace IRSpeedyVPN.Windows
{
    // Compile the production UI application method against tiny WPF substitutes.
    public partial class ShareVPNSetting
    {
        internal sealed class Element
        { internal bool IsEnabled; internal string Text; internal System.Windows.Visibility Visibility; }
        internal Element DirectTab = new Element(), DirectTabLock = new Element(),
            DirectAvailabilityText = new Element(), DirectAvailabilityNotice = new Element(),
            DirectPanel = new Element { Visibility = System.Windows.Visibility.Collapsed };
        internal bool ProxySelected = true;
        private void SelectTab(bool direct)
        { ProxySelected = !direct; DirectPanel.Visibility = direct ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; }
        internal void Apply(HotspotView view) => ApplyDirectAvailability(view);
    }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static async Task Until(Func<bool> condition)
    { for (int i = 0; i < 1000 && !condition(); i++) await Task.Delay(2); if (!condition()) throw new Exception("Worker did not settle"); }
    private static void Main() => Run().GetAwaiter().GetResult();
    private static async Task Run()
    {
        Func<string> forbidden = () => { throw new Exception("probe must not run"); };
        Check(DirectSharingAvailability.Check(false, true, forbidden) == DirectSharingSupport.WindowsUnsupported,
            "legacy Windows blocks feature before any hardware probe");
        Check(DirectSharingAvailability.Check(true, false, forbidden) == DirectSharingSupport.PayloadMissing,
            "missing payload blocks feature before any hardware probe");
        Check(DirectSharingAvailability.Parse("Wi-Fi Direct GO : Supported") == DirectSharingSupport.Supported, "GO capability enables tab");
        Check(DirectSharingAvailability.Parse("Wi-Fi Direct GO : Not supported") == DirectSharingSupport.HardwareUnsupported, "negative is not mistaken for supported");
        Check(DirectSharingAvailability.Parse("Hosted network supported : Yes\nWi-Fi Direct Device : Supported\nWi-Fi Direct Client : Supported") == DirectSharingSupport.Unknown,
            "Hosted Network, client or device alone do not prove GO capability");
        Check(DirectSharingAvailability.Parse("Wi-Fi Direct GO : Not supported\nWi-Fi Direct GO : Supported") == DirectSharingSupport.Supported,
            "a compatible second adapter enables sharing");
        Check(DirectSharingAvailability.Parse("Wi-Fi Direct GO : Not supported\nWi-Fi Direct GO : Unknown") == DirectSharingSupport.Unknown,
            "unknown adapter prevents false system-wide hardware rejection");
        Check(DirectSharingAvailability.Parse("Wi-Fi Direct GO : nicht unterstützt") == DirectSharingSupport.HardwareUnsupported, "localized negative parsed exactly");
        Check(DirectSharingAvailability.Parse("Wi-Fi Direct GO：支持") == DirectSharingSupport.Supported, "full-width colon and localized support");
        Check(DirectSharingAvailability.Parse("Wi-Fi Direct GO : future-value") == DirectSharingSupport.Unknown, "unknown output fails closed without hardware claim");
        Check(DirectSharingAvailability.Parse("The Wireless AutoConfig Service is not running") == DirectSharingSupport.Unknown, "stopped service is not unsupported hardware");
        Check(DirectSharingAvailability.Parse("") == DirectSharingSupport.Unknown, "no adapters/empty report remains unknown");
        Check(DirectSharingAvailability.Check(true, true, () => { throw new TimeoutException(); }) == DirectSharingSupport.Unknown, "query timeout fails closed");
        foreach (DirectSharingSupport status in Enum.GetValues(typeof(DirectSharingSupport)))
            Check(DirectSharingAvailability.CanSelect(status, "off", "") == (status == DirectSharingSupport.Supported), "idle selection policy: " + status);
        Check(DirectSharingAvailability.CanSelect(DirectSharingSupport.Unknown, "active", ""), "live session keeps Stop accessible");
        Check(DirectSharingAvailability.CanSelect(DirectSharingSupport.Unknown, "error", "cleanup-not-confirmed"), "failed cleanup keeps recovery accessible");
        Check(!DirectSharingAvailability.CanSelect(DirectSharingSupport.WindowsUnsupported, "active", ""), "Windows exclusion always wins");

        var session = new DirectSharingSession(); int calls = 0;
        session.BeginLogin(() => { Interlocked.Increment(ref calls); return DirectSharingSupport.Supported; });
        await Until(() => session.Current == DirectSharingSupport.Supported);
        for (int i = 0; i < 100; i++) { var cached = session.Current; }
        Check(calls == 1, "UI ticks and repeated reads do not repeat the login probe");
        session.BeginLogin(() => { Interlocked.Increment(ref calls); return DirectSharingSupport.HardwareUnsupported; });
        await Until(() => session.Current == DirectSharingSupport.HardwareUnsupported);
        Check(calls == 2, "next login performs exactly one new check");
        using (var release = new ManualResetEventSlim())
        using (var entered = new ManualResetEventSlim())
        using (var finished = new ManualResetEventSlim())
        {
            session.BeginLogin(() => { entered.Set(); release.Wait(); finished.Set(); return DirectSharingSupport.HardwareUnsupported; });
            await Until(() => entered.IsSet);
            Check(session.Current == DirectSharingSupport.Checking, "login probe runs asynchronously");
            session.BeginLogin(() => DirectSharingSupport.Supported);
            await Until(() => session.Current == DirectSharingSupport.Supported);
            release.Set(); await Until(() => finished.IsSet); await Task.Delay(10);
            Check(session.Current == DirectSharingSupport.Supported, "old login result cannot overwrite newer login");
        }

        var window = new ShareVPNSetting(); var view = new HotspotView();
        DirectSharingProbe.Session.BeginLogin(() => DirectSharingSupport.HardwareUnsupported);
        await Until(() => DirectSharingProbe.Session.Current == DirectSharingSupport.HardwareUnsupported);
        window.Apply(view);
        Check(!window.DirectTab.IsEnabled && window.ProxySelected && window.DirectTabLock.Visibility == System.Windows.Visibility.Visible,
            "unsupported device disables actual tab and shows lock while proxy remains selected");
        Check(window.DirectAvailabilityText.Text.Contains("پشتیبانی نمی‌کند"), "unsupported reason shown outside disabled tab");
        DirectSharingProbe.Session.BeginLogin(() => DirectSharingSupport.Supported);
        await Until(() => DirectSharingProbe.Session.Current == DirectSharingSupport.Supported); window.Apply(view);
        Check(window.DirectTab.IsEnabled && window.DirectAvailabilityNotice.Visibility == System.Windows.Visibility.Collapsed,
            "supported device enables tab and hides restriction");
        window.DirectPanel.Visibility = System.Windows.Visibility.Visible; window.ProxySelected = false;
        DirectSharingProbe.Session.BeginLogin(() => DirectSharingSupport.Unknown);
        await Until(() => DirectSharingProbe.Session.Current == DirectSharingSupport.Unknown); window.Apply(view);
        Check(!window.DirectTab.IsEnabled && window.ProxySelected && window.DirectPanel.Visibility == System.Windows.Visibility.Collapsed,
            "unavailable selected tab falls back to proxy");
        view.State = "active"; window.Apply(view);
        Check(window.DirectTab.IsEnabled, "window preserves controls to stop existing session");
        Console.WriteLine(checks + " checks passed.");
    }
}
