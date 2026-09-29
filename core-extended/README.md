# IRSPEEDY sing-box-extended core

Experimental branch `agent/sing-box-extended`. Upstream is pinned to
`v1.14.1-extended-2.7.2` / `55faa763f986f4ca8a492d9b2719bc6330d2bef5`.

This executable replaces ThroneCore. It links sing-box-extended, not Xray or
Throne's custom Hysteria adapter. The Windows application's existing ProtoRPC
wire contract and configuration builders are retained as a compatibility input.
`config.go` converts their output to native sing-box outbounds and scoped routes.
Internal SOCKS listeners are native sing-box listeners in the same instance;
there is no second core process. Unsupported conversion returns a clear failure
(or excludes an invalid Pool member), never a Direct fallback.

## Preserved behavior

- Main and AI least-load pools: expected 5, main max RTT 3s / AI 5s,
  failure tolerance 0.2, random choice among the qualified top candidates sorted
  by RTT deviation then average. One sample uses half the average as deviation.
- Initial probes only after successful Box.Start. Interval 15m, sampling 2:
  one initial HEAD, then two samples spread across each 30-minute window;
  last two samples, validity 60m. No external health/restart timer.
- AI fallback must be an AI member. An empty/rejected AI pool has a blocking
  target; its domain rule stays before main routing. Main's startup fallback
  remains its own first member.
- DNS for native Pool servers uses dns-direct. The app's 1.1.1.1 resolvers,
  split routing, game mode, sharing and public-IP lifecycle separation remain.
- XHTTP auto/packet-up/stream-up/stream-one and separate download TLS mapping.
  Certificate DER SHA256 pins use a small upstream patch for Go TLS and uTLS;
  they are never substituted with public-key pins or silently disabled.
- Standalone Test has its own instance, cancellation and incremental results.
  It sends a warm-up GET then measures a second GET with the same HTTP client,
  matching the previous Throne latency contract. Connected-instance tests send
  one GET. Each request retains its configured timeout; warm-up failure ends
  that candidate. StopTest cancels either stage without stopping the VPN.
- Conversion failures name the exact candidate using the app's existing
  `failed to build outbound config with tag xray-N` contract. The Windows runner
  removes that candidate and rebuilds the remaining test batch. Invalid pins
  stay rejected; certificate verification is not bypassed.

Legacy TCP HTTP-header camouflage, old XTLS and legacy Xray QUIC camouflage
are explicitly refused; these are not native equivalents of XHTTP or Hysteria2.
Legacy WireGuard outbounds migrate to native endpoints; nonzero reserved bytes
are explicitly refused because the endpoint has no equivalent field. Local TUN
profiles use the system stack on both architectures (including x86).
Existing C# class/RPC names containing Xray describe input compatibility only.

## Build and packaging

On a Windows 10/11 x64 build machine with Git, PowerShell 5+, .NET 8 SDK and
.NET Framework 4.8 targeting pack:

```
Build-SingBox-Extended.cmd -RuntimeZip C:\path\to\existing\Files.zip
```

The existing complete runtime archive is required (not committed to this repo).
All unrelated runtime assets are preserved. The builder removes the old SGuard
entries and embeds SGuard32/SGuard64/SGuard732/SGuard764 plus a hash manifest.
The app requires the extended-core.json engine marker and matching executable
entry before accepting a cached runtime, preventing reuse of a legacy Throne
runtime that has the same SGuard names. Keep the manifest beside the four EXEs.
Costura and the embedded Files.zip keep the app a single distributed EXE.
For Core-only output: `Build-SingBox-Extended.cmd -CoreOnly`.

If the public Go mirror returns HTTP 403, the builder changes Go's standard
`https://proxy.golang.org,direct` to `https://proxy.golang.org|direct` for this
build only. The pipe allows direct-source fallback on all download errors;
custom proxy/off settings are retained. `go.sum` and checksum database verification
remain enabled. Git access to upstream repositories is required for direct mode.
For an older builder, set this in the same PowerShell window before running it:

