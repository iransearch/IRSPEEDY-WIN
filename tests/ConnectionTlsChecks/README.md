Run `python3 tests/ConnectionTlsChecks/run.py /path/to/dotnet` with .NET 8 and OpenSSL.
The checks compile the production TCP/TLS probe. Local TLS servers and HTTP CONNECT
proxies exercise a valid certificate, hostname mismatch, an untrusted certificate,
proxy rejection/malformed/oversized responses, timeout and cancellation. The fixture
CA is trusted only in the test child process; production certificate checks remain
intact. Successful probes send no application HTTP request after TLS negotiation.
The direct-mode fixture binds loopback port 443 and requires that port to be free
and permitted. No Internet destination is contacted.

Run `python3 tests/ConnectionTestChecks/run.py /path/to/dotnet` for presentation,
active-session invalidation and animation lifecycle checks with UI/network doubles.

Windows acceptance: run the application with its real packaged Core. Check both
proxy and TUN modes, each site's confirmed/failed result, disconnect during a test,
and switching connections. Packet/log inspection should show CONNECT to port 443
for a local HTTP proxy, TLS with the site's SNI, and no GET/HEAD page request.
The eight-second deadline applies to the whole TCP/CONNECT/TLS probe. A successful
result confirms TCP and trusted TLS reachability, not page content or account access.
