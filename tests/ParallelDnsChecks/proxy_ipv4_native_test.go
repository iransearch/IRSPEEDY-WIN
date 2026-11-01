package boxbox

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"sync/atomic"
	"testing"
	"time"

	mdns "github.com/miekg/dns"
	"github.com/sagernet/sing-box/adapter"
	"github.com/sagernet/sing-box/adapter/inbound"
	"github.com/sagernet/sing-box/adapter/outbound"
	C "github.com/sagernet/sing-box/constant"
	"github.com/sagernet/sing-box/include"
	"github.com/sagernet/sing-box/log"
	"github.com/sagernet/sing-box/option"
	"github.com/sagernet/sing-box/protocol/mixed"
	sjson "github.com/sagernet/sing/common/json"
	xproxy "golang.org/x/net/proxy"
)

// Compiled as an overlay alongside the pinned Core's existing proxy harness.
// The actual C# fixtures decide the family policy. No Core source is modified.
func TestWindowsProxyIPv4Policy(t *testing.T) {
	for _, mode := range []string{"proxy", "tun"} {
		t.Run(mode, func(t *testing.T) {
			filename := "proxy-behavior.json"
			if mode == "tun" {
				filename = "tun-proxy-behavior.json"
			}
			raw, err := os.ReadFile(filepath.Join(os.Getenv("IRSPEEDY_DNS_FIXTURES"), filename))
			if err != nil {
				t.Fatal(err)
			}
			var cfg map[string]any
			if err = json.Unmarshal(raw, &cfg); err != nil {
				t.Fatal(err)
			}
			var a, aaaa, seedQueries atomic.Int32
			seedReady := make(chan struct{})
			ports := map[uint16]bool{}
			dnsServer := func(provider int) map[string]any {
				pc, err := net.ListenPacket("udp", "127.0.0.1:0")
				if err != nil {
					t.Fatal(err)
				}
				port := uint16(pc.LocalAddr().(*net.UDPAddr).Port)
				ports[port] = true
				server := &mdns.Server{PacketConn: pc, Handler: mdns.HandlerFunc(func(w mdns.ResponseWriter, q *mdns.Msg) {
					question := q.Question[0]
					if question.Qtype == mdns.TypeA {
						a.Add(1)
					}
					if question.Qtype == mdns.TypeAAAA {
						aaaa.Add(1)
					}
					if question.Name == "cached.example." && question.Qtype == mdns.TypeAAAA && provider >= 0 {
						if seedQueries.Add(1) == 7 {
							close(seedReady)
						}
						if provider == 2 {
							select {
							case <-seedReady:
							case <-time.After(time.Second):
								return
							}
						}
					}
					r := new(mdns.Msg).SetReply(q)
					if provider >= 0 && provider != 2 {
						r.Rcode = mdns.RcodeServerFailure
					} else if question.Qtype == mdns.TypeA && question.Name != "v6only.example." {
						r.Answer = []mdns.RR{&mdns.A{Hdr: mdns.RR_Header{Name: question.Name, Rrtype: mdns.TypeA, Class: mdns.ClassINET, Ttl: 60}, A: net.IPv4(192, 0, 2, 9)}}
					} else if question.Qtype == mdns.TypeAAAA && question.Name != "v4only.example." {
						r.Answer = []mdns.RR{&mdns.AAAA{Hdr: mdns.RR_Header{Name: question.Name, Rrtype: mdns.TypeAAAA, Class: mdns.ClassINET, Ttl: 60}, AAAA: net.ParseIP("2001:db8::9")}}
					}
					_ = w.WriteMsg(r)
				})}
				go func() { _ = server.ActivateAndServe() }()
				t.Cleanup(func() { _ = server.Shutdown() })
				options := map[string]any{"type": "udp", "server": "127.0.0.1", "server_port": port}
				if provider >= 0 {
					options["detour"] = "proxy"
				}
				return options
			}
			var providers []any
			for i := 0; i < 7; i++ {
				providers = append(providers, dnsServer(i))
			}
			directDNS := dnsServer(-1)
			directDNS["tag"] = "dns-direct"
			cfg["dns"].(map[string]any)["servers"] = []any{map[string]any{"type": "vpn-dns-race", "tag": "dns-remote", "servers": providers, "timeout": "1s"}, directDNS}
			cfg["route"].(map[string]any)["auto_detect_interface"] = false
			reservation, err := net.Listen("tcp", "127.0.0.1:0")
			if err != nil {
				t.Fatal(err)
			}
			address := reservation.Addr().String()
			port := reservation.Addr().(*net.TCPAddr).Port
			_ = reservation.Close()
			// Retain the generated TUN profile's rules, but exercise its local mixed
			// listener without creating an OS TUN interface in this loopback harness.
			cfg["inbounds"] = []any{map[string]any{"type": "mixed", "tag": "mixed-in", "listen": "127.0.0.1", "listen_port": port}}
			cfg["outbounds"] = []any{map[string]any{"type": "dns-capture", "tag": "proxy"}, map[string]any{"type": "dns-capture", "tag": "ai-proxy"}, map[string]any{"type": "dns-capture", "tag": "split-direct"}, map[string]any{"type": "block", "tag": "block"}, map[string]any{"type": "direct", "tag": "direct"}}
			cfg["log"] = map[string]any{"disabled": true}
			dials := make(chan proxyDNSDial, 32)
			registry := include.OutboundRegistry()
			outbound.Register[option.DirectOutboundOptions](registry, "dns-capture", func(_ context.Context, _ adapter.Router, _ log.ContextLogger, tag string, _ option.DirectOutboundOptions) (adapter.Outbound, error) {
				return &proxyDNSOutbound{Adapter: outbound.NewAdapter("dns-capture", tag, []string{"tcp", "udp"}, nil), dnsPorts: ports, dials: dials}, nil
			})
			inbounds := include.InboundRegistry()
			routes := new(proxyDNSRoutes)
			inbound.Register[option.HTTPMixedInboundOptions](inbounds, "mixed", func(ctx context.Context, r adapter.Router, l log.ContextLogger, tag string, o option.HTTPMixedInboundOptions) (adapter.Inbound, error) {
				return mixed.NewInbound(ctx, &proxyDNSRouter{Router: r, routes: routes}, l, tag, o)
			})
			ctx := Context(context.Background(), inbounds, registry, include.EndpointRegistry(), include.DNSTransportRegistry(), include.ServiceRegistry(), include.CertificateProviderRegistry())
			raw, _ = json.Marshal(cfg)
			var options option.Options
			if err = sjson.UnmarshalContext(ctx, raw, &options); err != nil {
				t.Fatal(err)
			}
			box, err := New(Options{Context: ctx, Options: options})
			if err != nil {
				t.Fatal(err)
			}
			t.Cleanup(func() { _ = box.inbound.Close(); routes.stop(); _ = box.Close() })
			if err = box.logFactory.Start(); err != nil {
				t.Fatal(err)
			}
			for _, stage := range adapter.ListStartStages {
				if err = adapter.Start(box.ctx, box.logger, stage, box.outbound, box.dnsTransport, box.dnsRouter, box.connection, box.router, box.endpoint, box.certificateProvider, box.inbound, box.service); err != nil {
					t.Fatal(err)
				}
			}
			// A prefer_ipv4 lookup must still accept an existing IPv6-only cache
			// answer, following the unchanged Core's first-positive-family policy.
			ips, err := box.dnsRouter.Lookup(box.ctx, "cached.example", adapter.DNSQueryOptions{Strategy: C.DomainStrategyIPv6Only})
			if err != nil || len(ips) != 1 || !ips[0].Is6() {
				t.Fatalf("IPv6 cache seed: %v %v", ips, err)
			}
			beforeAAAA := aaaa.Load()
			socks, err := xproxy.SOCKS5("tcp", address, nil, &net.Dialer{Timeout: time.Second})
			if err != nil {
				t.Fatal(err)
			}
			dial := func(protocol, target string) (net.Conn, error) {
				ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
				defer cancel()
				if protocol == "socks" {
					return socks.(xproxy.ContextDialer).DialContext(ctx, "tcp", net.JoinHostPort(target, "443"))
				}
				c, err := (&net.Dialer{}).DialContext(ctx, "tcp", address)
				if err != nil {
					return nil, err
				}
				_ = c.SetDeadline(time.Now().Add(2 * time.Second))
				authority := net.JoinHostPort(target, strconv.Itoa(443))
				_, err = fmt.Fprintf(c, "CONNECT %s HTTP/1.1\r\nHost: %s\r\n\r\n", authority, authority)
				if err == nil {
					var response *http.Response
					response, err = http.ReadResponse(bufio.NewReader(c), &http.Request{Method: "CONNECT"})
					if err == nil && response.StatusCode != 200 {
						err = fmt.Errorf("CONNECT status %d", response.StatusCode)
					}
				}
				if err != nil {
					_ = c.Close()
					return nil, err
				}
				return c, nil
			}
			for _, protocol := range []string{"http", "socks"} {
				for _, target := range []string{"invapi.hostkey.com", "chat.ai.example", "cached.example", "v4only.example", "v6only.example", "2001:db8::9"} {
					t.Run(protocol+"/"+target, func(t *testing.T) {
						c, err := dial(protocol, target)
						if err == nil {
							defer c.Close()
							_ = c.SetDeadline(time.Now().Add(2 * time.Second))
							_, err = io.WriteString(c, "unchanged-payload")
							if err == nil {
								reply := make([]byte, len("unchanged-payload"))
								_, err = io.ReadFull(c, reply)
								if err == nil && string(reply) != "unchanged-payload" {
									t.Fatal("payload changed")
								}
							}
						}
						if err != nil {
							t.Fatal(err)
						}
						select {
						case got := <-dials:
							want := "proxy"
							if target == "chat.ai.example" {
								want = "ai-proxy"
							}
							if got.tag != want {
								t.Fatalf("route changed: %+v", got)
							}
							if !got.destination.IsIP() {
								t.Fatalf("destination was not resolved: %+v", got)
							}
							if (target == "cached.example" || target == "v6only.example" || target == "2001:db8::9") && !got.destination.Addr.Is6() {
								t.Fatalf("%s lost IPv6: %+v", mode, got)
							}
							if target == "v4only.example" && !got.destination.Addr.Is4() {
								t.Fatalf("IPv4-only name did not use IPv4: %+v", got)
							}
						case <-time.After(time.Second):
							t.Fatal("outbound was not called")
						}
					})
				}
			}
			if aaaa.Load() <= beforeAAAA {
				t.Fatalf("%s did not exercise AAAA lookups: before=%d after=%d", mode, beforeAAAA, aaaa.Load())
			}
			if a.Load() == 0 {
				t.Fatal("A queries were not exercised")
			}
		})
	}
}
