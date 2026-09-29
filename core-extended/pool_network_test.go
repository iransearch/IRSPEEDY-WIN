package main

import (
	"context"
	"errors"
	"net"
	"net/http"
	"net/http/httptest"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/sagernet/sing-box/adapter"
	M "github.com/sagernet/sing/common/metadata"
)

type interruptedPoolMember struct {
	*fakeMember
	entered  chan struct{}
	attempts atomic.Int32
}

func (m *interruptedPoolMember) DialContext(ctx context.Context, network string, address M.Socksaddr) (net.Conn, error) {
	switch m.attempts.Add(1) {
	case 1:
		close(m.entered)
		<-ctx.Done()
		return nil, errors.New("network changed")
	case 2:
		// Model a caller joining the old shared Hysteria offer while it unwinds.
		return nil, errors.New("network changed")
	default:
		return m.fakeMember.DialContext(ctx, network, address)
	}
}

func TestPoolRecoversAfterNetworkReset(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(204) }))
	defer server.Close()
	p, a, b := testPool()
	defer p.Close()
	one := &interruptedPoolMember{fakeMember: a, entered: make(chan struct{})}
	two := &interruptedPoolMember{fakeMember: b, entered: make(chan struct{})}
	p.members = []adapter.Outbound{one, two}
	p.probeTarget = server.URL
	p.Ready()
	for _, member := range []*interruptedPoolMember{one, two} {
		select {
		case <-member.entered:
		case <-time.After(2 * time.Second):
			t.Fatal("initial probe did not start")
		}
	}
	p.NetworkResetCompleted(context.Background())
	deadline := time.Now().Add(2 * time.Second)
	recovered := false
	for time.Now().Before(deadline) {
		p.mu.RLock()
		recovered = len(p.history) == 2
		for _, samples := range p.history {
			recovered = recovered && len(samples) == 1 && !samples[0].failed
		}
		p.mu.RUnlock()
		if recovered {
			break
		}
		time.Sleep(time.Millisecond)
	}
	if !recovered {
		t.Fatal("pool did not refresh both members; obsolete errors may have poisoned history")
	}
	if one.attempts.Load() != 3 || two.attempts.Load() != 3 {
		t.Fatal("network-change retry was not bounded to one")
	}
	seen := map[string]bool{}
	for i := 0; i < 100; i++ {
		out, err := p.pick("tcp")
		if err != nil {
			t.Fatal(err)
		}
		seen[out.Tag()] = true
	}
	if len(seen) != 2 {
		t.Fatal("pool remained pinned to its fallback")
	}
}

func TestPoolNetworkResetReadinessAndClose(t *testing.T) {
	p, a, b := testPool()
	p.record(a.Tag(), sample{at: time.Now(), duration: time.Millisecond})
	p.NetworkResetCompleted(context.Background())
	if len(p.history) != 0 || a.calls.Load()+b.calls.Load() != 0 {
		t.Fatal("reset must clear stale history without starting before readiness")
	}
	p.Close()
	var wg sync.WaitGroup
	for i := 0; i < 20; i++ {
		wg.Add(1)
		go func() { defer wg.Done(); p.NetworkResetCompleted(context.Background()); p.Ready(); p.Close() }()
	}
	wg.Wait()
	if a.calls.Load()+b.calls.Load() != 0 {
		t.Fatal("closed pool restarted its probes")
	}
}
