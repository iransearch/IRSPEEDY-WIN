# Opaque desktop design

Implements the September 22 design handoff in the existing 420 x 700 WPF window.
The explicit request for an opaque interface overrides the handoff's Mica and
alpha-gradient instructions. The window uses native WindowChrome, no layered
window or backdrop; both gradient stops and the loading surface are opaque.
Rounded corners depend on native Windows chrome support.

The theme includes the new login and connected colors and Geometry resources
converted from the supplied SVGs. Original SVGs are embedded for traceability.
Existing bundled Vazirmatn faces, PNG flags and logo resources are retained.
Login and connected content scroll when necessary; disconnect remains pinned.
The change-server action uses the existing asynchronous disconnect flow.

## Data and configuration

- Country, flag, latency, elapsed time and account expiry use existing live data.
- IVPNService exposes neither session byte counters nor a verified public IP.
  The corresponding design slots display an unavailable dash, never sample data.
  Integrating those metrics requires a separate service contract implementation.
- No registration URL was supplied. The registration button is disabled unless
  an absolute HTTP(S) URL is provided in signup-url.txt beside the executable.
  The optional file is not needed for normal login or a single-file release.
- The existing remember-me and account-renewal actions remain available.

## Validation

XML, resource references, event handler references and bundled font families
checked locally. WPF compilation and rendering require Windows and were not
available in this environment.

Windows acceptance: build with the existing single-file builder; test at 100%,
150% and 200% scaling; verify no desktop bleed-through; login with saved/empty
credentials; toggle password; show renewal; connect/change/disconnect; check
search and server selection; verify timer and expiry; check loading/cancel and
header settings/sharing actions. Confirm no controls overlap or become clipped.
