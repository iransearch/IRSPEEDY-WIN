using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Components.ServerListControl;
using IRSpeedyVPN.Models;
using IRSpeedyVPN.Models.NewService;
using IRSpeedyVPN.Residential;

internal static class StateChecks
{
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static AccountInfoEx Account(string name) => new AccountInfoEx { residential_vpn = new ResidentialConfig { enabled=true,subscription_url="https://example.invalid/"+name },UserAccount=new UserAccount() };
    static ResidentialViewModel.Payload Payload(string city="Frankfurt")=>new ResidentialViewModel.Payload {Body="vless://id@example.invalid:443#"+city+", DE / ISP",TrafficHeader="upload=10;download=20;total=100"};
    static void Expire(ResidentialViewModel vm)=>typeof(ResidentialViewModel).GetField("fetchedAt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vm,DateTime.MinValue);
    static void Main()
    {
        int calls=0;
        var old=new TaskCompletionSource<ResidentialViewModel.Payload>();
        CancellationToken firstToken=default(CancellationToken);
        var vm=new ResidentialViewModel(new GlobalInfo(),(url,token)=>{calls++;if(url.EndsWith("a")){firstToken=token;return old.Task;}return Task.FromResult(Payload("Berlin"));});
        vm.Configure(Account("a"));vm.Configure(Account("b"));
        Check(firstToken.IsCancellationRequested,"account change cancels pending subscription GET");
        old.SetResult(Payload("Stale"));
        Check(((ResidentialService)vm.Services.Single()).City=="Berlin" && vm.VisibleTraffic.Remaining==70,"late account result cannot overwrite new catalog or quota");
        var service=vm.Services.Single();
        vm.SetVisible(true);vm.SetConnected(true);vm.RefreshAsync().GetAwaiter().GetResult();
        Check(calls==2,"startup/tab/connect share fresh cached quota and one fetch");
        Expire(vm);vm.RefreshAsync().GetAwaiter().GetResult();
        Check(ReferenceEquals(service,vm.Services.Single()),"quota refresh preserves live service selection and ping objects");
        vm.Configure(null);
        Check(vm.Services.Length==0&&vm.VisibleTraffic==null&&!vm.CanConnect,"logout clears account-scoped catalog and quota");
        var eco=Account("eco");eco.UserAccount.GroupID=39;vm.Configure(eco);
        Check(!vm.IsAllowed&&calls==3,"ECO account never starts a Residential fetch");
        int attempts=0;
        var retry=new ResidentialViewModel(new GlobalInfo(),(url,token)=>++attempts==1?Task.FromException<ResidentialViewModel.Payload>(new TimeoutException()):Task.FromResult(Payload()));
        retry.Configure(Account("retry"));
        Check(retry.VisibleTraffic==null,"fetch timeout remains hidden without zero/placeholder data");
        System.Windows.Threading.DispatcherTimer.Last.Fire();
        Check(attempts==2&&retry.VisibleTraffic.Remaining==70,"failed fetch recovers on periodic retry");
        var raw=new Url{url="vless://id@example.invalid:443",latency=101,latencychkTime=DateTime.Now};
        service.GetServerUrls().Clear();service.GetServerUrls().Add(raw);
        var residentialRow=new GroupItem{Service=service};residentialRow.RefreshSignals();
        Check(residentialRow.SignalText=="71 ms"&&residentialRow.SortKey==71&&raw.latency==101,"actual production GroupItem uses adjusted Residential display/order without altering raw latency");
        var vpn=new IRSpeedyVPN.Services.TunnelPlusService(new ServerEx{urls=new System.Collections.Generic.List<Url>{raw}},new GlobalInfo());
        var vpnRow=new GroupItem{Service=vpn};vpnRow.RefreshSignals();
        Check(vpnRow.SignalText=="101 ms"&&vpnRow.SortKey==101,"actual production GroupItem retains unchanged VPN ping and order");
        Check(ResidentialService.IsPoolCompatible("vless://a@host:443") && ResidentialService.IsPoolCompatible("hy2://a@host:443")
            && !ResidentialService.IsPoolCompatible("anytls://a@host:443") && !ResidentialService.IsPoolCompatible("tuic://a@host:443"),"Residential never advertises native protocols unsupported by the existing Windows country pool");
        var androidCatalog = new ResidentialViewModel(new GlobalInfo(), (url,token)=>Task.FromResult(new ResidentialViewModel.Payload {
            Body="\uFEFFhysteria2://test@one.example:443#Frankfurt, Hesse, DE / ISP A#private\n"
                +"hysteria2://test@two.example:443#Berlin, Berlin, DE / ISP B#other\n"
                +"anytls://test@nl.example:443#Amsterdam, North Holland, NL / ISP#private",
            TrafficHeader=null }));
        androidCatalog.Configure(Account("android"));
        Check(androidCatalog.Services.Length==2 && androidCatalog.Services.Sum(s=>s.GetServerUrls().Count)==2,
            "Android countries are published even when a country has no Windows-compatible pool candidates");
        Check(androidCatalog.VisibleTraffic==null && androidCatalog.CanConnect,
            "missing subscription-userinfo never blocks loading the real country list");
        HttpChecks.Run().GetAwaiter().GetResult();
    }
}

