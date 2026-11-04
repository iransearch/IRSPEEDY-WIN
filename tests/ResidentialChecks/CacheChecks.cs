using System;
using System.IO;
using System.Linq;
using System.Reflection;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Residential;
using IRSpeedyVPN.Services;
using Newtonsoft.Json;

internal static class CacheChecks
{
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static ResidentialService Service(string suffix)=>new ResidentialService(ResidentialSubscription.Parse("vless://id@cache.example:443#Frankfurt, DE / "+suffix)[0],new GlobalInfo(),"scope");
    static TunnelPlusService Vpn(int id,string link="vless://id@vpn.example:443?security=tls&path=%2F#old-label")
        =>new TunnelPlusService(new ServerEx{ID=id,Country="DE",urls=new System.Collections.Generic.List<Url>{new Url{url=link}}},new GlobalInfo()){Name="xfast"};
    static ServerCheckCache Seed(string dir,string account,TunnelPlusService service)
    {
        var cache=new ServerCheckCache(account,"xfast",dir);cache.Bind(new[]{service});
        cache.RecordRefreshMember(service,service.GetServerUrls()[0],512,DateTime.UtcNow);cache.PersistQueue();return cache;
    }
    static void HistoryChecks(string dir)
    {
        var original=Vpn(23);var cache=Seed(dir,"history",original);
        cache.Record(original,DateTime.Now.AddSeconds(-1));cache.CompleteService(original);
        cache.Bind(new IRSpeedyVPN.Interfaces.IVPNService[0]);cache.PersistQueue();
        var restored=Vpn(23);cache=new ServerCheckCache("history","xfast",dir);cache.Bind(new[]{restored});cache.RestartForLogin();
        Check(restored.GetServerUrls()[0].latency==512,"empty temporary catalog preserves VPN results across process restart");
        var state=(ServerCheckCache.State)typeof(ServerCheckCache).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cache);
        Check(state.RowResults.Count==1&&state.RowResults.Values.Single().HasSuccess,"temporary omission preserves last completed country outcome");
        var other=Vpn(24);cache.Bind(new[]{other});cache.PersistQueue();
        restored=Vpn(23);cache.Bind(new[]{restored,other});
        Check(restored.GetServerUrls()[0].latency==512&&other.GetServerUrls()[0].latency==0,"partial catalog rebind restores only matching country history");
        cache.ClearForManualRefresh();cache.PersistQueue();restored=Vpn(23);cache.Bind(new[]{restored});
        Check(restored.GetServerUrls()[0].latency==0&&restored.GetServerUrls()[0].LastSuccessfulLatency==0,"explicit manual refresh clears retained history too");
        var residential=Service("temporarily missing");var residentialCache=new ServerCheckCache("history","Residential",dir);residentialCache.Bind(new[]{residential});
        residentialCache.RecordRefreshMember(residential,residential.GetServerUrls()[0],137,DateTime.UtcNow);residentialCache.PersistQueue();
        residentialCache.Bind(new IRSpeedyVPN.Interfaces.IVPNService[0]);residentialCache.PersistQueue();
        residential=Service("temporarily missing");residentialCache=new ServerCheckCache("history","Residential",dir);residentialCache.Bind(new[]{residential});
        Check(residential.GetServerUrls()[0].latency==137,"Residential temporary empty catalog preserves persisted results");
    }
    static void FingerprintChecks(string dir)
    {
        var original=Vpn(23);var cache=Seed(dir,"labels",original);
        var renamed=Vpn(23,original.GetServerUrls()[0].url.Replace("old-label","new-label"));cache.Bind(new[]{renamed});cache.PersistQueue();
        renamed=Vpn(23,original.GetServerUrls()[0].url.Replace("old-label","third-label"));cache=new ServerCheckCache("labels","xfast",dir);cache.Bind(new[]{renamed});
        Check(renamed.GetServerUrls()[0].latency==512,"VPN fragment-only renames preserve ping across restart");
        foreach(var link in new[]{"vless://other@vpn.example:443?security=tls&path=%2F#third-label","vless://id@other.example:443?security=tls&path=%2F#third-label",
            "vless://id@vpn.example:8443?security=tls&path=%2F#third-label","vless://id@vpn.example:443?security=reality&path=%2F#third-label",
            "vless://id@vpn.example:443?security=tls&path=%2Fother#third-label","vless://id@vpn.example:443?security=tls&path=%2F%23other#third-label"})
        {
            var changed=Vpn(23,link);changed.GetServerUrls()[0].latency=999;cache.Bind(new[]{changed});
            Check(changed.GetServerUrls()[0].latency==0&&changed.GetServerUrls()[0].LastSuccessfulLatency==0,"real endpoint/credential/TLS/path change invalidates current and historical success");
        }
        foreach(Action<Url> change in new Action<Url>[] {u=>u.extra_field_1="changed",u=>u.extra_field_2="changed",u=>u.chainproxy=1,u=>u.is_subscription=1})
        {
            var changed=Vpn(23);change(changed.GetServerUrls()[0]);cache.Bind(new[]{changed});
            Check(changed.GetServerUrls()[0].latency==0,"connection metadata changes cannot reuse a prior success");
        }
        var isolated=Vpn(23);new ServerCheckCache("different-account","xfast",dir).Bind(new[]{isolated});
        Check(isolated.GetServerUrls()[0].latency==0,"retained results remain account isolated");
        new ServerCheckCache("labels","different-scope",dir).Bind(new[]{isolated});
        Check(isolated.GetServerUrls()[0].latency==0,"retained results remain service/protocol scope isolated");
        // JSON '#' characters can be real credentials/configuration, never a URI label.
        var json=Vpn(25,"{\"password\":\"secret#one\"}");var jsonCache=Seed(dir,"json",json);
        var changedJson=Vpn(25,"{\"password\":\"secret#two\"}");jsonCache.Bind(new[]{changedJson});
        Check(changedJson.GetServerUrls()[0].latency==0,"non-URI configuration keeps meaningful hash characters in its identity");
        // The previous release stores only a hash of the full VPN URI. Migrate an exact old fingerprint;
        // never guess a result by country when its old URI is unavailable.
        var legacy=Vpn(26);var oldCache=new ServerCheckCache("legacy-vpn","xfast",dir);
        string oldKey=(string)typeof(ServerCheckCache).GetMethod("ConfigKey",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{legacy,legacy.GetServerUrls()[0],legacy.GetServerUrls()[0].url});
        var oldState=new ServerCheckCache.State();oldState.Results[oldKey]=new ServerCheckCache.Result{Latency=155,LastSuccess=155,CheckedUtc=DateTime.UtcNow,LastSuccessUtc=DateTime.UtcNow};
        string oldRow=(string)typeof(ServerCheckCache).GetMethod("Hash",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{JsonConvert.SerializeObject(new object[]{legacy.ID,legacy.Name,new[]{oldKey}})});
        oldState.RowResults[oldRow]=new ServerCheckCache.RowResult{HasSuccess=true,CheckedUtc=DateTime.UtcNow};
        string path=(string)typeof(ServerCheckCache).GetField("path",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(oldCache);
        File.WriteAllText(path,JsonConvert.SerializeObject(oldState));oldCache=new ServerCheckCache("legacy-vpn","xfast",dir);oldCache.Bind(new[]{legacy});oldCache.PersistQueue();
        Check(legacy.GetServerUrls()[0].latency==155,"previous full-URI VPN fingerprint migrates without losing saved results");
        var migrated=(ServerCheckCache.State)typeof(ServerCheckCache).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(oldCache);
        Check(migrated.Results.Count==1&&migrated.RowResults.Count==1&&migrated.RowResults.Values.Single().HasSuccess,"legacy migration preserves completed-row outcome without duplicate fingerprints");
        var afterUpgrade=Vpn(26,legacy.GetServerUrls()[0].url.Replace("old-label","renamed-after-upgrade"));new ServerCheckCache("legacy-vpn","xfast",dir).Bind(new[]{afterUpgrade});
        Check(afterUpgrade.GetServerUrls()[0].latency==155,"migrated previous-release VPN result survives a later label change");
        string saved=File.ReadAllText(path);Check(!saved.Contains("vless://")&&!saved.Contains("vpn.example"),"persisted cache contains hashes and measurements, never raw connection links");
        migrated.Results.Values.Single().Latency=-1;migrated.Results[oldKey]=oldState.Results[oldKey];
        oldCache.PersistQueue();var failed=Vpn(26);new ServerCheckCache("legacy-vpn","xfast",dir).Bind(new[]{failed});
        Check(failed.GetServerUrls()[0].latency==-1&&failed.GetServerUrls()[0].LastSuccessfulLatency==155,"legacy success never replaces a recorded current failure");
    }
    static void BoundedHistoryChecks(string dir)
    {
        var service=Vpn(27);var cache=Seed(dir,"bounded",service);cache.Record(service,DateTime.Now.AddSeconds(-1));
        var state=(ServerCheckCache.State)typeof(ServerCheckCache).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cache);
        foreach(var value in state.Results.Values)value.CheckedUtc=DateTime.UtcNow.AddDays(-20);
        foreach(var value in state.RowResults.Values)value.CheckedUtc=DateTime.UtcNow.AddDays(-20);
        for(int i=1;i<=5000;i++)
        {
            string key=i.ToString("X64");var at=DateTime.UtcNow.AddDays(-1).AddMilliseconds(i);
            state.Results[key]=new ServerCheckCache.Result{Latency=200,LastSuccess=200,CheckedUtc=at,LastSuccessUtc=at};
            state.RowResults[key]=new ServerCheckCache.RowResult{HasSuccess=true,CheckedUtc=at};
        }
        cache.PersistQueue();
        Check(state.Results.Count==5000&&state.RowResults.Count==5000&&!state.Results.ContainsKey(1.ToString("X64"))&&state.Results.ContainsKey(5000.ToString("X64")),"retention is bounded and evicts oldest absent configurations first");
        var restored=Vpn(27);cache=new ServerCheckCache("bounded","xfast",dir);cache.Bind(new[]{restored});
        Check(restored.GetServerUrls()[0].latency==512,"current configuration survives retention pressure and a process restart");
    }
    static void AutomaticRoundChecks(string dir)
    {
        var good=Vpn(101);var failed=Vpn(102);var missing=Vpn(103);
        var rows=new[]{good,failed,missing};
        var cache=new ServerCheckCache("automatic","xfast",dir);cache.Bind(rows);
        cache.RecordRefreshMember(good,good.GetServerUrls()[0],120,DateTime.UtcNow);cache.CompleteRefreshRow(good);
        cache.RecordRefreshMember(failed,failed.GetServerUrls()[0],130,DateTime.UtcNow);cache.CompleteRefreshRow(failed);
        cache.RecordRefreshMember(failed,failed.GetServerUrls()[0],-1,DateTime.UtcNow);cache.CompleteRefreshRow(failed);
        cache.StopRound();
        for(int scenario=0;scenario<3;scenario++)
        {
            if(scenario==2)
            {
                rows=new[]{Vpn(101),Vpn(102),Vpn(103)};
                cache=new ServerCheckCache("automatic","xfast",dir);cache.Bind(rows);
            }
            if(scenario==0)cache.RestartFromFirstCountry();else cache.RestartForLogin();
            var seen=new System.Collections.Generic.List<int>();
            while(cache.NextService!=null){var row=cache.NextService;seen.Add(row.ID);cache.CompleteService(row);}
            Check(seen.Count==2&&seen.OrderBy(id=>id).SequenceEqual(new[]{102,103}),"disconnect/login/process restart checks only failed and missing country results");
            Check(rows[0].GetServerUrls()[0].latency==120&&rows[1].GetServerUrls()[0].LastSuccessfulLatency==130,"automatic filtering preserves success and separate failure history");
        }
        foreach(var row in rows){cache.RecordRefreshMember(row,row.GetServerUrls()[0],150,DateTime.UtcNow);cache.CompleteRefreshRow(row);}
        cache.RestartForLogin();Check(cache.NextService==null&&cache.InitialScanCompleted,"all-success login stays idle");
        cache.RestartFromFirstCountry();Check(cache.NextService==null,"all-success disconnect stays idle");
        rows[0].GetServerUrls()[0].extra_field_1="changed";cache.Bind(rows);cache.RestartForLogin();
        Check(ReferenceEquals(cache.NextService,rows[0])&&rows[0].GetServerUrls()[0].latency==0,"a changed configuration becomes missing without retesting unchanged successful countries");
        cache.ClearForManualRefresh();cache.RestartForLogin();var count=0;
        while(cache.NextService!=null){var row=cache.NextService;count++;cache.CompleteService(row);}
        Check(count==3,"explicit refresh can still clear results and test every country");
    }
    static void RetainedCompletedResultsChecks(string dir)
    {
        var rows=new[]{Vpn(201),Vpn(202),Vpn(203)};
        var cache=new ServerCheckCache("retained-order","xfast",dir);cache.Bind(rows);
        cache.RecordRefreshMember(rows[0],rows[0].GetServerUrls()[0],112,DateTime.UtcNow);cache.CompleteRefreshRow(rows[0]);
        cache.RecordRefreshMember(rows[1],rows[1].GetServerUrls()[0],80,DateTime.UtcNow); // Partial only.
        cache.RecordRefreshMember(rows[2],rows[2].GetServerUrls()[0],90,DateTime.UtcNow);cache.CompleteRefreshRow(rows[2]);
        cache.RecordRefreshMember(rows[2],rows[2].GetServerUrls()[0],-1,DateTime.UtcNow);cache.CompleteRefreshRow(rows[2]);
        cache.RestartForLogin();
        Check(cache.CompletedSuccessfulServices.SequenceEqual(new[]{rows[0]}),"retained ordering accepts only completed success, never partial success or failed-row history");
        rows=new[]{Vpn(201),Vpn(202),Vpn(203)};
        cache=new ServerCheckCache("retained-order","xfast",dir);cache.Bind(rows);
        Check(cache.CompletedSuccessfulServices.SequenceEqual(new[]{rows[0]}),"process restart returns current service objects for retained complete results");
        rows[0].GetServerUrls()[0].extra_field_1="changed";cache.Bind(rows);
        Check(cache.CompletedSuccessfulServices.Length==0,"changed configuration cannot retain stale completion eligibility");
        cache.ClearForManualRefresh();
        Check(cache.CompletedSuccessfulServices.Length==0,"explicit refresh clears retained completion evidence");
    }
    internal static void Run()
    {
        string dir=Path.Combine(Path.GetTempPath(),"residential-cache-"+Guid.NewGuid().ToString("N"));
        try
        {
            var a=Service("ISP (AS1)#old-tag");var u=a.GetServerUrls()[0];
            var cache=new ServerCheckCache("user","Residential",dir);cache.Bind(new[]{a});cache.RestartForLogin();
            cache.RecordRefreshMember(a,u,137,DateTime.UtcNow);cache.PersistQueue();cache.StopRound();
            var b=Service("ISP renamed (AS2)#new-private-tag");var v=b.GetServerUrls()[0];
            var reopened=new ServerCheckCache("user","Residential",dir);reopened.Bind(new[]{b});
            Check(v.latency==137,"real cache preserves measured Residential results after metadata/private-fragment changes");
            Check(reopened.NextService==null,"stopped prior-session scan reproduces an empty Residential queue");
            reopened.RestartForLogin();
            Check(ReferenceEquals(reopened.NextService,b)&&v.latency==137,"new Residential bootstrap restarts stopped queue without erasing visible results");
            v.latency=0;reopened.Restore(b);
            Check(v.latency==137,"cancellation rollback restores committed results");
            var vpn=new TunnelPlusService(new ServerEx{ID=23,Country="DE",urls=new System.Collections.Generic.List<Url>{new Url{url="vless://id@vpn.example:443"}}},new GlobalInfo()){Name="xfast"};
            var vpnCache=new ServerCheckCache("user","xfast",dir);vpnCache.Bind(new[]{vpn});
            vpnCache.RecordRefreshMember(vpn,vpn.GetServerUrls()[0],512,DateTime.UtcNow);vpnCache.PersistQueue();
            reopened.Bind(new[]{b});vpnCache.Bind(new[]{vpn});
            Check(v.latency==137&&vpn.GetServerUrls()[0].latency==512,"switching independent VPN/Residential caches cannot erase or exchange results");
            // Seed the exact pre-1.4.55 fingerprint to exercise migration, not a new cache.
            var legacyService=Service("legacy");var legacyUrl=legacyService.GetServerUrls()[0];
            var legacyCache=new ServerCheckCache("legacy-user","Residential",dir);
            string oldKey=(string)typeof(ServerCheckCache).GetMethod("ConfigKey",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{legacyService,legacyUrl,legacyUrl.url});
            string path=(string)typeof(ServerCheckCache).GetField("path",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(legacyCache);
            var state=new ServerCheckCache.State();state.Results[oldKey]=new ServerCheckCache.Result{Latency=155,LastSuccess=155,CheckedUtc=DateTime.UtcNow,LastSuccessUtc=DateTime.UtcNow};
            File.WriteAllText(path,JsonConvert.SerializeObject(state));
            legacyCache=new ServerCheckCache("legacy-user","Residential",dir);legacyCache.Bind(new[]{legacyService});
            Check(legacyUrl.latency==155,"existing full-URI Residential cache entries migrate without losing ping history");
            HistoryChecks(dir);
            FingerprintChecks(dir);
            BoundedHistoryChecks(dir);
            AutomaticRoundChecks(dir);
            RetainedCompletedResultsChecks(dir);
        }
        finally {if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
