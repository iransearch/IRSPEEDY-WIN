# VPN time zone (1.4.6.6)

The clock replaces the change-server button in the connected-server card.
Connecting alone never changes Windows time settings. Clicking the clock sends
one HTTPS GeoIP lookup through that connection's explicit loopback HTTP proxy.
The lookup obtains the public exit IP and IANA time zone from the same response,
which avoids mismatched answers from different Round-robin exits. A first-priority
route for `ipwho.is:443/TCP` on the user HTTP/mixed listeners selects the primary
`proxy` outbound regardless of AI, VOD, Game Mode or process exclusions. There is
no direct fallback, redirect following, or separate IP lookup.

A successful click changes the Windows time-zone setting (including normal
daylight-saving rules), not the UTC/system clock or NTP configuration. The icon
turns green; its tooltip shows the IANA zone and local time. That zone stays fixed
throughout the connection, including automatic Core recovery and pool rotation.
Clicking again restores the original Windows zone and daylight-saving preference.
A later activation performs a fresh lookup. Disconnect, server replacement,
logout and application exit restore the original setting before network/Core
cleanup. Connection duration uses a monotonic stopwatch; account expiry uses the
saved home zone while the VPN zone is active.

Before Windows is changed, the original zone, daylight-saving-disabled flag and
serialized local zone are atomically saved to
`%LOCALAPPDATA%\IRSpeedyVPN\time-zone-recovery.json`. The file is removed only
after successful restoration. Unhandled exceptions attempt restoration; abrupt
termination or power loss is recovered on the next app launch after it acquires
the single-instance mutex. Restoration failure leaves the journal for retry.
Disconnect invalidates in-flight lookups without waiting for network cancellation;
a late reply cannot change a disconnected or replacement session.

GeoIP uses `https://ipwho.is/?fields=success,ip,timezone.id`, with a 10-second
HTTP timeout, bounded response size/depth, and no API key. An unavailable provider,
unknown zone, missing installed Windows zone or denied `SeTimeZonePrivilege`
leaves the prior setting intact and shows a retry hint. No country-wide offset
guessing is used: countries with multiple zones are resolved by exit IP.
User or Windows automatic time-zone changes made outside this app are not managed.

The embedded IANA-to-Windows mapping is derived from Unicode CLDR release 48:

- [windowsZones.xml](https://github.com/unicode-org/cldr/blob/release-48/common/supplemental/windowsZones.xml)
- [bcp47/timezone.xml](https://github.com/unicode-org/cldr/blob/release-48/common/bcp47/timezone.xml)

Map-zone entries define the Windows IDs; BCP47 aliases and preferred IANA names
extend their coverage. The generated TSV contains 598 IDs/aliases. Unicode
License V3 is included in `docs/third-party/Unicode-CLDR-LICENSE.txt` and embedded
in the application. No Core or server change is required.

See [test instructions](../tests/TimeZoneChecks/README.md). Linux checks cover
session/routing/journal behavior and net48/WPF API compatibility. Actual Windows
zone mutation and WPF rendering require the documented Windows acceptance checks.
