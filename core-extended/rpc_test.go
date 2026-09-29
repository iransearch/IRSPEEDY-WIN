package main

import (
	"context"
	"github.com/chai2010/protorpc"
	"net"
	"net/http"
	"net/http/httptest"
	"net/rpc"
	"runtime"
	"strings"
	"testing"
)

func TestRPCProbeContract(t *testing.T) {
	a, b := net.Pipe()
	defer a.Close()
	defer b.Close()
	s := rpc.NewServer()
	service := &Server{}
	if e := s.RegisterName("LibcoreService", service); e != nil {
		t.Fatal(e)
	}
	go s.ServeCodec(protorpc.NewServerCodec(a))
	client := rpc.NewClientWithCodec(protorpc.NewClientCodec(b))
	defer client.Close()
	var out IsPrivilegedResponse
	if e := client.Call("LibcoreService.IsPrivileged", &EmptyReq{}, &out); e != nil {
		t.Fatal(e)
	}
	var errOut ErrorResp
	if e := client.Call("LibcoreService.Start", &LoadConfigReq{CoreConfig: "invalid"}, &errOut); e != nil {
		t.Fatal(e)
	}
	if errOut.Error == "" {
		t.Fatal("invalid config accepted")
	}
}
func TestNativeURLTestLifecycle(t *testing.T) {
	h := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.WriteHeader(204) }))
	defer h.Close()
	native := `{"outbounds":[{"type":"direct","tag":"direct"}]}`
	i, e := createInstance(context.Background(), native, "", true)
	if e != nil {
		if runtime.GOOS == "linux" && strings.Contains(e.Error(), "netlink socket: operation not permitted") {
			t.Skip("environment denies netlink; run lifecycle test on Windows builder")
		}
		t.Fatal(e)
	}
	i.close()
	s := &Server{}
	out := &TestResp{}
	e = s.Test(&TestReq{Config: native, OutboundTags: []string{"direct"}, Url: h.URL, MaxConcurrency: 2, TestTimeoutMs: 1000}, out)
	if e != nil {
		t.Fatal(e)
	}
	if len(out.Results) != 1 || out.Results[0].Error != "" {
		t.Fatal(out)
	}
	if s.running != nil {
		t.Fatal("test altered production instance")
	}
}
