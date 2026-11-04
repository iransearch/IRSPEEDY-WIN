# Country test result retention — 1.4.73

`ServerCheckCache.Bind` previously deleted every result and completed-row outcome absent
from the currently bound catalog. An empty or partial refresh could therefore erase
history before the same country/configuration returned. Ordinary VPN fingerprints also
included the display fragment of a connection URI.

The cache now retains matching URL measurements and completed-row outcomes across
temporary omissions. Only currently bound services enter the work queue. Each saved
measurement/outcome dictionary keeps at most 5,000 entries, preferring current configurations
and then newer history; existing cache validation and atomic replacement remain in use.
An explicit manual refresh still clears all retained results.

URI fragments are excluded from the cache identity. Non-URI payloads, endpoints,
credentials, query/path/TLS values, service identity and auxiliary connection fields remain
exact. A real configuration change cannot inherit a positive result from another config.
Account and service/protocol scopes remain separate. Failed current tests stay failed;
last-success history remains display-only. The runtime connection links are not rewritten.

Old exact full-URI fingerprints and completed-row outcomes migrate on binding. Already
deleted results cannot be reconstructed. If the URI label changed before its old hash
could be migrated, the old label cannot be recovered from the hash; that config must be
tested again. No country-only or credential-insensitive migration is attempted.

Regression cases live in `tests/ResidentialChecks/CacheChecks.cs` and run through the
existing Residential harness. They cover empty/partial catalog rebinding, restart,
URI labels and old cache migration, connection changes, account/scope isolation,
explicit clearing, bounded retention and failed-result preservation.
