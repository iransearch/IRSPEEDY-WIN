package main

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"sync/atomic"
	"testing"
	"time"
)

func TestInitialURLTestMeasuresWarmGET(t *testing.T) {
	var requests atomic.Int32
	h := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet {
			t.Errorf("method = %s", r.Method)
		}
		if requests.Add(1) == 1 {
			time.Sleep(300 * time.Millisecond)
		}
		w.WriteHeader(http.StatusNoContent)
	}))
	defer h.Close()
	member := &fakeMember{}
	duration, err := initialURLTest(context.Background(), h.URL, member, true, 2*time.Second)
	if err != nil {
		t.Fatal(err)
	}
	if requests.Load() != 2 || member.calls.Load() != 1 {
		t.Fatalf("requests=%d dials=%d; connection must be reused", requests.Load(), member.calls.Load())
	}
	if duration >= 200*time.Millisecond {
		t.Fatalf("warm-up counted in latency: %s", duration)
	}
}

func TestConnectedURLTestSendsOneGET(t *testing.T) {
	var requests atomic.Int32
	h := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet {
			t.Errorf("method = %s", r.Method)
		}
		requests.Add(1)
		w.WriteHeader(204)
	}))
	defer h.Close()
	if _, err := initialURLTest(context.Background(), h.URL, &fakeMember{}, false, time.Second); err != nil {
		t.Fatal(err)
	}
	if requests.Load() != 1 {
		t.Fatalf("connected requests=%d", requests.Load())
	}
}

func TestStopTestCancelsBothGETStagesWithoutStoppingVPN(t *testing.T) {
	for _, stage := range []int32{1, 2} {
		t.Run(string(rune('0'+stage)), func(t *testing.T) {
			var requests atomic.Int32
			entered := make(chan struct{})
			h := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				if requests.Add(1) == stage {
					close(entered)
					<-r.Context().Done()
					return
				}
				w.WriteHeader(204)
			}))
			defer h.Close()
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			active := &instance{}
			server := &Server{testCancel: cancel, running: active}
			result := make(chan error, 1)
			go func() { _, err := initialURLTest(ctx, h.URL, &fakeMember{}, true, 30*time.Second); result <- err }()
			select {
			case <-entered:
			case <-time.After(3 * time.Second):
				t.Fatal("request did not start")
			}
			server.StopTest(&EmptyReq{}, &EmptyResp{})
			select {
			case err := <-result:
				if !errors.Is(err, context.Canceled) {
					t.Fatalf("cancel result: %v", err)
				}
			case <-time.After(time.Second):
				t.Fatal("StopTest did not cancel promptly")
			}
			if server.running != active {
				t.Fatal("StopTest altered production VPN")
			}
			if requests.Load() != stage {
				t.Fatal("request started after cancellation")
			}
		})
	}
}

func TestURLTestWarmupFailureStopsMeasurement(t *testing.T) {
	var requests atomic.Int32
	h := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		requests.Add(1)
		<-r.Context().Done()
	}))
	defer h.Close()
	_, err := initialURLTest(context.Background(), h.URL, &fakeMember{}, true, 100*time.Millisecond)
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("expected timeout: %v", err)
	}
	if requests.Load() != 1 {
		t.Fatal("measurement ran after failed warmup")
	}
}
