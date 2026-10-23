// Copied temporarily into the pinned sing-box fork by run-core.py.
package dns

import (
	"context"
	stdjson "encoding/json"
	"net/netip"
	"os"
	"path/filepath"
	"testing"
	"time"

	mDNS "github.com/miekg/dns"
	"github.com/sagernet/sing-box/adapter"
	"github.com/sagernet/sing-box/log"
	"github.com/sagernet/sing-box/option"
	"github.com/sagernet/sing/common/json"
	M "github.com/sagernet/sing/common/metadata"
	"github.com/sagernet/sing/service"
	"github.com/stretchr/testify/require"
)

func irspeedyRules(t *testing.T, name string, transports ...*fakeDNSTransport) (*Router, []adapter.DNSRule) {
	t.Helper()
	data, err := os.ReadFile(filepath.Join(os.Getenv("IRSPEEDY_DNS_FIXTURES"), name+".json"))
	require.NoError(t, err)
	var document struct {
		DNS stdjson.RawMessage `json:"dns"`
	}
	require.NoError(t, stdjson.Unmarshal(data, &document))
	registry := NewTransportRegistry()
	registry.register("https", func() any { return new(option.RemoteHTTPSDNSServerOptions) }, nil)
	registry.register("udp", func() any { return new(option.RemoteDNSServerOptions) }, nil)
	ctx := service.ContextWith[option.DNSTransportOptionsRegistry](context.Background(), registry)
	var options option.DNSOptions
	require.NoError(t, json.UnmarshalContext(ctx, document.DNS, &options), "strict DNS schema")
	router := raceTestRouter(t, transports...)
	router.ctx = ctx
	router.rawRules = options.Rules
	manager := router.transport.(*fakeDNSTransportManager)
	manager.defaultTransport = manager.transports[options.Final]
	rules, legacy, _, err := router.buildRules(false)
	require.NoError(t, err, "real DNS router validation, including strategy and response bindings")
	require.False(t, legacy)
	return router, rules
}

const irspeedyRemoteCount = 4

func irspeedyTransports() []*fakeDNSTransport {
	tags := []string{"dns-remote", "dns-remote-cloudflare", "dns-remote-yandex", "dns-remote-additional", "dns-direct", "split-dns-direct"}
	transports := make([]*fakeDNSTransport, len(tags))
	for i, tag := range tags {
		transports[i] = &fakeDNSTransport{tag: tag, delay: 400 * time.Millisecond,
			address: netip.AddrFrom4([4]byte{192, 0, 2, byte(i + 1)})}
	}
	return transports
}