```powershell
$env:GOPROXY = 'https://proxy.golang.org|direct'
.\Build-SingBox-Extended.cmd -CoreOnly
```

There is no need to delete the module cache or download the compiler again.

The builder verifies the checksum of XTLS Go `patched-1.26.6`, applies the pinned
certificate patch, tests on Windows, builds both 386 and amd64 with CGO disabled,
and checks PE architecture/subsystem <= 6.1. The compatibility binaries also run
on modern Windows, so the modern names contain the same compiled code.
The target app requires Windows 7 SP1 + .NET Framework 4.8. Building on Windows 7
itself is not required or supported by this builder. Driver signing updates and
administrator privileges are required for TUN. Wintun is embedded by sing-tun.

**Cross-compilation and PE checks do not prove Windows 7 runtime compatibility.**
Release testing must run both architectures on Windows 7 SP1: RPC probe, initial
URL tests/cancellation, proxy, TUN, split tunneling, AI block/fallback, disconnect,
Wi-Fi loss/recovery and application exit. Direct hotspot has the existing OS
limitations; this migration does not add a Windows 7 hotspot implementation.

For Linux development clone the pinned upstream into `core-extended/upstream`,
apply `patches/certificate-pin.patch`, then run:

```
go test -race -tags with_quic,with_utls,with_wireguard,with_clash_api,with_v2ray_api .
```

The full lifecycle test skips only on Linux when the environment denies netlink;
the Windows builder runs it normally. Protocol handshakes against production
servers and real Windows TUN recovery require target-machine testing.

## Licenses

sing-box-extended: GPLv3 (`LICENSE.sing-box`, upstream source and local patch).
The vendored ProtoRPC transport is BSD licensed (`protorpc/LICENSE`), from
Throne-G c0fa2071bda243762b6387e56355f8a4242bcad5/core/protorpc, with error/frame
validation fixes. Pool selection follows the policy of Xray's least-load
strategy; only native sing-box dialers are used.

## Validation for this migration

- Patched Go 1.26.6 cross-builds: Windows 386 and amd64 pass; both PE OS and
  subsystem versions are 6.1. These are build checks, not Windows runtime tests.
- Go tests with the race detector pass: scoped AI routing/blocking, owned fallback,
  DNS/XHTTP conversion, certificate pins, pool readiness/selection and RPC framing.
  The Linux netlink restriction skips the real Box.Start lifecycle test.
- C# HysteriaXrayChecks, ConnectionDiagnosticChecks and all 107 SplitTunnelChecks
  pass. The existing Newtonsoft.Json 12.0.2 dependency reports NU1903.
- Full WPF/Costura packaging has not run here: it requires Windows and the existing
  complete runtime Files.zip. The build script performs packaging on that machine.

### URL test parity correction (irspeedy.2)

The migration initially used native sing-box HEAD URLTest, which did not preserve
Throne's warm-up behavior for ordinary/SOCKS-wrapped outbounds. The dedicated
initial URL test now uses GET twice and measures the second request. Tests verify
connection reuse, exclusion of an injected 300ms first response from the reported
latency, one request for connected tests, warm-up failure and StopTest during
either stage. The Pool's independent 15m/sampling=2 HEAD probes are unchanged.
This fixes measurement semantics; no percentage reduction is applied to results.

### Pool diagnostics (irspeedy.3)

Pool probes now report success or failure with elapsed time, HTTP status and a
fixed reason code. Failed probes distinguish outbound dialing, target TLS and
HTTP response phases. TCP/UDP pool dial failures also report a fixed reason.
The app preserves only allowlisted diagnostic values, never raw errors or
destinations. Build both the core and app to receive the new fields. This is a
diagnostic change, not a confirmed fix for the reported Hysteria2 outage; Pool
membership, fallback, sampling, DNS and connection behavior are unchanged.
