package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"net"
	"net/rpc"
	"os"
	"os/signal"
	"sync"
	"sync/atomic"
	"time"

	"github.com/chai2010/protorpc"
	box "github.com/sagernet/sing-box"
	"github.com/sagernet/sing-box/adapter"
	"github.com/sagernet/sing-box/include"
	"github.com/sagernet/sing-box/option"
	sjson "github.com/sagernet/sing/common/json"
)

const coreVersion = "sing-box-extended/v1.14.1-extended-2.7.2+irspeedy.2"

var instanceSequence atomic.Uint64

type instance struct {
	box    *box.Box
	cancel context.CancelFunc
}

func (i *instance) close() {
	if i != nil {
		i.cancel()
		_ = i.box.Close()
	}
}
func createInstance(ctx context.Context, native, legacy string, start bool) (*instance, error) {
	data, err := mergeConfig(native, legacy)
	if err != nil {
		return nil, err
	}
	// Register the pool inside the core. No app timer or public-IP health check.
	registry := include.OutboundRegistry()
	registerPool(registry)
	ctx = box.Context(ctx, include.InboundRegistry(), registry, include.EndpointRegistry(), include.ProviderRegistry(), include.DNSTransportRegistry(), include.ServiceRegistry(), include.CertificateProviderRegistry())
	ctx, cancel := context.WithCancel(ctx)
	opts, err := sjson.UnmarshalExtendedContext[option.Options](ctx, data)
	if err != nil {
		cancel()
		return nil, err
	}
	b, err := box.New(box.Options{Options: opts, Context: ctx})
	if err != nil {
		cancel()
		return nil, err
	}
	i := &instance{b, cancel}
	if start {
		if err = b.Start(); err != nil {
			i.close()
			return nil, err
		}
		id := instanceSequence.Add(1)
		fmt.Printf("[CoreDiagnostic] schema=core-network-v2 event=pool-probes-ready pid=%d seq=%d box=%d\n", os.Getpid(), id, id)
		for _, out := range b.Outbound().Outbounds() {
			if p, ok := out.(*Pool); ok {
				p.Ready()
			}
		}
	}
	return i, nil
}

type Server struct {
	life       sync.Mutex
	running    *instance
	testMu     sync.Mutex
	testCancel context.CancelFunc
	testSerial sync.Mutex
	results    []*URLTestResp
	probe      chan struct{}
	probeOnce  sync.Once
}

