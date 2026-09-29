package main

import (
	"context"
	"github.com/sagernet/sing-box/adapter"
	"github.com/sagernet/sing-box/adapter/outbound"
	"github.com/sagernet/sing-box/log"
	M "github.com/sagernet/sing/common/metadata"
	"net"
	"net/http"
	"net/http/httptest"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

type fakeMember struct {
	outbound.Adapter
	calls atomic.Int32
}

func (m *fakeMember) DialContext(c context.Context, n string, a M.Socksaddr) (net.Conn, error) {
	m.calls.Add(1)
	return (&net.Dialer{}).DialContext(c, n, a.String())
}
func (m *fakeMember) ListenPacket(context.Context, M.Socksaddr) (net.PacketConn, error) {
	return net.ListenPacket("udp", "127.0.0.1:0")
}
func testPool() (*Pool, *fakeMember, *fakeMember) {
	ctx, cancel := context.WithCancel(context.Background())
	a := &fakeMember{Adapter: outbound.NewAdapter("direct", "ai-1", []string{"tcp", "udp"}, nil)}
	b := &fakeMember{Adapter: outbound.NewAdapter("direct", "ai-2", []string{"tcp", "udp"}, nil)}
	return &Pool{ctx: ctx, cancel: cancel, members: []adapter.Outbound{a, b}, options: PoolOptions{Fallback: "ai-1", Expected: 5, Tolerance: .2}, maxRTT: 3 * time.Second, history: map[string][]sample{}, logger: log.NewNOPFactory().NewLogger("test")}, a, b
}
func TestProbesWaitForReadiness(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(204) }))
	defer server.Close()
	p, a, b := testPool()
	p.probeTarget = server.URL
	defer p.Close()
	time.Sleep(30 * time.Millisecond)
	if a.calls.Load()+b.calls.Load() != 0 {
		t.Fatal("probe ran before readiness")
	}
	p.Ready()
	deadline := time.Now().Add(time.Second)
	for time.Now().Before(deadline) {
		p.mu.RLock()
		ready := len(p.history) == 2
		p.mu.RUnlock()
		if ready {
			break
		}
		time.Sleep(time.Millisecond)
	}
	p.mu.RLock()
	if len(p.history) != 2 {
		t.Fatal("missing initial samples")
	}
	p.mu.RUnlock()
	seen := map[string]bool{}
	for i := 0; i < 100; i++ {
		o, e := p.pick("tcp")
		if e != nil {
			t.Fatal(e)
		}
		seen[o.Tag()] = true
	}
	if len(seen) != 2 {
		t.Fatal("pool collapsed to one member", seen)
	}
}
func TestPoolOwnFallbackAndFailedSamples(t *testing.T) {
	p, _, _ := testPool()
	defer p.Close()
	o, e := p.pick("tcp")
	if e != nil || o.Tag() != "ai-1" {
		t.Fatal("bad own fallback")
	}
	p.record("ai-1", sample{time.Now(), time.Millisecond, true})
	p.record("ai-1", sample{time.Now(), time.Millisecond, false})
	p.record("ai-2", sample{time.Now(), time.Millisecond * 20, false})
	for i := 0; i < 20; i++ {
		o, e = p.pick("tcp")
		if e != nil || o.Tag() != "ai-2" {
			t.Fatal("failed sample was ignored")
		}
	}
}
func TestReadyCloseRace(t *testing.T) {
	for i := 0; i < 20; i++ {
		p, a, b := testPool()
		p.Close()
		var wg sync.WaitGroup
		for j := 0; j < 10; j++ {
			wg.Add(1)
			go func() { defer wg.Done(); p.Ready(); p.Close() }()
		}
		wg.Wait()
		if a.calls.Load()+b.calls.Load() != 0 {
			t.Fatal("closed pool probed")
		}
	}
}
