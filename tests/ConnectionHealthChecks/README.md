# Connection health / Wi-Fi recovery

Run `dotnet run --project tests/ConnectionHealthChecks` (net8 SDK).
The checks compile the production monitor and exercise network-revision fencing,
offline waiting, failure thresholds, stale results after disconnect, explicit
HTTP proxy use on both endpoints, and cancellation against a stalled local proxy.
The other service partial is stubbed; this is not a WPF/Windows integration test.

Windows acceptance (same single-file builder):

1. Connect in System Proxy mode. Wait for the checked badge and fresh egress IP.
2. Disconnect Wi-Fi without pressing Disconnect in IRSPEEDY. The old IP and
   checked badge must clear, with a waiting/checking status on the same page.
3. Reconnect Wi-Fi, including a different access point. Inspect
   `health-proxy-reapplied` / `health-result`. Confirm browser and explicit local
   proxy requests both work. The IP must be freshly obtained, not cached UI data.
4. Stop the active core while network is available. After two unsuccessful probe
   rounds, recovery must run even if an inactive server object owned the process.
   During silent recovery, the System Proxy must remain enabled. Repeated failed
   recovery is capped at three attempts, then the normal disconnected UI appears.
5. Press Disconnect during a stalled check or queued recovery. No late probe may
   restore the IP/badge or re-enable System Proxy. Repeat with Share VPN active.
6. Verify TUN and Proxifier connections still start and disconnect normally.

Scope: the monitor checks the explicit local proxy path. It is not a firewall
kill switch and does not prove every browser honors System Proxy, or that all
TUN routes/IPv6/split-tunnel exceptions use the VPN. Intentional game/split routing
is unchanged. The supplied log ends with System Proxy active, a Wi-Fi loss at
00:57:59 and return at 00:59:24, with no core exit/reconnect in that interval.
The log does not record actual WinINet settings at the moment of the reported
IP exposure, so its exact direct-routing cause cannot be established from it.
