package main

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/sagernet/sing-box/log"
	"github.com/sagernet/sing-box/option"
)

func TestPoolErrorReasonsDoNotExposeEndpoints(t *testing.T) {
	for _, tc := range []struct {
		err  error
		want string
	}{
		{fmt.Errorf("private.example: %w", context.DeadlineExceeded), "timeout"},
		{fmt.Errorf("secret: %w", context.Canceled), "canceled"},
		{errors.New("network changed: private.example"), "network-changed"},
		{errors.New("connect error: timeout: no recent network activity"), "quic-timeout"},
		{&net.DNSError{Name: "private.example", Err: "server failure"}, "dns-error"},
		{errors.New("x509: certificate is valid for private.example"), "certificate-error"},
		{errors.New("password=secret private.example"), "other"},
	} {
		if got := poolErrorReason(tc.err); got != tc.want {
			t.Fatalf("want %s, got %s", tc.want, got)
		}
	}
}

func TestPoolProbeReportsHTTPFailureAndSuccess(t *testing.T) {
	for _, status := range []int{503, 204} {
		t.Run(fmt.Sprint(status), func(t *testing.T) {
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(status) }))
			defer server.Close()
			p, a, _ := testPool()
			defer p.Close()
			var output bytes.Buffer
			factory, err := log.New(log.Options{Context: context.Background(), DefaultWriter: &output, Options: option.LogOptions{DisableColor: true}})
			if err != nil {
				t.Fatal(err)
			}
			defer factory.Close()
			if err := factory.Start(); err != nil {
				t.Fatal(err)
			}
			p.logger = factory.NewLogger("test")
			p.probeTarget = server.URL
			p.probe(a)
			line := output.String()
			if !strings.Contains(line, fmt.Sprintf("status=%d", status)) {
				t.Fatal(line)
			}
			if status == 503 && (!strings.Contains(line, "phase=http reason=http-status") || !p.history[a.Tag()][0].failed) {
				t.Fatal(line)
			}
			if status == 204 && (!strings.Contains(line, "pool probe ok") || p.history[a.Tag()][0].failed) {
				t.Fatal(line)
			}
			if strings.Contains(line, server.URL) {
				t.Fatal("probe target leaked")
			}
		})
	}
}
