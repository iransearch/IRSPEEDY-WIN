package main

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"github.com/golang/protobuf/proto"
	stls "github.com/sagernet/sing-box/common/tls"
	"os"
	"strings"
	"testing"
)

func sampleConfig() (string, string) {
	base := `{"dns":{"servers":[{"type":"udp","tag":"dns-direct","server":"1.1.1.1"}]},"outbounds":[{"type":"direct","tag":"direct"},{"type":"socks","tag":"proxy","server":"127.0.0.1","server_port":19232}],"route":{"final":"proxy"}}`
	x := `{"inbounds":[{"tag":"smart-inbound","port":19232}],"outbounds":[{"protocol":"vless","tag":"smart-proxy-0","settings":{"address":"127.0.0.1","port":443,"id":"00000000-0000-0000-0000-000000000001"},"streamSettings":{"network":"xhttp","security":"none","xhttpSettings":{"host":"example.com","path":"/NEWS","mode":"auto"}}},{"protocol":"hysteria2","tag":"ai-proxy-1","settings":{"server":"127.0.0.1","server_port":8443,"password":"test","tls":{"enabled":true,"server_name":"example.com"}}},{"protocol":"blackhole","tag":"block"}],"routing":{"balancers":[{"tag":"main","selector":["smart-proxy-"],"strategy":{"settings":{"expected":5,"maxRTT":"3s","tolerance":0.2}}},{"tag":"ai","selector":["ai-proxy-"],"fallbackTag":"ai-proxy-1","strategy":{"settings":{"expected":5,"maxRTT":"5s","tolerance":0.2}}}],"rules":[{"domain":["domain:openai.com"],"balancerTag":"ai"},{"network":"tcp,udp","balancerTag":"main"}]}}`
	return base, x
}
func TestNativePoolAndAIIsolation(t *testing.T) {
	base, x := sampleConfig()
	b, e := mergeConfig(base, x)
	if e != nil {
		t.Fatal(e)
	}
	var root object
	json.Unmarshal(b, &root)
	outs := list(root["outbounds"])
	for _, v := range outs {
		o := obj(v)
		if str(o["type"]) == "irspeedy-pool" && str(o["tag"]) == tag("ai") {
			if str(o["fallback"]) != tag("ai-proxy-1") {
				t.Fatal(o)
			}
			for _, m := range stringsOf(o["outbounds"]) {
				if !strings.HasPrefix(m, tag("ai-proxy-")) {
					t.Fatal("AI escape", m)
				}
			}
		}
	}
	for _, r := range list(obj(root["route"])["rules"]) {
		if len(stringsOf(obj(r)["inbound"])) == 0 {
			t.Fatal("unscoped internal rule", r)
		}
	}
	i, e := createInstance(context.Background(), base, x, false)
	if e != nil {
		t.Fatal(e)
	}
	i.close()
}
func TestFallbackCannotEscapeAI(t *testing.T) {
	base, x := sampleConfig()
	x = strings.Replace(x, `"fallbackTag":"ai-proxy-1"`, `"fallbackTag":"smart-proxy-0"`, 1)
	if _, e := mergeConfig(base, x); e == nil {
		t.Fatal("accepted AI -> main fallback")
	}
}
func TestEmptyAIBlocks(t *testing.T) {
	base, x := sampleConfig()
	var xr object
	json.Unmarshal([]byte(x), &xr)
	route := obj(xr["routing"])
	route["balancers"] = list(route["balancers"])[:1]
	obj(list(route["rules"])[0])["outboundTag"] = "block"
	delete(obj(list(route["rules"])[0]), "balancerTag")
	d, _ := json.Marshal(xr)
	b, e := mergeConfig(base, string(d))
	if e != nil {
		t.Fatal(e)
	}
	var root object
	json.Unmarshal(b, &root)
	r := obj(list(obj(root["route"])["rules"])[1])
	if str(r["action"]) != "reject" {
		t.Fatal(r)
	}
}
func TestCertificatePinIsNotPublicKeyPin(t *testing.T) {
	der := []byte("test DER bytes")
	sum := sha256.Sum256(der)
	tls, e := nativeTLS(object{"pinnedPeerCertSha256": hex.EncodeToString(sum[:])}, "tls")
	if e != nil || tls["certificate_sha256"] == nil || tls["certificate_public_key_sha256"] != nil {
		t.Fatal(tls, e)
	}
	if e = stls.VerifyCertificateSHA256([][]byte{sum[:]}, [][]byte{der}); e != nil {
		t.Fatal(e)
	}
	if stls.VerifyCertificateSHA256([][]byte{sum[:]}, [][]byte{[]byte("other cert")}) == nil {
		t.Fatal("wrong certificate accepted")
	}
}
func TestXHTTPDownloadConversion(t *testing.T) {
	tr, e := xhttpTransport(object{"mode": "auto", "extra": object{"downloadSettings": object{"address": "download.example", "port": float64(8443), "security": "tls", "tlsSettings": object{"serverName": "download.sni", "pinnedPeerCertSha256": strings.Repeat("a", 64)}, "xhttpSettings": object{"host": "download.host", "path": "/download"}}}})
	if e != nil {
		t.Fatal(e)
	}
	d := obj(tr["download"])
	if d["server"] != "download.example" || d["host"] != "download.host" || obj(d["tls"])["server_name"] != "download.sni" {
		t.Fatal(tr)
	}
	if _, bad := d["domain_resolver"]; bad {
		t.Fatal("not part of native download schema")
	}
}
func TestWireContract(t *testing.T) {
	in := &TestReq{Config: "native", OutboundTags: []string{"a", "b"}, NeedXray: true, XrayConfig: "legacy", TestTimeoutMs: 5000}
	b, e := proto.Marshal(in)
	if e != nil {
		t.Fatal(e)
	}
	var out TestReq
	if e = proto.Unmarshal(b, &out); e != nil {
		t.Fatal(e)
	}
	if !proto.Equal(in, &out) {
		t.Fatal("RPC round trip")
	}
}

