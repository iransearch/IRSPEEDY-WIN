# Proxy destination IPv4 policy (1.4.66)

Normal Windows Proxy profiles now request `ipv4_only` in their existing scoped
HTTP/SOCKS `resolve` actions before main/AI VPN routes. The existing seven-provider
DNS race, provider TLS checks, detours and deadlines are retained. A cached AAAA
answer is not eligible for this A-only lookup. No 250 ms family grace or native
DNS implementation change is needed.

One `reject` rule, scoped to the user mixed/HTTP/SOCKS inbound tags and
`ip_version=6`, rejects literal IPv6 destinations before routing/sniffing. It uses
`method=default` and `no_drop=true`, so rejection does not silently become drop.
IPv6-only destinations cannot be reached through the main/AI Proxy path.

The mode check is the absence of a TUN inbound, not the presence of a mixed
listener: TUN profiles also have a mixed listener. Their configuration remains
identical to 1.4.65, including the local proxy listener's DNS strategy. Windows
network settings, physical/server transport address selection, Core-owned DNS
and Pool sockets, login, probes and Shield definitions are not changed.
Existing direct/bootstrap/Split Tunnel DNS exclusions retain their policy;
this is a destination policy for proxied VPN traffic, not an OS IPv6 switch.
The literal-IPv6 rejection applies to all user proxy inbound destinations.

Already-generated native-race Proxy profiles are upgraded in place: their older
scoped resolve rules receive `ipv4_only` rather than running an unrestricted
lookup first. Applying the transformation again does not duplicate resolution,
Shield guard bundles or the IPv6 rejection rule. Other custom DNS profiles and
profiles with no main/AI VPN route keep their existing policy.

No new Core build is required for this policy. The existing Core
`141a42f90f393244f47fa75261ad3d1eb4c830c9` already supports `ipv4_only` and
`ip_version`. Retain its modern/legacy payloads and the existing single-file
Windows build flow. Reconnect after updating so the new profile is applied.

Validation uses production-generated C# profiles against .NET Framework 4.8
references and the existing Core's real HTTP CONNECT/SOCKS5, DNS and routing
implementations. The additional native checks run through a Go test overlay;
they do not modify the Core checkout or dependencies. They verify dual-family
names, an old AAAA cache entry, A-only upstream queries, main/AI routing, literal
IPv6 and IPv6-only rejection, unchanged relayed bytes, and continued IPv6 access
through the local mixed listener of a generated TUN profile. The existing native
Shield/direct/bootstrap suites also run with the new production fixtures.

Manual Windows acceptance: connect to Turkey in Proxy mode, open and refresh
the reported Hostkey URL, then compare TUN. Hostkey diagnostics should show
Proxy lookup strategy `ipv4_only`'s numeric value, A results and family 4 dials.
The customer's IPv4 resets still require observation; this policy does not
certify HTTPS/application success.
