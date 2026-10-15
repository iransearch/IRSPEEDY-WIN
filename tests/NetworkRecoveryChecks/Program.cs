using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Diagnostics;
using IRSpeedyVPN.Services;
using IRSpeedyVPN.Services.Libcore;

internal static class Program
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(10); }
        throw new Exception("Timed out waiting for test state");
    }
    private static async Task<byte[]> Frame(NetworkStream stream)
    {
        int length=0;
        for(int shift=0;shift<28;shift+=7)
        {
            var one=new byte[1];if(await stream.ReadAsync(one,0,1)!=1)throw new EndOfStreamException();
            length|=(one[0]&127)<<shift;
            if((one[0]&128)!=0)continue;
            var data=new byte[length];int offset=0;
            while(offset<length){int n=await stream.ReadAsync(data,offset,length-offset);if(n==0)throw new EndOfStreamException();offset+=n;}
            return data;
        }
        throw new IOException();
    }
    private static async Task ActionRequest(NetworkStream stream,string action)
    {
        var header=await Frame(stream);var body=await Frame(stream);
        Check(Encoding.UTF8.GetString(header).Contains("LibcoreService.AutoSelectorAction"),"Wrong recovery RPC method");
        string text=Encoding.UTF8.GetString(body);
        Check(text.Contains("proxy")&&text.Contains(action)&&!text.Contains("ai-proxy"),"Availability acted on wrong pool/action");
    }
    private static byte[] TextField(int field,string value) => BytesField(field,Encoding.UTF8.GetBytes(value));
    private static byte[] BytesField(int field,byte[] value)
    {
        var bytes=new List<byte>(); bytes.Add((byte)((field<<3)|2));
        int size=value.Length;
        while(size>=128){bytes.Add((byte)((size&127)|128));size>>=7;}
        bytes.Add((byte)size); bytes.AddRange(value); return bytes.ToArray();
    }
    private static QueryConnectionsResponse Snapshot(string source,string domain)
    {
        var connection=new List<byte>();
        connection.AddRange(TextField(1,"probe"));connection.AddRange(TextField(6,"tcp"));
        connection.AddRange(TextField(9,domain));connection.AddRange(TextField(14,source));
        connection.AddRange(TextField(12,"smart-proxy-1"));connection.AddRange(TextField(12,"proxy"));
        return LibcoreProto.DecodeQueryConnections(BytesField(1,connection.ToArray()));
    }
    static async Task Main()
    {
        // TLS success is required by the caller before this route lookup. The
        // log's working fallback had zero qualified members and old probe data;
        // neither is a reason to leave a proven live main-path TLS in recovery.
        var connections = Snapshot("127.0.0.1:34567","www.microsoft.com");
        var flow = connections.Active[0];
        Check(MainPoolRecoveryProof.Confirmed(connections,34567), "Live main TLS flow was rejected without qualified pool samples");
        Check(!MainPoolRecoveryProof.Confirmed(connections,34568), "An unrelated main flow claimed recovery");
        Check(!MainPoolRecoveryProof.Confirmed(connections,0), "Missing source port claimed recovery");
        connections.Active.Clear(); connections.Closed.Add(flow);
        Check(!MainPoolRecoveryProof.Confirmed(connections,34567), "An old closed flow claimed recovery");
        connections.Active.Add(flow);
        flow.Chain.Clear(); flow.Chain.Add("ai-proxy");
        Check(!MainPoolRecoveryProof.Confirmed(connections,34567), "AI-only TLS claimed main recovery");
        flow.Chain.Add("proxy");
        Check(!MainPoolRecoveryProof.Confirmed(connections,34567), "AI/main mixed route claimed main recovery");
        flow.Chain.Clear(); flow.Chain.Add("direct");
        Check(!MainPoolRecoveryProof.Confirmed(connections,34567), "Direct TLS claimed VPN recovery");
        flow.Chain.Add("proxy");
        Check(!MainPoolRecoveryProof.Confirmed(connections,34567), "Direct/main mixed route claimed VPN recovery");
        flow.Chain.Clear(); flow.Chain.Add("proxy"); flow.IsRecoveryTarget=false;
        Check(!MainPoolRecoveryProof.Confirmed(connections,34567), "Unrelated target claimed recovery");
        flow.IsRecoveryTarget=true; flow.Network="udp";
        Check(!MainPoolRecoveryProof.Confirmed(connections,34567), "UDP traffic matched TCP/TLS proof");
        Check(!MainPoolRecoveryProof.Confirmed(null,34567), "Unavailable Core telemetry claimed recovery");
        Check(!MainPoolRecoveryProof.Confirmed(Snapshot("203.0.113.8:34567","www.microsoft.com"),34567), "Remote client source matched a local TLS probe");
        Check(!MainPoolRecoveryProof.Confirmed(Snapshot("127.0.0.1:34567","www.microsoft.com.evil.invalid"),34567), "Unrelated domain matched a fixed recovery target");
        Check(!MainPoolRecoveryProof.Confirmed(Snapshot("","www.microsoft.com"),34567), "Absent source metadata claimed recovery");
        Check(MainPoolRecoveryProof.Confirmed(Snapshot("127.0.0.1:34567","www.cloudflare.com"),34567), "Second recovery endpoint was rejected");
        Check(MainPoolRecoveryProof.Confirmed(Snapshot("[::ffff:127.0.0.1]:34567","www.microsoft.com"),34567), "Dual-stack listener's mapped IPv4 source was rejected");
        Console.WriteLine("PASS live main-path TLS proof is independent of pool ranking and rejects direct, AI, stale and unrelated flows");
        int calls = 0;
        var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var third = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var session = new NetworkRecoverySession(token =>
        {
            int call = Interlocked.Increment(ref calls);
            return call == 1 ? first.Task : call == 2 ? second.Task : third.Task;
        }, (ms, token) => retry.Task))
        {
            session.Observe(true);
            Check(calls == 0 && session.Status == ConnectionRecoveryStatus.None, "Routine online signal started health probes");
            session.Observe(false);
            Check(calls == 0 && session.Status == ConnectionRecoveryStatus.WaitingForInternet, "Probed while offline");
            session.Observe(true);
            await Until(() => calls == 1);
            session.Observe(true);
            Check(calls == 1 && session.Status == ConnectionRecoveryStatus.Recovering, "Duplicated pending recovery worker");
            session.Observe(false);
            first.SetResult(true);
            await Task.Delay(40);
            Check(session.Status == ConnectionRecoveryStatus.WaitingForInternet, "Stale success overwrote a new outage");
            session.Observe(true);
            await Until(() => calls == 2);
            second.SetResult(false);
            await Until(() => session.Status == ConnectionRecoveryStatus.Impaired);
            Check(session.Status != ConnectionRecoveryStatus.None, "Local acceptance/failure claimed recovery");
            retry.SetResult(true);
            await Until(() => calls == 3);
            third.SetResult(true);
            await Until(() => session.Status == ConnectionRecoveryStatus.None);
        }
        Console.WriteLine("PASS offline suspension, one worker, stale-result rejection and external proof before recovery");

        int afterDispose = 0;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new NetworkRecoverySession(token => { Interlocked.Increment(ref afterDispose); return pending.Task; });
        old.Observe(false); old.Observe(true);
        await Until(() => afterDispose == 1);
        old.Dispose(); pending.SetResult(false);
        await Task.Delay(40);
        old.Observe(true);
        Check(old.Status == ConnectionRecoveryStatus.None && afterDispose == 1, "Disposed connection revived recovery/probes");
        Console.WriteLine("PASS disconnect invalidates pending recovery without waiting for it");

        bool connected = true, internet = true;
        int pingCalls = 0;
        var signals = new List<InternetAvailabilityChangedEventArgs>();
        using (var monitor = new InternetConnectivityMonitor(() => connected,
            attempt => { pingCalls++; return Task.FromResult(internet); }, () => true))
        {
            monitor.AvailabilityChanged += (sender, e) => signals.Add(e);
            await monitor.CheckFastAsync(); signals.Clear();
            internet = false;
            for (int i=0;i<3;i++) await monitor.CheckFastAsync();
            Check(signals.Count==0, "One lost direct ping suspended the pool");
            await monitor.CheckFastAsync();
            Check(signals.Count==1 && !signals[0].IsOnline && !signals[0].NotifyUser,
                "Fast outage was not published independently of the ten-minute notification");
            await monitor.CheckFastAsync();
            Check(signals.Count==1, "Repeated fast outage duplicated down events");
            internet=true;
            await monitor.CheckFastAsync();
            Check(signals.Count==2 && signals[1].IsOnline, "WAN return without adapter events waited for periodic notification");
            connected=false; int before=pingCalls;
            await monitor.CheckFastAsync();
            Check(pingCalls==before, "Online disconnected app kept fast probing");
        }
        Console.WriteLine("PASS WAN return is detected on the next fast tick without OS events or extra notifications");

        using (var blockedIcmp = new InternetConnectivityMonitor(() => true,
            attempt=>Task.FromResult(false),()=>true,()=>Task.FromResult(true)))
        {
            int falseOutages=0;
            blockedIcmp.AvailabilityChanged+=(sender,e)=>{if(!e.IsOnline&&e.AffectsRecovery)falseOutages++;};
            for(int i=0;i<4;i++)await blockedIcmp.CheckFastAsync();
            Check(falseOutages==0,"A blocked fixed ping endpoint suspended a working VPN path");
        }
        Console.WriteLine("PASS a working external TLS path prevents false suspension from blocked ICMP");

        var latePing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int lateSignals=0;
        var stoppedMonitor = new InternetConnectivityMonitor(() => true, attempt => latePing.Task, () => true);
        stoppedMonitor.AvailabilityChanged += (sender,e) => lateSignals++;
        var pendingPing=stoppedMonitor.CheckFastAsync();
        stoppedMonitor.Dispose(); latePing.SetResult(true); await pendingPing;
        Check(lateSignals==0, "Disposed monitor emitted late availability");

        int requests=0, confirmations=0;
        var failedProof = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryProof = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var ordered = new NetworkRecoverySession(token =>
        {
            Check(requests==1, "TLS proof started before pool-return RPC");
            return Interlocked.Increment(ref confirmations)==1 ? failedProof.Task : retryProof.Task;
        }, (ms,token)=>retryGate.Task, token=>{ Interlocked.Increment(ref requests); return Task.FromResult(true); }))
        {
            ordered.Observe(false); ordered.Observe(true);
            await Until(()=>confirmations==1);
            failedProof.SetResult(false); await Until(()=>ordered.Status==ConnectionRecoveryStatus.Impaired);
            retryGate.SetResult(true); await Until(()=>confirmations==2);
            Check(requests==1, "Every recovery proof launched another pool sweep");
            retryProof.SetResult(true); await Until(()=>ordered.Status==ConnectionRecoveryStatus.None);
        }
        Console.WriteLine("PASS immediate pool-return RPC precedes proof and is acknowledged once per return");

        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var service=TunnelPlusService.RecoveryFixture(((IPEndPoint)listener.LocalEndpoint).Port);
        try
        {
            service.ObserveInternetAvailability(false);service.ObserveInternetAvailability(true);
            using(var downPeer=await listener.AcceptTcpClientAsync())
            {
                await ActionRequest(downPeer.GetStream(),"network-down");
                await Task.Delay(40);
                Check(!listener.Pending(),"Return RPC overtook pending down RPC");
                byte[] ack={2,0x18,2,2,0x0a,0};
                await downPeer.GetStream().WriteAsync(ack,0,ack.Length);
            }
            using(var returnPeer=await listener.AcceptTcpClientAsync())
            {
                await ActionRequest(returnPeer.GetStream(),"network-return");
                var clock=Stopwatch.StartNew();service.StopFixture();
                Check(clock.ElapsedMilliseconds<100,"Disconnect waited for availability RPC");
                var close=returnPeer.GetStream().ReadAsync(new byte[1],0,1);
                Check(await Task.WhenAny(close,Task.Delay(1500))==close&&await close==0,"Disconnect did not cancel pending availability socket");
            }
            await Task.Delay(40);
            Check(!listener.Pending()&&service.RecoveryStatus==ConnectionRecoveryStatus.None,"Cancelled return revived queries/session");
        }
        finally{service.StopFixture();listener.Stop();}
        Console.WriteLine("PASS production availability RPCs target main pool in order and cancel immediately on disconnect");
    }
}
