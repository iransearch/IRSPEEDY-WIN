package main

import (
	"context"
	"fmt"
	"math"
	"math/rand/v2"
	"net"
	"net/http"
	"net/http/httptrace"
	"sort"
	"sync"
	"sync/atomic"
	"time"

	"github.com/sagernet/sing-box/adapter"
	"github.com/sagernet/sing-box/adapter/outbound"
	"github.com/sagernet/sing-box/log"
	M "github.com/sagernet/sing/common/metadata"
	N "github.com/sagernet/sing/common/network"
	"github.com/sagernet/sing/service"
	"github.com/sagernet/sing/service/pause"
)

const poolInterval = 15 * time.Minute
const poolSamples = 2
const poolTimeout = 5 * time.Second
const probeURL = "https://connectivitycheck.gstatic.com/generate_204"

type PoolOptions struct {
	Outbounds []string `json:"outbounds"`
	Fallback  string   `json:"fallback"`
	Expected  int      `json:"expected"`
	MaxRTT    string   `json:"max_rtt"`
	Tolerance float64  `json:"tolerance"`
}
type sample struct {
	at       time.Time
	duration time.Duration
	failed   bool
}
type Pool struct {
	lifecycle   sync.Mutex
	closed      bool
	probeTarget string
	outbound.Adapter
	ctx         context.Context
	cancel      context.CancelFunc
	manager     adapter.OutboundManager
	logger      log.ContextLogger
	options     PoolOptions
	members     []adapter.Outbound
	mu          sync.RWMutex
	history     map[string][]sample
	probeCtx    context.Context
	probeCancel context.CancelFunc
	generation  uint64
	refresh     []chan struct{}
	pause       pause.Manager
	once        sync.Once
	workers     sync.WaitGroup
	maxRTT      time.Duration
}

func registerPool(r *outbound.Registry) { outbound.Register[PoolOptions](r, "irspeedy-pool", newPool) }
func newPool(ctx context.Context, _ adapter.Router, l log.ContextLogger, t string, o PoolOptions) (adapter.Outbound, error) {
	if len(o.Outbounds) == 0 {
		return nil, fmt.Errorf("empty pool")
	}
	owned := false
	for _, m := range o.Outbounds {
		owned = owned || m == o.Fallback
	}
	if !owned {
		return nil, fmt.Errorf("pool fallback must be its own member")
	}
	max, e := time.ParseDuration(o.MaxRTT)
	if e != nil {
		return nil, e
	}
	if o.Expected <= 0 {
		o.Expected = 1
	}
	ctx, cancel := context.WithCancel(ctx)
	return &Pool{Adapter: outbound.NewAdapter("irspeedy-pool", t, []string{N.NetworkTCP, N.NetworkUDP}, o.Outbounds), ctx: ctx, cancel: cancel, manager: service.FromContext[adapter.OutboundManager](ctx), logger: l, options: o, history: map[string][]sample{}, maxRTT: max, pause: service.FromContext[pause.Manager](ctx)}, nil
}
func (p *Pool) Start(stage adapter.StartStage) error {
	if stage != adapter.StartStateStart {
		return nil
	}
	for _, t := range p.options.Outbounds {
		o, ok := p.manager.Outbound(t)
		if !ok {
			return fmt.Errorf("missing pool member %s", t)
		}
		p.members = append(p.members, o)
	}
	return nil
}
func (p *Pool) Close() error {
	p.lifecycle.Lock()
	p.closed = true
	p.cancel()
	p.lifecycle.Unlock()
	p.workers.Wait()
	return nil
}

// Ready is released only after all sing-box services have started.
func (p *Pool) Ready() {
	p.lifecycle.Lock()
	defer p.lifecycle.Unlock()
	if p.closed {
		return
	}
	p.once.Do(func() {
		for _, o := range p.members {
			refresh := make(chan struct{}, 1)
			p.refresh = append(p.refresh, refresh)
			p.workers.Add(1)
			go p.observe(o, refresh)
		}
	})
}

