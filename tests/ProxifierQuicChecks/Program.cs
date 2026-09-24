using IRSpeedyVPN;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

static class Program
{
    [DllImport("fwpuclnt.dll")] static extern void Reset(int failAt);
    [DllImport("fwpuclnt.dll")] static extern int Count(int which);
    [DllImport("fwpuclnt.dll")] static extern UIntPtr Layout(int which);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Wait(Func<bool> predicate) { Assert(SpinWait.SpinUntil(predicate, 5000), "Timed out"); }

    static void Main(string[] args)
    {
        var library = NativeLibrary.Load(Path.GetFullPath(args[0]));
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly,
            (name, assembly, path) => name == "fwpuclnt.dll" ? library : IntPtr.Zero);
        Assert(Marshal.SizeOf(typeof(BrowserQuicFilterSession.Session)) == (int)Layout(0), "Session ABI");
        Assert(Marshal.SizeOf(typeof(BrowserQuicFilterSession.Filter)) == (int)Layout(1), "Filter ABI");
        Assert(Marshal.SizeOf(typeof(BrowserQuicFilterSession.FilterCondition)) == (int)Layout(2), "Condition ABI");
        Assert((long)Marshal.OffsetOf(typeof(BrowserQuicFilterSession.Filter), "Context") == (long)Layout(3), "Context union alignment");
        Assert((long)Marshal.OffsetOf(typeof(BrowserQuicFilterSession.Filter), "Id") == (long)Layout(4), "Filter ID alignment");
        Reset(0);
        using (var session = new BrowserQuicFilterSession()) session.AddBrowser("/tmp/firefox.exe");
        Assert(Count(0)==1 && Count(1)==1 && Count(2)==2 && Count(3)==1 && Count(4)==0 && Count(5)==0, "Both IP families committed and session closed");
        Reset(2);
        bool failed = false;
        try { using (var session = new BrowserQuicFilterSession()) session.AddBrowser("/tmp/chrome.exe"); }
        catch (System.ComponentModel.Win32Exception) { failed = true; }
        Assert(failed && Count(1)==1 && Count(3)==0 && Count(4)==1, "IPv6 failure rolls back IPv4 and closes handle");
        foreach (string core in new[] { "SGuard64.exe", "SGuard764.exe", "hysteria.exe", "xray.exe", "sni.exe", "IRSpeedyVPN.exe", "msedgewebview2.exe", "*.exe", "" })
            Assert(!BrowserExecutableDiscovery.IsBrowser(core), "Never target core/non-browser " + core);
        Assert(BrowserExecutableDiscovery.ParseExecutable("\"C:\\Program Files\\Firefox\\firefox.exe\" -url https://example.org") == @"C:\Program Files\Firefox\firefox.exe", "Quoted browser command");
        Assert(BrowserExecutableDiscovery.IsBrowser(@"D:\Portable\FIREFOX.EXE"), "Portable browser");
        Assert(ProxifierBrowserQuic.AppliesTo(ProxifierType.Global) && ProxifierBrowserQuic.AppliesTo(ProxifierType.Normal), "Browser proxy modes");
        Assert(!ProxifierBrowserQuic.AppliesTo(ProxifierType.Telegram) && !ProxifierBrowserQuic.AppliesTo(ProxifierType.None), "No filtering in Telegram/System Proxy");

        string root = Path.Combine(Path.GetTempPath(), "irspeedy-quic-checks-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "Proxifier/Profiles"));
        AppServices.GlobalInfo.TempPath = root;
        var context = new QueueContext();
        SynchronizationContext.SetSynchronizationContext(context);
        var proxy = new Proxifier();
        int success=0, errors=0;
        proxy.onResult += (ok, error) => { if (ok) success++; else errors++; };
        try
        {
            Reset(0);
            proxy.Attach("127.0.0.1",1080,null,null,ProxyType.SOCKS,ProxifierType.Global);
            Wait(() => proxy.IsAttached() && context.Pending);
            context.Drain();
            Assert(success==1 && Count(0)==1, "Connected with dynamic session");
            proxy.Detach();
            proxy.Detach();
            Assert(!proxy.IsAttached() && Count(1)==1, "Repeated detach closes once");

            Reset(0);
            proxy.Attach("127.0.0.1",1080,null,null,ProxyType.SOCKS,ProxifierType.Global);
            Wait(() => context.Pending);
            proxy.Detach(); // result is already queued on the UI context
            context.Drain();
            Assert(success==1 && Count(1)==1, "Cancelled connection cannot deliver stale success");

            Reset(0);
            ShellExecute.FailLaunch=true;
            proxy.Attach("127.0.0.1",1080,null,null,ProxyType.SOCKS,ProxifierType.Global);
            Wait(() => context.Pending);
            context.Drain();
            Assert(errors==1 && Count(0)==1 && Count(1)==1, "Launch failure releases filters");
            ShellExecute.FailLaunch=false;

            Reset(0);
            proxy.Attach("127.0.0.1",1080,null,null,ProxyType.SOCKS,ProxifierType.Global);
            Wait(() => proxy.IsAttached() && context.Pending);
            context.Drain();
            ShellExecute.LastProcess.Kill();
            Wait(() => context.Pending);
            context.Drain();
            Assert(errors==2 && !proxy.IsAttached() && Count(1)==1, "Unexpected Proxifier exit removes filters");

            Reset(0);
            proxy.Attach("127.0.0.1",1080,null,null,ProxyType.SOCKS,ProxifierType.Telegram);
            Wait(() => proxy.IsAttached() && context.Pending);
            context.Drain();
            proxy.Detach();
            Assert(Count(0)==0, "Telegram-only does not open a filter session");
        }
        finally { proxy.Detach(); Directory.Delete(root,true); }
        Console.WriteLine("PASS: native ABI/scoping, IPv4+IPv6 transaction rollback, core exclusions, mode gating, cancellation, launch failure and exit cleanup.");
    }

    sealed class QueueContext : SynchronizationContext
    {
        readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        public bool Pending => !queue.IsEmpty;
        public override void Post(SendOrPostCallback callback, object state) { queue.Enqueue(() => callback(state)); }
        public void Drain() { Action action; while (queue.TryDequeue(out action)) action(); }
    }
}
