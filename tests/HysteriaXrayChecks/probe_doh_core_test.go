// Run temporarily in dns/transport of the pinned Throne sing-box source.
package transport

import (
	"context"
	"crypto/ed25519"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"encoding/pem"
	"io"
	"math/big"
	"net"
	"net/http"
	"net/http/httptest"
	"net/netip"
	"os"
	"sync/atomic"
	"testing"
	"time"

	mDNS "github.com/miekg/dns"
	"github.com/sagernet/sing-box/adapter"
	D "github.com/sagernet/sing-box/dns"
	"github.com/sagernet/sing-box/log"
	"github.com/sagernet/sing-box/option"
	"github.com/sagernet/sing/common/json"
	"github.com/sagernet/sing/service"
	"github.com/stretchr/testify/require"
)

func TestIRSpeedyProbeDoH(t *testing.T) {
	registry := D.NewTransportRegistry()
	RegisterHTTPS(registry)
	ctx := service.ContextWith[option.DNSTransportOptionsRegistry](context.Background(), registry)
	var profile struct {
		DNS option.DNSOptions `json:"dns"`
	}
	raw, err := os.ReadFile(os.Getenv("IRSPEEDY_PROBE_DNS_FIXTURE"))
	require.NoError(t, err)
	require.NoError(t, json.UnmarshalContext(ctx, raw, &profile))
	require.Len(t, profile.DNS.Servers, 1)
	server := profile.DNS.Servers[0]
	require.Equal(t, "dns-direct", server.Tag)
	opts := server.Options.(*option.RemoteHTTPSDNSServerOptions)
	require.Equal(t, "8.8.8.8", opts.Server)
	require.Equal(t, "/dns-query", opts.Path)
	require.Equal(t, "dns.google", opts.TLS.ServerName)
	require.False(t, opts.TLS.Insecure)
	require.Empty(t, opts.Detour, "probe bootstrap must not depend on a VPN/Pool")
	require.Nil(t, opts.DomainResolver, "literal IP must not require OS/bootstrap DNS")

	pub, key, err := ed25519.GenerateKey(rand.Reader)
	require.NoError(t, err)
	cert := &x509.Certificate{SerialNumber: big.NewInt(1), DNSNames: []string{"dns.google"}, NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().Add(time.Hour),
		IsCA: true, BasicConstraintsValid: true, KeyUsage: x509.KeyUsageCertSign | x509.KeyUsageDigitalSignature, ExtKeyUsage: []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth}}
	der, err := x509.CreateCertificate(rand.Reader, cert, cert, pub, key)
	require.NoError(t, err)
	var calls atomic.Int32
	var unavailable atomic.Bool
	doh := httptest.NewUnstartedServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		calls.Add(1)
		if unavailable.Load() {
			http.Error(w, "resolver unavailable", 503)
			return
		}
		if r.URL.Path != "/dns-query" || r.TLS.ServerName != "dns.google" {
			http.Error(w, "bad path/SNI", 400)
			return
		}
		raw, err := io.ReadAll(r.Body)
		if err != nil {
			return
		}
		query := new(mDNS.Msg)
		if query.Unpack(raw) != nil {
			http.Error(w, "bad DNS", 400)
			return
		}
		answer := D.FixedResponse(query.Id, query.Question[0], []netip.Addr{netip.MustParseAddr("192.0.2.42")}, 60)
		response, err := answer.Pack()
		if err != nil {
			return
		}
		w.Header().Set("Content-Type", "application/dns-message")
		w.Write(response)
	}))
	doh.EnableHTTP2 = true
	doh.TLS = &tls.Config{Certificates: []tls.Certificate{{Certificate: [][]byte{der}, PrivateKey: key}}}
	doh.StartTLS()
	defer doh.Close()
	// Substitute only the network endpoint and test trust root. Production
	// SNI/path, direct dialer and TLS verification remain active.
	opts.Server = "127.0.0.1"
	opts.ServerPort = doh.Listener.Addr().(*net.TCPAddr).AddrPort().Port()
	opts.TLS.Certificate = []string{string(pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}))}
	transport, err := NewHTTPS(ctx, log.NewNOPFactory().Logger(), server.Tag, *opts)
	require.NoError(t, err)
	defer transport.Close()
	require.NoError(t, transport.Start(adapter.StartStateStart))
	query := new(mDNS.Msg)
	query.SetQuestion("vpn-server.example.org.", mDNS.TypeA)
	requestCtx, cancel := context.WithTimeout(ctx, time.Second)
	defer cancel()
	response, err := transport.Exchange(requestCtx, query)
	require.NoError(t, err)
	require.Equal(t, "192.0.2.42", response.Answer[0].(*mDNS.A).A.String())
	unavailable.Store(true)
	_, err = transport.Exchange(requestCtx, query)
	require.Error(t, err, "DoH failure must be reported")
	require.Equal(t, int32(2), calls.Load())
}
