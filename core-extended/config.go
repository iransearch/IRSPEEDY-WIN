package main

// This adapter accepts the existing Windows configuration contract. It creates
// only native sing-box outbounds; it does not embed or start the Xray core.
import (
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"strings"
)

type object = map[string]any

func obj(v any) object {
	m, _ := v.(map[string]any)
	if m == nil {
		return object{}
	}
	return m
}
func list(v any) []any { a, _ := v.([]any); return a }
func str(v any) string { s, _ := v.(string); return s }
func number(v any) int { f, _ := v.(float64); return int(f) }
func stringsOf(v any) []string {
	if s, ok := v.(string); ok {
		return []string{s}
	}
	var r []string
	for _, x := range list(v) {
		r = append(r, str(x))
	}
	return r
}
func tag(s string) string { return "extended/" + s }
func copyKeys(dst, src object, keys map[string]string) {
	for a, b := range keys {
		if v, ok := src[a]; ok && v != nil {
			dst[b] = v
		}
	}
}

func nativeTLS(s object, security string) (object, error) {
	if security == "" || security == "none" {
		return nil, nil
	}
	if security != "tls" && security != "reality" {
		return nil, fmt.Errorf("unsupported TLS security %s", security)
	}
	t := object{"enabled": true}
	copyKeys(t, s, map[string]string{"serverName": "server_name", "allowInsecure": "insecure", "alpn": "alpn"})
	if fp := str(s["fingerprint"]); fp != "" {
		t["utls"] = object{"enabled": true, "fingerprint": fp}
	}
	if security == "reality" {
		key := str(s["publicKey"])
		if key == "" {
			key = str(s["password"])
		}
		t["reality"] = object{"enabled": true, "public_key": key, "short_id": s["shortId"]}
	}
	if pin := s["pinnedPeerCertSha256"]; pin != nil {
		var pins []string
		for _, p := range stringsOf(pin) {
			if p == "" {
				continue
			}
			b, e := hex.DecodeString(strings.ReplaceAll(p, ":", ""))
			if e != nil || len(b) != 32 {
				b, e = base64.StdEncoding.DecodeString(p)
			}
			if e != nil || len(b) != 32 {
				return nil, fmt.Errorf("invalid certificate SHA256 pin")
			}
			pins = append(pins, base64.StdEncoding.EncodeToString(b))
		}
		if len(pins) > 0 {
			t["certificate_sha256"] = pins
		}
	}
	return t, nil
}
func xhttpTransport(s object) (object, error) {
	t := object{"type": "xhttp", "x_padding_bytes": "100-1000"}
	copyKeys(t, s, map[string]string{"host": "host", "path": "path", "mode": "mode"})
	names := map[string]string{"headers": "headers", "xPaddingBytes": "x_padding_bytes", "noGRPCHeader": "no_grpc_header", "noSSEHeader": "no_sse_header", "scMaxEachPostBytes": "sc_max_each_post_bytes", "scMinPostsIntervalMs": "sc_min_posts_interval_ms", "scMaxBufferedPosts": "sc_max_buffered_posts", "scStreamUpServerSecs": "sc_stream_up_server_secs", "xPaddingObfsMode": "x_padding_obfs_mode", "xPaddingKey": "x_padding_key", "xPaddingHeader": "x_padding_header", "xPaddingPlacement": "x_padding_placement", "xPaddingMethod": "x_padding_method", "uplinkHTTPMethod": "uplink_http_method", "sessionPlacement": "session_placement", "sessionKey": "session_key", "seqPlacement": "seq_placement", "seqKey": "seq_key", "uplinkDataPlacement": "uplink_data_placement", "uplinkDataKey": "uplink_data_key", "uplinkChunkSize": "uplink_chunk_size"}
	extra := obj(s["extra"])
	copyKeys(t, extra, names)
	if x := obj(extra["xmux"]); len(x) > 0 {
		m := object{}
		copyKeys(m, x, map[string]string{"maxConcurrency": "max_concurrency", "maxConnections": "max_connections", "cMaxReuseTimes": "c_max_reuse_times", "hMaxRequestTimes": "h_max_request_times", "hMaxReusableSecs": "h_max_reusable_secs", "hKeepAlivePeriod": "h_keep_alive_period"})
		t["xmux"] = m
	}
	if d := obj(extra["downloadSettings"]); len(d) > 0 {
		ds := obj(d["xhttpSettings"])
		if len(ds) == 0 {
			ds = d
		}
		dt, e := xhttpTransport(ds)
		if e != nil {
			return nil, e
		}
		delete(dt, "type")
		delete(dt, "mode")
		delete(dt, "download")
		copyKeys(dt, d, map[string]string{"address": "server", "port": "server_port"})
		sec := str(d["security"])
		if sec == "" && len(obj(d["tlsSettings"])) > 0 {
			sec = "tls"
		}
		tls, e := nativeTLS(obj(d[sec+"Settings"]), sec)
		if e != nil {
			return nil, e
		}
		if tls != nil {
			dt["tls"] = tls
		}

		t["download"] = dt
	}
	return t, nil
}
func nativeOutbound(x object) (object, error) {
	p := str(x["protocol"])
	s := obj(x["settings"])
	o := object{"tag": tag(str(x["tag"]))}
	switch p {
	case "freedom":
		o["type"] = "direct"
		return o, nil
	case "blackhole":
		o["type"] = "block"
		return o, nil
	case "hysteria2":
		o["type"] = "hysteria2"
		copyKeys(o, s, map[string]string{"server": "server", "server_port": "server_port", "password": "password", "obfs": "obfs", "tls": "tls"})
	case "vless", "vmess", "trojan", "shadowsocks", "socks", "http":
		o["type"] = p
		copyKeys(o, s, map[string]string{"address": "server", "port": "server_port", "id": "uuid", "password": "password", "method": "method", "flow": "flow"})
		if p == "vless" {
			if e := str(s["encryption"]); e != "" && e != "none" {
				o["encryption"] = e
			}
		}
		if p == "vmess" {
			copyKeys(o, s, map[string]string{"security": "security", "alterId": "alter_id"})
		}
		if users := list(s["users"]); len(users) > 0 {
			copyKeys(o, obj(users[0]), map[string]string{"user": "username", "pass": "password"})
		}
	default:
		return nil, fmt.Errorf("unsupported protocol %q", p)
	}
	o["domain_resolver"] = "dns-direct"
	stream := obj(x["streamSettings"])
	sec := str(stream["security"])
	tls, e := nativeTLS(obj(stream[sec+"Settings"]), sec)
	if e != nil {
		return nil, e
	}
	if tls != nil {
		o["tls"] = tls
	}
	var tr object
	switch net := str(stream["network"]); net {
	case "", "raw", "tcp":
		if str(obj(obj(stream["tcpSettings"])["header"])["type"]) == "http" {
			return nil, fmt.Errorf("legacy TCP HTTP camouflage is not supported by this native adapter")
		}
	case "xhttp":
		tr, e = xhttpTransport(obj(stream["xhttpSettings"]))
		if e != nil {
			return nil, e
		}
	case "ws":
		s := obj(stream["wsSettings"])
		tr = object{"type": "ws"}
		copyKeys(tr, s, map[string]string{"path": "path", "headers": "headers", "maxEarlyData": "max_early_data", "earlyDataHeaderName": "early_data_header_name"})
	case "h2", "http":
		tr = object{"type": "http"}
		copyKeys(tr, obj(stream["httpSettings"]), map[string]string{"path": "path", "host": "host"})
	case "grpc":
		tr = object{"type": "grpc"}
		copyKeys(tr, obj(stream["grpcSettings"]), map[string]string{"serviceName": "service_name"})
	case "httpupgrade":
		tr = object{"type": "httpupgrade"}
		copyKeys(tr, obj(stream["httpupgradeSettings"]), map[string]string{"path": "path", "host": "host"})
	case "kcp":
		tr = object{"type": "kcp"}
		copyKeys(tr, obj(stream["kcpSettings"]), map[string]string{"seed": "seed"})
		if h := str(obj(obj(stream["kcpSettings"])["header"])["type"]); h != "" {
			tr["header_type"] = h
		}
	default:
		return nil, fmt.Errorf("unsupported transport %q", net)
	}
	if tr != nil {
		o["transport"] = tr
	}
	return o, nil
}