func (s *Server) Start(in *LoadConfigReq, out *ErrorResp) error {
	s.life.Lock()
	defer s.life.Unlock()
	if in.NeedExtraProcess {
		out.Error = "external cores are disabled in the extended build"
		return nil
	}
	if s.running != nil {
		out.Error = "Stop must complete before Start"
		return nil
	}
	legacy := ""
	if in.NeedXray {
		legacy = in.XrayConfig
	}
	var err error
	s.running, err = createInstance(context.Background(), in.CoreConfig, legacy, true)
	if err != nil {
		out.Error = err.Error()
	}
	return nil
}
func (s *Server) Stop(_ *EmptyReq, out *ErrorResp) error {
	s.life.Lock()
	defer s.life.Unlock()
	s.running.close()
	s.running = nil
	return nil
}
func (s *Server) CheckConfig(in *LoadConfigReq, out *ErrorResp) error {
	legacy := ""
	if in.NeedXray {
		legacy = in.XrayConfig
	}
	i, e := createInstance(context.Background(), in.CoreConfig, legacy, false)
	if e != nil {
		out.Error = e.Error()
	} else {
		i.close()
	}
	return nil
}
func (s *Server) IsPrivileged(_ *EmptyReq, out *IsPrivilegedResponse) error {
	out.HasPrivilege = isPrivileged()
	if s.probe != nil {
		s.probeOnce.Do(func() { close(s.probe) })
	}
	return nil
}
func (s *Server) StopTest(_ *EmptyReq, _ *EmptyResp) error {
	s.testMu.Lock()
	defer s.testMu.Unlock()
	if s.testCancel != nil {
		s.testCancel()
	}
	return nil
}
func (s *Server) QueryURLTest(_ *EmptyReq, out *TestResp) error {
	s.testMu.Lock()
	defer s.testMu.Unlock()
	out.Results = append([]*URLTestResp(nil), s.results...)
	return nil
}
func (s *Server) Test(in *TestReq, out *TestResp) error {
	s.testSerial.Lock()
	defer s.testSerial.Unlock()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	s.testMu.Lock()
	s.testCancel = cancel
	s.results = nil
	s.testMu.Unlock()
	defer func() { s.testMu.Lock(); s.testCancel = nil; s.testMu.Unlock() }()
	var i *instance
	if in.TestCurrent {
		s.life.Lock()
		defer s.life.Unlock()
		i = s.running
		if i == nil {
			return errors.New("core is not running")
		}
	} else {
		legacy := ""
		if in.NeedXray {
			legacy = in.XrayConfig
		}
		var e error
		i, e = createInstance(ctx, in.Config, legacy, true)
		if e != nil {
			return e
		}
		defer i.close()
	}
	tags := in.OutboundTags
	if in.UseDefaultOutbound {
		tags = []string{i.box.Outbound().Default().Tag()}
	}
	concurrency := int(in.MaxConcurrency)
	if concurrency <= 0 {
		concurrency = 8
	}
	if concurrency > 64 {
		concurrency = 64
	}
	timeout := time.Duration(in.TestTimeoutMs) * time.Millisecond
	if timeout <= 0 {
		timeout = 5 * time.Second
	}
	if timeout > time.Minute {
		timeout = time.Minute
	}
	sem := make(chan struct{}, concurrency)
	var wg sync.WaitGroup
	for _, t := range tags {
		if ctx.Err() != nil {
			break
		}
		select {
		case sem <- struct{}{}:
		case <-ctx.Done():
			break
		}
		if ctx.Err() != nil {
			break
		}
		wg.Add(1)
		go func(t string) {
			defer wg.Done()
			defer func() { <-sem }()
			r := &URLTestResp{OutboundTag: t, LatencyMs: -1}
			o, ok := i.box.Outbound().Outbound(t)
			if !ok {
				r.Error = "outbound not found"
			} else {
				duration, e := initialURLTest(ctx, in.Url, o, !in.TestCurrent, timeout)
				if e != nil {
					r.Error = e.Error()
				} else {
					r.LatencyMs = int32(duration.Milliseconds())
				}
			}
			s.testMu.Lock()
			s.results = append(s.results, r)
			s.testMu.Unlock()
		}(t)
	}
	wg.Wait()
	return s.QueryURLTest(&EmptyReq{}, out)
}

var _ adapter.Outbound = (*Pool)(nil)

func main() {
	port := flag.Int("port", 19810, "loopback RPC port")
	probe := flag.Bool("probe-mode", false, "probe RPC and exit")
	version := flag.Bool("version", false, "print version")
	flag.Parse()
	if *version {
		fmt.Println(coreVersion)
		return
	}
	ln, e := net.Listen("tcp4", fmt.Sprintf("127.0.0.1:%d", *port))
	if e != nil {
		fmt.Fprintln(os.Stderr, e)
		os.Exit(1)
	}
	defer ln.Close()
	s := &Server{}
	r := rpc.NewServer()
	if e = r.RegisterName("LibcoreService", s); e != nil {
		panic(e)
	}
	if *probe {
		s.probe = make(chan struct{})
		fmt.Println("PROBE_READY", ln.Addr())
		_ = ln.(*net.TCPListener).SetDeadline(time.Now().Add(10 * time.Second))
		c, e := ln.Accept()
		if e != nil {
			os.Exit(1)
		}
		_ = c.SetDeadline(time.Now().Add(10 * time.Second))
		r.ServeCodec(protorpc.NewServerCodec(c))
		select {
		case <-s.probe:
			return
		default:
			os.Exit(1)
		}
	}
	fmt.Println("Core ProtoRPC ready at", ln.Addr(), coreVersion)
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, os.Interrupt)
	go func() { <-sig; _ = s.Stop(&EmptyReq{}, &ErrorResp{}); ln.Close() }()
	for {
		c, e := ln.Accept()
		if e != nil {
			return
		}
		go r.ServeCodec(protorpc.NewServerCodec(c))
	}
}