// Called by the core only AFTER protocol connections and DNS transports reset.
// The callback never dials, waits for tests, or restarts the running instance.
func (p *Pool) NetworkResetCompleted(ctx context.Context) {
	if ctx.Err() != nil {
		return
	}
	p.lifecycle.Lock()
	defer p.lifecycle.Unlock()
	if p.closed {
		return
	}
	p.mu.Lock()
	if p.probeCancel != nil {
		p.probeCancel()
	}
	p.probeCtx, p.probeCancel = context.WithCancel(p.ctx)
	p.generation++
	p.history = map[string][]sample{}
	p.mu.Unlock()
	for _, refresh := range p.refresh {
		select {
		case refresh <- struct{}{}:
		default:
		}
	}
}

func (p *Pool) observe(o adapter.Outbound, refresh <-chan struct{}) {
	defer p.workers.Done()
	probe := func() {
		// Coalesce changes already received. A change during the test cancels
		// that generation and leaves another notification for the worker.
		select {
		case <-refresh:
		default:
		}
		p.probe(o)
	}
	probe()
	waitUntil := func(deadline time.Time) bool {
		timer := time.NewTimer(time.Until(deadline))
		defer timer.Stop()
		for {
			select {
			case <-p.ctx.Done():
				return false
			case <-refresh:
				probe()
			case <-timer.C:
				return true
			}
		}
	}
	for { // two samples uniformly spread through each 30 minute sampling window
		offsets := []time.Duration{time.Duration(rand.Int64N(int64(poolInterval * poolSamples))), time.Duration(rand.Int64N(int64(poolInterval * poolSamples)))}
		sort.Slice(offsets, func(i, j int) bool { return offsets[i] < offsets[j] })
		start := time.Now()
		for _, off := range offsets {
			if !waitUntil(start.Add(off)) {
				return
			}
			probe()
		}
		if !waitUntil(start.Add(poolInterval * poolSamples)) {
			return
		}
	}
}
func (p *Pool) probe(o adapter.Outbound) {
	if p.pause != nil && (p.pause.IsDevicePaused() || p.pause.IsNetworkPaused()) {
		return
	}
	p.mu.Lock()
	if p.probeCtx == nil {
		p.probeCtx, p.probeCancel = context.WithCancel(p.ctx)
	}
	base, generation := p.probeCtx, p.generation
	p.mu.Unlock()
	ctx, cancel := context.WithTimeout(base, poolTimeout)
	defer cancel()
	for attempt := 0; attempt < 2; attempt++ {
		// A just-cancelled shared QUIC offer may finish after the network-reset
		// callback. Once it reports network-changed it has been retired: retry
		// once in the SAME budget, without scoring that obsolete handshake.
		if !p.probeAttempt(ctx, generation, o, attempt == 0) {
			return
		}
	}
}

func (p *Pool) probeAttempt(ctx context.Context, generation uint64, o adapter.Outbound, retryNetworkChange bool) bool {
	var phase atomic.Int32 // 0: outbound dial, 1: TLS, 2: HTTP response
	transport := &http.Transport{Proxy: nil, DialContext: func(_ context.Context, n, a string) (net.Conn, error) {
		// net/http detaches dial cancellation from individual requests. Pool
		// dials must instead stop with this network generation and probe budget.
		return o.DialContext(ctx, n, M.ParseSocksaddr(a))
	}, DisableKeepAlives: true}
	defer transport.CloseIdleConnections()
	client := http.Client{Transport: transport, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}
	target := p.probeTarget
	if target == "" {
		target = probeURL
	}
	start := time.Now()
	ctx = httptrace.WithClientTrace(ctx, &httptrace.ClientTrace{
		TLSHandshakeStart: func() { phase.Store(1) },
		GotConn:           func(httptrace.GotConnInfo) { phase.Store(2) },
	})
	req, err := http.NewRequestWithContext(ctx, http.MethodHead, target, nil)
	var res *http.Response
	if err == nil {
		res, err = client.Do(req)
	}
	failed := err != nil
	status := 0
	if res != nil {
		status = res.StatusCode
		failed = failed || res.StatusCode < 200 || res.StatusCode >= 400
		res.Body.Close()
	}
	p.mu.Lock()
	if p.ctx.Err() != nil || generation != p.generation {
		p.mu.Unlock()
		return false
	}
	if retryNetworkChange && ctx.Err() == nil && poolErrorReason(err) == "network-changed" {
		p.mu.Unlock()
		return true
	}
	p.recordLocked(o.Tag(), sample{time.Now(), time.Since(start), failed})
	p.mu.Unlock()
	if failed {
		reason := poolErrorReason(err)
		if err == nil {
			reason = "http-status"
		}
		p.logger.Warn("pool probe failed member=", o.Tag(), " phase=", []string{"dial", "tls", "http"}[phase.Load()], " reason=", reason, " elapsed_ms=", time.Since(start).Milliseconds(), " status=", status)
	} else {
		p.logger.Info("pool probe ok member=", o.Tag(), " elapsed_ms=", time.Since(start).Milliseconds(), " status=", status)
	}
	return false
}
func (p *Pool) record(tag string, s sample) {
	p.mu.Lock()
	defer p.mu.Unlock()
	p.recordLocked(tag, s)
}
func (p *Pool) recordLocked(tag string, s sample) {
	a := append(p.history[tag], s)
	if len(a) > poolSamples {
		a = a[len(a)-poolSamples:]
	}
	p.history[tag] = a
}