func mergeConfig(native, legacy string) ([]byte, error) {
	var root object
	if err := json.Unmarshal([]byte(native), &root); err != nil {
		return nil, err
	}
	if err := normalizeNative(root); err != nil {
		return nil, err
	}
	if strings.TrimSpace(legacy) == "" || strings.TrimSpace(legacy) == "{}" {
		return json.Marshal(root)
	}
	var xr object
	if err := json.Unmarshal([]byte(legacy), &xr); err != nil {
		return nil, err
	}
	outs := list(root["outbounds"])
	xo := list(xr["outbounds"])
	if len(xo) == 0 {
		return nil, fmt.Errorf("missing native proxy candidates")
	}
	nativeTags := map[string]bool{}
	for _, o := range outs {
		nativeTags[str(obj(o)["tag"])] = true
	}
	converted := map[string]bool{}
	rejected := map[string]bool{}
	for _, v := range xo {
		x := obj(v)
		o, e := nativeOutbound(x)
		if e != nil {
			name := str(x["tag"])
			if strings.HasPrefix(name, "ai-proxy-") || strings.HasPrefix(name, "smart-proxy-") {
				rejected[name] = true
				fmt.Printf("extended candidate rejected tag=%s reason=unsupported-or-invalid-config\n", name)
				continue
			}
			return nil, fmt.Errorf("outbound %s: %w", name, e)
		}
		if nativeTags[str(o["tag"])] {
			return nil, fmt.Errorf("duplicate outbound tag")
		}
		outs = append(outs, o)
		nativeTags[str(o["tag"])] = true
		converted[str(x["tag"])] = true
	}
	dns := obj(root["dns"])
	found := false
	for _, v := range list(dns["servers"]) {
		if str(obj(v)["tag"]) == "dns-direct" {
			found = true
		}
	}
	if !found {
		dns["servers"] = append(list(dns["servers"]), object{"type": "udp", "tag": "dns-direct", "server": "1.1.1.1"})
		root["dns"] = dns
	}
	var inner []string
	ins := list(root["inbounds"])
	for _, v := range list(xr["inbounds"]) {
		x := obj(v)
		it := tag(str(x["tag"]))
		inner = append(inner, it)
		in := object{"type": "socks", "tag": it, "listen": "127.0.0.1", "listen_port": x["port"]}
		var users []any
		for _, a := range list(obj(x["settings"])["accounts"]) {
			u := obj(a)
			users = append(users, object{"username": u["user"], "password": u["pass"]})
		}
		if len(users) > 0 {
			in["users"] = users
		}
		ins = append(ins, in)
	}
	if len(inner) == 0 {
		return nil, fmt.Errorf("missing internal inbound")
	}
	root["inbounds"] = ins
	route := obj(root["route"])
	var rules []any
	rules = append(rules, object{"inbound": inner, "action": "sniff", "sniffer": []string{"http", "tls"}, "timeout": "300ms"})
	routing := obj(xr["routing"])
	balancers := map[string]bool{}
	for _, v := range list(routing["balancers"]) {
		b := obj(v)
		var members []string
		for _, o := range xo {
			ot := str(obj(o)["tag"])
			if !converted[ot] {
				continue
			}
			for _, prefix := range stringsOf(b["selector"]) {
				if strings.HasPrefix(ot, prefix) {
					members = append(members, tag(ot))
					break
				}
			}
		}
		if len(members) == 0 {
			aiOnly := true
			for _, prefix := range stringsOf(b["selector"]) {
				aiOnly = aiOnly && prefix == "ai-proxy-"
			}
			if !aiOnly {
				return nil, fmt.Errorf("empty main balancer")
			}
			outs = append(outs, object{"type": "block", "tag": tag(str(b["tag"]))})
			balancers[str(b["tag"])] = true
			continue
		}
		fb := str(b["fallbackTag"])
		if fb == "" {
			fb = strings.TrimPrefix(members[0], "extended/")
		}
		owned := false
		for _, m := range members {
			owned = owned || m == tag(fb)
		}
		if !owned {
			originallyOwned := false
			for _, prefix := range stringsOf(b["selector"]) {
				originallyOwned = originallyOwned || strings.HasPrefix(fb, prefix)
			}
			if !originallyOwned || !rejected[fb] {
				return nil, fmt.Errorf("fallback escapes pool %s", str(b["tag"]))
			}
			fb = strings.TrimPrefix(members[0], "extended/")
		}
		settings := obj(obj(b["strategy"])["settings"])
		o := object{"type": "irspeedy-pool", "tag": tag(str(b["tag"])), "outbounds": members, "fallback": tag(fb), "expected": settings["expected"], "max_rtt": settings["maxRTT"], "tolerance": settings["tolerance"]}
		outs = append(outs, o)
		balancers[str(b["tag"])] = true
	}
	for _, v := range list(routing["rules"]) {
		x := obj(v)
		r := object{"inbound": inner}
		if a := stringsOf(x["inboundTag"]); len(a) > 0 {
			for i := range a {
				a[i] = tag(a[i])
			}
			r["inbound"] = a
		}
		if n := str(x["network"]); n != "" {
			r["network"] = strings.Split(n, ",")
		}
		if p := str(x["port"]); p != "" {
			if p == "443" {
				r["port"] = []int{443}
			} else {
				return nil, fmt.Errorf("unsupported route port %s", p)
			}
		}
		var domains, suffix, regex, rs, ips []string
		private := false
		for _, d := range stringsOf(x["domain"]) {
			switch {
			case strings.HasPrefix(d, "domain:"):
				suffix = append(suffix, strings.TrimPrefix(d, "domain:"))
			case strings.HasPrefix(d, "full:"):
				domains = append(domains, strings.TrimPrefix(d, "full:"))
			case strings.HasPrefix(d, "regexp:"):
				regex = append(regex, strings.TrimPrefix(d, "regexp:"))
			case strings.HasPrefix(d, "geosite:"):
				rs = append(rs, strings.TrimPrefix(d, "geosite:")+"_SITE")
			default:
				return nil, fmt.Errorf("unsupported domain rule %s", d)
			}
		}
		for _, ip := range stringsOf(x["ip"]) {
			if ip == "geoip:private" {
				private = true
			} else if strings.HasPrefix(ip, "geoip:") {
				rs = append(rs, strings.TrimPrefix(ip, "geoip:")+"_IP")
			} else {
				ips = append(ips, ip)
			}
		}
		if len(domains) > 0 {
			r["domain"] = domains
		}
		if len(suffix) > 0 {
			r["domain_suffix"] = suffix
		}
		if len(regex) > 0 {
			r["domain_regex"] = regex
		}
		if len(ips) > 0 {
			r["ip_cidr"] = ips
		}
		if private {
			r["ip_is_private"] = true
		}
		if len(rs) > 0 {
			available := map[string]bool{}
			for _, a := range list(route["rule_set"]) {
				available[str(obj(a)["tag"])] = true
			}
			all := true
			for _, a := range rs {
				all = all && available[a]
			}
			if !all {
				continue
			}
			r["rule_set"] = rs
		}
		ot := str(x["outboundTag"])
		bt := str(x["balancerTag"])
		if ot == "block" {
			r["action"] = "reject"
		} else if bt != "" && balancers[bt] {
			r["action"] = "route"
			r["outbound"] = tag(bt)
		} else if converted[ot] {
			r["action"] = "route"
			r["outbound"] = tag(ot)
		} else {
			return nil, fmt.Errorf("unknown routing target")
		}
		rules = append(rules, r)
	}
	// The internal SOCKS path cannot fall into the application's outer default.
	defaultTag := ""
	for _, o := range xo {
		n := str(obj(o)["tag"])
		if converted[n] {
			defaultTag = tag(n)
			break
		}
	}
	if defaultTag == "" {
		return nil, fmt.Errorf("no valid default outbound")
	}
	rules = append(rules, object{"inbound": inner, "action": "route", "outbound": defaultTag})
	for _, v := range list(route["rule_set"]) {
		rs := obj(v)
		if rs["type"] == "remote" {
			detour := str(rs["download_detour"])
			for _, outer := range list(root["outbounds"]) {
				o := obj(outer)
				if o["type"] == "socks" && str(o["tag"]) == detour && str(o["server"]) == "127.0.0.1" {
					for _, in := range list(xr["inbounds"]) {
						if number(o["server_port"]) == number(obj(in)["port"]) {
							rs["download_detour"] = defaultTag
						}
					}
				}
			}
		}
	}
	route["rules"] = append(rules, list(route["rules"])...)
	root["route"] = route
	root["outbounds"] = outs
	return json.Marshal(root)
}

