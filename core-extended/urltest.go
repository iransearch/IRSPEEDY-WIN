package main

import (
	"context"
	"net"
	"net/http"
	"sync"
	"time"

	M "github.com/sagernet/sing/common/metadata"
	N "github.com/sagernet/sing/common/network"
)

// initialURLTest follows the existing Windows/Throne measurement contract:
// warm up a standalone test with GET, then report only the second GET. A test
// against the connected instance sends one GET. Each request has its own budget.
func initialURLTest(ctx context.Context, link string, outbound N.Dialer, warm bool, timeout time.Duration) (time.Duration, error) {
	if link == "" {
		link = "https://www.gstatic.com/generate_204"
	}
	dialCtx, cancelDials := context.WithCancel(ctx)
	defer cancelDials()
	var mu sync.Mutex
	var conns []net.Conn
	closed := false
	transport := &http.Transport{Proxy: nil, DialContext: func(_ context.Context, network, address string) (net.Conn, error) {
		// Request contexts end between warm-up and measurement. Keep the tunnel
		// alive for both, while StopTest cancels the batch and all outstanding dials.
		conn, err := outbound.DialContext(dialCtx, network, M.ParseSocksaddr(address))
		if err != nil {
			return nil, err
		}
		mu.Lock()
		if closed {
			mu.Unlock()
			go conn.Close()
			return nil, net.ErrClosed
		}
		conns = append(conns, conn)
		mu.Unlock()
		return conn, nil
	}}
	defer func() {
		cancelDials()
		mu.Lock()
		closed = true
		owned := conns
		conns = nil
		mu.Unlock()
		// A tunnel's close handshake must not stall cancellation of the batch.
		for _, conn := range owned {
			go conn.Close()
		}
		transport.CloseIdleConnections()
	}()
	client := &http.Client{Transport: transport}
	request := func() (time.Duration, error) {
		requestCtx, cancel := context.WithTimeout(ctx, timeout)
		defer cancel()
		begin := time.Now()
		req, err := http.NewRequestWithContext(requestCtx, http.MethodGet, link, nil)
		if err != nil {
			return 0, err
		}
		response, err := client.Do(req)
		if err != nil {
			return 0, err
		}
		_ = response.Body.Close()
		return time.Since(begin), nil
	}
	if warm {
		if _, err := request(); err != nil {
			return 0, err
		}
	}
	return request()
}
