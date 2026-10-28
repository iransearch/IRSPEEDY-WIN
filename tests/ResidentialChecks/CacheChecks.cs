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
        }
        finally {if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
