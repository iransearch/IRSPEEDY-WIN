# Proxy domain strategies (1.4.67)

Windows Proxy mode uses the existing Core settings corresponding to the two
Throne controls in the approved reference:

| Throne setting | Native configuration | Value |
| --- | --- | --- |
| Resolve Domain Strategy | Scoped `route.rules` actions with `action: resolve` | `prefer_ipv4` |
| Default Domain Strategy | `route.default_domain_resolver.strategy` | `prefer_ipv4` (already present) |

These UI setting names are not top-level Core JSON fields. Throne's generator
maps `resolve_domain_strategy` to resolve actions and `default_domain_strategy`
to the default resolver. Windows uses those native fields directly. Its existing
HTTP/SOCKS resolve rules stay before the main/AI VPN destinations, retaining
direct/bypass precedence and the Shield checks before and after resolution.
It does not copy Throne's global resolve rule ahead of Windows routing.

`prefer_ipv4` permits both A and AAAA. It is not an IPv6 switch: the current
`vpn-dns-race` can return a first-positive AAAA-only result or a cached AAAA-only
answer without waiting for A. No family grace period, DNS race algorithm change
or automatic retry is added. Site accessibility on the customer's network
therefore still needs live validation.

Version 1.4.66's generated user-proxy `ip_version: 6` reject rule is removed.
Already-generated native-race Proxy profiles are upgraded in place: matching
resolve actions receive `prefer_ipv4`, and only the exact generated 1.4.66
IPv6 blocker is removed. Other Shield/custom reject rules are retained.
Repeated application does not duplicate resolve or Shield guard bundles.
IPv6 literals and IPv6-only names can again follow existing Proxy routing.

The default resolver remains `dns-direct` with `prefer_ipv4`. TUN profiles,
including their mixed listener, are unchanged. The seven-provider VPN DNS
race, TLS verification, 8+4 second stages/12 second outer budget, direct/bootstrap
DNS, Iran bypass, Split Tunnel, Shield, Pool, login, tests/probes, UI and OS
IPv6 settings retain their existing policy. Residential uses the same
connection-mode policy as VPN.

Existing Core `141a42f90f393244f47fa75261ad3d1eb4c830c9` supports these settings;
no Core source, dependency or packaged payload update is needed for 1.4.67.
Use the usual Windows single-file build and reconnect to apply the new profile.

Configuration checks compile production C# against .NET Framework 4.8 reference
assemblies and run with the .NET 8 harness. They verify the two strategies,
seven DNS endpoints and TLS checks, legacy-profile migration, idempotency and
preserved TUN/direct/Shield routing. Native checks use generated profiles with
seven loopback DNS providers and real HTTP CONNECT/SOCKS inbounds. They verify
IPv4-only and IPv6-only names, cached AAAA reuse, literal IPv6, main/AI outbound
selection and unchanged payloads. The native overlay leaves Core sources intact.

Manual Windows acceptance: build 1.4.67 with the existing modern/legacy Core,
connect to Turkey in Proxy mode and open/refresh Hostkey, then compare TUN.
Hostkey diagnostics may now show either address family; `prefer_ipv4` does not
guarantee an A answer if only AAAA won the race. Check Shield and Iran bypass
with the same enabled settings as the previous build.