type ranked struct {
	o           adapter.Outbound
	avg, dev    float64
	fail, count int
}

func (p *Pool) pick(network string) (adapter.Outbound, error) {
	p.mu.RLock()
	defer p.mu.RUnlock()
	var nodes []ranked
	var fallback adapter.Outbound
	for _, o := range p.members {
		supports := false
		for _, n := range o.Network() {
			supports = supports || network == n
		}
		if !supports {
			continue
		}
		if o.Tag() == p.options.Fallback {
			fallback = o
		}
		a := p.history[o.Tag()]
		r := ranked{o: o}
		var values []float64
		for _, s := range a {
			if time.Since(s.at) > poolInterval*poolSamples*2 {
				continue
			}
			r.count++
			if s.failed {
				r.fail++
				continue
			}
			values = append(values, float64(s.duration))
			r.avg += float64(s.duration)
		}
		if len(values) == 0 || r.count == 0 {
			continue
		}
		if p.options.Tolerance > 0 && float64(r.fail)/float64(r.count) > p.options.Tolerance {
			continue
		}
		r.avg /= float64(len(values))
		if r.avg >= float64(p.maxRTT) {
			continue
		}
		for _, v := range values {
			r.dev += (v - r.avg) * (v - r.avg)
		}
		r.dev = math.Sqrt(r.dev / float64(len(values)))
		if len(values) == 1 {
			r.dev = r.avg / 2
		}
		nodes = append(nodes, r)
	}
	sort.Slice(nodes, func(i, j int) bool {
		a, b := nodes[i], nodes[j]
		if a.dev != b.dev {
			return a.dev < b.dev
		}
		if a.avg != b.avg {
			return a.avg < b.avg
		}
		if a.fail != b.fail {
			return a.fail < b.fail
		}
		if a.count != b.count {
			return a.count > b.count
		}
		return a.o.Tag() < b.o.Tag()
	})
	n := len(nodes)
	if n > p.options.Expected {
		n = p.options.Expected
	}
	if n > 0 {
		return nodes[rand.IntN(n)].o, nil
	}
	if fallback != nil {
		return fallback, nil
	}
	return nil, fmt.Errorf("pool has no eligible %s member", network)
}
func (p *Pool) DialContext(ctx context.Context, n string, d M.Socksaddr) (net.Conn, error) {
	o, e := p.pick(n)
	if e != nil {
		return nil, e
	}
	p.logger.InfoContext(ctx, "pool-route outbound=", o.Tag())
	start := time.Now()
	conn, err := o.DialContext(ctx, n, d)
	if err != nil {
		p.logger.WarnContext(ctx, "pool dial failed member=", o.Tag(), " reason=", poolErrorReason(err), " elapsed_ms=", time.Since(start).Milliseconds())
	}
	return conn, err
}
func (p *Pool) ListenPacket(ctx context.Context, d M.Socksaddr) (net.PacketConn, error) {
	o, e := p.pick(N.NetworkUDP)
	if e != nil {
		return nil, e
	}
	p.logger.InfoContext(ctx, "pool-route outbound=", o.Tag())
	start := time.Now()
	conn, err := o.ListenPacket(ctx, d)
	if err != nil {
		p.logger.WarnContext(ctx, "pool dial failed member=", o.Tag(), " reason=", poolErrorReason(err), " elapsed_ms=", time.Since(start).Milliseconds())
	}
	return conn, err
}
