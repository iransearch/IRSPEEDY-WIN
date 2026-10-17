// Copied temporarily into ThroneCore/internal/boxmain by run-probe-dns-core.py.
package boxmain

import (
 "os"
 "testing"

 "github.com/sagernet/sing-box/option"
)

func TestIRSpeedySystemProbeDNS(t *testing.T) {
 raw,err:=os.ReadFile(os.Getenv("IRSPEEDY_PROBE_DNS_FIXTURE"));if err!=nil{t.Fatal(err)}
 cfg,err:=parseConfig(newBoxContext(),raw);if err!=nil{t.Fatal(err)}
 if cfg.DNS==nil||len(cfg.DNS.Servers)!=2{t.Fatal("original local DNS profile missing")}
 direct,local:=cfg.DNS.Servers[0],cfg.DNS.Servers[1]
 if direct.Type!="local"||direct.Tag!="dns-direct"||local.Type!="local"||local.Tag!="dns-local"{t.Fatal("probe unexpectedly depends on public DoH")}
 opts,ok:=direct.Options.(*option.LocalDNSServerOptions)
 if !ok||opts.DomainResolver==nil||opts.DomainResolver.Server!="dns-local"{t.Fatal("local resolver bootstrap changed")}
 if cfg.Route==nil||!cfg.Route.AutoDetectInterface||cfg.Route.DefaultDomainResolver.Server!="dns-direct"{t.Fatal("probe physical DNS route lost")}
 // Instantiate actual outbounds (including native Shadowsocks), but do not
 // start host networking or contact DNS/VPN servers from the test container.
 if err=Check(raw);err!=nil{t.Fatal(err)}
}
