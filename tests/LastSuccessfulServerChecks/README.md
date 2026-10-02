Run with Python and .NET 8:

    python3 tests/LastSuccessfulServerChecks/run.py /path/to/dotnet
    python3 tests/LastSuccessfulServerChecks/connection_result.py /path/to/dotnet

The checks compile the production persistence helper, picker success/ordering methods,
and connection-result handler against registry/UI doubles. They cover numbered-row
identity, one starred row, latency refresh, search/selection preservation, replacing
service objects, global vs country-scoped Smart, removed rows, registry write failures,
and successful/failed/cancelled/replaced/superseded connection outcomes.

On Windows, connect successfully to a country row, disconnect and check its gold star
and first position below the fixed Smart card. Connect to another row, fail a connection,
run server tests and restart the app. Only a successful country connection transfers the
star. Connecting through global Smart leaves the previous country marker intact. No
server URL or credentials are stored; the registry contains the service type and API ID.
