using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Common;
using IRSpeedyVPN.Services.Libcore;
using Sentry;

internal static class CoreRpcChecks
{
    internal static int Checks;
    internal static void Require(bool condition,string name)
    {if(!condition)throw new Exception(name);Checks++;Console.WriteLine("PASS "+name);}
    internal static async Task Run()
    {
        await Deadline(false);await Deadline(true);await LegacyStop();await Cancel();
        var timeout=new TimeoutException("SECRET");new CoreRpcTrace("start",30000){Stage="read-header"}.Attach(timeout,"deadline");
        ErrorReporting.Annotate(timeout,"core.operation","start","core.operation_id",new string('a',32),"core.lifecycle_stage","start",
            "core.outcome","retry-succeeded","core.attempts","2","core.attempt1_ms","30001","core.attempt2_ms","500",
            "core.runtime_sha256",new string('b',64),"core.runtime_commit",new string('c',40),"core.runtime_version","1.13.0-beta.1",
            "core.runtime_arch","windows/amd64","core.pid","42","core.process_state","alive",
            "core.config_id",new string('d',16),"core.need_xray","true","core.main_members","8",
            "core.runtime_path","C:/SECRET","core.rpc_stage","SECRET","core.runtime_commit","SECRET",
            "core.config_id","https://SECRET","core.pid","SECRET","core.runtime_version","https://SECRET");
        timeout.Data["core.runtime_path"]="SECRET";timeout.Data["core.pid"]="SECRET";
        var report=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(timeout,false));
        Require(report.Level==SentryLevel.Warning&&report.Tags["core.outcome"]=="retry-succeeded","recovered timeout is a handled warning with the final retry outcome");
        Require(report.Tags["core.rpc_stage"]=="read-header"&&report.Tags["core.runtime_commit"]==new string('c',40),"invalid later annotation cannot overwrite validated RPC/runtime fields");
        Require(report.Tags["core.runtime_arch"]=="windows/amd64"&&report.Tags["core.runtime_sha256"].Length==64,"exact runtime hash and validated architecture survive before-send");
        Require(report.Tags["diagnostic.schema"]=="core-rpc-v1"&&report.Tags["exception.handled"]=="true","core schema and handled classification survive filtering");
        Require((report.Message.Formatted ?? report.Message.Message ?? "").Contains("read-header")&&(report.Message.Formatted ?? report.Message.Message ?? "").Contains("retry-succeeded"),"Sentry event message states the RPC stage and retry outcome without raw messages");
        Require(!report.Tags.ContainsKey("core.runtime_path")&&!report.Tags.Values.Any(v=>v.Contains("SECRET"))&&!report.SentryExceptions.Any(e=>e.Value.Contains("SECRET")),"no raw paths, messages, config URLs, credentials or arbitrary Exception.Data escape");
        var repeated=new TimeoutException("SECRET");new CoreRpcTrace("start",30000){Stage="read-header"}.Attach(repeated,"deadline");
        ErrorReporting.Annotate(repeated,"core.operation","start","core.lifecycle_stage","start","core.outcome","retry-succeeded","core.operation_id",new string('f',32));
        Require(ErrorReporting.DeduplicationKey(repeated,false)==ErrorReporting.DeduplicationKey(timeout,false),"deduplication ignores correlation IDs and preserves the reporting bound");
        ErrorReporting.Annotate(repeated,"core.outcome","retry-failed");
        Require(ErrorReporting.DeduplicationKey(repeated,false)!=ErrorReporting.DeduplicationKey(timeout,false),"a recovered warning cannot rate-limit a later terminal failure");
        Require(ErrorReporting.DeduplicationKey(timeout,false)!=ErrorReporting.DeduplicationKey(timeout,true),"a handled report cannot rate-limit a later fatal event");
        var failed=new TimeoutException("SECRET");new CoreRpcTrace("start",30000){Stage="read-body"}.Attach(failed,"deadline");
        ErrorReporting.Annotate(failed,"core.operation","start","core.lifecycle_stage","start","core.outcome","retry-failed");
        var failure=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(failed,false));
        Require(failure.Level==SentryLevel.Error&&!failure.Fingerprint.SequenceEqual(report.Fingerprint),"terminal failures remain errors and cannot be grouped with recovered retries");
        var fatal=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(timeout,true));
        Require(fatal.Level==SentryLevel.Fatal&&fatal.Tags["exception.handled"]=="false","core context never downgrades a fatal exception");
        var generic=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(new Exception("SECRET"),false));
        Require(!generic.Tags.Keys.Any(k=>k.StartsWith("core."))&&generic.Tags["diagnostic.schema"]=="http-context-v2","unrelated exceptions cannot inherit a prior core operation context");
        var malicious=new SentryEvent();malicious.SetTag("report.kind","exception");malicious.SetTag("core.rpc","SECRET");malicious.SetTag("core.process_state","SECRET");malicious.SetTag("core.pid","SECRET");
        Require(!ErrorReporting.KeepSafeFields(malicious).Tags.Keys.Any(k=>k.StartsWith("core.")),"before-send independently rejects forged invalid core tags");
        Require(CoreRpcTrace.Alias("https://SECRET")=="other","unknown RPC method never exports its raw method string");
        var traces=Enumerable.Range(0,8).Select(async i=>{await Task.Yield();var ex=new Exception("SECRET");new CoreRpcTrace("test",100+i){Stage="read-header"}.Attach(ex,"transport");return ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(ex,false));});
        var isolated=await Task.WhenAll(traces);
        Require(isolated.Select(r=>r.Tags["core.rpc_budget_ms"]).Distinct().Count()==8,"concurrent RPC exceptions retain independent budgets and stages");
    }
    private static async Task Deadline(bool body)
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try
        {
            int port=((IPEndPoint)listener.LocalEndpoint).Port;var accepted=listener.AcceptTcpClientAsync();
            var call=Task.Run(()=>new LibcoreServiceClient("127.0.0.1",port,200).StartWithDeadline(new LoadConfigReq{CoreConfig="SECRET"},180,CancellationToken.None));
            using(var peer=await accepted)
            {
                var stream=peer.GetStream();await ReadFrame(stream);await ReadFrame(stream);
                if(body)await stream.WriteAsync(new byte[]{2,0x18,2},0,3);
                Exception failure=null;try{await call;}catch(Exception ex){failure=ex;}
                Require(failure is TimeoutException,"native stalled "+(body?"response body":"response header")+" remains a typed Start timeout");
                var report=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(failure,false));
                Require(report.Tags["core.rpc"]=="start"&&report.Tags["core.rpc_stage"]==(body?"read-body":"read-header"),"native timeout exports the exact blocked RPC read stage");
                Require(report.Tags["core.close_reason"]=="deadline"&&report.Tags["core.rpc_budget_ms"]=="180"&&long.Parse(report.Tags["core.rpc_elapsed_ms"])>=100,"native timeout includes the closing cause, deadline and measured duration");
                var closed=stream.ReadAsync(new byte[1],0,1);Require(await Task.WhenAny(closed,Task.Delay(1500))==closed&&await closed==0,"deadline drains the real local RPC socket");
            }
        }
        finally{listener.Stop();}
    }
    private static async Task LegacyStop()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try
        {
            var accepted=listener.AcceptTcpClientAsync();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            var call=Task.Run(()=>new LibcoreServiceClient("127.0.0.1",port,200).StopWithDeadline(180));
            using(var peer=await accepted)
            {
                await ReadFrame(peer.GetStream());await ReadFrame(peer.GetStream());
                Exception failure=null;try{await call;}catch(Exception ex){failure=ex;}
                Require(failure is IOException||failure is ObjectDisposedException||failure is SocketException,"legacy Stop timeout preserves its existing transport-failure category");
                var report=ErrorReporting.KeepSafeFields(ErrorReporting.PrepareException(failure,false));
                Require(report.Tags["core.rpc"]=="stop"&&report.Tags["core.close_reason"]=="deadline","Stop deadlines are distinguished from an unrelated connection reset");
            }
        }
        finally{listener.Stop();}
    }
    private static async Task Cancel()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using(var cancellation=new CancellationTokenSource())
        try
        {
            var accepted=listener.AcceptTcpClientAsync();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            var call=Task.Run(()=>new LibcoreServiceClient("127.0.0.1",port,500).StartWithDeadline(new LoadConfigReq(),5000,cancellation.Token));
            using(var peer=await accepted)
            {
                await ReadFrame(peer.GetStream());await ReadFrame(peer.GetStream());cancellation.Cancel();
                Exception failure=null;try{await call;}catch(Exception ex){failure=ex;}
                Require(failure is OperationCanceledException&&CoreRpcTrace.Read(failure)["core.close_reason"]=="canceled","user cancellation remains cancellation, not a timeout");
            }
        }
        finally{listener.Stop();}
    }
    internal static async Task<byte[]> ReadFrame(NetworkStream stream)
    {
        int size=0;for(int shift=0;shift<35;shift+=7){var b=new byte[1];if(await stream.ReadAsync(b,0,1)!=1)throw new EndOfStreamException();size|=(b[0]&127)<<shift;if(b[0]<128)break;}
        if(size>65536)throw new Exception("oversized fixture request");var buffer=new byte[size];int offset=0;while(offset<size){int n=await stream.ReadAsync(buffer,offset,size-offset);if(n==0)throw new EndOfStreamException();offset+=n;}
        return buffer;
    }
}