func irspeedyExchange(router *Router, rules []adapter.DNSRule, domain, inbound, process string, qType uint16) exchangeWithRulesResult {
	message := new(mDNS.Msg)
	message.SetQuestion(domain+".", qType)
	processPath := `C:\Apps\` + process
	// filepath.Base uses the test host's separator; this process-name fixture
	// must also work on Linux while the selected-app regex stays Windows-shaped.
	if process == "svchost.exe" {
		processPath = process
	}
	metadata := &adapter.InboundContext{Domain: domain, QueryType: qType, Inbound: inbound,
		Source: M.ParseSocksaddr("127.0.0.1:12345"), ProcessInfo: &adapter.ConnectionOwner{ProcessPath: processPath}}
	ctx, cancel := context.WithTimeout(adapter.WithContext(context.Background(), metadata), time.Second)
	defer cancel()
	return router.exchangeWithRules(ctx, rules, message, adapter.DNSQueryOptions{}, false)
}

func TestIRSpeedyParallelDNS(t *testing.T) {
	for _, fixture := range []string{"proxy", "tun", "split-proxy", "split-tun", "scoped"} {
		for _, winner := range []int{0, 1, 2, 3} {
			t.Run(fixture+"/winner-"+string(rune('0'+winner)), func(t *testing.T) {
				transports := irspeedyTransports()
				transports[winner].delay = 25 * time.Millisecond
				router, rules := irspeedyRules(t, fixture, transports...)
				start := time.Now()
				inbound := "tun-in"
				if fixture == "proxy" || fixture == "split-proxy" || fixture == "scoped" {
					inbound = "mixed-in"
				}
				result := irspeedyExchange(router, rules, "race.example.org", inbound, "browser.exe", mDNS.TypeA)
				require.NoError(t, result.err)
				require.Equal(t, transports[winner].address, responseAddress(t, result.response))
				require.Less(t, time.Since(start), 250*time.Millisecond, "must not wait for a slower earlier resolver")
				for _, transport := range transports[:irspeedyRemoteCount] {
					require.Equal(t, int32(1), transport.queryCount.Load())
				}
				for _, transport := range transports[irspeedyRemoteCount:] {
					require.Zero(t, transport.queryCount.Load(), "no direct DNS leak")
				}
				require.Less(t, transports[irspeedyRemoteCount-1].firstQueried.Sub(transports[0].firstQueried).Abs(), 100*time.Millisecond)
			})
		}
	}
	for _, rcode := range []int{mDNS.RcodeNameError, mDNS.RcodeServerFailure, mDNS.RcodeRefused} {
		t.Run("rcode-"+mDNS.RcodeToString[rcode], func(t *testing.T) {
			transports := irspeedyTransports()
			transports[0].delay, transports[0].rcode = time.Millisecond, rcode
			transports[2].delay = 25 * time.Millisecond
			router, rules := irspeedyRules(t, "tun", transports...)
			result := irspeedyExchange(router, rules, "race.example.org", "tun-in", "browser.exe", mDNS.TypeA)
			require.NoError(t, result.err)
			if rcode == mDNS.RcodeNameError {
				require.Equal(t, rcode, result.response.Rcode)
			} else {
				require.Equal(t, transports[2].address, responseAddress(t, result.response))
			}
		})
	}
	t.Run("all-fail", func(t *testing.T) {
		transports := irspeedyTransports()
		for _, transport := range transports[:irspeedyRemoteCount] {
			transport.delay, transport.rcode = time.Millisecond, mDNS.RcodeRefused
		}
		router, rules := irspeedyRules(t, "split-tun", transports...)
		result := irspeedyExchange(router, rules, "race.example.org", "tun-in", "svchost.exe", mDNS.TypeA)
		require.NoError(t, result.err)
		require.Equal(t, mDNS.RcodeServerFailure, result.response.Rcode)
		for _, transport := range transports[:irspeedyRemoteCount] {
			require.Equal(t, int32(1), transport.queryCount.Load(), "no sequential requery")
		}
		for _, transport := range transports[irspeedyRemoteCount:] {
			require.Zero(t, transport.queryCount.Load())
		}
	})
	t.Run("IPv6-and-NODATA", func(t *testing.T) {
		for _, nodata := range []bool{false, true} {
			transports := irspeedyTransports()
			transports[0].delay = 25 * time.Millisecond
			if !nodata {
				transports[0].address = netip.MustParseAddr("2001:db8::1")
			}
			router, rules := irspeedyRules(t, "tun", transports...)
			result := irspeedyExchange(router, rules, "race.example.org", "tun-in", "browser.exe", mDNS.TypeAAAA)
			require.NoError(t, result.err)
			require.Equal(t, mDNS.RcodeSuccess, result.response.Rcode)
			if nodata {
				require.Empty(t, result.response.Answer)
			} else {
				require.Equal(t, "2001:db8::1", result.response.Answer[0].(*mDNS.AAAA).AAAA.String())
			}
		}
	})
	t.Run("timeouts-do-not-block-additional-resolver", func(t *testing.T) {
		transports := irspeedyTransports()
		for _, transport := range transports[:irspeedyRemoteCount-1] {
			transport.exchangeErr = context.DeadlineExceeded
		}
		transports[irspeedyRemoteCount-1].delay = 25 * time.Millisecond
		router, rules := irspeedyRules(t, "tun", transports...)
		result := irspeedyExchange(router, rules, "race.example.org", "tun-in", "browser.exe", mDNS.TypeA)
		require.NoError(t, result.err)
		require.Equal(t, transports[irspeedyRemoteCount-1].address, responseAddress(t, result.response))
	})
	t.Run("additional-error-does-not-block-doh", func(t *testing.T) {
		transports := irspeedyTransports()
		transports[3].delay, transports[3].rcode = time.Millisecond, mDNS.RcodeRefused
		transports[1].delay = 25 * time.Millisecond
		router, rules := irspeedyRules(t, "tun", transports...)
		result := irspeedyExchange(router, rules, "race.example.org", "tun-in", "browser.exe", mDNS.TypeA)
		require.NoError(t, result.err)
		require.Equal(t, transports[1].address, responseAddress(t, result.response))
	})
	t.Run("cached-answer-retained", func(t *testing.T) {
		transports := irspeedyTransports()
		transports[0].delay = 25 * time.Millisecond
		router, rules := irspeedyRules(t, "tun", transports...)
		router.client = NewClient(ClientOptions{Context: context.Background(), Logger: log.NewNOPFactory().Logger()})
		router.client.Start()
		for i := 0; i < 2; i++ {
			result := irspeedyExchange(router, rules, "race.example.org", "tun-in", "browser.exe", mDNS.TypeA)
			require.NoError(t, result.err)
			require.Equal(t, transports[0].address, responseAddress(t, result.response))
		}
		require.Equal(t, int32(1), transports[0].queryCount.Load(), "winning transport retains TTL cache")
	})
	for _, tc := range []struct {
		domain, inbound, process string
		transport                int
	}{
		{"bootstrap.example.org", "mixed-in", "browser.exe", 4},
		{"race.example.org", "tun-in", "other.exe", 5},
		{"race.example.org", "tun-in", "svchost.exe", 3},
		{"scoped.example.org", "mixed-in", "browser.exe", 3},
	} {
		t.Run(tc.domain+"/"+tc.process, func(t *testing.T) {
			transports := irspeedyTransports()
			transports[tc.transport].delay = 25 * time.Millisecond
			fixture := "split-tun"
			if tc.domain == "scoped.example.org" {
				fixture = "scoped"
			}
			router, rules := irspeedyRules(t, fixture, transports...)
			result := irspeedyExchange(router, rules, tc.domain, tc.inbound, tc.process, mDNS.TypeA)
			require.NoError(t, result.err)
			require.Equal(t, transports[tc.transport].address, responseAddress(t, result.response))
			if tc.transport >= irspeedyRemoteCount {
				for _, transport := range transports[:irspeedyRemoteCount] {
					require.Zero(t, transport.queryCount.Load(), "direct scope must not launch VPN queries")
				}
			}
		})
	}
}
