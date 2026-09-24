Run `python3 tests/ServerRefreshChecks/run.py /path/to/dotnet` with .NET 8.
The harness compiles the actual scheduler region against fake UI/network dependencies.
It verifies initial enumeration, oldest-country selection, connection suppression,
and cancellation/draining before connection. It does not exercise Windows routing.

Manual Windows acceptance:
- Initial list checks all countries once.
- Connect during a test: probe drains before connection begins.
- Leave connected >15 minutes: no list probes; previous values remain on return.
- Disconnect: list appears immediately; no probe until cleanup completes.
- After cleanup: one country, then another after three minutes.
- Multiple rows in one country are checked together.
- API list refresh preserves measurements only for identical URL configurations.
- Rapid connect/disconnect and failed connection must not overlap a list probe with VPN.