// Migrate fields still emitted by the Windows WireGuard/local profile builders.
func normalizeNative(root object) error {
	var outbounds []any
	endpoints := list(root["endpoints"])
	for _, v := range list(root["outbounds"]) {
		o := obj(v)
		if o["type"] != "wireguard" {
			outbounds = append(outbounds, o)
			continue
		}
		for _, r := range list(o["reserved"]) {
			if number(r) != 0 {
				return fmt.Errorf("nonzero WireGuard reserved bytes are unsupported by the native endpoint")
			}
		}
		peer := object{"address": o["server"], "port": o["server_port"], "public_key": o["peer_public_key"], "allowed_ips": []string{"0.0.0.0/0", "::/0"}}
		copyKeys(peer, o, map[string]string{"pre_shared_key": "pre_shared_key"})
		endpoint := object{"type": "wireguard", "tag": o["tag"], "address": o["local_address"], "private_key": o["private_key"], "peers": []any{peer}}
		copyKeys(endpoint, o, map[string]string{"system_interface": "system", "interface_name": "name", "workers": "workers", "mtu": "mtu", "detour": "detour"})
		endpoints = append(endpoints, endpoint)
	}
	root["outbounds"] = outbounds
	if len(endpoints) > 0 {
		root["endpoints"] = endpoints
	}
	for _, v := range list(root["inbounds"]) {
		o := obj(v)
		if o["type"] != "tun" {
			continue
		}
		if a := o["inet4_address"]; a != nil {
			o["address"] = stringsOf(a)
			delete(o, "inet4_address")
		}
		delete(o, "endpoint_independent_nat")
		if o["stack"] == "gvisor" {
			o["stack"] = "system"
		}
	}
	return nil
}