func TestInvalidAIConvertedToBlock(t *testing.T) {
	base, x := sampleConfig()
	x = strings.Replace(x, `"protocol":"hysteria2"`, `"protocol":"invalid"`, 1)
	b, e := mergeConfig(base, x)
	if e != nil {
		t.Fatal(e)
	}
	var root object
	json.Unmarshal(b, &root)
	found := false
	for _, v := range list(root["outbounds"]) {
		o := obj(v)
		if o["tag"] == tag("ai") {
			found = true
			if o["type"] != "block" {
				t.Fatal("empty AI was not blocked", o)
			}
		}
	}
	if !found {
		t.Fatal("AI target missing")
	}
}

func TestWindowsBaseConfig(t *testing.T) {
	base, e := os.ReadFile("testdata/windows-base.json")
	if e != nil {
		t.Fatal(e)
	}
	_, x := sampleConfig()
	i, e := createInstance(context.Background(), string(base), x, false)
	if e != nil {
		t.Fatal(e)
	}
	i.close()
}
func TestDownloadTLSOptionsValidate(t *testing.T) {
	base, x := sampleConfig()
	var xr object
	json.Unmarshal([]byte(x), &xr)
	stream := obj(obj(list(xr["outbounds"])[0])["streamSettings"])
	stream["security"] = "tls"
	stream["tlsSettings"] = object{"serverName": "up.example", "pinnedPeerCertSha256": strings.Repeat("a", 64)}
	obj(stream["xhttpSettings"])["extra"] = object{"downloadSettings": object{"address": "down.example", "port": float64(443), "security": "tls", "tlsSettings": object{"serverName": "down.sni", "pinnedPeerCertSha256": strings.Repeat("b", 64)}, "xhttpSettings": object{"host": "down.host", "path": "/download"}}}
	data, _ := json.Marshal(xr)
	i, e := createInstance(context.Background(), base, string(data), false)
	if e != nil {
		t.Fatal(e)
	}
	i.close()
}
